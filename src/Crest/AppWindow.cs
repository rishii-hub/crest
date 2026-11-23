using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Crest.Controls;
using Crest.Core;
using Crest.Services;
using Crest.Views;

namespace Crest
{
    /// <summary>
    /// The Crest app: a normal window with a sidebar that holds every feature and all settings, so the island
    /// itself can stay small and minimal. Closing it just hides it; Crest keeps running in the tray.
    /// </summary>
    public sealed class AppWindow : Window
    {
        static AppSettings S => SettingsStore.Current;
        static AppWindow? _instance;

        public static readonly (string Section, string Key, string Glyph, string Title, string Sub)[] Pages = BuildPages();

        static (string, string, string, string, string)[] BuildPages()
        {
            var list = new List<(string, string, string, string, string)> { ("", "Home", Glyphs.Home, "Dashboard", "Everything at a glance") };
            foreach (var key in new[] { "Clipboard", "Notes", "Timers", "Shelf", "Launch", "Assistant", "Tools", "System", "Weather", "Clock", "Units", "ScreenTime", "AI", "Message" })
            {
                var t = NotchWindow.AllTabs.First(x => x.Key == key);
                list.Add(("Features", key, t.Glyph, t.Title, string.IsNullOrEmpty(t.Sub) ? Default(key) : t.Sub));
            }
            list.Add(("Settings", "Appearance", "", "Appearance", "Theme, background, font, eyes and the notch itself"));
            list.Add(("Settings", "Island", "", "Island", "What the island shows, live activities and behaviour"));
            list.Add(("Settings", "General", Glyphs.Settings, "General", "Clipboard, backup, shortcuts and updates"));
            return list.ToArray();
        }

        static string Default(string key) => key switch
        {
            "Clipboard" => "Everything you copied, searchable",
            "Weather" => "Now, hourly and the week ahead",
            _ => ""
        };

        /// <summary>Dashboard-style pages that look best at their designed height rather than stretched.</summary>
        static readonly HashSet<string> FixedHeight = new HashSet<string> { "Home", "Timers", "System", "Weather", "Clock", "Tools", "AI", "Message", "Units", "ScreenTime" };

        readonly Dictionary<string, FrameworkElement> _views = new Dictionary<string, FrameworkElement>();
        readonly Dictionary<string, Border> _items = new Dictionary<string, Border>();
        readonly ContentControl _host = new ContentControl { Focusable = false };
        readonly TextBlock _title = new TextBlock(), _sub = new TextBlock();
        readonly Button _popOut = new Button();
        string _page = "Home", _sig = "";
        IntPtr _hwnd;

        public static AppWindow? Current => _instance;

        /// <summary>Show the app on a page (null = last page). "Settings" opens Appearance.</summary>
        public static void ShowPage(string? key)
        {
            if (key == "Settings") key = "Appearance";
            if (key != null && !Pages.Any(p => p.Key == key)) key = "Home";
            _instance ??= new AppWindow();
            var w = _instance;
            if (!w.IsVisible) w.Show();
            if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
            w.Activate();
            w.Select(key ?? (Pages.Any(p => p.Key == S.AppPage) ? S.AppPage : "Home"));
        }

        AppWindow()
        {
            Title = "Crest";
            Width = Math.Max(860, S.AppWidth);
            Height = Math.Max(560, S.AppHeight);
            MinWidth = 860;
            MinHeight = 560;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            SetResourceReference(FontFamilyProperty, "UiFont");
            SetResourceReference(ForegroundProperty, "TextBrush");
            SetResourceReference(BackgroundProperty, "PanelBrush");
            try { Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/crest.ico")); } catch { }

            var wa = SystemParameters.WorkArea;
            if (S.AppLeft is double l && S.AppTop is double t && l > wa.Left - 100 && l < wa.Right - 200 && t > wa.Top - 20 && t < wa.Bottom - 120)
            { WindowStartupLocation = WindowStartupLocation.Manual; Left = l; Top = t; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Build();
            _sig = ThemeService.Signature;

            SourceInitialized += (s, e) => { _hwnd = new WindowInteropHelper(this).Handle; ApplyChrome(); };
            Closing += (s, e) =>
            {
                SavePlacement();
                (_host.Content as ITabView)?.OnDeactivated();
                if (Application.Current?.Dispatcher.HasShutdownStarted == true) return;
                e.Cancel = true; // hide instead; Crest keeps running in the tray
                Hide();
            };
            StateChanged += (s, e) =>
            {
                if (WindowState == WindowState.Minimized) (_host.Content as ITabView)?.OnDeactivated();
                else (_host.Content as ITabView)?.OnActivated();
            };
            SettingsStore.Changed += () =>
            {
                if (ThemeService.Signature == _sig) { ApplyChrome(); return; }
                _sig = ThemeService.Signature;
                (_host.Content as ITabView)?.OnDeactivated();
                _host.Content = null;
                _views.Clear();
                Build();
                ApplyChrome();
                Select(_page, force: true);
            };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && Keyboard.FocusedElement is not System.Windows.Controls.Primitives.TextBoxBase) Close(); };
        }

