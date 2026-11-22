using System;
using System.Linq;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    public partial class HomeView : UserControl, ITabView
    {
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        bool _seeking, _syncing;

        public HomeView()
        {
            InitializeComponent();
            _timer.Tick += (s, e) => Tick();

            PrevBtn.Click += async (s, e) => await MediaService.Instance.PreviousAsync();
            NextBtn.Click += async (s, e) => await MediaService.Instance.NextAsync();
            PlayBtn.Click += async (s, e) => await MediaService.Instance.TogglePlayPauseAsync();
            SeekSlider.PreviewMouseLeftButtonDown += (s, e) => _seeking = true;
            SeekSlider.PreviewMouseLeftButtonUp += async (s, e) =>
            {
                var to = TimeSpan.FromSeconds(SeekSlider.Value);
                _seeking = false;
                await MediaService.Instance.SeekAsync(to);
            };
            SeekSlider.ValueChanged += (s, e) => { if (_seeking) PosText.Text = Fmt(TimeSpan.FromSeconds(SeekSlider.Value)); };

            VolSlider.ValueChanged += (s, e) =>
            {
                VolText.Text = $"{Math.Round(VolSlider.Value)}";
                if (_syncing) return;
                AudioService.Instance.Volume = (float)(VolSlider.Value / 100.0);
                if (AudioService.Instance.Muted && VolSlider.Value > 0) { AudioService.Instance.Muted = false; MuteTile.IsChecked = false; }
                UpdateVolGlyph();
            };
            BriSlider.ValueChanged += (s, e) =>
            {
                BriText.Text = $"{Math.Round(BriSlider.Value)}";
                if (!_syncing) BrightnessService.Instance.Set((int)Math.Round(BriSlider.Value));
            };
            MuteTile.Click += (s, e) => { AudioService.Instance.Muted = MuteTile.IsChecked == true; UpdateVolGlyph(); };
            MicTile.Click += (s, e) => AudioService.Instance.MicMuted = MicTile.IsChecked == true;
            AwakeTile.Click += (s, e) => KeepAwake.Set(AwakeTile.IsChecked == true);
            SnipTile.Click += (s, e) =>
            {
                NotchWindow.Instance?.Collapse();
                Ui.Later(350, () => Ui.Open("ms-screenclip:"));
            };
            LockTile.Click += (s, e) => Win32.LockWorkStation();
            WeatherMini.Click += (s, e) => NotchWindow.Instance?.SelectTab("Weather");
            SysWidget.Click += (s, e) => NotchWindow.Instance?.SelectTab("System");
            ScreenWidget.Click += (s, e) => NotchWindow.Instance?.SelectTab("ScreenTime");
            FocusWidget.Click += (s, e) => { TimerService.Instance.PomoToggle(); Widgets(); };
            FocusWidget.MouseRightButtonUp += (s, e) => NotchWindow.Instance?.SelectTab("Timers");
            TimerService.Instance.Tick += () => { if (IsVisible) Widgets(); };

            MediaService.Instance.Changed += () => { if (IsVisible) RefreshMedia(); };
            OutputBtn.Click += (s, e) =>
            {
                var name = AudioOutputs.Cycle();
                RefreshOutput();
                if (name != null) NotchWindow.Instance?.ShowToast(new Toast { Kind = "output", Glyph = "\uE7F5", Title = "Output", Value = name, Duration = TimeSpan.FromSeconds(1.8) });
            };
            AudioService.Instance.VolumeChanged += (v, m) => { if (IsVisible) SyncAudio(); };
            BrightnessService.Instance.Changed += b => { if (IsVisible) SyncBrightness(); };
            WeatherService.Instance.Updated += () => { if (IsVisible) RefreshWeather(); };
        }

        public void OnActivated()
        {
            _active = true;
            RefreshMedia();
            RefreshOutput();
            SyncAudio();
            SyncBrightness();
            RefreshWeather();
            AwakeTile.IsChecked = KeepAwake.On;
            MicTile.IsEnabled = AudioService.Instance.HasMic;
            MicTile.IsChecked = AudioService.Instance.MicMuted;
            Widgets();
            SampleSystem();
            Tick();
            _timer.Start();
            _ = WeatherService.Instance.RefreshIfStaleAsync();
        }

        public void OnDeactivated() { _timer.Stop(); _active = false; Spectrum.Running = false; }

        bool _active;

        void RefreshOutput()
        {
            var list = AudioOutputs.List();
            var def = list.Find(x => x.IsDefault);
            OutputText.Text = string.IsNullOrEmpty(def.Name) ? "No output" : Short(def.Name);
            OutputBtn.ToolTip = (def.Name ?? "") + (list.Count > 1 ? "\nClick to switch to the next output" : "");
            OutputBtn.IsEnabled = list.Count > 1;
        }

        static string Short(string n)
        {
            int p = n.IndexOf(" (", StringComparison.Ordinal);
            return p > 0 ? n[..p] : n;
        }

        long _idle, _kernel, _user;
        int _widgetTick;

        void Widgets()
        {
            var t = TimerService.Instance;
            FocusTime.Text = Ui.ClockCeil(t.PomoLeft);
            FocusLabel.Text = t.PomoRunning ? t.PhaseName(t.Phase) : t.PomoLeft < t.PhaseLength(t.Phase) ? "Paused" : t.PhaseName(t.Phase);
            FocusGlyph.Text = t.PomoRunning ? Glyphs.Pause : Glyphs.Play;
        }

        void SampleSystem()
        {
            if (Win32.GetSystemTimes(out var idle, out var kernel, out var user))
            {
                long di = idle - _idle, total = (kernel - _kernel) + (user - _user);
                if (_kernel != 0 && total > 0)
                {
                    double cpu = Math.Clamp(1 - di / (double)total, 0, 1);
                    CpuRing.Progress = cpu;
                    CpuText.Text = $"{cpu * 100:0}%";
                }
                _idle = idle; _kernel = kernel; _user = user;
            }
            var m = new Win32.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
            if (Win32.GlobalMemoryStatusEx(ref m)) { RamRing.Progress = m.dwMemoryLoad / 100.0; RamText.Text = $"{m.dwMemoryLoad}%"; }
            var st = ScreenTimeService.Instance;
            var day = DateTime.Now.ToString("yyyy-MM-dd");
            ScreenTotal.Text = ScreenTimeService.Format(st.TotalSeconds(day));
            var top = st.Apps(day).FirstOrDefault();
            ScreenTop.Text = top.Exe != null ? $"{st.FriendlyName(top.Exe)} · {ScreenTimeService.Format(top.Seconds)}" : "";
        }

        void Tick()
        {
            if (_widgetTick++ % 3 == 0) SampleSystem();
            var now = DateTime.Now;
            ClockText.Text = now.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H") ? "HH:mm" : "h:mm", CultureInfo.CurrentCulture);
            DateText.Text = now.ToString("dddd, d MMMM", CultureInfo.CurrentCulture);
            var md = MediaService.Instance;
            if (!_seeking && md.HasSession && md.Duration > TimeSpan.Zero)
            {
                var pos = md.Position;
                SeekSlider.Maximum = Math.Max(1, md.Duration.TotalSeconds);
                SeekSlider.Value = Math.Min(SeekSlider.Maximum, pos.TotalSeconds);
                PosText.Text = Fmt(pos);
            }
        }

        void RefreshMedia()
        {
            var md = MediaService.Instance;
            bool has = md.HasSession && !string.IsNullOrEmpty(md.Title);
            TitleText.Text = has ? md.Title : "Nothing playing";
            ArtistText.Text = has ? md.Artist : "Play something in Spotify, a browser or any media app";
            AppText.Text = has && !string.IsNullOrEmpty(md.AppName) ? md.AppName.ToUpperInvariant() : "NOW PLAYING";
            PlayBtn.Content = md.IsPlaying ? Glyphs.Pause : Glyphs.Play;
            Spectrum.Running = _active && md.IsPlaying;
            PlayBtn.IsEnabled = md.HasSession;
            PrevBtn.IsEnabled = md.HasSession && md.CanPrev;
            NextBtn.IsEnabled = md.HasSession && md.CanNext;
            SeekRow.Visibility = md.HasSession && md.Duration > TimeSpan.Zero ? Visibility.Visible : Visibility.Hidden;
            SeekSlider.IsEnabled = md.CanSeek;
            DurText.Text = Fmt(md.Duration);
            var tint = md.ArtColor ?? Ui.AccentColor;
            SeekSlider.Foreground = new SolidColorBrush(tint);
            Spectrum.Brush = new SolidColorBrush(tint);
            if (md.Art != null)
            {
                ArtBox.Background = new ImageBrush(md.Art) { Stretch = Stretch.UniformToFill };
                ArtGlow.Background = new ImageBrush(md.Art) { Stretch = Stretch.UniformToFill };
                ArtPlaceholder.Visibility = Visibility.Collapsed;
            }
            else
            {
                ArtBox.Background = Ui.Res("Card2Brush");
                ArtGlow.Background = Brushes.Transparent;
                ArtPlaceholder.Visibility = Visibility.Visible;
            }
            Tick();
        }

        void SyncAudio()
        {
            _syncing = true;
            var a = AudioService.Instance;
            VolSlider.IsEnabled = MuteTile.IsEnabled = a.Available;
            VolSlider.Value = Math.Round(a.Volume * 100);
            VolText.Text = $"{Math.Round(VolSlider.Value)}";
            MuteTile.IsChecked = a.Muted;
            _syncing = false;
            UpdateVolGlyph();
        }

        void UpdateVolGlyph()
        {
            var a = AudioService.Instance;
            double v = VolSlider.Value / 100.0;
            VolGlyph.Text = a.Muted || v < 0.005 ? Glyphs.Mute : v < 0.34 ? Glyphs.Volume1 : v < 0.67 ? Glyphs.Volume2 : Glyphs.Volume3;
        }

        void SyncBrightness()
        {
            var b = BrightnessService.Instance;
            BriRow.Visibility = b.Supported ? Visibility.Visible : Visibility.Collapsed;
            if (!b.Supported) return;
            _syncing = true;
            BriSlider.Value = Math.Max(0, b.Current);
            BriText.Text = $"{Math.Round(BriSlider.Value)}";
            _syncing = false;
        }

        void RefreshWeather()
        {
            var w = WeatherService.Instance;
            var s = SettingsStore.Current;
            if (!w.HasLocation)
            {
                WeatherSymbol.Text = "☁";
                WeatherTemp.Text = "";
                WeatherCity.Text = "Set location";
                return;
            }
            WeatherCity.Text = s.City ?? "";
            if (w.Data == null) { WeatherTemp.Text = "--°"; return; }
            WeatherSymbol.Text = WeatherService.Symbol(w.Data.Code, w.Data.IsDay);
            WeatherTemp.Text = $"{Math.Round(w.Data.Temp)}°";
        }

        static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}
