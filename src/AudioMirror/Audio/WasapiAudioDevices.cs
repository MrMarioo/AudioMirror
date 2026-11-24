using System.Runtime.InteropServices;
using AudioMirror.Core;
using AudioMirror.Infrastructure;
using NAudio.CoreAudioApi;

namespace AudioMirror.Audio;

/// <summary>Lists output devices through the Windows Core Audio API and reports changes.</summary>
internal sealed class WasapiAudioDevices : IAudioDevices, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator = new();
    private readonly DeviceWatcher _watcher; // must stay referenced while registered with COM

    public event Action? Changed;

    public WasapiAudioDevices()
    {
        _watcher = new DeviceWatcher(() => Changed?.Invoke());
        _enumerator.RegisterEndpointNotificationCallback(_watcher);
    }

    public AudioDevice? GetDefaultOutput()
    {
        try
        {
            using var device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            return ToSnapshot(device);
        }
        catch (COMException ex)
        {
            Log.Warn("No default render device: " + ex.Message);
            return null;
        }
    }

    public IReadOnlyList<AudioDevice> GetActiveOutputs()
    {
        var result = new List<AudioDevice>();
        foreach (var device in _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            using (device) result.Add(ToSnapshot(device));
        }
        return result;
    }

    public void Dispose()
    {
        try { _enumerator.UnregisterEndpointNotificationCallback(_watcher); }
        catch (COMException ex) { Log.Warn("Unregistering device notifications failed: " + ex.Message); }
        _enumerator.Dispose();
    }

    private static AudioDevice ToSnapshot(MMDevice device) => new(device.ID, device.FriendlyName);
}
