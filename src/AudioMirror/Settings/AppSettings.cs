namespace AudioMirror.Settings;

/// <summary>User choices persisted between runs.</summary>
/// <param name="SelectedDeviceIds">Extra output devices, including ones that are currently unplugged.</param>
/// <param name="MirrorEnabled">Whether mirroring was on when the app last closed.</param>
internal sealed record AppSettings(IReadOnlyList<string> SelectedDeviceIds, bool MirrorEnabled)
{
    public static AppSettings Default { get; } = new([], false);
}
