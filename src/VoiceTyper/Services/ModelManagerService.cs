using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
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
        await using var dst = new FileStream(tmpPath, FileMode.Append, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);

        var buffer = new byte[BufferSize];
        long downloaded = existingLength;
        long lastReportedPercent = -1;
        long lastProgressBytes = existingLength;
        long lastLoggedPercent = -1;
        int bytesRead;

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

        File.Move(tmpPath, destPath, overwrite: true);
        Log.Info($"[ModelManager] downloaded {m} -> {destPath} ({downloaded} bytes)");
        return destPath;
    }
}
