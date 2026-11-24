using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace AudioMirror
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // NotifyIcon is not a Control, so make sure a UI SynchronizationContext exists
            // before TrayApp captures it for marshalling audio-thread events.
            SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
            using (new TrayApp())
            {
                Application.Run();
            }
        }
    }

    public class TrayApp : IDisposable
    {
        private readonly NotifyIcon _tray;
        private readonly SynchronizationContext _ui;
        private readonly MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();
        private readonly HashSet<string> _selectedIds = new HashSet<string>();

        // Guards _pipes: written by the UI thread, read by the capture thread.
        private readonly object _sync = new object();
        private OutputPipe[] _pipes = Array.Empty<OutputPipe>();
        private WasapiLoopbackCapture _capture;
        private bool _running;

        public TrayApp()
        {
            _ui = SynchronizationContext.Current
                  ?? throw new InvalidOperationException("TrayApp must be created on the UI thread.");
            _tray = new NotifyIcon
            {
                Icon = LoadTrayIcon(),
                Text = "Audio Mirror — wyłączony",
                Visible = true
            };
            _tray.ContextMenuStrip = new ContextMenuStrip();
            _tray.ContextMenuStrip.Opening += (s, e) => BuildMenu();
            BuildMenu();
        }

        private MMDevice TryGetDefaultDevice()
        {
            try { return _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
            catch (COMException ex)
            {
                Log.Warn("No default render device: " + ex.Message);
                return null;
            }
        }

        private List<MMDevice> ActiveOutputs()
            => _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

        private void BuildMenu()
        {
            var menu = _tray.ContextMenuStrip;
            menu.Items.Clear();

            // MMDevice wraps COM objects; release them as soon as the menu is built.
            using var def = TryGetDefaultDevice();
            var outputs = ActiveOutputs();
            try
            {
                menu.Items.Add(new ToolStripMenuItem(
                    def != null ? "Główne (gra zawsze): " + def.FriendlyName : "Brak domyślnego urządzenia")
                { Enabled = false });

                menu.Items.Add(new ToolStripSeparator());
                menu.Items.Add(new ToolStripMenuItem("Wyślij dźwięk DODATKOWO na:") { Enabled = false });

                foreach (var dev in outputs)
                {
                    bool isDefault = def != null && dev.ID == def.ID;
                    var id = dev.ID;
                    var item = new ToolStripMenuItem(dev.FriendlyName + (isDefault ? "   (główne)" : ""))
                    {
                        Checked = _selectedIds.Contains(id),
                        Enabled = !isDefault
                    };
                    item.Click += (s, e) =>
                    {
                        if (!_selectedIds.Remove(id)) _selectedIds.Add(id);
                        if (_running) Restart();
                    };
                    menu.Items.Add(item);
                }
            }
            finally
            {
                foreach (var d in outputs) d.Dispose();
            }

            menu.Items.Add(new ToolStripSeparator());
            var toggle = new ToolStripMenuItem(_running ? "■  Zatrzymaj" : "▶  Włącz mirror");
            toggle.Click += (s, e) => { if (_running) Stop(); else Start(); };
            menu.Items.Add(toggle);

            var exit = new ToolStripMenuItem("Zamknij");
            exit.Click += (s, e) => { Stop(); _tray.Visible = false; Application.Exit(); };
            menu.Items.Add(exit);
        }

        private void Start()
        {
            var pipes = new List<OutputPipe>();
            var targets = new List<MMDevice>();
            try
            {
                using var primary = TryGetDefaultDevice()
                    ?? throw new InvalidOperationException("Brak domyślnego urządzenia wyjściowego.");

                foreach (var d in ActiveOutputs())
                {
                    if (_selectedIds.Contains(d.ID) && d.ID != primary.ID) targets.Add(d);
                    else d.Dispose();
                }

                if (targets.Count == 0)
                {
                    _tray.ShowBalloonTip(3000, "Audio Mirror",
                        "Zaznacz najpierw co najmniej jedno urządzenie dodatkowe.", ToolTipIcon.Warning);
                    return;
                }

                var names = targets.Select(t => t.FriendlyName).ToList();
                _capture = new WasapiLoopbackCapture(primary);
                foreach (var dev in targets)
                {
                    // The pipe takes ownership of the device and disposes it.
                    var pipe = new OutputPipe(dev, _capture.WaveFormat);
                    pipe.Faulted += OnAudioFault;
                    pipes.Add(pipe);
                }

                _capture.DataAvailable += OnDataAvailable;
                _capture.RecordingStopped += (s, e) => { if (e.Exception != null) OnAudioFault(e.Exception); };

                lock (_sync) _pipes = pipes.ToArray();
                foreach (var p in pipes) p.Start();
                _capture.StartRecording();

                _running = true;
                _tray.Text = "Audio Mirror — włączony";
                _tray.ShowBalloonTip(2500, "Audio Mirror",
                    "Dźwięk leci teraz także na: " + string.Join(", ", names), ToolTipIcon.Info);
                Log.Info("Mirroring started: " + string.Join(", ", names));
            }
            catch (Exception ex)
            {
                Log.Error("Start failed", ex);
                // Devices not yet owned by a pipe, and pipes not yet published to _pipes, would leak.
                foreach (var d in targets.Skip(pipes.Count)) d.Dispose();
                bool published;
                lock (_sync) published = _pipes.Length > 0;
                if (!published) foreach (var p in pipes) p.Dispose();
                Stop();
                _tray.ShowBalloonTip(5000, "Audio Mirror — błąd", ex.Message, ToolTipIcon.Error);
            }
        }

        // Runs on the capture thread.
        private void OnDataAvailable(object sender, WaveInEventArgs e)
        {
            lock (_sync)
            {
                foreach (var p in _pipes)
                    p.Write(e.Buffer, e.BytesRecorded);
            }
        }

        // May be raised on any thread; always handled on the UI thread.
        private void OnAudioFault(Exception ex)
        {
            _ui.Post(_ =>
            {
                if (!_running) return;
                Log.Error("Audio device failed, stopping mirror", ex);
                Stop();
                _tray.ShowBalloonTip(5000, "Audio Mirror — błąd",
                    "Urządzenie audio przestało odpowiadać. Mirror zatrzymany.", ToolTipIcon.Error);
            }, null);
        }

        private void Stop()
        {
            _running = false;

            OutputPipe[] pipes;
            lock (_sync)
            {
                pipes = _pipes;
                _pipes = Array.Empty<OutputPipe>();
            }

            // Dispose the capture outside the lock: it waits for the capture thread,
            // which may itself be waiting on _sync inside OnDataAvailable.
            if (_capture != null)
            {
                _capture.DataAvailable -= OnDataAvailable;
                try { _capture.StopRecording(); }
                catch (Exception ex) { Log.Warn("StopRecording failed: " + ex.Message); }
                _capture.Dispose();
                _capture = null;
            }

            foreach (var p in pipes) p.Dispose();
            _tray.Text = "Audio Mirror — wyłączony";
        }

        private void Restart() { Stop(); Start(); }

        private static Icon LoadTrayIcon()
        {
            try
            {
                var path = Environment.ExpandEnvironmentVariables(@"%windir%\System32\SndVolSSO.dll");
                var small = new IntPtr[1];
                if (ExtractIconEx(path, 0, null, small, 1) > 0 && small[0] != IntPtr.Zero)
                {
                    var icon = (Icon)Icon.FromHandle(small[0]).Clone();
                    DestroyIcon(small[0]);
                    return icon;
                }
            }
            catch (Exception ex)
            {
                Log.Warn("Could not load tray icon: " + ex.Message);
            }
            return SystemIcons.Application;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern uint ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, uint count);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        public void Dispose()
        {
            Stop();
            _tray.Dispose();
            _enumerator.Dispose();
        }
    }

    internal class OutputPipe : IDisposable
    {
        private readonly MMDevice _device;
        private readonly BufferedWaveProvider _buffer;
        private readonly WasapiOut _out;
        private readonly IDisposable _resampler;

        public event Action<Exception> Faulted;

        public OutputPipe(MMDevice device, WaveFormat captureFormat)
        {
            _device = device;
            _buffer = new BufferedWaveProvider(captureFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(2)
            };

            _out = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);
            _out.PlaybackStopped += (s, e) => { if (e.Exception != null) Faulted?.Invoke(e.Exception); };

            // MMDevice.AudioClient activates a new COM client on every access, so dispose it.
            WaveFormat target;
            using (var client = device.AudioClient) target = client.MixFormat;

            if (!captureFormat.Equals(target))
            {
                var res = new MediaFoundationResampler(_buffer, target) { ResamplerQuality = 60 };
                _resampler = res;
                _out.Init(res);
            }
            else
            {
                _out.Init(_buffer);
            }
        }

        public void Start() => _out.Play();

        public void Write(byte[] data, int count) => _buffer.AddSamples(data, 0, count);

        public void Dispose()
        {
            try { _out.Stop(); }
            catch (Exception ex) { Log.Warn("WasapiOut.Stop failed: " + ex.Message); }
            _out.Dispose();
            _resampler?.Dispose();
            _device.Dispose();
        }
    }

    /// <summary>Minimal append-only log in %LocalAppData%\AudioMirror.</summary>
    internal static class Log
    {
        private const long MaxBytes = 512 * 1024;
        private static readonly object Gate = new object();

        public static string FilePath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AudioMirror", "audiomirror.log");

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message, Exception ex) => Write("ERROR", message + Environment.NewLine + ex);

        private static void Write(string level, string message)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    var info = new FileInfo(FilePath);
                    if (info.Exists && info.Length > MaxBytes) info.Delete();
                    File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}");
                }
            }
            catch (IOException) { /* logging must never crash the app */ }
            catch (UnauthorizedAccessException) { }
        }
    }
}
