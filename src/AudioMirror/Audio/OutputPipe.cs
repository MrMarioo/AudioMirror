using AudioMirror.Infrastructure;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioMirror.Audio;

/// <summary>
/// One mirror target: a buffer fed by the capture thread, an optional resampler
/// and a WASAPI output that plays it.
/// </summary>
internal sealed class OutputPipe : IDisposable
{
    private readonly MMDevice _device;
    private readonly string _name; // cached: COM properties shouldn't be read from the audio thread
    private readonly BufferedWaveProvider _buffer;
    private readonly LatencyGuard _latency;
    private readonly WasapiOut _out;
    private readonly MediaFoundationResampler? _resampler;
    private byte[] _discard = [];

    /// <summary>Playback failed, e.g. the device was unplugged. May be raised on any thread.</summary>
    public event Action<Exception>? Faulted;

    /// <param name="device">Ownership passes to the pipe only if the constructor succeeds.</param>
    public OutputPipe(MMDevice device, WaveFormat captureFormat)
    {
        _device = device;
        _name = device.FriendlyName;
        _buffer = new BufferedWaveProvider(captureFormat)
        {
            DiscardOnBufferOverflow = true,
            BufferDuration = TimeSpan.FromSeconds(1),
        };
        _latency = new LatencyGuard(captureFormat.AverageBytesPerSecond, captureFormat.BlockAlign,
            LatencyGuard.DefaultTarget, LatencyGuard.DefaultMax);

        _out = new WasapiOut(device, AudioClientShareMode.Shared, useEventSync: true, latency: 100);
        try
        {
            _out.PlaybackStopped += (_, e) =>
            {
                if (e.Exception is not null) Faulted?.Invoke(e.Exception);
            };

            // MMDevice.AudioClient activates a new COM client on every access, so dispose it.
            WaveFormat mixFormat;
            using (var client = device.AudioClient) mixFormat = client.MixFormat;

            if (captureFormat.Equals(mixFormat))
            {
                _out.Init(_buffer);
            }
            else
            {
                _resampler = new MediaFoundationResampler(_buffer, mixFormat) { ResamplerQuality = 60 };
                _out.Init(_resampler);
            }
        }
        catch
        {
            _resampler?.Dispose();
            _out.Dispose();
            throw;
        }
    }

    public void Start() => _out.Play();

    /// <summary>Queues captured audio. Called on the capture thread.</summary>
    public void Write(byte[] data, int count)
    {
        _buffer.AddSamples(data, 0, count);

        int drop = _latency.BytesToDrop(_buffer.BufferedBytes);
        if (drop == 0) return;

        // BufferedWaveProvider has no "skip", so read the oldest bytes into a scratch array.
        if (_discard.Length < drop) _discard = new byte[drop];
        _buffer.Read(_discard, 0, drop);
        Log.Info($"{_name}: drift compensation dropped {drop * 1000L / _buffer.WaveFormat.AverageBytesPerSecond} ms");
    }

    public void Dispose()
    {
        try { _out.Stop(); }
        catch (Exception ex) { Log.Warn($"{_name}: WasapiOut.Stop failed: {ex.Message}"); }
        _out.Dispose();
        _resampler?.Dispose();
        _device.Dispose();
    }
}
