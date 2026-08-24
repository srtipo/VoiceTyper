using System.Threading;
using System.Threading.Tasks;
using VoiceTyper.Models;

namespace VoiceTyper.Services;

public interface ITranscriptionEngine
{
    TranscriptionEngine Engine { get; }

    string BackendMode { get; }

    Task<string> TranscribeAsync(byte[] wavBytes, CancellationToken ct = default);

    Task<bool> SmokeTestAsync();
}
