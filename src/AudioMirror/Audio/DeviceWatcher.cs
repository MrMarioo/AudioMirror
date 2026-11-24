using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace AudioMirror.Audio;

/// <summary>
/// Receives Windows audio endpoint notifications. They arrive on a COM worker thread where
/// calling back into Core Audio is unsafe, so this class only signals; the work happens elsewhere.
/// </summary>
internal sealed class DeviceWatcher(Action changed) : IMMNotificationClient
{
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
    {
        if (flow == DataFlow.Render && role == Role.Multimedia) changed();
    }

    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => changed();
    public void OnDeviceAdded(string pwstrDeviceId) => changed();
    public void OnDeviceRemoved(string deviceId) => changed();
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
}
