using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using Crest.Controls;
using Crest.Core;
using Crest.Services;
using Crest.Views;

namespace Crest
{
    public interface ITabView
    {
        void OnActivated();
        void OnDeactivated();
    }

    /// <summary>A transient live activity (HUD, alert) shown in the collapsed notch.</summary>
    public sealed class Toast
    {
        public string Kind = "";
        public string Glyph = "";
        public bool Emoji;
        public Brush? GlyphBrush;
        public string Title = "";
        public string? Value;
        public Brush? ValueBrush;
        public double? Level;
        public int? Battery;
        public bool Charging;
        public TimeSpan Duration = TimeSpan.FromSeconds(2.4);
        public bool IsHud => Level.HasValue;
    }

    public partial class NotchWindow : Window
    {
        public static NotchWindow? Instance { get; private set; }
        static AppSettings S => SettingsStore.Current;

        enum Mode { Idle, Media, Activity, Toast, Hud }

        public static readonly (string Key, string Glyph, string Title, string Sub)[] AllTabs =
        {
            ("Home", Glyphs.Home, "Home", ""),
            ("Shelf", Glyphs.Folder, "Shelf", "Parked files"),
            ("Launch", Glyphs.Link, "Links", "Apps, sites & web search"),
            ("Clipboard", Glyphs.Clipboard, "Clipboard", ""),
            ("Notes", Glyphs.Note, "Notes", "Notes & to-dos"),
            ("Timers", Glyphs.Stopwatch, "Timers", "Focus & intervals"),
            ("System", Glyphs.Chart, "System", "Performance · Network · Power"),
            ("Weather", Glyphs.Cloud, "Weather", ""),
            ("Clock", Glyphs.Calendar, "Clock", "Calendar & world time"),
            ("Units", Glyphs.Calculator, "Units", "Conversion"),
            ("ScreenTime", Glyphs.ScreenTime, "Screen Time", "Today"),
            ("AI", Glyphs.Robot, "AI Coding", "Claude Code & Codex"),
            ("Message", Glyphs.Message, "Message", "Notch banner"),
            ("Settings", Glyphs.Settings, "Settings", "")
        };

        static IEnumerable<(string Key, string Glyph, string Title, string Sub)> Tabs =>
            AllTabs.Where(t => t.Key == "Home" || t.Key == "Settings" || !S.HiddenTabs.Contains(t.Key));

        IntPtr _hwnd, _monitor, _prevFg, _lastFg;
        HwndSource? _source;
        bool _expanded, _hiddenFs, _userHidden, _dragOut, _canActivate, _selecting, _capsInit, _capsState, _tucked;
        int _tabIndex;
        string _lastMinute = "";
        readonly DispatcherTimer _msgTimer = new DispatcherTimer();
        string _msgText = "";
        int _msgPos;
        Mode _mode = Mode.Idle;
        Size _bodySize;
        Toast? _toast;
        string _tab = "Home";
        int _gen;
        DateTime? _outsideSince;
        DateTime _lastDragOver = DateTime.MinValue;
        readonly Rectangle[] _bars;
        readonly double[] _eq = new double[4];
        readonly Random _rng = new Random();
        readonly DispatcherTimer _toastTimer, _hoverTimer, _leaveTimer, _envTimer, _eqTimer, _capsTimer, _bannerTimer;
        readonly Dictionary<string, FrameworkElement> _views = new Dictionary<string, FrameworkElement>();
        readonly Dictionary<string, RadioButton> _tabButtons = new Dictionary<string, RadioButton>();

        // ---------- geometry ----------
        double K => S.Size == "Small" ? 0.86 : S.Size == "Large" ? 1.16 : 1.0;
        /// <summary>Scale of the open panel.</summary>
        double KE => S.Size == "Small" ? 0.9 : S.Size == "Large" ? 1.12 : 1.0;
        bool Island => S.Style == "Island";
        bool HiddenIdle => S.Visibility == "Hidden";
        bool LineIdle => S.Visibility == "Minimal" || (S.Visibility == "Smart" && _tucked);
        bool CornerInfo => S.RestingInfo == "Corners" && !LineIdle && !HiddenIdle;
        double Shoulder => Island ? 0 : 9;
        double TopGap => Island ? 7 : 0;
        double RowH => Math.Round(32 * K);
        Size NotchIdle => new Size(Math.Round(186 * K), RowH);
        Size IdleBody => HiddenIdle ? new Size(0, 0) : LineIdle ? new Size(Math.Round(150 * K), 5) : CornerInfo ? new Size(Math.Round(250 * K), RowH) : NotchIdle;
        Size ActivityBody => new Size(Math.Round(300 * K), RowH);
        Size ToastBody => new Size(Math.Round(350 * K), RowH);
        Size HudBody => new Size(Math.Round(300 * K), Math.Round(50 * K));
        const double BaseW = 744, BaseH = 432;
        Size ExpandedBody => new Size(Math.Round(BaseW * KE), Math.Round(BaseH * KE));
        double ExpandedRadius => (Island ? 30 : 26) * KE;
        Size CollapsedWindow => new Size(Math.Round(400 * K) + 2 * Shoulder + 40 + 2 * (RowH + 18), Math.Round(56 * K) + Math.Max(TopGap, 5) + 14);
        Size ExpandedWindow => new Size(ExpandedBody.Width + 2 * Shoulder + 40, ExpandedBody.Height + TopGap + 30);

        public bool IsExpanded => _expanded;
        public bool UserHidden => _userHidden;
        public bool DragOut { set => _dragOut = value; }
        bool DragHovering => (DateTime.Now - _lastDragOver).TotalMilliseconds < 450;

