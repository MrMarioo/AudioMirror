using AudioMirror.Core;
using AudioMirror.Infrastructure;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioMirror.Audio;

/// <summary>
/// Captures the primary device with WASAPI loopback and fans the audio out to one
/// <see cref="OutputPipe"/> per target device.
/// </summary>
/// <remarks>
/// Threading: <see cref="Start"/> and <see cref="Stop"/> run on the UI thread, while
/// <see cref="OnDataAvailable"/> runs on NAudio's capture thread. <see cref="_sync"/>
/// guards the pipe array shared between them.
/// </remarks>
internal sealed class WasapiMirrorEngine : IMirrorEngine, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly object _sync = new();
    private OutputPipe[] _pipes = [];
    private WasapiLoopbackCapture? _capture;

    public bool IsRunning => _capture is not null;
    public int Session { get; private set; }

    public event Action<EngineFault>? Faulted;

    public void Start(MirrorRouting routing)
    {
        if (IsRunning) throw new InvalidOperationException("The mirror engine is already running.");

        int session = ++Session;
        var pipes = new List<OutputPipe>();
        try
        {
            using (var primary = _enumerator.GetDevice(routing.Primary.Id))
                _capture = new WasapiLoopbackCapture(primary);

            foreach (var target in routing.Targets)
            {
                var device = _enumerator.GetDevice(target.Id);
                OutputPipe pipe;
                try { pipe = new OutputPipe(device, _capture.WaveFormat); }
                catch
                {
                    device.Dispose();
                    throw;
                }
                pipe.Faulted += ex => RaiseFault(session, ex);
                pipes.Add(pipe);
            }

            _capture.DataAvailable += OnDataAvailable;
            _capture.RecordingStopped += (_, e) =>
            {
                if (e.Exception is not null) RaiseFault(session, e.Exception);
            };

            lock (_sync) _pipes = [.. pipes];
            foreach (var pipe in pipes) pipe.Start();
            _capture.StartRecording();
        }
        catch
        {
            lock (_sync) _pipes = [];
            DisposeCapture();
            foreach (var pipe in pipes) pipe.Dispose();
            throw;
        }
    }

    public void Stop()
    {
        OutputPipe[] pipes;
        lock (_sync)
        {
            pipes = _pipes;
            _pipes = [];
        }

        // Dispose the capture outside the lock: it waits for the capture thread,
        // which may itself be waiting on _sync inside OnDataAvailable.
        DisposeCapture();
        foreach (var pipe in pipes) pipe.Dispose();
    }

    public void Dispose()
    {
        Stop();
        _enumerator.Dispose();
    }

    // Runs on the capture thread.
    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        lock (_sync)
        {
            foreach (var pipe in _pipes)
                pipe.Write(e.Buffer, e.BytesRecorded);
        }
    }

    private void RaiseFault(int session, Exception error) => Faulted?.Invoke(new EngineFault(session, error));

    private void DisposeCapture()
    {
        if (_capture is null) return;

        _capture.DataAvailable -= OnDataAvailable;
        try { _capture.StopRecording(); }
        catch (Exception ex) { Log.Warn("StopRecording failed: " + ex.Message); }
        _capture.Dispose();
        _capture = null;
    }
}
