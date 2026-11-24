using AudioMirror.Core;
using AudioMirror.Settings;

namespace AudioMirror.Tests;

internal sealed class FakeAudioDevices : IAudioDevices
{
    public AudioDevice? Default { get; set; }
    public List<AudioDevice> Active { get; } = [];

    public AudioDevice? GetDefaultOutput() => Default;
    public IReadOnlyList<AudioDevice> GetActiveOutputs() => [.. Active];

    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
}

internal sealed class FakeMirrorEngine : IMirrorEngine
{
    public List<MirrorRouting> Started { get; } = [];
    public MirrorRouting? Current { get; private set; }
    public Exception? FailNextStart { get; set; }

    public bool IsRunning => Current is not null;
    public int Session { get; private set; }

    public event Action<EngineFault>? Faulted;

    public void Start(MirrorRouting routing)
    {
        if (IsRunning) throw new InvalidOperationException("Already running.");
        Session++;
        if (FailNextStart is { } error)
        {
            FailNextStart = null;
            throw error;
        }
        Current = routing;
        Started.Add(routing);
    }

    public void Stop() => Current = null;

    public void RaiseFault(int session) => Faulted?.Invoke(new EngineFault(session, new IOException("device gone")));
}

internal sealed class InMemorySettingsStore(AppSettings? initial = null) : ISettingsStore
{
    public AppSettings Saved { get; private set; } = initial ?? AppSettings.Default;
    public AppSettings Load() => Saved;
    public void Save(AppSettings settings) => Saved = settings;
}

internal sealed class RecordingNotifier : IUserNotifier
{
    public List<(string Level, string Message)> Messages { get; } = [];
    public void Info(string message) => Messages.Add(("info", message));
    public void Warn(string message) => Messages.Add(("warn", message));
    public void Error(string message) => Messages.Add(("error", message));
}
