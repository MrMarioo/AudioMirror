using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;

namespace AudioMirror.Resources;

/// <summary>
/// Typed access to <c>Strings.resx</c> (English) and <c>Strings.pl.resx</c> (Polish).
/// The language follows the Windows display language.
/// </summary>
internal static class Strings
{
    private static readonly ResourceManager Resources = new(typeof(Strings));

    public static string AppName => Get();
    public static string ErrorTitle => Get();

    public static string TrayOff => Get();
    public static string TrayWaiting => Get();
    public static string TrayRunning(string deviceNames) => Format(deviceNames);

    public static string MenuPrimary(string deviceName) => Format(deviceName);
    public static string MenuNoDefault => Get();
    public static string MenuMirrorTo => Get();
    public static string MenuPrimarySuffix => Get();
    public static string MenuStart => Get();
    public static string MenuStop => Get();
    public static string MenuExit => Get();

    public static string Started(string deviceNames) => Format(deviceNames);
    public static string NoTargets => Get();
    public static string NoDefaultDevice => Get();
    public static string DeviceFailed => Get();

    private static string Get([CallerMemberName] string key = "")
        => Resources.GetString(key, CultureInfo.CurrentUICulture)
           ?? throw new MissingManifestResourceException($"Missing string resource '{key}'.");

    [SuppressMessage("Performance", "CA1863:Use 'CompositeFormat'",
        Justification = "The format comes from resources and depends on the UI culture; these run a few times per user action.")]
    private static string Format(object arg, [CallerMemberName] string key = "")
        => string.Format(CultureInfo.CurrentCulture, Get(key), arg);
}
