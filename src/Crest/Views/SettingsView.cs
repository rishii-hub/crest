using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    public sealed class SettingsView : UserControl, ITabView
    {
        static AppSettings S => SettingsStore.Current;
        readonly StackPanel _left = new StackPanel(), _right = new StackPanel();
        CheckBox? _startup;

        public SettingsView()
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.Children.Add(_left);
            Grid.SetColumn(_right, 2);
            grid.Children.Add(_right);
            Content = new ScrollViewer { Content = grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(0, 0, 6, 0) };
            Build();
        }

        public void OnActivated() { if (_startup != null) _startup.IsChecked = Startup.Enabled; }
        public void OnDeactivated() { }

        void Build()
        {
            // ---- Appearance ----
            var look = Section(_left, "Appearance");
            Seg(look, "Style", new[] { ("Notch", "Notch"), ("Island", "Island") }, S.Style, v => S.Style = v);
            Seg(look, "Size", new[] { ("Small", "Small"), ("Medium", "Medium"), ("Large", "Large") }, S.Size, v => S.Size = v);
            Seg(look, "When idle", new[] { ("Notch", "Notch"), ("Line", "Thin line") }, S.IdleLook, v => S.IdleLook = v,
                "Thin line keeps browser tabs under it clickable");
            Accents(look);

            // ---- Behaviour ----
            var beh = Section(_left, "Behaviour");
            Toggle(beh, "Open on hover", "Otherwise click the notch", S.ExpandOnHover, v => S.ExpandOnHover = v);
            Seg(beh, "Hover delay", new[] { ("60", "Instant"), ("150", "Normal"), ("350", "Relaxed") }, S.HoverDelayMs.ToString(),
                v => S.HoverDelayMs = int.Parse(v));
            Toggle(beh, "Hide in full screen", "Games, videos, presentations", S.HideInFullscreen, v => S.HideInFullscreen = v);
            Toggle(beh, "Hide from screen sharing", "Invisible in screenshots, recordings and calls", S.HideFromCapture, v => S.HideFromCapture = v);
            Seg(beh, "Show on", new[] { ("Primary", "Main display"), ("Cursor", "Display with cursor") }, S.Display, v => S.Display = v);
            _startup = Toggle(beh, "Start with Windows", null, Startup.Enabled, v => Startup.Enabled = v, saves: false);

            // ---- Live activities ----
            var live = Section(_right, "Live activities");
            Toggle(live, "Now playing", "Album art and equalizer while music plays", S.MediaActivity, v => S.MediaActivity = v);
            Toggle(live, "Timers", "Countdown in the notch while a timer runs", S.TimerActivity, v => S.TimerActivity = v);
            Toggle(live, "Volume", "Show volume changes in the notch", S.VolumeHud, v => S.VolumeHud = v);
            Toggle(live, "Brightness", "Laptop screens only", S.BrightnessHud, v => S.BrightnessHud = v);
            Toggle(live, "Charging & low battery", null, S.BatteryAlerts, v => S.BatteryAlerts = v);
            Toggle(live, "Bluetooth devices", "Earbuds and headphones connecting", S.BluetoothAlerts, v => S.BluetoothAlerts = v);
            Toggle(live, "Caps Lock", null, S.CapsLockHud, v => S.CapsLockHud = v);
            Toggle(live, "Privacy indicators", "Orange dot = microphone, green = camera", S.PrivacyDots, v => S.PrivacyDots = v);
            Toggle(live, "Sounds", "Chime when timers finish", S.Sounds, v => S.Sounds = v);

            // ---- Clipboard ----
            var clip = Section(_right, "Clipboard");
            Toggle(clip, "Clipboard history", "Skips anything password managers mark private", S.ClipboardHistory, v => S.ClipboardHistory = v);

            // ---- Shortcuts & about ----
            var about = Section(_left, "Shortcuts");
            Info(about, "Ctrl + Alt + N", "Open / close the notch");
            Info(about, "Ctrl + Alt + V", "Clipboard history");
            Info(about, "Ctrl + Alt + P", "Start / pause Pomodoro");
            Info(about, "Esc", "Close");

            var info = Section(_right, "About");
            var ver = Assembly.GetExecutingAssembly().GetName().Version;
            Info(info, "Crest " + (ver == null ? "" : $"{ver.Major}.{ver.Minor}.{ver.Build}"), "Open source · MIT License");
            var links = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            links.Children.Add(LinkButton("GitHub", () => Ui.Open("https://github.com/rishii-hub/crest")));
            links.Children.Add(LinkButton("Report a bug", () => Ui.Open("https://github.com/rishii-hub/crest/issues/new")));
            links.Children.Add(LinkButton("Data folder", () => { System.IO.Directory.CreateDirectory(Storage.Dir); Ui.Open(Storage.Dir); }));
            links.Children.Add(LinkButton("Quit", () => Application.Current.Shutdown()));
            info.Children.Add(links);
        }

        // ---------- builders ----------
        static StackPanel Section(StackPanel host, string title)
        {
            host.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("Text3Brush"), Margin = new Thickness(6, host.Children.Count == 0 ? 0 : 12, 0, 6) });
            var body = new StackPanel();
            host.Children.Add(new Border { Style = Ui.StyleRes("Card"), Padding = new Thickness(14, 6, 14, 6), Child = body });
            return body;
        }

        static void Changed(bool saves)
        {
            if (saves) SettingsStore.Update(_ => { });
        }

        static CheckBox Toggle(StackPanel host, string title, string? sub, bool value, Action<bool> set, bool saves = true)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var sw = new CheckBox { Style = Ui.StyleRes("Switch"), IsChecked = value, Margin = new Thickness(12, 0, 0, 0) };
            sw.Click += (s, e) => { set(sw.IsChecked == true); Changed(saves); };
            DockPanel.SetDock(sw, Dock.Right);
            row.Children.Add(sw);
            row.Children.Add(Labels(title, sub));
            host.Children.Add(row);
            return sw;
        }

        static void Seg(StackPanel host, string title, (string Value, string Label)[] options, string current, Action<string> set, string? sub = null)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var group = "seg-" + title;
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (value, label) in options)
            {
                var rb = new RadioButton { Style = Ui.StyleRes("Seg"), Content = label, GroupName = group, IsChecked = value == current, Padding = new Thickness(10, 4, 10, 4) };
                var v = value;
                rb.Checked += (s, e) => { set(v); Changed(true); };
                panel.Children.Add(rb);
            }
            var seg = new Border { Background = Ui.Res("Card2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(2), Child = panel, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            DockPanel.SetDock(seg, Dock.Right);
            row.Children.Add(seg);
            row.Children.Add(Labels(title, sub));
            host.Children.Add(row);
        }

        static void Accents(StackPanel host)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var colors = new[] { "#FF9F0A", "#FF453A", "#FF375F", "#BF5AF2", "#5E5CE6", "#0A84FF", "#64D2FF", "#30D158", "#FFD60A" };
            foreach (var hex in colors)
            {
                var ring = new Ellipse { Width = 22, Height = 22, StrokeThickness = 2, Stroke = string.Equals(hex, S.Accent, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent };
                var dot = new Ellipse { Width = 16, Height = 16, Fill = Ui.Brush(hex) };
                var g = new Grid { Margin = new Thickness(2, 0, 2, 0), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent, ToolTip = hex };
                g.Children.Add(ring);
                g.Children.Add(dot);
                var h = hex;
                g.MouseLeftButtonUp += (s, e) =>
                {
                    S.Accent = h;
                    foreach (Grid other in swatches.Children) ((Ellipse)other.Children[0]).Stroke = Brushes.Transparent;
                    ring.Stroke = Brushes.White;
                    Changed(true);
                };
                swatches.Children.Add(g);
            }
            DockPanel.SetDock(swatches, Dock.Right);
            row.Children.Add(swatches);
            row.Children.Add(Labels("Accent", null));
            host.Children.Add(row);
        }

        static void Info(StackPanel host, string title, string sub)
        {
            var row = new DockPanel { Margin = new Thickness(0, 5, 0, 5) };
            var s = new TextBlock { Text = sub, FontSize = 12, Foreground = Ui.Res("Text2Brush"), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(s, Dock.Right);
            row.Children.Add(s);
            row.Children.Add(new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            host.Children.Add(row);
        }

        static Button LinkButton(string text, Action a)
        {
            var b = new Button { Style = Ui.StyleRes("Pill"), Content = text, Margin = new Thickness(0, 0, 6, 6) };
            b.Click += (s, e) => a();
            return b;
        }

        static FrameworkElement Labels(string title, string? sub)
        {
            var p = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            p.Children.Add(new TextBlock { Text = title, FontSize = 13 });
            if (!string.IsNullOrEmpty(sub)) p.Children.Add(new TextBlock { Text = sub, FontSize = 11, Foreground = Ui.Res("Text3Brush"), TextWrapping = TextWrapping.Wrap });
            return p;
        }
    }
}