        void SavePlacement()
        {
            if (WindowState != WindowState.Normal) return;
            S.AppLeft = Left; S.AppTop = Top; S.AppWidth = Width; S.AppHeight = Height; S.AppPage = _page;
            SettingsStore.Save();
        }

        // ---------------- layout ----------------
        void Build()
        {
            _items.Clear();
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(232) });
            root.ColumnDefinitions.Add(new ColumnDefinition());

            // sidebar
            var list = new StackPanel();
            string section = "";
            foreach (var (sec, key, glyph, title, _) in Pages)
            {
                if (sec != section)
                {
                    section = sec;
                    list.Children.Add(new TextBlock { Text = sec.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("Text3Brush"), Margin = new Thickness(12, 16, 0, 6) });
                }
                list.Children.Add(Item(key, glyph, title));
            }
            var brand = new DockPanel { Margin = new Thickness(8, 2, 8, 6) };
            var logo = new Border { Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = Ui.Res("InkBrush"), Margin = new Thickness(0, 0, 10, 0) };
            logo.Child = new Border { Width = 16, Height = 6, CornerRadius = new CornerRadius(3), Background = Ui.Res("OnInkBrush"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 0, 0) };
            DockPanel.SetDock(logo, Dock.Left);
            brand.Children.Add(logo);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = "Crest", FontSize = 16, FontWeight = FontWeights.SemiBold };
            name.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            names.Children.Add(name);
            names.Children.Add(new TextBlock { Text = "Version " + UpdateService.CurrentText, FontSize = 11, Foreground = Ui.Res("Text3Brush") });
            brand.Children.Add(names);

            var sideDock = new DockPanel();
            DockPanel.SetDock(brand, Dock.Top);
            sideDock.Children.Add(brand);
            var island = new Button { Style = Ui.StyleRes("Pill"), Content = "Show the island", HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(4, 8, 4, 0), Padding = new Thickness(10, 7, 10, 7) };
            island.Click += (s, e) => NotchWindow.Instance?.Expand("Home");
            DockPanel.SetDock(island, Dock.Bottom);
            sideDock.Children.Add(island);
            sideDock.Children.Add(new ScrollViewer { Content = list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var sidebar = new Border { Margin = new Thickness(10), CornerRadius = new CornerRadius(16), Background = Ui.Res("DockBrush"), Padding = new Thickness(8, 12, 8, 10), Child = sideDock };
            root.Children.Add(sidebar);

            // content
            _title.FontSize = 24;
            _title.FontWeight = FontWeights.SemiBold;
            _title.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            _sub.FontSize = 12.5;
            _sub.Foreground = Ui.Res("Text2Brush");
            _sub.Margin = new Thickness(0, 2, 0, 0);
            _popOut.Style = Ui.StyleRes("Pill");
            _popOut.Content = "Pop out as widget";
            _popOut.VerticalAlignment = VerticalAlignment.Center;
            _popOut.ToolTip = "Keep this page floating on your desktop";
            _popOut.Click -= PopOutClick;
            _popOut.Click += PopOutClick;
            var head = new DockPanel { Margin = new Thickness(4, 4, 0, 16) };
            DockPanel.SetDock(_popOut, Dock.Right);
            if (_popOut.Parent is Panel old) old.Children.Remove(_popOut);
            head.Children.Add(_popOut);
            var titles = new StackPanel();
            if (_title.Parent is Panel t1) t1.Children.Remove(_title);
            if (_sub.Parent is Panel t2) t2.Children.Remove(_sub);
            titles.Children.Add(_title);
            titles.Children.Add(_sub);
            head.Children.Add(titles);

            var content = new DockPanel { Margin = new Thickness(14, 18, 22, 18) };
            DockPanel.SetDock(head, Dock.Top);
            content.Children.Add(head);
            if (_host.Parent is Panel h) h.Children.Remove(_host);
            content.Children.Add(_host);
            Grid.SetColumn(content, 1);
            root.Children.Add(content);
            Content = root;
        }

        void PopOutClick(object sender, RoutedEventArgs e) => FloatWindow.Open(_page, null);

        Border Item(string key, string glyph, string title)
        {
            var g = new TextBlock { Text = glyph, Style = Ui.StyleRes("Icon"), FontSize = 14, Width = 22, Foreground = Ui.Res("Text2Brush") };
            var t = new TextBlock { Text = title, FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(g);
            row.Children.Add(t);
            var b = new Border { Height = 32, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 0, 10, 0), Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Child = row, Cursor = Cursors.Hand };
            b.MouseEnter += (s, e) => { if (_page != key) b.Background = Ui.Res("Card2Brush"); };
            b.MouseLeave += (s, e) => { if (_page != key) b.Background = Brushes.Transparent; };
            b.MouseLeftButtonUp += (s, e) => Select(key);
            _items[key] = b;
            return b;
        }

        void Paint()
        {
            foreach (var (key, b) in _items)
            {
                bool on = key == _page;
                b.Background = on ? Ui.Res("HoverBrush") : Brushes.Transparent;
                var row = (StackPanel)b.Child;
                var g = (TextBlock)row.Children[0];
                var t = (TextBlock)row.Children[1];
                if (on) g.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush"); else g.Foreground = Ui.Res("Text2Brush");
                t.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        public void Select(string key, bool force = false)
        {
            if (!Pages.Any(p => p.Key == key)) key = "Home";
            if (key == _page && _host.Content != null && !force) { Paint(); return; }
            (_host.Content as ITabView)?.OnDeactivated();
            _page = key;
            var page = Pages.First(p => p.Key == key);
            _title.Text = page.Title;
            _sub.Text = key == "Home" ? DateTime.Now.ToString("dddd, d MMMM") : page.Sub;
            _popOut.Visibility = page.Section == "Settings" ? Visibility.Collapsed : Visibility.Visible;
            if (!_views.TryGetValue(key, out var view))
            {
                try { view = NotchWindow.CreateViewFor(key); }
                catch (Exception ex) { Log.Error("view " + key, ex); view = new TextBlock { Text = "This page failed to load. See crest.log." }; }
                if (FixedHeight.Contains(key)) { view.MaxHeight = 420; view.VerticalAlignment = VerticalAlignment.Top; }
                _views[key] = view;
            }
            _host.Content = view;
            if (IsVisible && WindowState != WindowState.Minimized) (view as ITabView)?.OnActivated();
            view.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
            if (view.RenderTransform is not TranslateTransform) view.RenderTransform = new TranslateTransform();
            ((TranslateTransform)view.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            Paint();
            if (S.AppPage != key) { S.AppPage = key; SettingsStore.Save(); }
        }

        // ---------------- title bar matches the theme (Windows 11) ----------------
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        void ApplyChrome()
        {
            if (_hwnd == IntPtr.Zero) return;
            try
            {
                int dark = ThemeService.IsLight ? 0 : 1;
                DwmSetWindowAttribute(_hwnd, 20, ref dark, 4);              // DWMWA_USE_IMMERSIVE_DARK_MODE
                var c = ThemeService.Panel;
                int colorref = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(_hwnd, 35, ref colorref, 4);          // DWMWA_CAPTION_COLOR
                DwmSetWindowAttribute(_hwnd, 34, ref colorref, 4);          // DWMWA_BORDER_COLOR
            }
            catch { }
        }

        /// <summary>Rebuild every page from the current settings (after a reset).</summary>
        public void Reload()
        {
            _sig = ThemeService.Signature;
            (_host.Content as ITabView)?.OnDeactivated();
            _host.Content = null;
            _views.Clear();
            Build();
            ApplyChrome();
            Select(_page, force: true);
        }

        public FrameworkElement? View(string key) => _views.TryGetValue(key, out var v) ? v : null;

        /// <summary>Tour / README screenshots of the app window.</summary>
        public void CaptureTo(string path)
        {
            try
            {
                if (Content is not FrameworkElement root) return;
                var dpi = VisualTreeHelper.GetDpi(this);
                int w = (int)Math.Ceiling(root.ActualWidth * dpi.DpiScaleX), h = (int)Math.Ceiling(root.ActualHeight * dpi.DpiScaleY);
                var rtb = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(Background, null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                    dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, root.ActualWidth, root.ActualHeight));
                }
                rtb.Render(dv);
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(rtb));
                using var fs = File.Create(path);
                enc.Save(fs);
                Log.Write("tour: captured " + Path.GetFileNameWithoutExtension(path));
            }
            catch (Exception ex) { Log.Error("capture " + path, ex); }
        }
    }
}
