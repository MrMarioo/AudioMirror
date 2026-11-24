using AudioMirror.Core;
using AudioMirror.Settings;

namespace AudioMirror.Tests;

public class MirrorControllerTests
{
    private static readonly AudioDevice Speakers = new("speakers", "Speakers");
    private static readonly AudioDevice Hdmi = new("hdmi", "HDMI Monitor");
    private static readonly AudioDevice Headset = new("headset", "Headset");

    private readonly FakeAudioDevices _devices = new() { Default = Speakers, Active = { Speakers, Hdmi, Headset } };
    private readonly FakeMirrorEngine _engine = new();
    private readonly RecordingNotifier _notifier = new();
    private InMemorySettingsStore _settings = new();

    private MirrorController CreateController()
        => new(_devices, _engine, _settings, _notifier);

    private MirrorController CreateRunningController(params string[] selected)
    {
        var controller = CreateController();
        foreach (var id in selected) controller.ToggleDevice(id);
        controller.Enable();
        Assert.Equal(MirrorState.Running, controller.State);
        _notifier.Messages.Clear();
        return controller;
    }

    [Fact]
    public void Enable_without_selected_devices_warns_and_stays_off()
    {
        var controller = CreateController();

        controller.Enable();

        Assert.Equal(MirrorState.Off, controller.State);
        Assert.Empty(_engine.Started);
        Assert.Equal("warn", Assert.Single(_notifier.Messages).Level);
    }

    [Fact]
    public void Enable_mirrors_default_device_to_selected_devices()
    {
        var controller = CreateController();
        controller.ToggleDevice("hdmi");

        controller.Enable();

        Assert.Equal(MirrorState.Running, controller.State);
        var routing = Assert.Single(_engine.Started);
        Assert.Equal(Speakers, routing.Primary);
        Assert.Equal([Hdmi], routing.Targets);
        Assert.Equal("info", Assert.Single(_notifier.Messages).Level);
    }

    [Fact]
    public void Engine_failure_on_enable_reports_error_and_stays_off()
    {
        var controller = CreateController();
        controller.ToggleDevice("hdmi");
        _engine.FailNextStart = new InvalidOperationException("exclusive mode");

        controller.Enable();

        Assert.Equal(MirrorState.Off, controller.State);
        Assert.Equal(("error", "exclusive mode"), Assert.Single(_notifier.Messages));
    }

    [Fact]
    public void Toggling_a_device_while_running_rebuilds_the_mirror()
    {
        var controller = CreateRunningController("hdmi");

        controller.ToggleDevice("headset");

        Assert.Equal([Hdmi, Headset], _engine.Current!.Targets); // sorted by ID
    }

    [Fact]
    public void Unrelated_device_change_does_not_restart_audio()
    {
        var controller = CreateRunningController("hdmi");
        _devices.Active.Add(new AudioDevice("usb", "USB Speaker")); // not selected

        controller.OnDevicesChanged();

        Assert.Single(_engine.Started);
    }

    [Fact]
    public void Default_device_change_moves_capture_to_new_device_quietly()
    {
        var controller = CreateRunningController("hdmi");
        _devices.Default = Headset;

        controller.OnDevicesChanged();

        Assert.Equal(Headset, _engine.Current!.Primary);
        Assert.Empty(_notifier.Messages);
    }

    [Fact]
    public void Unplugged_device_waits_and_resumes_when_it_returns()
    {
        var controller = CreateRunningController("hdmi");

        _devices.Active.Remove(Hdmi);
        controller.OnDevicesChanged();
        Assert.Equal(MirrorState.Waiting, controller.State);

        _devices.Active.Add(Hdmi);
        controller.OnDevicesChanged();
        Assert.Equal(MirrorState.Running, controller.State);
        Assert.Empty(_notifier.Messages);
    }

    [Fact]
    public void Engine_fault_stops_audio_but_keeps_mirroring_enabled()
    {
        var controller = CreateRunningController("hdmi");

        controller.OnEngineFault(new EngineFault(_engine.Session, new IOException()));

        Assert.Equal(MirrorState.Waiting, controller.State);
        Assert.Equal("warn", Assert.Single(_notifier.Messages).Level);
        // No immediate retry: that is what would turn a broken device into a restart loop.
        Assert.Single(_engine.Started);
    }

    [Fact]
    public void Fault_from_a_replaced_pipeline_is_ignored()
    {
        var controller = CreateRunningController("hdmi");
        int oldSession = _engine.Session;
        controller.ToggleDevice("headset"); // restarts the engine

        controller.OnEngineFault(new EngineFault(oldSession, new IOException()));

        Assert.Equal(MirrorState.Running, controller.State);
    }

    [Fact]
    public void Disable_stops_the_engine()
    {
        var controller = CreateRunningController("hdmi");

        controller.ToggleMirroring();

        Assert.Equal(MirrorState.Off, controller.State);
        Assert.False(_engine.IsRunning);
    }

    [Fact]
    public void Device_changes_are_ignored_while_off()
    {
        var controller = CreateController();
        controller.ToggleDevice("hdmi");

        controller.OnDevicesChanged();

        Assert.Empty(_engine.Started);
    }

    [Fact]
    public void Selection_and_state_are_saved()
    {
        CreateRunningController("hdmi");

        Assert.Equal(["hdmi"], _settings.Saved.SelectedDeviceIds);
        Assert.True(_settings.Saved.MirrorEnabled);
    }

    [Fact]
    public void Previous_session_is_restored_on_startup()
    {
        _settings = new InMemorySettingsStore(new AppSettings(["hdmi"], MirrorEnabled: true));
        var controller = CreateController();

        controller.Initialize();

        Assert.True(controller.IsSelected("hdmi"));
        Assert.Equal(MirrorState.Running, controller.State);
        Assert.Empty(_notifier.Messages);
    }

    [Fact]
    public void Saved_selection_survives_while_device_is_unplugged()
    {
        _devices.Active.Remove(Hdmi);
        _settings = new InMemorySettingsStore(new AppSettings(["hdmi"], MirrorEnabled: true));
        var controller = CreateController();

        controller.Initialize();
        controller.ToggleDevice("headset");

        Assert.Equal(["hdmi", "headset"], _settings.Saved.SelectedDeviceIds);
    }

    [Fact]
    public void State_changes_are_announced()
    {
        var controller = CreateController();
        controller.ToggleDevice("hdmi");
        int raised = 0;
        controller.StateChanged += () => raised++;

        controller.Enable();
        controller.Disable();

        Assert.Equal(2, raised);
    }
}
