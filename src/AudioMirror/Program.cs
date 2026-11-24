using AudioMirror.Audio;
using AudioMirror.Core;
using AudioMirror.Infrastructure;
using AudioMirror.Settings;
using AudioMirror.UI;

namespace AudioMirror;

/// <summary>Composition root: the only place that knows about concrete implementations.</summary>
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();

        // NotifyIcon is not a Control, so install the UI SynchronizationContext explicitly
        // before anything captures it for marshalling audio-thread events.
        var ui = new WindowsFormsSynchronizationContext();
        SynchronizationContext.SetSynchronizationContext(ui);

        Log.Info($"Audio Mirror {Application.ProductVersion} starting");

        using var devices = new WasapiAudioDevices();
        using var engine = new WasapiMirrorEngine();
        using var icon = new TrayIcon();
        var settings = new JsonSettingsStore(JsonSettingsStore.DefaultPath);
        var controller = new MirrorController(devices, engine, settings, icon);

        using var app = new TrayApp(icon, controller, devices, engine, ui);
        controller.Initialize();
        Application.Run();

        Log.Info("Audio Mirror exiting");
    }
}
