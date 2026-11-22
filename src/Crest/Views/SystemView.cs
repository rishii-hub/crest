using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Controls;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    /// <summary>Live CPU, memory, network, disks, battery and uptime (iStat-style).</summary>
    public sealed class SystemView : UserControl, ITabView
    {
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly Sparkline _cpuLine = new Sparkline { FixedMax = 100, Height = 46 };
        readonly Sparkline _netLine = new Sparkline { Height = 46 };
        readonly TextBlock _cpuBig = Big(), _memBig = Big(), _down = Big(17), _up = Big(17), _batBig = Big(), _upBig = Big(20);
        readonly TextBlock _cpuSub = Sub(), _memSub = Sub(), _netSub = Sub(), _batSub = Sub(), _upSub = Sub();
        readonly ProgressLine _memBar = new ProgressLine();
        readonly StackPanel _disks = new StackPanel();
        readonly BatteryGlyph _batGlyph = new BatteryGlyph { Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        long _idle, _kernel, _user;
        long _rx, _tx;
        DateTime _netAt;
        int _diskTick;
        string _wifi = "", _health = "";
        bool _healthRead;
        readonly TextBlock _netTitle = new TextBlock();

        public SystemView()
        {
            _cpuLine.SetResourceReference(Sparkline.StrokeProperty, "AccentBrush");
            _netLine.Stroke = Ui.Res("CyanBrush");

            var grid = new UniformGrid { Columns = 3, Rows = 2 };
            grid.Children.Add(Card("CPU", Stack(_cpuBig, _cpuSub, Spaced(_cpuLine))));
            grid.Children.Add(Card("Memory", Stack(_memBig, _memSub, Spaced(_memBar))));
            var netRow = new StackPanel { Orientation = Orientation.Horizontal };
            netRow.Children.Add(new TextBlock { Text = "↓ ", FontSize = 15, Foreground = Ui.Res("CyanBrush"), VerticalAlignment = VerticalAlignment.Center });
            netRow.Children.Add(_down);
            netRow.Children.Add(new TextBlock { Text = "   ↑ ", FontSize = 15, Foreground = Ui.Res("Text2Brush"), VerticalAlignment = VerticalAlignment.Center });
            netRow.Children.Add(_up);
            grid.Children.Add(Card("Network", Stack(netRow, _netSub, Spaced(_netLine)), _netTitle));
            grid.Children.Add(Card("Disks", _disks));
            var batRow = new StackPanel { Orientation = Orientation.Horizontal };
            batRow.Children.Add(_batGlyph);
            batRow.Children.Add(_batBig);
            grid.Children.Add(Card("Battery", Stack(batRow, _batSub)));
            grid.Children.Add(Card("Uptime", Stack(_upBig, _upSub)));
            Content = grid;
            _timer.Tick += (s, e) => Sample();
        }

        public void OnActivated()
        {
            Win32.GetSystemTimes(out _idle, out _kernel, out _user);
            ReadNet(out _rx, out _tx);
            _netAt = DateTime.Now;
            _diskTick = 0;
            ReadWifi();
            if (!_healthRead) ReadHealth();
            Sample();
            _timer.Start();
        }

        public void OnDeactivated() => _timer.Stop();

        void Sample()
        {
            // CPU
            if (Win32.GetSystemTimes(out var idle, out var kernel, out var user))
            {
                long di = idle - _idle, dk = kernel - _kernel, du = user - _user;
                _idle = idle; _kernel = kernel; _user = user;
                double total = dk + du;
                double cpu = total > 0 ? Math.Clamp((1 - di / total) * 100, 0, 100) : 0;
                _cpuBig.Text = $"{cpu:0}%";
                _cpuLine.Push(cpu);
                _cpuSub.Text = $"{Environment.ProcessorCount} logical processors";
            }

            // Memory
            var m = new Win32.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
            if (Win32.GlobalMemoryStatusEx(ref m))
            {
                double used = m.ullTotalPhys - m.ullAvailPhys;
                _memBig.Text = $"{m.dwMemoryLoad}%";
                _memSub.Text = $"{Ui.Bytes(used)} of {Ui.Bytes(m.ullTotalPhys)} used";
                _memBar.Value = m.dwMemoryLoad / 100.0;
                _memBar.Brush = m.dwMemoryLoad > 85 ? Ui.Res("RedBrush") : m.dwMemoryLoad > 70 ? Ui.Res("OrangeBrush") : Ui.Res("GreenBrush");
            }

            // Network
            ReadNet(out var rx, out var tx);
            var secs = Math.Max(0.2, (DateTime.Now - _netAt).TotalSeconds);
            double down = Math.Max(0, (rx - _rx) / secs), up = Math.Max(0, (tx - _tx) / secs);
            _rx = rx; _tx = tx; _netAt = DateTime.Now;
            _down.Text = Ui.Bytes(down) + "/s";
            _up.Text = Ui.Bytes(up) + "/s";
            _netLine.Push(down);
            _netSub.Text = $"Since connected: ↓ {Ui.Bytes(rx)}  ↑ {Ui.Bytes(tx)}";

            // Disks (every 10 s), Wi-Fi (every 20 s)
            if (_diskTick++ % 10 == 0) RefreshDisks();
            if (_diskTick % 20 == 0) ReadWifi();
            _netTitle.Text = string.IsNullOrEmpty(_wifi) ? "NETWORK" : "NETWORK · " + _wifi.ToUpperInvariant();

            // Battery
            var p = PowerService.Instance;
            p.Poll();
            if (p.HasBattery)
            {
                _batGlyph.Visibility = Visibility.Visible;
                _batGlyph.Level = p.Percent;
                _batGlyph.Charging = p.OnAc && p.Percent < 100;
                _batBig.Text = $"{p.Percent}%";
                _batSub.Text = (p.OnAc ? (p.Percent >= 100 ? "Fully charged" : "Charging") :
                    p.SecondsLeft > 0 ? $"{TimeSpan.FromSeconds(p.SecondsLeft):h\\:mm} remaining" : "On battery") + _health;
            }
            else
            {
                _batGlyph.Visibility = Visibility.Collapsed;
                _batBig.Text = "AC";
                _batSub.Text = "No battery — desktop power";
            }

            // Uptime
            var upt = TimeSpan.FromMilliseconds(Environment.TickCount64);
            _upBig.Text = upt.TotalDays >= 1 ? $"{(int)upt.TotalDays}d {upt.Hours}h {upt.Minutes}m" : $"{upt.Hours}h {upt.Minutes}m";
            if (_diskTick % 5 == 1)
            {
                var procs = Process.GetProcesses();
                _upSub.Text = $"{procs.Length} processes running";
                foreach (var pr in procs) pr.Dispose();
            }
        }

        void RefreshDisks()
        {
            _disks.Children.Clear();
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed).Take(3))
            {
                double used = d.TotalSize - d.TotalFreeSpace;
                double frac = d.TotalSize > 0 ? used / d.TotalSize : 0;
                var head = new DockPanel { Margin = new Thickness(0, 2, 0, 3) };
                var free = new TextBlock { Text = $"{Ui.Bytes(d.TotalFreeSpace)} free", FontSize = 11, Foreground = Ui.Res("Text2Brush") };
                DockPanel.SetDock(free, Dock.Right);
                head.Children.Add(free);
                head.Children.Add(new TextBlock { Text = d.Name.TrimEnd('\\') + "  " + (string.IsNullOrEmpty(d.VolumeLabel) ? "" : d.VolumeLabel), FontSize = 12, FontWeight = FontWeights.SemiBold });
                _disks.Children.Add(head);
                _disks.Children.Add(new ProgressLine
                {
                    Value = frac, Margin = new Thickness(0, 0, 0, 8),
                    Brush = frac > 0.9 ? Ui.Res("RedBrush") : (Brush)FindResource("AccentBrush")
                });
            }
        }

        /// <summary>Wi-Fi name, signal and link rate from `netsh wlan show interfaces` (runs off the UI thread).</summary>
        async void ReadWifi()
        {
            var text = await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    var psi = new ProcessStartInfo("netsh", "wlan show interfaces") { RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
                    using var pr = Process.Start(psi);
                    if (pr == null) return "";
                    var o = pr.StandardOutput.ReadToEnd();
                    pr.WaitForExit(3000);
                    return o;
                }
                catch { return ""; }
            });
            string ssid = "", signal = "", rate = "";
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                int colon = line.IndexOf(':');
                if (colon < 0) continue;
                var key = line.Substring(0, colon).Trim();
                var val = line.Substring(colon + 1).Trim();
                if (key == "SSID") ssid = val;
                else if (key == "Signal") signal = val;
                else if (key.StartsWith("Receive rate")) rate = val + " Mbps";
            }
            _wifi = ssid.Length == 0 ? "" : string.Join(" · ", new[] { ssid, signal, rate }.Where(x => x.Length > 0 && x != " Mbps"));
        }

        /// <summary>Battery wear: full-charge capacity vs design capacity (WMI).</summary>
        async void ReadHealth()
        {
            _healthRead = true;
            var h = await System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    double full = 0, design = 0;
                    using (var a = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT FullChargedCapacity FROM BatteryFullChargedCapacity"))
                        foreach (System.Management.ManagementObject o in a.Get()) full += Convert.ToDouble(o["FullChargedCapacity"]);
                    using (var b = new System.Management.ManagementObjectSearcher(@"root\WMI", "SELECT DesignedCapacity FROM BatteryStaticData"))
                        foreach (System.Management.ManagementObject o in b.Get()) design += Convert.ToDouble(o["DesignedCapacity"]);
                    return design > 0 && full > 0 ? Math.Min(100, full / design * 100) : -1;
                }
                catch { return -1; }
            });
            _health = h > 0 ? $" · health {h:0}%" : "";
        }

        static void ReadNet(out long rx, out long tx)
        {
            rx = tx = 0;
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback || ni.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                    var st = ni.GetIPStatistics();
                    rx += st.BytesReceived;
                    tx += st.BytesSent;
                }
            }
            catch { }
        }

        static TextBlock Big(double size = 24) => new TextBlock { FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = size, FontWeight = FontWeights.SemiBold, Typography = { NumeralAlignment = FontNumeralAlignment.Tabular } };
        static TextBlock Sub() => new TextBlock { FontSize = 11, Foreground = Ui.Res("Text2Brush"), Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        static FrameworkElement Spaced(FrameworkElement e) { e.Margin = new Thickness(0, 8, 0, 0); return e; }

        static StackPanel Stack(params UIElement[] items)
        {
            var s = new StackPanel();
            foreach (var i in items) s.Children.Add(i);
            return s;
        }

        static Border Card(string title, UIElement body, TextBlock? titleBlock = null)
        {
            var s = new StackPanel();
            var t = titleBlock ?? new TextBlock();
            t.Text = title.ToUpperInvariant(); t.FontSize = 10.5; t.FontWeight = FontWeights.SemiBold; t.Foreground = Ui.Res("Text3Brush"); t.Margin = new Thickness(0, 0, 0, 6);
            t.TextTrimming = TextTrimming.CharacterEllipsis;
            s.Children.Add(t);
            s.Children.Add(body);
            return new Border { Style = Ui.StyleRes("Card"), Margin = new Thickness(4), Padding = new Thickness(14, 12, 14, 12), Child = s };
        }
    }

    /// <summary>Rounded horizontal progress bar.</summary>
    public sealed class ProgressLine : FrameworkElement
    {
        double _value;
        Brush _brush = Ui.Res("TextBrush");
        public double Value { get => _value; set { _value = Math.Clamp(value, 0, 1); InvalidateVisual(); } }
        public Brush Brush { get => _brush; set { _brush = value; InvalidateVisual(); } }
        readonly Brush Track = Ui.Res("TrackBrush");

        protected override Size MeasureOverride(Size available) => new Size(0, 6);

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0) return;
            dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), h / 2, h / 2);
            if (_value > 0) dc.DrawRoundedRectangle(_brush, null, new Rect(0, 0, Math.Max(h, w * _value), h), h / 2, h / 2);
        }
    }
}
