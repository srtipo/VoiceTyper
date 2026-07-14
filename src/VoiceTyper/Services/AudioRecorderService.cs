using System.IO;
using NAudio.Wave;

namespace VoiceTyper.Services;

public sealed class AudioRecorderService : IDisposable
{
    private const int MaxDurationMs = 5 * 60 * 1000;

    private readonly object _lock = new();
    private WaveInEvent? _waveIn;
    private WaveFileWriter? _writer;
    private MemoryStream? _buffer;
    private TaskCompletionSource<bool>? _stopTcs;
    private CancellationTokenSource? _autoStopCts;
    private long _totalBytes;
    private bool _firstChunkLogged;
    private bool _disposed;

    public int DeviceNumber { get; set; } = -1;
    public bool IsRecording { get; private set; }

#pragma warning disable CS0067
    public event Action<byte[]>? AudioCaptured;
    public event Action<Exception?>? RecordingFailed;
#pragma warning restore CS0067

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(AudioRecorderService));
        if (IsRecording) return;

        ValidateMicOrThrow();

        _buffer = new MemoryStream();
        _writer = new WaveFileWriter(_buffer, new WaveFormat(16000, 16, 1));
        _totalBytes = 0;
        _firstChunkLogged = false;
        _stopTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        _waveIn = new WaveInEvent
        {
            DeviceNumber = DeviceNumber,
            WaveFormat = new WaveFormat(16000, 16, 1)
        };
        _waveIn.DataAvailable += OnDataAvailable;
        _waveIn.RecordingStopped += OnRecordingStopped;

        try
        {
            _waveIn.StartRecording();
        }
        catch (Exception ex)
        {
            CleanupAfterStop();
            Log.Error($"[Audio] start failed: {ex.Message}");
            RecordingFailed?.Invoke(ex);
            throw;
        }

        IsRecording = true;
        _autoStopCts = new CancellationTokenSource();
        _autoStopCts.Token.Register(() =>
        {
            if (IsRecording)
            {
                Log.Warn("[Audio] auto-stop reached (max duration)");
                _ = StopAsync();
            }
        });
        _autoStopCts.CancelAfter(MaxDurationMs);

        Log.Info($"[Audio] recording started (device={DeviceNumber})");
    }

    public async Task<byte[]> StopAsync()
    {
        if (!IsRecording)
        {
            return Array.Empty<byte>();
        }

        var tcs = _stopTcs!;

        try
        {
            _waveIn?.StopRecording();
        }
        catch (Exception ex)
        {
            Log.Error($"[Audio] StopRecording threw: {ex.Message}");
            IsRecording = false;
            CleanupAfterStop();
            RecordingFailed?.Invoke(ex);
            return Array.Empty<byte>();
        }

        await tcs.Task.ConfigureAwait(false);

        byte[] wav;
        try
        {
            _writer?.Flush();
        }
        catch (Exception ex)
        {
            Log.Error($"[Audio] flush failed: {ex.Message}");
            CleanupAfterStop();
            RecordingFailed?.Invoke(ex);
            return Array.Empty<byte>();
        }

        try
        {
            wav = _buffer?.ToArray() ?? Array.Empty<byte>();
        }
        catch (Exception ex)
        {
            Log.Error($"[Audio] buffer read failed: {ex.Message}");
            CleanupAfterStop();
            RecordingFailed?.Invoke(ex);
            return Array.Empty<byte>();
        }

        var headerValid = wav.Length >= 12
            && wav[0] == (byte)'R' && wav[1] == (byte)'I'
            && wav[2] == (byte)'F' && wav[3] == (byte)'F';
        Log.Info($"[Audio] recording stopped, {wav.Length} bytes captured, headerValid={headerValid}");

        IsRecording = false;
        CleanupAfterStop();
        return wav;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
        }

        try { _waveIn?.Dispose(); } catch { }
        try { _writer?.Dispose(); } catch { }
        try { _buffer?.Dispose(); } catch { }
        try { _autoStopCts?.Dispose(); } catch { }
        _waveIn = null;
        _writer = null;
        _buffer = null;
        _autoStopCts = null;

        GC.SuppressFinalize(this);
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (e.BytesRecorded <= 0) return;

        lock (_lock)
        {
            try
            {
                _writer?.Write(e.Buffer, 0, e.BytesRecorded);
                _totalBytes += e.BytesRecorded;
                if (!_firstChunkLogged)
                {
                    _firstChunkLogged = true;
                    Log.Info($"[Audio] first chunk received, {e.BytesRecorded} bytes");
                }
            }
            catch (Exception ex)
            {
                Log.Error($"[Audio] write chunk failed: {ex.Message}");
            }
        }
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        var tcs = _stopTcs;
        try
        {
            if (_waveIn is not null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
            }
        }
        catch
        {
        }

        if (e.Exception is not null)
        {
            Log.Error($"[Audio] recording stopped with error: {e.Exception.Message}");
            tcs?.TrySetResult(true);
            RecordingFailed?.Invoke(e.Exception);
            return;
        }

        Log.Info($"[Audio] RecordingStopped fired, totalBytes={_totalBytes}");
        tcs?.TrySetResult(true);
    }

    private void CleanupAfterStop()
    {
        try { _writer?.Dispose(); } catch { }
        try { _buffer?.Dispose(); } catch { }
        try { _autoStopCts?.Dispose(); } catch { }
        try
        {
            if (_waveIn is not null)
            {
                _waveIn.DataAvailable -= OnDataAvailable;
                _waveIn.RecordingStopped -= OnRecordingStopped;
                _waveIn.Dispose();
            }
        }
        catch
        {
        }
        _writer = null;
        _buffer = null;
        _autoStopCts = null;
        _waveIn = null;
        _stopTcs = null;
    }

    private static void ValidateMicOrThrow()
    {
        if (WaveInEvent.DeviceCount == 0)
        {
            throw new InvalidOperationException("No hay dispositivos de captura de audio disponibles");
        }
    }
}
