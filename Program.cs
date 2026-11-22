using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
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
            using (new TrayApp())
            {
                Application.Run();
            }
        }
    }

    public class TrayApp : IDisposable
    {
        private readonly NotifyIcon _tray;
        private readonly MMDeviceEnumerator _enumerator = new MMDeviceEnumerator();
        private readonly List<OutputPipe> _pipes = new List<OutputPipe>();
        private readonly HashSet<string> _selectedIds = new HashSet<string>();
        private WasapiLoopbackCapture _capture;
        private bool _running;

        public TrayApp()
        {
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

        private MMDevice DefaultDevice()
            => _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

        private List<MMDevice> ActiveOutputs()
            => _enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();

        private void BuildMenu()
        {
            var menu = _tray.ContextMenuStrip;
            menu.Items.Clear();

            MMDevice def = null;
            try { def = DefaultDevice(); } catch { }

            menu.Items.Add(new ToolStripMenuItem(
                def != null ? "Główne (gra zawsze): " + def.FriendlyName : "Brak domyślnego urządzenia")
            { Enabled = false });

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Wyślij dźwięk DODATKOWO na:") { Enabled = false });

            foreach (var dev in ActiveOutputs())
            {
                bool isDefault = def != null && dev.ID == def.ID;
                var item = new ToolStripMenuItem(dev.FriendlyName + (isDefault ? "   (główne)" : ""))
                {
                    Checked = _selectedIds.Contains(dev.ID),
                    Enabled = !isDefault,
                    Tag = dev.ID
                };
                item.Click += (s, e) =>
                {
                    var id = (string)((ToolStripMenuItem)s).Tag;
                    if (!_selectedIds.Remove(id)) _selectedIds.Add(id);
                    if (_running) Restart();
                };
                menu.Items.Add(item);
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
            try
            {
                var primary = DefaultDevice();
                var targets = ActiveOutputs()
                    .Where(d => _selectedIds.Contains(d.ID) && d.ID != primary.ID)
                    .ToList();

                if (targets.Count == 0)
                {
                    _tray.ShowBalloonTip(3000, "Audio Mirror",
                        "Zaznacz najpierw co najmniej jedno urządzenie dodatkowe.", ToolTipIcon.Warning);
                    return;
                }

                _capture = new WasapiLoopbackCapture(primary);
                foreach (var dev in targets)
                    _pipes.Add(new OutputPipe(dev, _capture.WaveFormat));

                _capture.DataAvailable += (s, e) =>
                {
                    foreach (var p in _pipes)
                        p.Buffer.AddSamples(e.Buffer, 0, e.BytesRecorded);
                };

                foreach (var p in _pipes) p.Start();
                _capture.StartRecording();

                _running = true;
                _tray.Text = "Audio Mirror — włączony";
                _tray.ShowBalloonTip(2500, "Audio Mirror",
                    "Dźwięk leci teraz także na: " + string.Join(", ", targets.Select(t => t.FriendlyName)),
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                Stop();
                _tray.ShowBalloonTip(5000, "Audio Mirror — błąd", ex.Message, ToolTipIcon.Error);
            }
        }

        private void Stop()
        {
            _running = false;
            try { _capture?.StopRecording(); } catch { }
            _capture?.Dispose();
            _capture = null;
            foreach (var p in _pipes) p.Dispose();
            _pipes.Clear();
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
            catch { }
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
        public BufferedWaveProvider Buffer { get; }
        private readonly WasapiOut _out;
        private readonly IDisposable _resampler;

        public OutputPipe(MMDevice device, WaveFormat captureFormat)
        {
            Buffer = new BufferedWaveProvider(captureFormat)
            {
                DiscardOnBufferOverflow = true,
                BufferDuration = TimeSpan.FromSeconds(2)
            };

            _out = new WasapiOut(device, AudioClientShareMode.Shared, true, 100);

            var target = device.AudioClient.MixFormat;
            if (!captureFormat.Equals(target))
            {
                var res = new MediaFoundationResampler(Buffer, target) { ResamplerQuality = 60 };
                _resampler = res;
                _out.Init(res);
            }
            else
            {
                _out.Init(Buffer);
            }
        }

        public void Start() => _out.Play();

        public void Dispose()
        {
            try { _out?.Stop(); } catch { }
            _out?.Dispose();
            _resampler?.Dispose();
        }
    }
}
