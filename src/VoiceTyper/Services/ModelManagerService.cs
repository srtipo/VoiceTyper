using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public sealed class ModelManagerService
{
    private const int BufferSize = 8192;
    private const long ProgressEveryBytes = 5L * 1024 * 1024;

    private readonly HttpClient _http;

    public string ModelDir { get; }

    public ModelManagerService(HttpClient http)
    {
        _http = http;

        var configured = Env.Get("VT_MODEL_DIR");
        ModelDir = !string.IsNullOrWhiteSpace(configured)
            ? configured
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VoiceTyper",
                "models");

        Directory.CreateDirectory(ModelDir);
        Log.Info($"[ModelManager] model dir: {ModelDir}");
    }

    public string GetModelPath(WhisperModel m) => Path.Combine(ModelDir, m.GetFileName());

    public bool IsModelAvailable(WhisperModel m) => File.Exists(GetModelPath(m));

    public string GetWav2Vec2ModelDir(Wav2Vec2Model m) => Path.Combine(ModelDir, "wav2vec2", m.ToString());

    public bool IsWav2Vec2ModelAvailable(Wav2Vec2Model m)
    {
        var dir = GetWav2Vec2ModelDir(m);
        return File.Exists(Path.Combine(dir, "model.onnx"))
            && File.Exists(Path.Combine(dir, "vocab.json"));
    }

    public bool IsActiveModelAvailable(VoiceTyper.Models.TranscriptionEngine engine, VoiceTyper.Models.WhisperModel wmodel, VoiceTyper.Models.Wav2Vec2Model w2model)
        => engine switch
        {
            VoiceTyper.Models.TranscriptionEngine.Wav2Vec2 => IsWav2Vec2ModelAvailable(w2model),
            _ => IsModelAvailable(wmodel)
        };

    public Task<string> EnsureActiveModelAsync(
        VoiceTyper.Models.TranscriptionEngine engine,
        VoiceTyper.Models.WhisperModel wmodel,
        VoiceTyper.Models.Wav2Vec2Model w2model,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        return engine switch
        {
            VoiceTyper.Models.TranscriptionEngine.Wav2Vec2 => EnsureWav2Vec2ModelAsync(w2model, progress, ct),
            _ => EnsureModelAsync(wmodel, progress, ct)
        };
    }

    public async Task<string> EnsureModelAsync(WhisperModel m, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var destPath = GetModelPath(m);
        if (File.Exists(destPath))
        {
            return destPath;
        }

        var url = m.GetDownloadUrl();
        var tmpPath = destPath + ".tmp";
        var existingLength = File.Exists(tmpPath) ? new FileInfo(tmpPath).Length : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
            Log.Info($"[ModelManager] resuming {m} from byte {existingLength}");
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (existingLength > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            Log.Warn($"[ModelManager] server returned {response.StatusCode}, no resume support, restarting from 0");
            try { File.Delete(tmpPath); } catch { }
            existingLength = 0;
        }

        response.EnsureSuccessStatusCode();

        long? totalBytes = response.Content.Headers.ContentLength;
        if (response.StatusCode == System.Net.HttpStatusCode.PartialContent
            && totalBytes.HasValue
            && existingLength > 0)
        {
            totalBytes += existingLength;
        }

        await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

        var buffer = new byte[BufferSize];
        long downloaded = existingLength;
        long lastReportedPercent = -1;
        long lastProgressBytes = existingLength;
        long lastLoggedPercent = -1;
        int bytesRead;

        await using (var dst = new FileStream(tmpPath, FileMode.Append, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            while ((bytesRead = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                downloaded += bytesRead;

                if (progress is not null)
                {
                    bool shouldReport = false;
                    if (totalBytes.HasValue && totalBytes.Value > 0)
                    {
                        var percent = (int)(downloaded * 100L / totalBytes.Value);
                        if (percent != lastReportedPercent)
                        {
                            lastReportedPercent = percent;
                            shouldReport = true;
                        }
                    }
                    else if (downloaded - lastProgressBytes >= ProgressEveryBytes)
                    {
                        lastProgressBytes = downloaded;
                        shouldReport = true;
                    }

                    if (shouldReport)
                    {
                        progress.Report(totalBytes.HasValue ? (double)downloaded / totalBytes.Value * 100.0 : downloaded);
                    }
                }

                if (totalBytes.HasValue && totalBytes.Value > 0)
                {
                    var percent = (int)(downloaded * 100L / totalBytes.Value);
                    if (percent / 10 > lastLoggedPercent / 10)
                    {
                        lastLoggedPercent = percent;
                        Log.Info($"[ModelManager] download {percent}% ({downloaded}/{totalBytes} bytes)");
                    }
                }
            }
        }

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                File.Move(tmpPath, destPath, overwrite: true);
                break;
            }
            catch (IOException) when (attempt < 5)
            {
                Log.Warn($"[ModelManager] move retry {attempt}/5 (file in use)");
                Thread.Sleep(500);
            }
        }
        Log.Info($"[ModelManager] downloaded {m} -> {destPath} ({downloaded} bytes)");
        return destPath;
    }

    public async Task<string> EnsureWav2Vec2ModelAsync(
        Wav2Vec2Model model,
        IProgress<double>? progress = null,
        CancellationToken ct = default)
    {
        var dir = GetWav2Vec2ModelDir(model);
        Directory.CreateDirectory(dir);

        var files = new (string Name, string Url, long Size)[]
        {
            ("model.onnx", "https://huggingface.co/srtipo/wav2vec2-spanish-onnx/resolve/main/model.onnx", 342_433_945L),
            ("vocab.json", "https://huggingface.co/srtipo/wav2vec2-spanish-onnx/resolve/main/vocab.json", 508L),
        };

        long totalSize = files.Sum(f => f.Size);
        long downloaded = 0;

        foreach (var f in files)
        {
            var destPath = Path.Combine(dir, f.Name);
            if (File.Exists(destPath))
            {
                var existing = new FileInfo(destPath).Length;
                if (existing == f.Size)
                {
                    Log.Info($"[ModelManager] Wav2Vec2 {f.Name} already present, skipping");
                    downloaded += f.Size;
                    progress?.Report((double)downloaded / totalSize * 100.0);
                    continue;
                }
                Log.Warn($"[ModelManager] Wav2Vec2 {f.Name} size mismatch (existing={existing} expected={f.Size}), re-downloading");
                try { File.Delete(destPath); } catch { }
            }

            await DownloadFileAsync(f.Url, destPath, f.Size, downloaded, totalSize, progress, ct).ConfigureAwait(false);
            downloaded += f.Size;
            progress?.Report((double)downloaded / totalSize * 100.0);
        }

        Log.Info($"[ModelManager] Wav2Vec2 {model} ready in {dir}");
        return dir;
    }

    private async Task DownloadFileAsync(
        string url,
        string destPath,
        long fileSize,
        long alreadyDownloaded,
        long totalSize,
        IProgress<double>? progress,
        CancellationToken ct)
    {
        var tmpPath = destPath + ".tmp";
        var existingLength = File.Exists(tmpPath) ? new FileInfo(tmpPath).Length : 0L;

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (existingLength > 0)
        {
            request.Headers.Range = new RangeHeaderValue(existingLength, null);
        }

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);

        if (existingLength > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
        {
            Log.Warn($"[ModelManager] server returned {response.StatusCode}, no resume support, restarting");
            try { File.Delete(tmpPath); } catch { }
            existingLength = 0;
        }

        response.EnsureSuccessStatusCode();

        await using var src = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var buffer = new byte[BufferSize];
        long downloaded = existingLength;
        long lastReported = -1;
        long lastLogged = -1;

        await using (var dst = new FileStream(tmpPath, FileMode.Append, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            int bytesRead;
            while ((bytesRead = await src.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, bytesRead), ct).ConfigureAwait(false);
                downloaded += bytesRead;

                if (progress is not null)
                {
                    var totalDownloaded = alreadyDownloaded + downloaded;
                    var pct = totalDownloaded * 100L / totalSize;
                    if (pct != lastReported)
                    {
                        lastReported = pct;
                        progress.Report((double)totalDownloaded / totalSize * 100.0);
                    }
                }

                if (fileSize > 0)
                {
                    var pct = downloaded * 100L / fileSize;
                    if (pct / 10 > lastLogged / 10)
                    {
                        lastLogged = pct;
                        Log.Info($"[ModelManager] Wav2Vec2 {Path.GetFileName(destPath)} {pct}% ({downloaded}/{fileSize} bytes)");
                    }
                }
            }
        }

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                File.Move(tmpPath, destPath, overwrite: true);
                break;
            }
            catch (IOException) when (attempt < 5)
            {
                Log.Warn($"[ModelManager] move retry {attempt}/5 (file in use)");
                Thread.Sleep(500);
            }
        }
    }
}
