using System;
using System.IO;
using System.Linq;

namespace VoiceTyper.Services;

public static class NativeLibPathResolver
{
    public static void EnsureWhisperRuntimeOnPath()
    {
        try
        {
            var sourceDir = ResolveWhisperNativeDir();
            if (sourceDir is null)
            {
                Log.Warn("[NativeLib] could not locate whisper native dir in single-file extraction path");
                return;
            }

            var targetDir = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64");
            Directory.CreateDirectory(targetDir);

            var copied = 0;
            foreach (var src in Directory.EnumerateFiles(sourceDir, "*.dll"))
            {
                var dst = Path.Combine(targetDir, Path.GetFileName(src));
                File.Copy(src, dst, overwrite: true);
                copied++;
            }
            Log.Info($"[NativeLib] copied {copied} native dll(s) from {sourceDir} -> {targetDir}");
        }
        catch (Exception ex)
        {
            Log.Error($"[NativeLib] failed to copy whisper native dir: {ex.Message}");
        }
    }

    private static string? ResolveWhisperNativeDir()
    {
        var temp = Path.GetTempPath();
        var extractionRoot = Path.Combine(temp, ".net", "VoiceTyper");
        if (!Directory.Exists(extractionRoot)) return null;

        var candidates = Directory.EnumerateDirectories(extractionRoot)
            .Select(dir => new
            {
                Dir = dir,
                Native = Path.Combine(dir, "runtimes", "win-x64", "whisper.dll")
            })
            .Where(x => File.Exists(x.Native))
            .Select(x => new
            {
                x.Dir,
                x.Native,
                LastWrite = File.GetLastWriteTimeUtc(x.Native)
            })
            .OrderByDescending(x => x.LastWrite)
            .ToList();

        if (candidates.Count == 0) return null;

        var best = candidates[0];
        return Path.GetDirectoryName(best.Native);
    }
}
