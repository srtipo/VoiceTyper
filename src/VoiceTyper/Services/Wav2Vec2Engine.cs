using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using NAudio.Wave;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public sealed class Wav2Vec2Engine : IDisposable, ITranscriptionEngine
{
    private readonly ModelManagerService _modelManager;
    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    private InferenceSession? _session;
    private Wav2Vec2Model? _loadedModel;
    private string? _loadedVocabLang;
    private Dictionary<int, string>? _idToChar;
    private bool _disposed;

    public TranscriptionEngine Engine => TranscriptionEngine.Wav2Vec2;

    public string BackendMode => "CPU";

    public Wav2Vec2Engine(ModelManagerService modelManager, SettingsService settings)
    {
        _modelManager = modelManager;
        _settings = settings;
    }

    public async Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken ct = default)
    {
        var startTs = Stopwatch.GetTimestamp();
        var model = _settings.Current.Wav2Vec2Model;
        var vtLanguage = _settings.Current.Language;
        var mmsLang = MapLanguageToMms(vtLanguage);
        if (vtLanguage != "es")
        {
            Log.Warn($"[Wav2Vec2] user selected vt_lang={vtLanguage}, but Wav2Vec2 model is Spanish-specific — best results only for es");
        }
        Log.Info($"[Wav2Vec2] start: model={model} vt_lang={vtLanguage} mms_lang={mmsLang} bytes={wavBytes.Length}");

        await _initLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_session is null || _loadedModel != model || _loadedVocabLang != mmsLang)
            {
                Log.Info($"[Wav2Vec2] cache miss: session_null={_session is null} model_changed={_loadedModel != model} lang_changed={_loadedVocabLang != mmsLang}");

                _session?.Dispose();
                _session = null;
                _idToChar = null;

                var modelDir = await _modelManager.EnsureWav2Vec2ModelAsync(model, progress: null, ct).ConfigureAwait(false);
                var modelPath = Path.Combine(modelDir, "model.onnx");
                var vocabPath = Path.Combine(modelDir, "vocab.json");

                if (!File.Exists(modelPath))
                {
                    throw new FileNotFoundException($"Wav2Vec2 model file missing: {modelPath}");
                }
                if (!File.Exists(vocabPath))
                {
                    throw new FileNotFoundException($"Wav2Vec2 vocab file missing: {vocabPath}");
                }

                var sessionOptions = new SessionOptions
                {
                    LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
                };

                var sessionTs = Stopwatch.GetTimestamp();
                _session = new InferenceSession(modelPath, sessionOptions);
                var sessionMs = Stopwatch.GetElapsedTime(sessionTs).TotalMilliseconds;

                _idToChar = LoadVocab(vocabPath, mmsLang);
                _loadedModel = model;
                _loadedVocabLang = mmsLang;
                Log.Info($"[Wav2Vec2] session ready: vocab_size={_idToChar.Count} load_ms={sessionMs:F0}");
            }
            else
            {
                Log.Info($"[Wav2Vec2] cache hit");
            }
        }
        finally
        {
            _initLock.Release();
        }

        var samples = DecodeWav(wavBytes);
        if (samples.Length < 1600)
        {
            Log.Warn($"[Wav2Vec2] too few samples ({samples.Length}), skipping");
            return string.Empty;
        }
        Normalize(samples);

        var inputTensor = new DenseTensor<float>(samples, new[] { 1, samples.Length });
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_values", inputTensor)
        };

        var processTs = Stopwatch.GetTimestamp();
        using var outputs = _session!.Run(inputs);
        var logits = outputs.First().AsTensor<float>();
        var processMs = Stopwatch.GetElapsedTime(processTs).TotalMilliseconds;

        var text = CtcDecode(logits, _idToChar!);

        var totalMs = Stopwatch.GetElapsedTime(startTs).TotalMilliseconds;
        var preview = text.Length > 50 ? text.Substring(0, 50) + "..." : text;
        Log.Info($"[Wav2Vec2] end: total_ms={totalMs:F0} process_ms={processMs:F0} chars={text.Length} preview='{preview}'");

        return text;
    }

    public Task<bool> SmokeTestAsync()
    {
        return Task.Run(() =>
        {
            string? modelDir = null;
            try
            {
                var model = _settings.Current.Wav2Vec2Model;
                modelDir = _modelManager.GetWav2Vec2ModelDir(model);
                var modelPath = Path.Combine(modelDir, "model.onnx");
                var vocabPath = Path.Combine(modelDir, "vocab.json");

                if (!File.Exists(modelPath))
                {
                    Log.Error($"[SmokeTest Wav2Vec2] model not found: {modelPath}");
                    return false;
                }
                if (!File.Exists(vocabPath))
                {
                    Log.Error($"[SmokeTest Wav2Vec2] vocab not found: {vocabPath}");
                    return false;
                }

                using var session = new InferenceSession(modelPath);
                Log.Info($"[SmokeTest Wav2Vec2] OK - InferenceSession loaded (CPU), inputs={session.InputMetadata.Count}, outputs={session.OutputMetadata.Count}");
                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[SmokeTest Wav2Vec2] FAIL - {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        });
    }

    private static float[] DecodeWav(byte[] wavBytes)
    {
        using var ms = new MemoryStream(wavBytes);
        using var reader = new WaveFileReader(ms);
        if (reader.WaveFormat.Channels != 1 || reader.WaveFormat.SampleRate != 16000)
        {
            Log.Warn($"[Wav2Vec2] unexpected WAV format: channels={reader.WaveFormat.Channels} rate={reader.WaveFormat.SampleRate}, expected 1 ch @ 16 kHz");
        }

        var sampleProvider = reader.ToSampleProvider();
        var sampleCount = (int)reader.SampleCount;
        var samples = new float[Math.Max(sampleCount, 0)];
        var read = sampleProvider.Read(samples, 0, samples.Length);
        if (read < samples.Length)
        {
            Array.Resize(ref samples, read);
        }
        return samples;
    }

    private static void Normalize(float[] samples)
    {
        if (samples.Length == 0) return;
        double sum = 0;
        for (int i = 0; i < samples.Length; i++) sum += samples[i];
        var mean = (float)(sum / samples.Length);

        double sqSum = 0;
        for (int i = 0; i < samples.Length; i++)
        {
            var d = samples[i] - mean;
            sqSum += d * d;
        }
        var std = (float)Math.Sqrt(sqSum / samples.Length);

        if (std < 1e-7f) return;
        for (int i = 0; i < samples.Length; i++)
        {
            samples[i] = (samples[i] - mean) / std;
        }
    }

    private static string CtcDecode(Tensor<float> logits, Dictionary<int, string> idToChar)
    {
        var dims = logits.Dimensions;
        if (dims.Length != 3 || dims[0] != 1)
        {
            Log.Warn($"[Wav2Vec2] unexpected logits shape: [{string.Join(",", dims.ToArray())}]");
            return string.Empty;
        }
        int timeSteps = dims[1];
        int vocabSize = dims[2];

        var sb = new StringBuilder();
        int prevId = -1;

        for (int t = 0; t < timeSteps; t++)
        {
            int bestId = 0;
            float bestLogit = float.NegativeInfinity;
            for (int v = 0; v < vocabSize; v++)
            {
                var l = logits[0, t, v];
                if (l > bestLogit)
                {
                    bestLogit = l;
                    bestId = v;
                }
            }

            if (bestId == 0) { prevId = bestId; continue; }
            if (bestId == prevId) continue;
            prevId = bestId;

            if (idToChar.TryGetValue(bestId, out var ch))
            {
                if (ch == "|")
                {
                    sb.Append(' ');
                }
                else
                {
                    sb.Append(ch);
                }
            }
        }

        var result = sb.ToString().Trim();
        return CollapseSpaces(result);
    }

    private static string CollapseSpaces(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        var sb = new StringBuilder(s.Length);
        bool prevSpace = false;
        foreach (var c in s)
        {
            if (c == ' ')
            {
                if (!prevSpace) sb.Append(c);
                prevSpace = true;
            }
            else
            {
                sb.Append(c);
                prevSpace = false;
            }
        }
        return sb.ToString();
    }

    private static Dictionary<int, string> LoadVocab(string vocabPath, string mmsLang)
    {
        var json = File.ReadAllText(vocabPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        JsonElement langVocab;
        bool isNested = root.ValueKind == JsonValueKind.Object
            && root.EnumerateObject().Any()
            && root.EnumerateObject().First().Value.ValueKind == JsonValueKind.Object;

        if (isNested && root.TryGetProperty(mmsLang, out langVocab))
        {
            Log.Info($"[Wav2Vec2] using sub-vocab for '{mmsLang}'");
        }
        else if (isNested)
        {
            Log.Warn($"[Wav2Vec2] no sub-vocab for '{mmsLang}' in {vocabPath}, using root");
            langVocab = root;
        }
        else
        {
            Log.Info($"[Wav2Vec2] using flat vocab from {vocabPath}");
            langVocab = root;
        }

        var idToChar = new Dictionary<int, string>();
        foreach (var prop in langVocab.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number)
            {
                var id = prop.Value.GetInt32();
                var token = prop.Name;
                if (id == 0 || token == "<pad>" || token == "<s>" || token == "</s>" || token == "<unk>") continue;
                idToChar[id] = token;
            }
        }
        return idToChar;
    }

    private static string MapLanguageToMms(string vtLang) => vtLang switch
    {
        "es" => "global",
        "en" => "global",
        "pt" => "global",
        "fr" => "global",
        _ => "global"
    };

    public async Task<bool> RunDiagnosticAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                var modelDir = _modelManager.GetWav2Vec2ModelDir(Wav2Vec2Model.SpanishXlsr53);
                var modelPath = Path.Combine(modelDir, "model.onnx");
                var vocabPath = Path.Combine(modelDir, "vocab.json");

                if (!File.Exists(modelPath))
                {
                    Log.Error($"[diagnose] model not found: {modelPath}");
                    return false;
                }
                if (!File.Exists(vocabPath))
                {
                    Log.Error($"[diagnose] vocab not found: {vocabPath}");
                    return false;
                }

                Log.Info($"[diagnose] === MODEL METADATA ===");
                Log.Info($"[diagnose] path: {modelPath}");

                using var session = new InferenceSession(modelPath);

                Log.Info($"[diagnose] === INPUTS ({session.InputMetadata.Count}) ===");
                foreach (var input in session.InputMetadata)
                {
                    var dims = string.Join(",", input.Value.Dimensions);
                    Log.Info($"[diagnose]   {input.Key}: shape=[{dims}] type={input.Value.ElementType}");
                }

                Log.Info($"[diagnose] === OUTPUTS ({session.OutputMetadata.Count}) ===");
                foreach (var output in session.OutputMetadata)
                {
                    var dims = string.Join(",", output.Value.Dimensions);
                    Log.Info($"[diagnose]   {output.Key}: shape=[{dims}] type={output.Value.ElementType}");
                }

                var samples = GenerateTestAudio();
                Log.Info($"[diagnose] test audio: {samples.Length} samples ({samples.Length / 16000.0:F2}s @ 16kHz)");

                var globalVocab = LoadGlobalVocab(vocabPath);
                var spaVocab = LoadVocab(vocabPath, "spa");
                Log.Info($"[diagnose] vocab: global={globalVocab.Count} chars, spa={spaVocab.Count} chars");

                RunVariant("A_only_input_values", session, samples, attentionMask: null, languageId: null, globalVocab, spaVocab);
                RunVariant("B_with_attention_mask", session, samples, attentionMask: BuildAllOnesMask(samples.Length), languageId: null, globalVocab, spaVocab);
                RunVariant("C_with_lang_spa", session, samples, attentionMask: BuildAllOnesMask(samples.Length), languageId: 0x1F, globalVocab, spaVocab);

                return true;
            }
            catch (Exception ex)
            {
                Log.Error($"[diagnose] FAIL: {ex.GetType().Name}: {ex.Message}");
                return false;
            }
        }).ConfigureAwait(false);
    }

    private static float[] GenerateTestAudio()
    {
        const int sampleRate = 16000;
        const int seconds = 2;
        var samples = new float[sampleRate * seconds];

        for (int i = 0; i < sampleRate; i++)
        {
            var t = (double)i / sampleRate;
            samples[i] = 0.5f * (float)Math.Sin(2.0 * Math.PI * 200.0 * t);
        }
        for (int i = sampleRate; i < samples.Length; i++)
        {
            samples[i] = (float)((Random.Shared.NextDouble() - 0.5) * 0.001);
        }
        return samples;
    }

    private static long[] BuildAllOnesMask(int length)
    {
        var mask = new long[length];
        for (int i = 0; i < length; i++) mask[i] = 1L;
        return mask;
    }

    private static Dictionary<int, string> LoadGlobalVocab(string vocabPath)
    {
        var json = File.ReadAllText(vocabPath);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var idToChar = new Dictionary<int, string>();
        foreach (var langProp in root.EnumerateObject())
        {
            if (langProp.Value.ValueKind != JsonValueKind.Object) continue;
            foreach (var tokenProp in langProp.Value.EnumerateObject())
            {
                if (tokenProp.Value.ValueKind != JsonValueKind.Number) continue;
                var id = tokenProp.Value.GetInt32();
                var token = tokenProp.Name;
                if (id == 0 || token == "<pad>" || token == "<s>" || token == "</s>" || token == "<unk>") continue;
                idToChar[id] = token;
            }
        }
        return idToChar;
    }

    private void RunVariant(string name, InferenceSession session, float[] samples, long[]? attentionMask, long? languageId, Dictionary<int, string> globalVocab, Dictionary<int, string> spaVocab)
    {
        try
        {
            var inputTensor = new DenseTensor<float>(samples, new[] { 1, samples.Length });
            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_values", inputTensor)
            };

            if (attentionMask is not null)
            {
                var maskTensor = new DenseTensor<long>(attentionMask, new[] { 1, attentionMask.Length });
                inputs.Add(NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor));
            }

            if (languageId is not null)
            {
                var langTensor = new DenseTensor<long>(new[] { languageId.Value }, new[] { 1 });
                inputs.Add(NamedOnnxValue.CreateFromTensor("language", langTensor));
            }

            Log.Info($"[diagnose] === variant {name} (inputs={inputs.Count}) ===");

            var ts = System.Diagnostics.Stopwatch.GetTimestamp();
            using var outputs = session.Run(inputs);
            var ms = System.Diagnostics.Stopwatch.GetElapsedTime(ts).TotalMilliseconds;
            Log.Info($"[diagnose] inference_ms={ms:F0}");

            var logitsTensor = outputs.First().AsTensor<float>();
            Log.Info($"[diagnose] logits shape=[{string.Join(",", logitsTensor.Dimensions.ToArray())}]");

            var (globalText, spaText) = DecodeAndLog(logitsTensor, globalVocab, spaVocab);
            Log.Info($"[diagnose] global_decode='{Truncate(globalText)}'");
            Log.Info($"[diagnose] spa_decode='{Truncate(spaText)}'");
        }
        catch (Exception ex)
        {
            Log.Error($"[diagnose] variant {name} FAIL: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static (string global, string spa) DecodeAndLog(Tensor<float> logits, Dictionary<int, string> globalVocab, Dictionary<int, string> spaVocab)
    {
        var dims = logits.Dimensions;
        if (dims.Length != 3 || dims[0] != 1) return ("?", "?");

        int timeSteps = dims[1];
        int vocabSize = dims[2];

        var topIdsByStep = new int[Math.Min(timeSteps, 10)];
        for (int t = 0; t < topIdsByStep.Length; t++)
        {
            int bestId = 0;
            float bestLogit = float.NegativeInfinity;
            for (int v = 0; v < vocabSize; v++)
            {
                var l = logits[0, t, v];
                if (l > bestLogit)
                {
                    bestLogit = l;
                    bestId = v;
                }
            }
            topIdsByStep[t] = bestId;
        }
        Log.Info($"[diagnose] top10_argmax_ids=[{string.Join(",", topIdsByStep)}]");

        var globalText = CtcDecode(logits, globalVocab);
        var spaText = CtcDecode(logits, spaVocab);
        return (globalText, spaText);
    }

    private static string Truncate(string s, int max = 80) => s.Length <= max ? s : s.Substring(0, max) + "...";

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _session?.Dispose(); } catch { }
        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
