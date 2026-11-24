using System.Runtime.CompilerServices;
using AudioMirror.Infrastructure;

namespace AudioMirror.Tests;

internal static class TestSetup
{
    /// <summary>Simulated failures in the tests must not end up in the user's real log file.</summary>
    [ModuleInitializer]
    internal static void DisableFileLog() => Log.Enabled = false;
}
