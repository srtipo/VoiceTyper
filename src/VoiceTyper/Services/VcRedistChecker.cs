using System;
using System.IO;

namespace VoiceTyper.Services;

public static class VcRedistChecker
{
    public static bool IsInstalled()
    {
        try
        {
            var system32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            var vcruntime = Path.Combine(system32, "vcruntime140.dll");
            var vcruntime1 = Path.Combine(system32, "vcruntime140_1.dll");

            var hasVcruntime = File.Exists(vcruntime);
            var hasVcruntime1 = File.Exists(vcruntime1);

            if (hasVcruntime && hasVcruntime1)
            {
                Log.Info("[VcRedist] vcruntime140.dll and vcruntime140_1.dll found in System32");
                return true;
            }

            Log.Warn($"[VcRedist] missing in System32: vcruntime140={hasVcruntime}, vcruntime140_1={hasVcruntime1}");
            return false;
        }
        catch (Exception ex)
        {
            Log.Error($"[VcRedist] check failed: {ex.Message}");
            return false;
        }
    }

    public static string GetDownloadUrl() => "https://aka.ms/vc14/vc_redist.x64.exe";
}
