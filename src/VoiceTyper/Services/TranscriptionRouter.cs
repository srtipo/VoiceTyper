using System.Threading;
using System.Threading.Tasks;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public sealed class TranscriptionRouter : ITranscriptionEngine
{
    private readonly SettingsService _settings;
    private readonly TranscriberService _whisper;
    private readonly Wav2Vec2Engine _wav2vec2;

    public TranscriptionRouter(
        SettingsService settings,
        TranscriberService whisper,
        Wav2Vec2Engine wav2vec2)
    {
        _settings = settings;
        _whisper = whisper;
        _wav2vec2 = wav2vec2;
    }

    public TranscriptionEngine Engine => _settings.Current.Engine;

    public string BackendMode => Active.BackendMode;

    public ITranscriptionEngine Active => _settings.Current.Engine switch
    {
        TranscriptionEngine.Wav2Vec2 => _wav2vec2,
        _ => _whisper
    };

    public Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken ct = default)
        => Active.TranscribeAsync(wavBytes, ct);

    public Task<bool> SmokeTestAsync()
        => Active.SmokeTestAsync();
}