        public NotchWindow()
        {
            InitializeComponent();
            Instance = this;
            _bars = new[] { Bar0, Bar1, Bar2, Bar3 };

            _toastTimer = new DispatcherTimer();
            _toastTimer.Tick += (s, e) => { _toastTimer.Stop(); _toast = null; UpdateCollapsed(); };
            _hoverTimer = new DispatcherTimer();
            _hoverTimer.Tick += (s, e) => HoverTick();
            _leaveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(110) };
            _leaveTimer.Tick += (s, e) => LeaveTick();
            _envTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
            _envTimer.Tick += (s, e) => EnvTick();
            _eqTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(50) };
            _eqTimer.Tick += (s, e) => EqTick();
            _capsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _capsTimer.Tick += (s, e) => CapsTick();
            _bannerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.2) };
            _bannerTimer.Tick += (s, e) => { _bannerTimer.Stop(); Banner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300))); };

            Notch.MouseEnter += (s, e) => OnHoverEnter();
            HoverZone.MouseEnter += (s, e) => OnHoverEnter();
            Notch.MouseLeave += (s, e) => { if (!_expanded) _hoverTimer.Stop(); };
            HoverZone.MouseLeave += (s, e) => { if (!_expanded) _hoverTimer.Stop(); };
            HoverZone.MouseLeftButtonUp += OnCollapsedClick;
            HookGestures(Notch);
            HookGestures(Bubble);
            PopOutButton.Click += (s, e) => PopOut(_tab, true);
            HookHeaderPull();

            PreviewMouseLeftButtonDown += OnPreviewMouseDown;
            PreviewMouseWheel += OnWheel;
            _scrollTimer.Tick += (s, e) => ScrollStep();
            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Escape && _expanded) { PinButton.IsChecked = false; Collapse(); e.Handled = true; }
            };
            Deactivated += (s, e) =>
            {
                if (_expanded && PinButton.IsChecked != true && !_dragOut && !IsCursorOver(Notch, 6)) Collapse();
            };
            PreviewDragEnter += OnDragEnter;
            PreviewDragOver += (s, e) => _lastDragOver = DateTime.Now;
            PreviewDrop += (s, e) => _lastDragOver = DateTime.MinValue;

            SettingsButton.Click += (s, e) => SelectTab("Settings");
            CloseButton.Click += (s, e) => { PinButton.IsChecked = false; Collapse(); };
            DockBorder.PreviewMouseWheel += (s, e) =>
            {
                var keys = Tabs.Select(t => t.Key).ToList();
                int i = keys.IndexOf(_tab);
                if (i < 0) return;
                i = Math.Clamp(i + (e.Delta < 0 ? 1 : -1), 0, keys.Count - 1);
                SelectTab(keys[i]);
                e.Handled = true;
            };
            _msgTimer.Tick += (s, e) => MessageTick();

            BuildTabs();
            SettingsStore.Changed += ApplySettings;
            SourceInitialized += OnSourceInitialized;
            DpiChanged += (s, e) => Ui.Later(40, () => PositionWindow(_expanded ? ExpandedWindow : CollapsedWindow));
            Closed += (s, e) => Cleanup();
        }

        // ================= lifecycle =================
        void OnSourceInitialized(object? sender, EventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(_hwnd);
            _source.AddHook(WndProc);

            long ex = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
            ex |= Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE;
            ex &= ~Win32.WS_EX_APPWINDOW;
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));

            RegisterHotkeys();
            _tab = S.LastTab;
            ApplySettings();
            StartServices();
            _envTimer.Start();
        }

        void StartServices()
        {
            Safe("media", () => MediaService.Instance.Init(Dispatcher));
            MediaService.Instance.Changed += OnMediaChanged;
            Safe("audio", () => AudioService.Instance.Init(Dispatcher));
            AudioService.Instance.VolumeChanged += OnVolume;
            Safe("brightness", () => BrightnessService.Instance.Init(Dispatcher));
            BrightnessService.Instance.Changed += OnBrightness;
            Safe("power", () => PowerService.Instance.Init(Dispatcher));
            PowerService.Instance.Changed += UpdateBattery;
            PowerService.Instance.AcChanged += OnAc;
            PowerService.Instance.Low += OnLow;
            Safe("privacy", () => PrivacyService.Instance.Init(Dispatcher));
            PrivacyService.Instance.Changed += () => { UpdatePrivacy(); UpdateCollapsed(); };
            Safe("bluetooth", () => BluetoothService.Instance.Init(Dispatcher));
            BluetoothService.Instance.Changed += OnBluetooth;
            Safe("clipboard", () => ClipboardService.Instance.Init(_source!));
            Safe("timers", () => TimerService.Instance.Init(Dispatcher));
            TimerService.Instance.StateChanged += () => UpdateCollapsed();
            TimerService.Instance.Tick += () => { if (_mode == Mode.Activity && _primary?.Id == "timer") RefreshActivityContent(); if (_secondary?.Id == "timer") RenderBubble(); };
            ActivityHub.Register(TimerActivities);
            ActivityHub.Register(MediaActivities);
            ActivityHub.Changed += () => UpdateCollapsed();
            TimerService.Instance.Alert += OnTimerAlert;
            Safe("screentime", () => ScreenTimeService.Instance.Init(Dispatcher));
            ScreenTimeService.Instance.LimitReached += m => ShowToast(new Toast { Kind = "limit", Glyph = "⏳", Emoji = true, Title = "Screen time limit", Value = ScreenTimeService.Format(m * 60), ValueBrush = Ui.Res("OrangeBrush"), Duration = TimeSpan.FromSeconds(4) });
            ClipboardService.Instance.Copied += OnCopied;
            UpdateService.Changed += () =>
            {
                if (!UpdateService.Available || UpdateService.Busy || S.NotifiedVersion == UpdateService.LatestTag) return;
                S.NotifiedVersion = UpdateService.LatestTag ?? "";
                SettingsStore.Save();
                ShowToast(new Toast { Kind = "update", Glyph = Glyphs.Download, GlyphBrush = Ui.Res("BlueBrush"), Title = "Update available", Value = UpdateService.LatestTag, ValueBrush = Ui.Res("BlueBrush"), Duration = TimeSpan.FromSeconds(5) });
            };
            PowerService.Instance.Full += () => { if (S.BatteryAlerts) ShowToast(new Toast { Kind = "power", Title = "Fully charged", Value = "100%", ValueBrush = Ui.Res("GreenBrush"), Battery = 100, Charging = false, Duration = TimeSpan.FromSeconds(2.6) }); };
            Safe("stores", () => { NotesStore.Load(); TrayStore.Shelf.EnsureLoaded(); TrayStore.Launch.EnsureLoaded(); });
            UpdateBattery();
            UpdatePrivacy();
            if (WeatherService.Instance.HasLocation) _ = WeatherService.Instance.RefreshAsync();
        }

        static void Safe(string name, Action a)
        {
            try { a(); } catch (Exception ex) { Log.Error("init " + name, ex); }
        }

        void Cleanup()
        {
            try
            {
                for (int i = 1; i <= 3; i++) Win32.UnregisterHotKey(_hwnd, i);
                BrightnessService.Instance.Stop();
                BluetoothService.Instance.Stop();
            }
            catch { }
        }

        void RegisterHotkeys()
        {
            uint mods = Win32.MOD_CONTROL | Win32.MOD_ALT | Win32.MOD_NOREPEAT;
            if (!Win32.RegisterHotKey(_hwnd, 1, mods, 0x4E)) Log.Write("hotkey Ctrl+Alt+N unavailable");
            if (!Win32.RegisterHotKey(_hwnd, 2, mods, 0x56)) Log.Write("hotkey Ctrl+Alt+V unavailable");
            if (!Win32.RegisterHotKey(_hwnd, 3, mods, 0x50)) Log.Write("hotkey Ctrl+Alt+P unavailable");
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == 0x020E && !_expanded) // WM_MOUSEHWHEEL: two-finger horizontal swipe on a trackpad
            {
                int delta = (short)((wParam.ToInt64() >> 16) & 0xFFFF);
                var target = IsCursorOver(Bubble, 2) && Bubble.IsVisible ? (FrameworkElement)Bubble : IsCursorOver(Notch, 4) ? Notch : null;
                if (target != null) { HorizontalSwipe(target, delta); handled = true; }
                return IntPtr.Zero;
            }
            if (msg == Win32.WM_HOTKEY)
            {
                switch (wParam.ToInt32())
                {
                    case 1: Toggle(); break;
                    case 2: Expand("Clipboard"); break;
                    case 3:
                        var t = TimerService.Instance;
                        t.PomoToggle();
                        if (!t.PomoRunning)
                            ShowToast(new Toast { Kind = "pomo", Glyph = Glyphs.Pause, Title = "Pomodoro paused", Value = Ui.ClockCeil(t.PomoLeft), Duration = TimeSpan.FromSeconds(1.8) });
                        break;
                }
                handled = true;
            }
            return IntPtr.Zero;
        }

        // ================= settings / placement =================
        public void ApplySettings()
        {
            Ui.ApplyAccent(S.Accent);
            if (_hwnd == IntPtr.Zero) return;

            Shape.IsIsland = Island;
            Shape.Shoulder = Shoulder;
            Shape.Stroke = !S.Oled && (Island || S.Outline) ? Ui.Brush(S.Outline ? "#47FFFFFF" : "#2EFFFFFF") : null;
            Shape.ShowLens = S.CameraCutout;
            BubbleBg.Stroke = S.Oled ? null : Ui.Brush("#2EFFFFFF");
            Bubble.Margin = new Thickness(0, Math.Max(TopGap, 5), 0, 0);
            Bubble.Width = Bubble.Height = RowH;
            BuildTabs();
            Notch.Margin = new Thickness(0, TopGap, 0, 0);
            HoverZone.Margin = new Thickness(0, 0, 0, 0);
            HoverZone.Height = Math.Max(10, TopGap + 8);

            ActivityRow.Height = RowH;
            double pad = Shoulder + Math.Round(13 * K);
            CollapsedLayer.Margin = new Thickness(pad, 0, pad, 0);
            LeftText.FontSize = Math.Round(12.5 * K, 1);
            RightText.FontSize = Math.Round(13 * K, 1);
            LeftGlyph.FontSize = Math.Round(14 * K, 1);
            MediaArt.Width = MediaArt.Height = Math.Round(21 * K);
            Equalizer.Height = Math.Round(16 * K);
            HudBar.Margin = new Thickness(2, RowH + Math.Round(2 * K), 2, 0);

            // The panel is laid out at 100% and scaled as a whole, so every size looks identical, just bigger or smaller.
            ExpandedLayer.Width = BaseW - 36;
            ExpandedLayer.Height = BaseH - 26;
            ExpandedLayer.LayoutTransform = new ScaleTransform(KE, KE);
            ExpandedLayer.Margin = new Thickness(0, Math.Round(12 * KE), 0, 0);

            try { Win32.SetWindowDisplayAffinity(_hwnd, S.HideFromCapture ? Win32.WDA_EXCLUDEFROMCAPTURE : Win32.WDA_NONE); } catch { }
            if (S.CapsLockHud) { if (!_capsTimer.IsEnabled) { _capsInit = false; _capsTimer.Start(); } } else _capsTimer.Stop();
            try { ClipboardService.Instance.Apply(); } catch { }

            if (_expanded)
            {
                PositionWindow(ExpandedWindow);
                AnimateBody(ExpandedBody, ExpandedRadius, true);
            }
            else
            {
                PositionWindow(CollapsedWindow);
                UpdateCollapsed(true);
            }
        }

        IntPtr TargetMonitor()
        {
            if (S.Display == "Cursor" && Win32.GetCursorPos(out var p)) return Win32.MonitorFromPoint(p, Win32.MONITOR_DEFAULTTONEAREST);
            return Win32.MonitorFromPoint(new Win32.POINT { X = 0, Y = 0 }, Win32.MONITOR_DEFAULTTOPRIMARY);
        }

        void PositionWindow(Size dip)
        {
            if (_hwnd == IntPtr.Zero) return;
            _monitor = TargetMonitor();
            var b = Win32.MonitorBounds(_monitor);
            double sc = Win32.MonitorScale(_monitor);
            int w = (int)Math.Ceiling(dip.Width * sc), h = (int)Math.Ceiling(dip.Height * sc);
            int x = b.Left + (b.Width - w) / 2 + (int)Math.Round(S.NotchOffset * sc);
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, x, b.Top, w, h, Win32.SWP_NOACTIVATE);
        }

        void ReassertTopmost() =>
            Win32.SetWindowPos(_hwnd, Win32.HWND_TOPMOST, 0, 0, 0, 0, Win32.SWP_NOMOVE | Win32.SWP_NOSIZE | Win32.SWP_NOACTIVATE);

        void SetCanActivate(bool can)
        {
            if (_hwnd == IntPtr.Zero || can == _canActivate) return;
            long ex = (long)Win32.GetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE);
            ex = can ? ex & ~Win32.WS_EX_NOACTIVATE : ex | Win32.WS_EX_NOACTIVATE;
            Win32.SetWindowLongPtr(_hwnd, Win32.GWL_EXSTYLE, new IntPtr(ex));
            _canActivate = can;
        }

        public void SetUserHidden(bool hidden)
        {
            _userHidden = hidden;
            if (hidden) { if (_expanded) Collapse(); Win32.ShowWindow(_hwnd, Win32.SW_HIDE); }
            else if (!_hiddenFs) { Win32.ShowWindow(_hwnd, Win32.SW_SHOWNOACTIVATE); ReassertTopmost(); }
        }

        // ================= expand / collapse =================
        public void Toggle()
        {
            if (_expanded) Collapse(); else Expand(null);
        }

        public void Expand(string? tab)
        {
            if (_hwnd == IntPtr.Zero) return;
            if (_userHidden) SetUserHidden(false);
            if (_hiddenFs) return;
            if (_expanded) { if (tab != null) SelectTab(tab); return; }

            if (tab != null) SelectTab(tab);
            else if (TabHost.Content == null) SelectTab(_tab);

            _expanded = true;
            _gen++;
            RenderBubble();
            _hoverTimer.Stop();
            _outsideSince = null;
            _prevFg = Win32.GetForegroundWindow();
            _toastTimer.Stop();
            _toast = null;
            _eqTimer.Stop();

            PositionWindow(ExpandedWindow);
            HoverZone.Visibility = Visibility.Collapsed;
            Shape.Fill = Brushes.Black;
            (TabHost.Content as ITabView)?.OnActivated();

            ApplyShadow(true);
            UpdateHeader();
            Fade(CollapsedLayer, 0, 90, 0);
            ExpandedLayer.Visibility = Visibility.Visible;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => MovePill(false)));
            Fade(ExpandedLayer, 1, 240, 110);
            ExpandedShift.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(420)) { BeginTime = TimeSpan.FromMilliseconds(90), EasingFunction = new SpringEase { Damping = 0.8 } });
            AnimateBody(ExpandedBody, ExpandedRadius, true);
            _leaveTimer.Start();
        }

        public void Collapse()
        {
            if (!_expanded) return;
            _expanded = false;
            PinButton.IsChecked = false;
            _leaveTimer.Stop();
            (TabHost.Content as ITabView)?.OnDeactivated();

            bool wasForeground = Win32.GetForegroundWindow() == _hwnd;
            SetCanActivate(false);
            Keyboard.ClearFocus();
            if (wasForeground && _prevFg != IntPtr.Zero && _prevFg != _hwnd && Win32.IsWindow(_prevFg)) Win32.SetForegroundWindow(_prevFg);

            Fade(ExpandedLayer, 0, 110, 0);
            ExpandedShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-8, TimeSpan.FromMilliseconds(150)));
            Fade(CollapsedLayer, 1, 200, 230);
            HoverZone.Visibility = Visibility.Visible;
            UpdateCollapsed(true);

            int gen = ++_gen;
            Ui.Later(560, () =>
            {
                if (_expanded || gen != _gen) return;
                ExpandedLayer.Visibility = Visibility.Collapsed;
                ApplyShadow(false);
                PositionWindow(CollapsedWindow);
            });
        }

        void AnimateBody(Size body, double radius, bool expanding)
        {
            var dur = TimeSpan.FromMilliseconds(expanding ? 560 : 470);
            var ease = new SpringEase { Damping = expanding ? 0.7 : 0.82 };
            Notch.BeginAnimation(WidthProperty, new DoubleAnimation(body.Width + 2 * Shoulder, dur) { EasingFunction = ease });
            Notch.BeginAnimation(HeightProperty, new DoubleAnimation(body.Height, dur) { EasingFunction = ease });
            Shape.BeginAnimation(NotchShape.BottomRadiusProperty,
                new DoubleAnimation(radius, TimeSpan.FromMilliseconds(320)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        static void Fade(UIElement el, double to, int ms, int delay) =>
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { BeginTime = TimeSpan.FromMilliseconds(delay) });

        // ================= tabs =================
        void BuildTabs()
        {
            var keys = Tabs.Select(t => t.Key).ToList();
            if (keys.SequenceEqual(_tabButtons.Keys) && TabBar.Children.Count == keys.Count) return;
            TabBar.Children.Clear();
            _tabButtons.Clear();
            foreach (var (key, glyph, title, _) in Tabs)
            {
                var rb = new RadioButton { Style = Ui.StyleRes("TabButton"), Content = glyph, GroupName = "notch-tabs", ToolTip = title };
                var k = key;
                rb.Checked += (s, e) => { if (!_selecting) SelectTab(k); };
                _tabButtons[key] = rb;
                TabBar.Children.Add(rb);
            }
            if (!_tabButtons.ContainsKey(_tab)) _tab = "Home";
            _selecting = true;
            _tabButtons[_tab].IsChecked = true;
            _selecting = false;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => MovePill(false)));
        }

        public void SelectTab(string key)
        {
            if (!_tabButtons.ContainsKey(key))
            {
                if (AllTabs.Any(t => t.Key == key)) { S.HiddenTabs.Remove(key); SettingsStore.Save(); BuildTabs(); }
                else key = "Home";
            }
            bool changed = key != _tab || TabHost.Content == null;
            var old = TabHost.Content as ITabView;
            var keys = Tabs.Select(t => t.Key).ToList();
            int newIndex = keys.IndexOf(key), oldIndex = keys.IndexOf(_tab);
            _tab = key;
            _tabIndex = newIndex;
            if (S.LastTab != key) { S.LastTab = key; SettingsStore.Save(); }
            _selecting = true;
            _tabButtons[key].IsChecked = true;
            _selecting = false;
            if (!changed) return;

            if (_expanded) old?.OnDeactivated();
            if (!_views.TryGetValue(key, out var view))
            {
                try { view = CreateView(key); }
                catch (Exception ex) { Log.Error("view " + key, ex); view = new TextBlock { Text = "This view failed to load. See crest.log.", Foreground = Brushes.White }; }
                _views[key] = view;
            }
            TabHost.Content = view;
            if (_expanded) (view as ITabView)?.OnActivated();
            UpdateHeader();
            MovePill(_expanded && S.TabAnimations);

            // Direction-aware slide: new content comes in from the side you moved towards.
            if (S.TabAnimations && _expanded && oldIndex >= 0 && newIndex != oldIndex)
            {
                double from = newIndex > oldIndex ? 28 : -28;
                TabShift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(from, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = new SpringEase { Damping = 0.86 } });
                view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
            }
            else view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }

        /// <summary>Slide the white dock pill under the selected tab, stretching mid-flight like iOS.</summary>
        void MovePill(bool animate)
        {
            if (!_tabButtons.TryGetValue(_tab, out var rb) || !rb.IsLoaded) return;
            double x;
            try { x = rb.TranslatePoint(new Point(0, 0), PillCanvas).X; } catch { return; }
            if (!animate)
            {
                PillX.BeginAnimation(TranslateTransform.XProperty, null);
                PillX.X = x;
                return;
            }
            double dist = Math.Abs(x - PillX.X);
            PillX.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(x, TimeSpan.FromMilliseconds(420)) { EasingFunction = new SpringEase { Damping = 0.74 } });
            if (dist > 1)
            {
                var stretch = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(360) };
                stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(0)));
                stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1 + Math.Min(0.45, dist / 220), KeyTime.FromPercent(0.35), new QuadraticEase { EasingMode = EasingMode.EaseOut }));
                stretch.KeyFrames.Add(new EasingDoubleKeyFrame(1, KeyTime.FromPercent(1), new SpringEase { Damping = 0.6 }));
                PillScale.BeginAnimation(ScaleTransform.ScaleXProperty, stretch);
            }
        }

        public void UpdateHeader()
        {
            var t = AllTabs.FirstOrDefault(x => x.Key == _tab);
            if (t.Key == null) return;
            HeaderGlyph.Text = t.Glyph;
            HeaderTitle.Text = t.Title;
            string sub = t.Sub;
            if (t.Key == "Home") sub = DateTime.Now.ToString("dddd, d MMM", System.Globalization.CultureInfo.CurrentCulture);
            else if (t.Key == "Clipboard") { int n = ClipboardService.Instance.Items.Count; sub = n == 1 ? "1 item" : $"{n} items"; }
            else if (t.Key == "Weather") sub = S.City ?? "Set a location";
            else if (t.Key == "Settings") { var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version; sub = v == null ? "Crest" : $"Crest {v.Major}.{v.Minor}.{v.Build}"; }
            HeaderSub.Text = sub;
        }

        void ApplyShadow(bool on)
        {
            double blur = S.Oled ? 0 : S.Shadow switch { "Subtle" => 16, "Medium" => 28, "Deep" => 44, _ => 0 };
            if (!on || blur <= 0) { Notch.Effect = null; return; }
            Notch.Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = blur, ShadowDepth = blur / 4, Direction = 270, Opacity = S.Shadow == "Deep" ? 0.75 : 0.55, Color = Colors.Black,
                RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
            };
        }

        static FrameworkElement CreateView(string key) => key switch
        {
            "Home" => new HomeView(),
            "Shelf" => new TrayView(true),
            "Launch" => new TrayView(false),
            "Clipboard" => new ClipboardView(),
            "Notes" => new NotesView(),
            "Timers" => new TimersView(),
            "System" => new SystemView(),
            "Weather" => new WeatherView(),
            "Clock" => new ClockView(),
            "Units" => new UnitsView(),
            "ScreenTime" => new ScreenTimeView(),
            "AI" => new AiView(),
            "Message" => new MessageView(),
            _ => new SettingsView()
        };

        // ================= input =================
        void OnHoverEnter()
        {
            if (_expanded || !S.ExpandOnHover) return;
            _hoverTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(1, S.HoverDelayMs));
            _hoverTimer.Stop();
            _hoverTimer.Start();
        }

        void HoverTick()
        {
            _hoverTimer.Stop();
            if (_expanded || _gDown) return;
            bool leftDown = Win32.GetKeyState(0x01) < 0;
            if (leftDown && !DragHovering) return; // user is dragging a window (e.g. to Snap Layouts) — don't pop open
            if (IsCursorOver(Notch, 4) || IsCursorOver(HoverZone, 2)) Expand(null);
        }

        void OnCollapsedClick(object sender, MouseButtonEventArgs e)
        {
            if (_expanded) return;
            _hoverTimer.Stop();
            Expand(null);
            e.Handled = true;
        }

        // ---------- smooth, distance-based wheel scrolling ----------
        readonly DispatcherTimer _scrollTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(15) };
        readonly Dictionary<ScrollViewer, double> _scrollTargets = new Dictionary<ScrollViewer, double>();

        void OnWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.OriginalSource is DependencyObject src && IsInside(src, DockBorder)) return; // dock flips tabs
            if (e.OriginalSource is DependencyObject d && Ui.FindAncestor<Slider>(d) != null) return;
            var sv = FindScrollable(e.OriginalSource as DependencyObject, e.Delta);
            if (sv == null) return;
            double current = _scrollTargets.TryGetValue(sv, out var t) ? t : sv.VerticalOffset;
            double step = -e.Delta * 0.75;                       // one notch (120) ≈ 90 px, scaled by the actual delta
            _scrollTargets[sv] = Math.Clamp(current + step, 0, sv.ScrollableHeight);
            if (!_scrollTimer.IsEnabled) _scrollTimer.Start();
            e.Handled = true;
        }

        static bool IsInside(DependencyObject child, DependencyObject parent)
        {
            for (var d = child; d != null; d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d == parent) return true;
            return false;
        }

        static ScrollViewer? FindScrollable(DependencyObject? d, int delta)
        {
            while (d != null)
            {
                if (d is ScrollViewer sv && sv.ScrollableHeight > 0.5 &&
                    ((delta < 0 && sv.VerticalOffset < sv.ScrollableHeight - 0.5) || (delta > 0 && sv.VerticalOffset > 0.5)))
                    return sv;
                d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        void ScrollStep()
        {
            foreach (var sv in _scrollTargets.Keys.ToList())
            {
                double target = Math.Clamp(_scrollTargets[sv], 0, sv.ScrollableHeight);
                double cur = sv.VerticalOffset, next = cur + (target - cur) * 0.32;
                if (Math.Abs(target - next) < 0.6) { sv.ScrollToVerticalOffset(target); _scrollTargets.Remove(sv); }
                else sv.ScrollToVerticalOffset(next);
            }
            if (_scrollTargets.Count == 0) _scrollTimer.Stop();
        }

        void OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (!_expanded || _canActivate) return;
            if (Ui.FindAncestor<TextBoxBase>(e.OriginalSource as DependencyObject) != null)
            {
                SetCanActivate(true);
                Activate();
            }
        }

        void OnDragEnter(object sender, DragEventArgs e)
        {
            _lastDragOver = DateTime.Now;
            if (_dragOut) return;
            bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
            bool text = e.Data.GetDataPresent(DataFormats.UnicodeText);
            if (!files && !text) return;
            if (!_expanded) Expand(_tab == "Launch" ? "Launch" : "Shelf");
            else if (_tab != "Shelf" && _tab != "Launch") SelectTab("Shelf");
        }

        void LeaveTick()
        {
            if (!_expanded) { _leaveTimer.Stop(); return; }
            bool keep = PinButton.IsChecked == true || _dragOut || DragHovering || IsCursorOver(Notch, 10)
                        || (IsActive && Keyboard.FocusedElement is TextBoxBase);
            if (keep) { _outsideSince = null; return; }
            _outsideSince ??= DateTime.Now;
            if ((DateTime.Now - _outsideSince.Value).TotalMilliseconds > 380) Collapse();
        }

        bool IsCursorOver(FrameworkElement el, double margin)
        {
            if (!el.IsVisible || !Win32.GetCursorPos(out var p)) return false;
            try
            {
                var pt = el.PointFromScreen(new Point(p.X, p.Y));
                return pt.X >= -margin && pt.Y >= -(margin + TopGap + 2) && pt.X <= el.ActualWidth + margin && pt.Y <= el.ActualHeight + margin;
            }
            catch { return false; }
        }

        // ================= environment =================
        void EnvTick()
        {
            if (_hwnd == IntPtr.Zero) return;
            bool fs = S.HideInFullscreen && IsFullscreenActive();
            if (fs != _hiddenFs)
            {
                _hiddenFs = fs;
                if (fs) { if (_expanded) Collapse(); Win32.ShowWindow(_hwnd, Win32.SW_HIDE); }
                else if (!_userHidden) { Win32.ShowWindow(_hwnd, Win32.SW_SHOWNOACTIVATE); ReassertTopmost(); }
            }
            var fg = Win32.GetForegroundWindow();
            if (fg != _lastFg)
            {
                _lastFg = fg;
                if (!_hiddenFs && !_userHidden) ReassertTopmost();
            }
            if (!_expanded && S.Display == "Cursor" && TargetMonitor() != _monitor) PositionWindow(CollapsedWindow);
            bool tuck = S.Visibility == "Smart" && fg != _hwnd && fg != IntPtr.Zero && Win32.IsZoomed(fg) &&
                        Win32.MonitorFromWindow(fg, Win32.MONITOR_DEFAULTTONEAREST) == _monitor;
            if (tuck != _tucked) { _tucked = tuck; UpdateCollapsed(true); }
            var minute = DateTime.Now.ToString("HHmm");
            if (minute != _lastMinute) { _lastMinute = minute; if (_mode == Mode.Idle && CornerInfo) RefreshActivityContent(); if (_expanded && _tab == "Home") UpdateHeader(); }
        }

        bool IsFullscreenActive()
        {
            try
            {
                if (Win32.SHQueryUserNotificationState(out int st) == 0 && (st == 3 || st == 4)) return true;
                var fg = Win32.GetForegroundWindow();
                if (fg == IntPtr.Zero || fg == _hwnd) return false;
                var cls = Win32.ClassName(fg);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd" ||
                    cls == "Windows.UI.Core.CoreWindow" || cls == "XamlExplorerHostIslandWindow") return false;
                if (Win32.MonitorFromWindow(fg, Win32.MONITOR_DEFAULTTONEAREST) != _monitor) return false;
                if (!Win32.GetWindowRect(fg, out var r)) return false;
                var b = Win32.MonitorBounds(_monitor);
                if (r.Left > b.Left || r.Top > b.Top || r.Right < b.Right || r.Bottom < b.Bottom) return false;
                long style = (long)Win32.GetWindowLongPtr(fg, Win32.GWL_STYLE);
                return (style & Win32.WS_CAPTION) != Win32.WS_CAPTION;
            }
            catch { return false; }
        }

        void CapsTick()
        {
            bool on = (Win32.GetKeyState(0x14) & 1) != 0;
            if (!_capsInit) { _capsInit = true; _capsState = on; return; }
            if (on == _capsState) return;
            _capsState = on;
            ShowToast(new Toast
            {
                Kind = "caps", Glyph = Glyphs.Keyboard, Title = "Caps Lock", Value = on ? "On" : "Off",
                ValueBrush = on ? (Brush)FindResource("AccentBrush") : Ui.Res("Text2Brush"), Duration = TimeSpan.FromSeconds(1.4)
            });
        }

        // ================= live activities =================
        public void ShowToast(Toast t)
        {
            if (_hiddenFs || _userHidden || _hwnd == IntPtr.Zero) return;
            if (_expanded) { if (!t.IsHud) ShowBanner(t); return; }
            bool same = _toast != null && _toast.Kind == t.Kind && _mode != Mode.Idle;
            _toast = t;
            _toastTimer.Stop();
            _toastTimer.Interval = t.Duration;
            _toastTimer.Start();
            if (same) { RightBattery.Visibility = t.Battery.HasValue ? Visibility.Visible : Visibility.Collapsed; RefreshActivityContent(); }
            else UpdateCollapsed();
        }

        public void ShowBanner(Toast t)
        {
            BannerGlyph.FontFamily = (FontFamily)FindResource(t.Emoji ? "EmojiFont" : "IconFont");
            BannerGlyph.Text = t.Glyph;
            BannerGlyph.Visibility = string.IsNullOrEmpty(t.Glyph) ? Visibility.Collapsed : Visibility.Visible;
            BannerGlyph.Foreground = t.GlyphBrush ?? Brushes.White;
            BannerText.Text = string.IsNullOrEmpty(t.Value) ? t.Title : $"{t.Title}  ·  {t.Value}";
            Banner.BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            _bannerTimer.Stop();
            _bannerTimer.Start();
        }

        void UpdateCollapsed(bool force = false)
        {
            if (_expanded || _hwnd == IntPtr.Zero) return;
            var acts = ActivityHub.Current();
            _dismissed.RemoveWhere(k => !acts.Any(a => a.Key == k));
            acts = acts.Where(a => !_dismissed.Contains(a.Key)).ToList();
            _primary = _toast == null ? acts.FirstOrDefault() : null;
            _secondary = _toast == null ? acts.Skip(1).FirstOrDefault() : null;
            Mode m = _toast != null ? (_toast.IsHud ? Mode.Hud : Mode.Toast)
                   : _primary == null ? Mode.Idle
                   : _primary.Id == "media" ? Mode.Media
                   : Mode.Activity;
            bool modeChanged = m != _mode;
            _mode = m;

            bool privacy = S.PrivacyDots && (PrivacyService.Instance.MicInUse || PrivacyService.Instance.CamInUse);
            MediaArt.Visibility = m == Mode.Media ? Visibility.Visible : Visibility.Collapsed;
            Equalizer.Visibility = m == Mode.Media ? Visibility.Visible : Visibility.Collapsed;
            bool labelled = m == Mode.Activity || m == Mode.Toast || m == Mode.Hud || (m == Mode.Idle && CornerInfo);
            LeftLabel.Visibility = labelled ? Visibility.Visible : Visibility.Collapsed;
            RightText.Visibility = labelled ? Visibility.Visible : Visibility.Collapsed;
            RightBattery.Visibility = (m == Mode.Toast && _toast?.Battery != null) || (m == Mode.Idle && CornerInfo && PowerService.Instance.HasBattery) ? Visibility.Visible : Visibility.Collapsed;
            if (_toast?.Kind != "msg") { _msgTimer.Stop(); LeftText.MaxWidth = 170; LeftText.BeginAnimation(OpacityProperty, null); }
            bool bar = m == Mode.Hud || (m == Mode.Activity && _primary?.Progress != null);
            HudBar.Visibility = bar ? Visibility.Visible : Visibility.Collapsed;
            if (m != Mode.Activity) { ActionPanel.Visibility = Visibility.Collapsed; LeftGlyph.BeginAnimation(OpacityProperty, null); }
            RefreshActivityContent();

            Size body = m switch
            {
                Mode.Idle => (LineIdle || HiddenIdle) && privacy ? NotchIdle : IdleBody,
                Mode.Media => ActivityBody,
                Mode.Activity => new Size(_primary!.Actions.Count > 0 ? Math.Round(390 * K) : ActivityBody.Width, _primary.Progress != null ? HudBody.Height : RowH),
                Mode.Hud => HudBody,
                _ => _toast?.Kind == "msg" ? new Size(Math.Round(420 * K), RowH) : ToastBody
            };
            bool line = m == Mode.Idle && LineIdle && !privacy;
            double radius = Island ? body.Height / 2 : line ? 2.5 : m == Mode.Idle ? Math.Round(11 * K) : m == Mode.Hud ? Math.Round(16 * K) : Math.Round(13 * K);
            Shape.Fill = line ? Ui.Brush("#FF48484A") : Brushes.Black;
            HoverZone.Width = Math.Max(Math.Max(body.Width, IdleBody.Width), 150) + 2 * Shoulder;
            HoverZone.Visibility = HiddenIdle || _expanded ? Visibility.Collapsed : Visibility.Visible;

            if (force || modeChanged || body != _bodySize)
            {
                _bodySize = body;
                AnimateBody(body, radius, false);
                if (modeChanged && m != Mode.Idle)
                {
                    LeftSlot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
                    RightSlot.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)));
                }
            }

            if (m == Mode.Media) { if (!_eqTimer.IsEnabled) _eqTimer.Start(); }
            else _eqTimer.Stop();
            UpdatePrivacy();
            RenderBubble();
        }

        void RefreshActivityContent()
        {
            switch (_mode)
            {
                case Mode.Media:
                    var md = MediaService.Instance;
                    if (md.Art != null)
                    {
                        MediaArt.Background = new ImageBrush(md.Art) { Stretch = Stretch.UniformToFill };
                        MediaArtGlyph.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        MediaArt.Background = Ui.Brush("#FF2C2C2E");
                        MediaArtGlyph.Visibility = Visibility.Visible;
                    }
                    if (S.EqColor == "Rainbow")
                    {
                        var hues = new[] { "#FF453A", "#FF9F0A", "#30D158", "#0A84FF" };
                        for (int i = 0; i < _bars.Length; i++) _bars[i].Fill = Ui.Brush(hues[i]);
                    }
                    else
                    {
                        var eq = new SolidColorBrush(S.EqColor == "Accent" ? Ui.AccentColor : md.ArtColor ?? Ui.AccentColor);
                        eq.Freeze();
                        foreach (var b in _bars) b.Fill = eq;
                    }
                    break;

                case Mode.Idle:
                    if (!CornerInfo) break;
                    LeftGlyph.Visibility = Visibility.Collapsed;
                    LeftText.Margin = new Thickness(0);
                    LeftText.Visibility = Visibility.Visible;
                    LeftText.Foreground = Brushes.White;
                    LeftText.Text = DateTime.Now.ToString(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H") ? "HH:mm" : "h:mm", System.Globalization.CultureInfo.CurrentCulture);
                    var pw = PowerService.Instance;
                    RightText.Text = pw.HasBattery ? $"{pw.Percent}%" : DateTime.Now.ToString("ddd d", System.Globalization.CultureInfo.CurrentCulture);
                    RightText.Foreground = Ui.Res("Text2Brush");
                    RightBattery.Level = pw.Percent;
                    RightBattery.Charging = pw.OnAc && pw.Percent < 100;
                    break;

                case Mode.Activity:
                    var act = _primary;
                    if (act == null) break;
                    LeftGlyph.FontFamily = (FontFamily)FindResource(act.Emoji ? "EmojiFont" : "IconFont");
                    LeftGlyph.Text = act.Glyph;
                    LeftGlyph.Visibility = string.IsNullOrEmpty(act.Glyph) ? Visibility.Collapsed : Visibility.Visible;
                    if (act.GlyphBrush != null) LeftGlyph.Foreground = act.GlyphBrush; else LeftGlyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                    if (act.Pulse && LeftGlyph.Tag as string != "pulse")
                    {
                        LeftGlyph.Tag = "pulse";
                        LeftGlyph.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.3, TimeSpan.FromMilliseconds(650)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                    }
                    else if (!act.Pulse && LeftGlyph.Tag as string == "pulse") { LeftGlyph.Tag = null; LeftGlyph.BeginAnimation(OpacityProperty, null); }
                    LeftText.Text = act.Title;
                    LeftText.Margin = new Thickness(string.IsNullOrEmpty(act.Glyph) ? 0 : 7, 0, 0, 0);
                    LeftText.Visibility = string.IsNullOrEmpty(act.Title) ? Visibility.Collapsed : Visibility.Visible;
                    LeftText.Foreground = Ui.Res("Text2Brush");
                    RightText.Text = act.Value;
                    if (act.ValueBrush != null) RightText.Foreground = act.ValueBrush; else RightText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                    if (act.Progress != null)
                    {
                        HudFill.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                        HudScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(Math.Clamp(act.Progress.Value, 0, 1), TimeSpan.FromMilliseconds(200)));
                    }
                    RenderActions(act);
                    break;

                case Mode.Toast:
                case Mode.Hud:
                    var t = _toast;
                    if (t == null) break;
                    LeftGlyph.FontFamily = (FontFamily)FindResource(t.Emoji ? "EmojiFont" : "IconFont");
                    LeftGlyph.Text = t.Glyph;
                    LeftGlyph.Visibility = string.IsNullOrEmpty(t.Glyph) ? Visibility.Collapsed : Visibility.Visible;
                    LeftGlyph.Foreground = t.GlyphBrush ?? Brushes.White;
                    LeftText.Text = t.Title;
                    LeftText.Visibility = string.IsNullOrEmpty(t.Title) ? Visibility.Collapsed : Visibility.Visible;
                    LeftText.Margin = new Thickness(string.IsNullOrEmpty(t.Glyph) ? 0 : 7, 0, 0, 0);
                    LeftText.Foreground = Brushes.White;
                    RightText.Text = t.Value ?? "";
                    RightText.Foreground = t.ValueBrush ?? Brushes.White;
                    if (t.Kind == "msg")
                    {
                        LeftText.MaxWidth = Math.Round(420 * K) - 2 * Math.Round(13 * K) - 30;
                        LeftText.Foreground = t.GlyphBrush ?? Brushes.White;
                        StartMessage(t.Title);
                    }
                    if (t.Battery.HasValue) { RightBattery.Level = t.Battery.Value; RightBattery.Charging = t.Charging; }
                    if (t.IsHud) HudFill.Background = Brushes.White;
                    if (t.IsHud)
                        HudScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                            new DoubleAnimation(Math.Clamp(t.Level!.Value, 0, 1), TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    break;
            }
        }

        // ================= split pill, gestures, pop-out =================
        LiveActivity? _primary, _secondary;
        readonly HashSet<string> _dismissed = new HashSet<string>();
        Point _gStart;
        bool _gDown, _gHoriz, _gPull;
        FrameworkElement? _gTarget;
        double _swipeAccum;
        DateTime _swipeAt;

        IEnumerable<LiveActivity> TimerActivities()
        {
            if (!S.TimerActivity) yield break;
            var t = TimerService.Instance;
            var a = t.LiveActivity;
            if (a == null) yield break;
            string kind = t.PomoRunning ? "pomo-" + t.Phase : t.CdRunning ? "cd" : "sw";
            yield return new LiveActivity
            {
                Id = "timer", Priority = 60, Glyph = Glyphs.Stopwatch, Title = a.Value.Label, Value = a.Value.Time,
                Tab = "Timers", Signature = kind, Progress = null
            };
        }

        IEnumerable<LiveActivity> MediaActivities()
        {
            var md = MediaService.Instance;
            if (!S.MediaActivity || !md.HasSession || !md.IsPlaying) yield break;
            yield return new LiveActivity { Id = "media", Priority = 40, Glyph = Glyphs.Music, Title = md.Title, Art = md.Art, Tab = "Home", Signature = md.Title + "|" + md.AppName };
        }

        void RenderActions(LiveActivity act)
        {
            ActionPanel.Children.Clear();
            ActionPanel.Visibility = act.Actions.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var (id, label) in act.Actions.Take(2))
            {
                var b = new Button { Style = Ui.StyleRes("Pill"), Content = label, Padding = new Thickness(10, 2, 10, 2), FontSize = Math.Round(11.5 * K, 1), Margin = new Thickness(4, 0, 0, 0), Background = Ui.Res("HoverBrush") };
                var aid = id;
                b.Click += (s, e) => { e.Handled = true; try { act.OnAction?.Invoke(aid); } catch (Exception ex) { Log.Error("activity action", ex); } };
                ActionPanel.Children.Add(b);
            }
        }

        void RenderBubble()
        {
            var sec = _expanded ? null : _secondary;
            if (sec == null)
            {
                if (Bubble.Visibility == Visibility.Visible)
                {
                    var shrink = new DoubleAnimation(0.2, TimeSpan.FromMilliseconds(180));
                    shrink.Completed += (s, e) => { if (_secondary == null || _expanded) Bubble.Visibility = Visibility.Collapsed; };
                    BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
                    BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.2, TimeSpan.FromMilliseconds(180)));
                }
                return;
            }
            bool appearing = Bubble.Visibility != Visibility.Visible;
            Bubble.Visibility = Visibility.Visible;
            Bubble.ToolTip = string.IsNullOrEmpty(sec.Value) ? sec.Title : sec.Title + "  ·  " + sec.Value;
            if (sec.Art != null)
            {
                BubbleArt.Fill = new ImageBrush(sec.Art) { Stretch = Stretch.UniformToFill };
                BubbleArt.Visibility = Visibility.Visible;
                BubbleGlyph.Visibility = Visibility.Collapsed;
            }
            else
            {
                BubbleArt.Visibility = Visibility.Collapsed;
                BubbleGlyph.Visibility = Visibility.Visible;
                BubbleGlyph.FontFamily = (FontFamily)FindResource(sec.Emoji ? "EmojiFont" : "IconFont");
                BubbleGlyph.Text = sec.Glyph;
                if (sec.GlyphBrush != null) BubbleGlyph.Foreground = sec.GlyphBrush; else BubbleGlyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            }
            BubbleRing.Visibility = sec.Progress != null ? Visibility.Visible : Visibility.Collapsed;
            if (sec.Progress != null) BubbleRing.Progress = sec.Progress.Value;

            // Sit just right of the notch body, spring along as the notch morphs.
            double target = _bodySize.Width / 2 + Shoulder + 8 + RowH / 2;
            BubbleX.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(520)) { EasingFunction = new SpringEase { Damping = 0.72 } });
            if (appearing)
            {
                BubbleX.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(target - RowH, target, TimeSpan.FromMilliseconds(560)) { EasingFunction = new SpringEase { Damping = 0.66 } });
                BubbleScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new SpringEase { Damping = 0.62 } });
                BubbleScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.3, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new SpringEase { Damping = 0.62 } });
            }
        }

        TranslateTransform DragTransform(FrameworkElement el) => el == Bubble ? BubbleDrag : NotchShift;

        void HookGestures(FrameworkElement el)
        {
            el.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (_expanded) return;
                if (Ui.FindAncestor<System.Windows.Controls.Primitives.ButtonBase>(e.OriginalSource as DependencyObject) != null) return;
                _gDown = true; _gHoriz = _gPull = false; _gTarget = el;
                _gStart = e.GetPosition(this);
                _hoverTimer.Stop();
                el.CaptureMouse();
                e.Handled = true;
            };
            el.PreviewMouseMove += (s, e) =>
            {
                if (!_gDown || _gTarget != el) return;
                var p = e.GetPosition(this);
                double dx = p.X - _gStart.X, dy = p.Y - _gStart.Y;
                if (!_gHoriz && !_gPull)
                {
                    if (Math.Abs(dx) > 8 && Math.Abs(dx) > Math.Abs(dy)) _gHoriz = true;
                    else if (dy > 10 && dy > Math.Abs(dx)) _gPull = true;
                }
                var tt = DragTransform(el);
                if (_gHoriz) { tt.BeginAnimation(TranslateTransform.XProperty, null); tt.X = dx * 0.75; el.Opacity = Math.Max(0.35, 1 - Math.Abs(dx) / 260); }
                if (_gPull) { tt.BeginAnimation(TranslateTransform.YProperty, null); tt.Y = Math.Min(40, dy * 0.4); }
            };
            el.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (!_gDown || _gTarget != el) return;
                _gDown = false;
                el.ReleaseMouseCapture();
                var p = e.GetPosition(this);
                double dx = p.X - _gStart.X, dy = p.Y - _gStart.Y;
                e.Handled = true;
                if (_gHoriz && Math.Abs(dx) > 70) { Dismiss(el, Math.Sign(dx)); return; }
                SpringBack(el);
                if (_gPull && dy > 55) { PopOut(el == Bubble ? _secondary?.Tab ?? _tab : _primary?.Tab ?? _tab, false); return; }
                if (Math.Abs(dx) < 6 && Math.Abs(dy) < 6)
                {
                    if (el == Bubble) Expand(_secondary?.Tab);
                    else Expand(_toast == null ? _primary?.Tab : null);
                }
            };
            el.LostMouseCapture += (s, e) => { if (_gDown && _gTarget == el) { _gDown = false; SpringBack(el); } };
        }

        void SpringBack(FrameworkElement el)
        {
            var tt = DragTransform(el);
            tt.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new SpringEase { Damping = 0.6 } });
            tt.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(420)) { EasingFunction = new SpringEase { Damping = 0.6 } });
            el.BeginAnimation(OpacityProperty, null);
            el.Opacity = 1;
        }

        /// <summary>Hide the activity shown by this element (notch or bubble) without stopping it.</summary>
        void Dismiss(FrameworkElement el, int dir)
        {
            var tt = DragTransform(el);
            var fly = new DoubleAnimation(dir * 240, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            fly.Completed += (s, e) =>
            {
                if (el == Bubble) { if (_secondary != null) _dismissed.Add(_secondary.Key); }
                else if (_toast != null) { _toast = null; _toastTimer.Stop(); }
                else if (_primary != null) _dismissed.Add(_primary.Key);
                tt.BeginAnimation(TranslateTransform.XProperty, null);
                tt.X = 0;
                el.Opacity = 1;
                if (el == Bubble) { Bubble.Visibility = Visibility.Collapsed; }
                UpdateCollapsed(true);
                if (el != Bubble) Notch.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
            };
            tt.BeginAnimation(TranslateTransform.XProperty, fly);
            el.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(200)));
        }

        void HorizontalSwipe(FrameworkElement el, int delta)
        {
            if ((DateTime.Now - _swipeAt).TotalMilliseconds > 350) _swipeAccum = 0;
            _swipeAt = DateTime.Now;
            _swipeAccum += delta;
            var tt = DragTransform(el);
            tt.BeginAnimation(TranslateTransform.XProperty, null);
            tt.X = Math.Clamp(_swipeAccum * 0.25, -90, 90);
            if (Math.Abs(_swipeAccum) > 360) { _swipeAccum = 0; Dismiss(el, Math.Sign(tt.X == 0 ? 1 : tt.X)); return; }
            var at = _swipeAt;
            Ui.Later(380, () => { if (_swipeAt == at && !_gDown) { _swipeAccum = 0; SpringBack(el); } });
        }

        void HookHeaderPull()
        {
            Point start = default;
            bool down = false;
            HeaderLeft.PreviewMouseLeftButtonDown += (s, e) => { start = e.GetPosition(this); down = true; HeaderLeft.CaptureMouse(); };
            HeaderLeft.PreviewMouseMove += (s, e) =>
            {
                if (!down) return;
                double dy = e.GetPosition(this).Y - start.Y;
                ExpandedShift.BeginAnimation(TranslateTransform.YProperty, null);
                ExpandedShift.Y = Math.Clamp(dy * 0.3, 0, 30);
                if (dy > 60) { down = false; HeaderLeft.ReleaseMouseCapture(); ExpandedShift.Y = 0; PopOut(_tab, true); }
            };
            HeaderLeft.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (!down) return;
                down = false;
                HeaderLeft.ReleaseMouseCapture();
                ExpandedShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new SpringEase { Damping = 0.7 } });
            };
        }

        /// <summary>Detach a tab into a persistent floating widget.</summary>
        public void PopOut(string? tab, bool fromExpanded)
        {
            tab ??= _tab;
            if (tab == "Settings") tab = "Home";
            Point? at = null;
            if (Win32.GetCursorPos(out var cp))
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                at = new Point(cp.X / dpi.DpiScaleX, cp.Y / dpi.DpiScaleY);
            }
            if (fromExpanded) { PinButton.IsChecked = false; Collapse(); }
            FloatWindow.Open(tab, at);
        }

        internal static FrameworkElement CreateViewFor(string key) => CreateView(key);

        void EqTick()
        {
            double peak = AudioService.Instance.Peak;
            double maxH = Math.Round(16 * K), minH = 3;
            bool synthetic = peak < 0.003;
            for (int i = 0; i < 4; i++)
            {
                double shape = i == 1 || i == 2 ? 1.0 : 0.72;
                double level = synthetic
                    ? 0.25 + 0.45 * _rng.NextDouble()
                    : Math.Clamp(peak * 1.7 * (0.55 + 0.45 * _rng.NextDouble()), 0, 1);
                double target = minH + (maxH - minH) * level * shape;
                _eq[i] += (target - _eq[i]) * (synthetic ? 0.25 : 0.5);
                _bars[i].Height = Math.Max(minH, _eq[i]);
            }
        }

        // ================= event handlers =================
        void OnMediaChanged()
        {
            if (_mode == Mode.Media) RefreshActivityContent();
            UpdateCollapsed();
        }

        void OnVolume(float v, bool muted)
        {
            if (!S.VolumeHud) return;
            string glyph = muted || v < 0.005 ? Glyphs.Mute : v < 0.34 ? Glyphs.Volume1 : v < 0.67 ? Glyphs.Volume2 : Glyphs.Volume3;
            ShowToast(new Toast
            {
                Kind = "vol", Glyph = glyph, Value = muted ? "Muted" : Math.Round(v * 100).ToString(),
                Level = muted ? 0 : v, Duration = TimeSpan.FromSeconds(1.5)
            });
        }

        void OnBrightness(int b)
        {
            if (!S.BrightnessHud) return;
            ShowToast(new Toast { Kind = "bri", Glyph = Glyphs.Brightness, Value = b.ToString(), Level = b / 100.0, Duration = TimeSpan.FromSeconds(1.5) });
        }

        void OnAc(bool onAc)
        {
            UpdateBattery();
            if (!S.BatteryAlerts) return;
            var p = PowerService.Instance;
            ShowToast(new Toast
            {
                Kind = "power", Title = onAc ? "Charging" : "On battery", Value = $"{p.Percent}%",
                ValueBrush = onAc ? Ui.Res("GreenBrush") : null, Battery = p.Percent, Charging = onAc, Duration = TimeSpan.FromSeconds(2.6)
            });
        }

        void OnLow(int pct)
        {
            if (!S.BatteryAlerts) return;
            ShowToast(new Toast
            {
                Kind = "low", Title = "Low battery", Value = $"{pct}%", ValueBrush = Ui.Res("RedBrush"),
                Battery = pct, Duration = TimeSpan.FromSeconds(4)
            });
        }

        void OnBluetooth(string name, bool connected)
        {
            if (!S.BluetoothAlerts) return;
            ShowToast(new Toast
            {
                Kind = "bt", Glyph = BluetoothService.LooksLikeAudio(name) ? Glyphs.Headphone : Glyphs.Bluetooth, GlyphBrush = Ui.Res("BlueBrush"),
                Title = name, Value = connected ? "Connected" : "Disconnected", ValueBrush = connected ? Ui.Res("GreenBrush") : Ui.Res("Text2Brush"),
                Duration = TimeSpan.FromSeconds(2.8)
            });
        }

        void OnCopied(ClipItem item)
        {
            if (!S.CopiedHud) return;
            string what = item.Kind == "image" ? "Image" : item.Kind == "files" ? (item.Files?.Count == 1 ? "1 file" : $"{item.Files?.Count} files") : item.IsLink ? "Link" : "Text";
            string? value = S.CopiedHideContent || item.Kind != "text" ? what : item.Preview;
            ShowToast(new Toast { Kind = "copied", Glyph = Glyphs.Copy, Title = "Copied", Value = value, ValueBrush = Ui.Res("Text2Brush"), Duration = TimeSpan.FromSeconds(1.3) });
        }

        /// <summary>Show custom text on the collapsed notch (Message tab).</summary>
        public void Flash(string text, Brush color, int seconds)
        {
            if (_expanded) { PinButton.IsChecked = false; Collapse(); }
            Ui.Later(_expanded ? 0 : 450, () => ShowToast(new Toast { Kind = "msg", Glyph = "", Title = text, GlyphBrush = color, Duration = TimeSpan.FromSeconds(Math.Max(2, seconds)) }));
        }

        void StartMessage(string text)
        {
            _msgText = text;
            _msgPos = 0;
            _msgTimer.Stop();
            LeftText.BeginAnimation(OpacityProperty, null);
            switch (S.MsgAnim)
            {
                case "Scroll":
                    _msgTimer.Interval = TimeSpan.FromMilliseconds(130);
                    LeftText.Text = text;
                    if (text.Length > 34) _msgTimer.Start();
                    break;
                case "Pulse":
                    LeftText.Text = text;
                    LeftText.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.35, TimeSpan.FromMilliseconds(700)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
                    break;
                default:
                    _msgTimer.Interval = TimeSpan.FromMilliseconds(45);
                    LeftText.Text = "";
                    _msgTimer.Start();
                    break;
            }
        }

        void MessageTick()
        {
            if (_toast?.Kind != "msg") { _msgTimer.Stop(); return; }
            if (S.MsgAnim == "Scroll")
            {
                var padded = _msgText + "      ";
                _msgPos = (_msgPos + 1) % padded.Length;
                LeftText.Text = (padded.Substring(_msgPos) + padded.Substring(0, _msgPos));
            }
            else
            {
                _msgPos++;
                LeftText.Text = _msgText.Substring(0, Math.Min(_msgPos, _msgText.Length)) + (_msgPos < _msgText.Length ? "▌" : "");
                if (_msgPos >= _msgText.Length) _msgTimer.Stop();
            }
        }

        void OnTimerAlert(TimerAlert a)
        {
            ShowToast(new Toast { Kind = "timer-alert", Glyph = a.Glyph, Emoji = a.Emoji, Title = a.Title, Value = a.Value, Duration = TimeSpan.FromSeconds(4.5) });
        }

        void UpdateBattery()
        {
            var p = PowerService.Instance;
            HeaderBattery.Visibility = p.HasBattery ? Visibility.Visible : Visibility.Collapsed;
            HeaderBatteryText.Text = $"{p.Percent}%";
            HeaderBatteryGlyph.Level = p.Percent;
            HeaderBatteryGlyph.Charging = p.OnAc && p.Percent < 100;
        }

        void UpdatePrivacy()
        {
            var pv = PrivacyService.Instance;
            bool on = S.PrivacyDots;
            bool mic = on && pv.MicInUse, cam = on && pv.CamInUse;
            MicDot.Visibility = HeaderMicDot.Visibility = mic ? Visibility.Visible : Visibility.Collapsed;
            CamDot.Visibility = HeaderCamDot.Visibility = cam ? Visibility.Visible : Visibility.Collapsed;
            bool any = mic || cam;
            bool line = _mode == Mode.Idle && LineIdle && !any;
            PrivacyDots.Visibility = any && !line ? Visibility.Visible : Visibility.Collapsed;
            PrivacyDots.Margin = new Thickness(_mode == Mode.Idle ? 0 : 8, 0, 0, 0);
            HeaderPrivacy.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            var tip = new List<string>();
            if (mic) tip.Add("Microphone: " + pv.MicApps);
            if (cam) tip.Add("Camera: " + pv.CamApps);
            HeaderPrivacy.ToolTip = tip.Count > 0 ? string.Join("\n", tip) : null;
        }

        public void Welcome()
        {
            Expand("Home");
            PinButton.IsChecked = true;
            ShowBanner(new Toast { Glyph = Glyphs.Home, Title = "Welcome to Crest", Value = "hover the notch anytime · Ctrl+Alt+N" });
            Ui.Later(6500, () => PinButton.IsChecked = false);
        }
    }
}
