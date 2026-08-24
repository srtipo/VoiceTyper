using System.Text.Json.Serialization;

namespace VoiceTyper.Models;

public sealed class AppSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public TranscriptionEngine Engine { get; set; } = TranscriptionEngine.Whisper;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public WhisperModel Model { get; set; } = WhisperModel.Small;

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Wav2Vec2Model Wav2Vec2Model { get; set; } = Wav2Vec2Model.SpanishXlsr53;

    public string Language { get; set; } = "es";
    public string HotkeyModifier { get; set; } = "RMenu";
    public string HotkeyTrigger { get; set; } = "Space";
    public bool AutoStart { get; set; } = false;
    public bool PauseOnFullscreen { get; set; } = true;
    public int MicrophoneDeviceIndex { get; set; } = -1;
    public bool RestoreClipboard { get; set; } = true;
    public bool GpuEnabled { get; set; } = false;
    public int GpuDeviceIndex { get; set; } = 0;
    public bool GpuSuggestionShown { get; set; } = false;
}
