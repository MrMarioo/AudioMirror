using AudioMirror.Core;
using AudioMirror.Resources;

namespace AudioMirror.UI;

/// <summary>
/// Presents <see cref="MirrorController"/> in the tray menu and moves events
/// from audio/COM threads onto the UI thread.
/// </summary>
internal sealed class TrayApp : IDisposable
{
    /// <summary>Windows sends a burst of notifications per change (per role, per state...).</summary>
    private const int DeviceChangeDebounceMs = 500;

    private readonly TrayIcon _icon;
    private readonly MirrorController _controller;
    private readonly IAudioDevices _devices;
    private readonly IMirrorEngine _engine;
    private readonly SynchronizationContext _ui;
    private readonly System.Windows.Forms.Timer _debounce;

    public TrayApp(TrayIcon icon, MirrorController controller, IAudioDevices devices,
        IMirrorEngine engine, SynchronizationContext ui)
    {
        _icon = icon;
        _controller = controller;
        _devices = devices;
        _engine = engine;
        _ui = ui;

        _debounce = new System.Windows.Forms.Timer { Interval = DeviceChangeDebounceMs };
        _debounce.Tick += OnDebounceElapsed;

        _icon.Menu.Opening += OnMenuOpening;
        _controller.StateChanged += UpdateToolTip;
        _devices.Changed += OnDevicesChanged;
        _engine.Faulted += OnEngineFaulted;

        UpdateToolTip();
    }

    public void Dispose()
    {
        _engine.Faulted -= OnEngineFaulted;
        _devices.Changed -= OnDevicesChanged;
        _controller.StateChanged -= UpdateToolTip;
        _icon.Menu.Opening -= OnMenuOpening;
        _debounce.Dispose();
    }

    // Raised on a COM thread.
    private void OnDevicesChanged() => _ui.Post(_ =>
    {
        _debounce.Stop();
        _debounce.Start();
    }, null);

    // Raised on an audio thread.
    private void OnEngineFaulted(EngineFault fault) => _ui.Post(_ => _controller.OnEngineFault(fault), null);

    private void OnDebounceElapsed(object? sender, EventArgs e)
    {
        _debounce.Stop();
        _controller.OnDevicesChanged();
    }

    private void UpdateToolTip()
    {
        _icon.ToolTip = _controller.State switch
        {
            MirrorState.Running => Strings.TrayRunning(
                string.Join(", ", _controller.ActiveTargets.Select(t => t.Name))),
            MirrorState.Waiting => Strings.TrayWaiting,
            _ => Strings.TrayOff,
        };
    }

    // Rebuilt on every open so newly plugged-in devices show up.
    private void OnMenuOpening(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var items = _icon.Menu.Items;
        foreach (var old in items.Cast<ToolStripItem>().ToList()) old.Dispose();
        items.Clear();

        var primary = _devices.GetDefaultOutput();
        items.Add(new ToolStripMenuItem(primary is null
            ? Strings.MenuNoDefault
            : Strings.MenuPrimary(primary.Name)) { Enabled = false });

        items.Add(new ToolStripSeparator());
        items.Add(new ToolStripMenuItem(Strings.MenuMirrorTo) { Enabled = false });

        foreach (var device in _devices.GetActiveOutputs())
        {
            bool isPrimary = device.Id == primary?.Id;
            var item = new ToolStripMenuItem(isPrimary ? $"{device.Name}   {Strings.MenuPrimarySuffix}" : device.Name)
            {
                Checked = _controller.IsSelected(device.Id),
                Enabled = !isPrimary,
            };
            var id = device.Id;
            item.Click += (_, _) => _controller.ToggleDevice(id);
            items.Add(item);
        }

        items.Add(new ToolStripSeparator());

        var toggle = new ToolStripMenuItem(_controller.State == MirrorState.Off ? Strings.MenuStart : Strings.MenuStop);
        toggle.Click += (_, _) => _controller.ToggleMirroring();
        items.Add(toggle);

        // Exiting keeps the saved "enabled" state, so mirroring resumes on the next launch.
        var exit = new ToolStripMenuItem(Strings.MenuExit);
        exit.Click += (_, _) => Application.Exit();
        items.Add(exit);

        e.Cancel = false; // the menu is never empty, so let it open
    }
}
