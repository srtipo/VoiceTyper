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

            var cudaCopied = CopyGpuRuntimes(sourceDir, AppContext.BaseDirectory);

            Log.Info($"[NativeLib] copied {copied} native dll(s) from {sourceDir} -> {targetDir}");
            if (cudaCopied > 0)
            {
                Log.Info($"[NativeLib] copied {cudaCopied} GPU runtime dll(s) to <BaseDir>\\runtimes\\<backend>\\win-x64");
            }
            else
            {
                Log.Info("[NativeLib] no GPU runtime dlls found, CPU-only build");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"[NativeLib] failed to copy whisper native dir: {ex.Message}");
        }
    }

    private static int CopyGpuRuntimes(string sourceDir, string baseDir)
    {
        var runtimesRoot = Directory.GetParent(sourceDir);
        if (runtimesRoot is null) return 0;

        var copied = 0;
        foreach (var sub in Directory.EnumerateDirectories(runtimesRoot.FullName))
        {
            var leaf = Path.GetFileName(sub);
            if (string.Equals(leaf, "win-x64", StringComparison.OrdinalIgnoreCase)) continue;

            foreach (var platformDir in Directory.EnumerateDirectories(sub))
            {
                var platformLeaf = Path.GetFileName(platformDir);
                if (!string.Equals(platformLeaf, "win-x64", StringComparison.OrdinalIgnoreCase)) continue;

                var targetSubdir = Path.Combine(baseDir, "runtimes", leaf, "win-x64");
                Directory.CreateDirectory(targetSubdir);

                foreach (var src in Directory.EnumerateFiles(platformDir, "*.dll"))
                {
                    var dst = Path.Combine(targetSubdir, Path.GetFileName(src));
                    try
                    {
                        File.Copy(src, dst, overwrite: true);
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        Log.Warn($"[NativeLib] failed to copy {src}: {ex.Message}");
                    }
                }
            }
        }
        return copied;
    }

    private static string? ResolveWhisperNativeDir()
    {
        var temp = Path.GetTempPath();
        var extractionRoot = Path.Combine(temp, ".net", "VoiceTyper");
        if (!Directory.Exists(extractionRoot)) return null;

        var candidates = Directory.EnumerateDirectories(extractionRoot)
            .Select(dir => Path.Combine(dir, "runtimes", "win-x64", "whisper.dll"))
            .Where(File.Exists)
            .Select(p => new { Native = p, LastWrite = File.GetLastWriteTimeUtc(p) })
            .OrderByDescending(x => x.LastWrite)
            .ToList();

        if (candidates.Count == 0) return null;

        var best = candidates[0];
        return Path.GetDirectoryName(best.Native);
    }
}
