using System;
using System.Collections.Generic;
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

        enum Mode { Idle, Media, Timer, Toast, Hud }

        static readonly (string Key, string Glyph, string Tip)[] Tabs =
        {
            ("Home", Glyphs.Home, "Home"),
            ("Shelf", Glyphs.Folder, "Shelf — drop files to keep them handy"),
            ("Launch", Glyphs.Apps, "Quick launch"),
            ("Clipboard", Glyphs.Clipboard, "Clipboard history  (Ctrl+Alt+V)"),
            ("Notes", Glyphs.Note, "Notes & to-dos"),
            ("Timers", Glyphs.Stopwatch, "Pomodoro, timer, stopwatch, water"),
            ("System", Glyphs.Chart, "System monitor"),
            ("Weather", Glyphs.Cloud, "Weather"),
            ("Clock", Glyphs.Calendar, "Calendar & world clock"),
            ("Settings", Glyphs.Settings, "Settings")
        };

        IntPtr _hwnd, _monitor, _prevFg, _lastFg;
        HwndSource? _source;
        bool _expanded, _hiddenFs, _userHidden, _dragOut, _canActivate, _selecting, _capsInit, _capsState;
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
        double K => S.Size == "Small" ? 0.9 : S.Size == "Large" ? 1.12 : 1.0;
        bool Island => S.Style == "Island";
        bool LineIdle => S.IdleLook == "Line";
        double Shoulder => Island ? 0 : 9;
        double TopGap => Island ? 7 : 0;
        double RowH => Math.Round(32 * K);
        Size NotchIdle => new Size(Math.Round(186 * K), RowH);
        Size IdleBody => LineIdle ? new Size(Math.Round(150 * K), 5) : NotchIdle;
        Size ActivityBody => new Size(Math.Round(300 * K), RowH);
        Size ToastBody => new Size(Math.Round(350 * K), RowH);
        Size HudBody => new Size(Math.Round(300 * K), Math.Round(50 * K));
        static readonly Size ExpandedBody = new Size(740, 376);
        double ExpandedRadius => Island ? 30 : 26;
        Size CollapsedWindow => new Size(Math.Round(360 * K) + 2 * Shoulder + 40, Math.Round(56 * K) + TopGap + 14);
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
            Notch.MouseLeftButtonUp += OnCollapsedClick;
            HoverZone.MouseLeftButtonUp += OnCollapsedClick;

            PreviewMouseLeftButtonDown += OnPreviewMouseDown;
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
            TimerService.Instance.Tick += () => { if (_mode == Mode.Timer) RefreshActivityContent(); };
            TimerService.Instance.Alert += OnTimerAlert;
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
            Shape.Stroke = Island ? Ui.Brush("#2EFFFFFF") : null;
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

            ExpandedLayer.Width = ExpandedBody.Width - 36;
            ExpandedLayer.Height = ExpandedBody.Height - 26;
            ExpandedLayer.Margin = new Thickness(0, 12, 0, 0);

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
            int x = b.Left + (b.Width - w) / 2;
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

            Fade(CollapsedLayer, 0, 90, 0);
            ExpandedLayer.Visibility = Visibility.Visible;
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
            foreach (var (key, glyph, tip) in Tabs)
            {
                var rb = new RadioButton { Style = Ui.StyleRes("TabButton"), Content = glyph, GroupName = "notch-tabs", ToolTip = tip };
                var k = key;
                rb.Checked += (s, e) => { if (!_selecting) SelectTab(k); };
                _tabButtons[key] = rb;
                TabBar.Children.Add(rb);
            }
        }

        public void SelectTab(string key)
        {
            if (!_tabButtons.ContainsKey(key)) key = "Home";
            bool changed = key != _tab || TabHost.Content == null;
            var old = TabHost.Content as ITabView;
            _tab = key;
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
            view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)));
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
            if (_expanded) return;
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
            var timer = S.TimerActivity ? TimerService.Instance.LiveActivity : null;
            var media = MediaService.Instance;
            Mode m = _toast != null ? (_toast.IsHud ? Mode.Hud : Mode.Toast)
                   : timer != null ? Mode.Timer
                   : S.MediaActivity && media.HasSession && media.IsPlaying ? Mode.Media
                   : Mode.Idle;
            bool modeChanged = m != _mode;
            _mode = m;

            bool privacy = S.PrivacyDots && (PrivacyService.Instance.MicInUse || PrivacyService.Instance.CamInUse);
            MediaArt.Visibility = m == Mode.Media ? Visibility.Visible : Visibility.Collapsed;
            Equalizer.Visibility = m == Mode.Media ? Visibility.Visible : Visibility.Collapsed;
            bool labelled = m == Mode.Timer || m == Mode.Toast || m == Mode.Hud;
            LeftLabel.Visibility = labelled ? Visibility.Visible : Visibility.Collapsed;
            RightText.Visibility = labelled ? Visibility.Visible : Visibility.Collapsed;
            RightBattery.Visibility = m == Mode.Toast && _toast?.Battery != null ? Visibility.Visible : Visibility.Collapsed;
            HudBar.Visibility = m == Mode.Hud ? Visibility.Visible : Visibility.Collapsed;
            RefreshActivityContent();

            Size body = m switch
            {
                Mode.Idle => LineIdle && privacy ? NotchIdle : IdleBody,
                Mode.Media => ActivityBody,
                Mode.Timer => ActivityBody,
                Mode.Hud => HudBody,
                _ => ToastBody
            };
            bool line = m == Mode.Idle && LineIdle && !privacy;
            double radius = Island ? body.Height / 2 : line ? 2.5 : m == Mode.Idle ? Math.Round(11 * K) : m == Mode.Hud ? Math.Round(16 * K) : Math.Round(13 * K);
            Shape.Fill = line ? Ui.Brush("#FF48484A") : Brushes.Black;
            HoverZone.Width = Math.Max(body.Width, IdleBody.Width) + 2 * Shoulder;

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
                    var eq = new SolidColorBrush(md.ArtColor ?? Ui.AccentColor);
                    eq.Freeze();
                    foreach (var b in _bars) b.Fill = eq;
                    break;

                case Mode.Timer:
                    var a = TimerService.Instance.LiveActivity;
                    if (a == null) break;
                    LeftGlyph.FontFamily = (FontFamily)FindResource("IconFont");
                    LeftGlyph.Text = Glyphs.Stopwatch;
                    LeftGlyph.Visibility = Visibility.Visible;
                    LeftGlyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                    LeftText.Text = a.Value.Label;
                    LeftText.Visibility = Visibility.Visible;
                    LeftText.Foreground = Ui.Res("Text2Brush");
                    RightText.Text = a.Value.Time;
                    RightText.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
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
                    if (t.Battery.HasValue) { RightBattery.Level = t.Battery.Value; RightBattery.Charging = t.Charging; }
                    if (t.IsHud)
                        HudScale.BeginAnimation(ScaleTransform.ScaleXProperty,
                            new DoubleAnimation(Math.Clamp(t.Level!.Value, 0, 1), TimeSpan.FromMilliseconds(140)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                    break;
            }
        }

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
