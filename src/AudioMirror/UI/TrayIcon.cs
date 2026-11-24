using System.Runtime.InteropServices;
using AudioMirror.Core;
using AudioMirror.Infrastructure;
using AudioMirror.Resources;

namespace AudioMirror.UI;

/// <summary>The notification-area icon: tooltip, context menu host and balloon messages.</summary>
internal sealed class TrayIcon : IUserNotifier, IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _image;

    public TrayIcon()
    {
        _image = LoadSpeakerIcon();
        _icon = new NotifyIcon
        {
            Icon = _image,
            Text = Strings.AppName,
            ContextMenuStrip = new ContextMenuStrip(),
            Visible = true,
        };
    }

    public ContextMenuStrip Menu => _icon.ContextMenuStrip!;

    public string ToolTip
    {
        // NotifyIcon throws above 127 characters.
        set => _icon.Text = value.Length <= 127 ? value : value[..124] + "...";
    }

    public void Info(string message) => Show(Strings.AppName, message, ToolTipIcon.Info, 2500);
    public void Warn(string message) => Show(Strings.AppName, message, ToolTipIcon.Warning, 4000);
    public void Error(string message) => Show(Strings.ErrorTitle, message, ToolTipIcon.Error, 5000);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _image.Dispose();
    }

    private void Show(string title, string message, ToolTipIcon kind, int timeoutMs)
        => _icon.ShowBalloonTip(timeoutMs, title, message, kind);

    /// <summary>Borrows the speaker icon from the Windows volume mixer so the app needs no assets.</summary>
    private static Icon LoadSpeakerIcon()
    {
        try
        {
            var path = Environment.ExpandEnvironmentVariables(@"%windir%\System32\SndVolSSO.dll");
            var small = new IntPtr[1];
            if (NativeMethods.ExtractIconEx(path, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
            {
                try { return (Icon)Icon.FromHandle(small[0]).Clone(); }
                finally { NativeMethods.DestroyIcon(small[0]); }
            }
        }
        catch (Exception ex) when (ex is ExternalException or ArgumentException)
        {
            Log.Warn("Could not load tray icon: " + ex.Message);
        }
        return (Icon)SystemIcons.Application.Clone();
    }

    private static class NativeMethods
    {
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern uint ExtractIconEx(string file, int index, IntPtr[]? large, IntPtr[]? small, uint count);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DestroyIcon(IntPtr handle);
    }
}
