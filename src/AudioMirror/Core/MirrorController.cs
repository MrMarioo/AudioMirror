using AudioMirror.Infrastructure;
using AudioMirror.Resources;
using AudioMirror.Settings;

namespace AudioMirror.Core;

internal enum MirrorState
{
    /// <summary>The user hasn't turned mirroring on.</summary>
    Off,

    /// <summary>Turned on, but no selected device is available right now; resumes automatically.</summary>
    Waiting,

    /// <summary>Audio is being mirrored.</summary>
    Running,
}

/// <summary>
/// Application logic: what the user wants (enabled, selected devices) versus what is
/// possible right now (available devices), kept in sync with the audio engine.
/// </summary>
/// <remarks>
/// Single-threaded: every public member must be called on the UI thread.
/// Depends only on abstractions, so it is unit-tested without audio hardware.
/// </remarks>
internal sealed class MirrorController
{
    private readonly IAudioDevices _devices;
    private readonly IMirrorEngine _engine;
    private readonly ISettingsStore _settings;
    private readonly IUserNotifier _notifier;

    private readonly HashSet<string> _selectedIds;
    private readonly bool _resumeOnStartup;
    private bool _enabled;
    private MirrorRouting? _routing;

    public MirrorController(IAudioDevices devices, IMirrorEngine engine, ISettingsStore settings, IUserNotifier notifier)
    {
        _devices = devices;
        _engine = engine;
        _settings = settings;
        _notifier = notifier;

        var saved = settings.Load();
        // Saved IDs are kept even when the device is unplugged, so it's picked up again later.
        _selectedIds = new HashSet<string>(saved.SelectedDeviceIds, StringComparer.Ordinal);
        _resumeOnStartup = saved.MirrorEnabled;
    }

    public event Action? StateChanged;

    public MirrorState State => _engine.IsRunning ? MirrorState.Running
        : _enabled ? MirrorState.Waiting
        : MirrorState.Off;

    /// <summary>Devices currently receiving the mirrored audio (empty unless running).</summary>
    public IReadOnlyList<AudioDevice> ActiveTargets => _routing?.Targets ?? [];

    public bool IsSelected(string deviceId) => _selectedIds.Contains(deviceId);

    /// <summary>Restores the state from the previous session.</summary>
    public void Initialize()
    {
        if (!_resumeOnStartup) return;
        _enabled = true;
        TryStart(quiet: true);
        OnStateChanged();
    }

    public void ToggleMirroring()
    {
        if (_enabled) Disable();
        else Enable();
    }

    public void Enable()
    {
        if (_enabled) return;
        _enabled = TryStart(quiet: false);
        SaveSettings();
        OnStateChanged();
    }

    public void Disable()
    {
        _enabled = false;
        StopPipeline();
        SaveSettings();
        OnStateChanged();
    }

    public void ToggleDevice(string deviceId)
    {
        if (!_selectedIds.Remove(deviceId)) _selectedIds.Add(deviceId);

        if (_enabled)
        {
            StopPipeline();
            _enabled = TryStart(quiet: false);
        }

        SaveSettings();
        OnStateChanged();
    }

    /// <summary>Call after <see cref="IAudioDevices.Changed"/>, debounced.</summary>
    public void OnDevicesChanged()
    {
        if (!_enabled) return;

        // Notifications also cover microphones and unrelated devices. Restarting causes an
        // audible gap, so only rebuild when our routing actually changes.
        MirrorRouting? wanted;
        try { wanted = ResolveRouting(); }
        catch (Exception ex)
        {
            Log.Error("Could not enumerate devices", ex);
            return;
        }
        if (_engine.IsRunning && wanted == _routing) return;

        Log.Info("Audio routing changed, rebuilding mirror");
        StopPipeline();
        TryStart(quiet: true);
        OnStateChanged();
    }

    public void OnEngineFault(EngineFault fault)
    {
        // A fault queued by a pipeline that has since been replaced must not stop the new one.
        if (!_engine.IsRunning || fault.Session != _engine.Session) return;

        Log.Error("Audio device failed, stopping pipeline", fault.Error);
        // Stay enabled: the device-change notification that usually follows (unplug, driver
        // reset) rebuilds the mirror with whatever is still available. Never retry here
        // directly, or a broken device would cause a restart loop.
        StopPipeline();
        _notifier.Warn(Strings.DeviceFailed);
        OnStateChanged();
    }

    private MirrorRouting? ResolveRouting()
    {
        var primary = _devices.GetDefaultOutput();
        return primary is null
            ? null
            : MirrorRouting.Resolve(primary, _devices.GetActiveOutputs(), _selectedIds);
    }

    /// <summary>Builds and starts the audio pipeline.</summary>
    /// <param name="quiet">Automatic (re)start: no success message, and a missing device
    /// is not an error, because mirroring resumes when it comes back.</param>
    /// <returns>Whether mirroring should stay enabled.</returns>
    private bool TryStart(bool quiet)
    {
        try
        {
            var routing = ResolveRouting();
            if (routing is null)
            {
                if (quiet) return true;
                _notifier.Error(Strings.NoDefaultDevice);
                return false;
            }

            if (routing.Targets.Count == 0)
            {
                Log.Info("No selected device available");
                if (quiet) return true;
                _notifier.Warn(Strings.NoTargets);
                return false;
            }

            _engine.Start(routing);
            _routing = routing;

            var names = string.Join(", ", routing.Targets.Select(t => t.Name));
            Log.Info($"Mirroring {routing.Primary.Name} -> {names}");
            if (!quiet) _notifier.Info(Strings.Started(names));
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("Start failed", ex);
            StopPipeline();
            if (quiet) return true; // keep waiting; the next device change retries
            _notifier.Error(ex.Message);
            return false;
        }
    }

    private void StopPipeline()
    {
        _engine.Stop();
        _routing = null;
    }

    private void SaveSettings()
        => _settings.Save(new AppSettings(
            _selectedIds.Order(StringComparer.Ordinal).ToList(),
            _enabled));

    private void OnStateChanged() => StateChanged?.Invoke();
}
