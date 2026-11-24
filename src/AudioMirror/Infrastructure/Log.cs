namespace AudioMirror.Infrastructure;

/// <summary>Minimal append-only log in %LocalAppData%\AudioMirror. Thread-safe and never throws.</summary>
internal static class Log
{
    private const long MaxBytes = 512 * 1024;
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AudioMirror", "audiomirror.log");

    /// <summary>Turned off by the test suite so it doesn't pollute the real log.</summary>
    public static bool Enabled { get; set; } = true;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
    public static void Error(string message, Exception ex) => Write("ERROR", message + Environment.NewLine + ex);

    private static void Write(string level, string message)
    {
        if (!Enabled) return;
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes) info.Delete();
                File.AppendAllText(FilePath,
                    $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Logging must never take the app down.
        }
    }
}
