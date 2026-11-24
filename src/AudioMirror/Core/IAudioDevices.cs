namespace AudioMirror.Core;

/// <summary>Read-only view of the system's audio output devices.</summary>
internal interface IAudioDevices
{
    /// <summary>The Windows default output device, or null if there is none.</summary>
    AudioDevice? GetDefaultOutput();

    IReadOnlyList<AudioDevice> GetActiveOutputs();

    /// <summary>
    /// Raised when devices are added, removed or the default changes.
    /// May be raised on any thread, several times for a single physical change.
    /// </summary>
    event Action? Changed;
}
