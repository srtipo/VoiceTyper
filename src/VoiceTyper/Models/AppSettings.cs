using System.Text.Json.Serialization;

namespace VoiceTyper.Models;

public sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WhisperModel Model { get; set; } = WhisperModel.Small;

    public string Language { get; set; } = "es";
    public string HotkeyModifier { get; set; } = "RMenu";
    public string HotkeyTrigger { get; set; } = "Space";
    public bool AutoStart { get; set; } = false;
    public bool PauseOnFullscreen { get; set; } = true;
    public int MicrophoneDeviceIndex { get; set; } = -1;
    public bool RestoreClipboard { get; set; } = true;
}
