namespace VoiceTyper.Models;

public static class WhisperModelExtensions
{
    public static string GetFileName(this WhisperModel m) => $"ggml-{m switch
    {
        WhisperModel.Tiny => "tiny",
        WhisperModel.Base => "base",
        WhisperModel.Small => "small",
        WhisperModel.Medium => "medium",
        _ => "small"
    }}.bin";

    public static string GetDownloadUrl(this WhisperModel m) =>
        $"https://huggingface.co/ggerganov/whisper.cpp/resolve/main/{m.GetFileName()}";

    public static long GetApproxSizeBytes(this WhisperModel m) => m switch
    {
        WhisperModel.Tiny => 75_000_000L,
        WhisperModel.Base => 142_000_000L,
        WhisperModel.Small => 466_000_000L,
        WhisperModel.Medium => 1_500_000_000L,
        _ => 466_000_000L
    };
}
