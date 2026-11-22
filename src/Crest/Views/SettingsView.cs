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
        Button _updateBtn = null!;
        TextBlock _updateStatus = null!;

        void RenderUpdate()
        {
            if (UpdateService.Busy) { _updateBtn.IsEnabled = false; return; }
            _updateBtn.IsEnabled = true;
            if (UpdateService.Available)
            {
                _updateBtn.Content = $"Install {UpdateService.LatestTag}";
                _updateBtn.SetResourceReference(BackgroundProperty, "AccentBrush");
                _updateBtn.Foreground = Brushes.Black;
                _updateStatus.Text = $"{UpdateService.LatestTag} is available. Crest restarts by itself after installing.";
            }
            else
            {
                _updateBtn.Content = "Check for updates";
                _updateBtn.Background = Ui.Res("Card2Brush");
                _updateBtn.Foreground = Ui.Res("TextBrush");
                var last = S.LastUpdateCheck;
                _updateStatus.Text = UpdateService.Latest != null ? "You're on the latest version." : last.HasValue ? $"Last checked {Ui.Ago(last.Value)}" : "Not checked yet";
            }
        }

        async System.Threading.Tasks.Task UpdateClicked()
        {
            if (UpdateService.Available)
            {
                _updateBtn.IsEnabled = false;
                var progress = new Progress<double>(p => _updateStatus.Text = $"Downloading… {p * 100:0}%");
                var err = await UpdateService.InstallAsync(progress);
                if (err != null) { _updateStatus.Text = err; _updateBtn.IsEnabled = true; }
                return;
            }
            _updateStatus.Text = "Checking…";
            _updateBtn.IsEnabled = false;
            bool found = await UpdateService.CheckAsync();
            if (!found && UpdateService.Latest == null) _updateStatus.Text = "Couldn't reach GitHub. Check your connection.";
            RenderUpdate();
        }

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
            Seg(look, "Visibility", new[] { ("Always", "Always"), ("Smart", "Smart"), ("Minimal", "Minimal"), ("Hidden", "Hidden") }, S.Visibility, v => S.Visibility = v,
                "Smart tucks into a thin line over maximized apps · Hidden shows only events");
            Seg(look, "Resting notch", new[] { ("None", "Clean"), ("Corners", "Time & battery") }, S.RestingInfo, v => S.RestingInfo = v);
            Accents(look);
            Toggle(look, "Outline", "Hairline around the notch", S.Outline, v => S.Outline = v);
            Seg(look, "Shadow", new[] { ("None", "None"), ("Subtle", "Subtle"), ("Medium", "Medium"), ("Deep", "Deep") }, S.Shadow, v => S.Shadow = v, "Depth under the open notch");
            Seg(look, "Music colour", new[] { ("Album", "Album art"), ("Accent", "Accent"), ("Rainbow", "Rainbow") }, S.EqColor, v => S.EqColor = v);
            Toggle(look, "Tab animations", "Sliding pill and direction-aware transitions", S.TabAnimations, v => S.TabAnimations = v);
            Toggle(look, "OLED black", "Pure #000 with no outline or shadow, so it melts into the bezel", S.Oled, v => S.Oled = v);
            Toggle(look, "Camera cutout", "Draw a lens in the notch", S.CameraCutout, v => S.CameraCutout = v);
            SliderRow(look, "Horizontal offset", "Line the notch up with your webcam", -400, 400, S.NotchOffset, v => S.NotchOffset = (int)v, v => v == 0 ? "Centre" : $"{v:+0;-0} px");

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
            Toggle(live, "Clipboard copied", "Short confirmation when you copy something", S.CopiedHud, v => S.CopiedHud = v);
            Toggle(live, "Hide copied text", "Show “Text” or “Image” instead of what you copied", S.CopiedHideContent, v => S.CopiedHideContent = v);
            Toggle(live, "Sounds", "Chime when timers finish", S.Sounds, v => S.Sounds = v);

            // ---- Clipboard ----
            var clip = Section(_right, "Clipboard");
            Toggle(clip, "Clipboard history", "Skips anything password managers mark private", S.ClipboardHistory, v => S.ClipboardHistory = v);

            // ---- Modules ----
            var mods = Section(_left, "Modules in the dock");
            foreach (var (key, glyph, title, sub) in NotchWindow.AllTabs)
            {
                if (key == "Home" || key == "Settings") continue;
                var k = key;
                Toggle(mods, title, string.IsNullOrEmpty(sub) ? null : sub, !S.HiddenTabs.Contains(key), on =>
                {
                    if (on) S.HiddenTabs.Remove(k);
                    else if (!S.HiddenTabs.Contains(k)) S.HiddenTabs.Add(k);
                });
            }

            // ---- Backup ----
            var backup = Section(_right, "Backup");
            Info(backup, "Everything stays on this PC", "%AppData%\\Crest");
            var brow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            brow.Children.Add(LinkButton("Export backup…", ExportBackup));
            brow.Children.Add(LinkButton("Import backup…", ImportBackup));
            backup.Children.Add(brow);

            // ---- Shortcuts & about ----
            var about = Section(_left, "Shortcuts");
            Info(about, "Ctrl + Alt + N", "Open / close the notch");
            Info(about, "Ctrl + Alt + V", "Clipboard history");
            Info(about, "Ctrl + Alt + P", "Start / pause Pomodoro");
            Info(about, "Esc", "Close");
            Info(about, "Swipe a live activity sideways", "Hide it (it keeps running)");
            Info(about, "Pull the notch down", "Pop the tab out as a floating widget");

            var info = Section(_right, "About & updates");
            Info(info, "Crest " + UpdateService.CurrentText, "Open source · MIT License");
            Toggle(info, "Check for updates automatically", "One request to GitHub every 12 hours", S.AutoUpdateCheck, v => S.AutoUpdateCheck = v);
            var upRow = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            _updateBtn = new Button { Style = Ui.StyleRes("Pill"), Content = "Check for updates", Margin = new Thickness(10, 0, 0, 0) };
            _updateBtn.Click += async (s, e) => await UpdateClicked();
            DockPanel.SetDock(_updateBtn, Dock.Right);
            upRow.Children.Add(_updateBtn);
            _updateStatus = new TextBlock { FontSize = 11.5, Foreground = Ui.Res("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            upRow.Children.Add(_updateStatus);
            info.Children.Add(upRow);
            UpdateService.Changed += () => Dispatcher.BeginInvoke(new Action(RenderUpdate));
            RenderUpdate();
            var links = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            links.Children.Add(LinkButton("GitHub", () => Ui.Open("https://github.com/rishii-hub/crest")));
            links.Children.Add(LinkButton("Report a bug", () => Ui.Open("https://github.com/rishii-hub/crest/issues/new")));
            links.Children.Add(LinkButton("Data folder", () => { System.IO.Directory.CreateDirectory(Storage.Dir); Ui.Open(Storage.Dir); }));
            links.Children.Add(LinkButton("Quit", () => Application.Current.Shutdown()));
            info.Children.Add(links);
        }

        static void ExportBackup()
        {
            try
            {
                Debouncer.FlushAll();
                SettingsStore.SaveNow();
                var dlg = new Microsoft.Win32.SaveFileDialog { FileName = $"Crest-backup-{DateTime.Now:yyyy-MM-dd}.zip", Filter = "Zip archive|*.zip", InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop) };
                if (NotchWindow.Instance != null) NotchWindow.Instance.DragOut = true;
                bool ok = dlg.ShowDialog() == true;
                if (NotchWindow.Instance != null) NotchWindow.Instance.DragOut = false;
                if (!ok) return;
                if (System.IO.File.Exists(dlg.FileName)) System.IO.File.Delete(dlg.FileName);
                using (var zip = System.IO.Compression.ZipFile.Open(dlg.FileName, System.IO.Compression.ZipArchiveMode.Create))
                    foreach (var f in System.IO.Directory.GetFiles(Storage.Dir, "*", System.IO.SearchOption.AllDirectories))
                    {
                        if (f.EndsWith(".tmp") || f.EndsWith("crest.log")) continue;
                        System.IO.Compression.ZipFileExtensions.CreateEntryFromFile(zip, f, System.IO.Path.GetRelativePath(Storage.Dir, f));
                    }
                NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Check, GlyphBrush = Ui.Res("GreenBrush"), Title = "Backup saved" });
            }
            catch (Exception ex) { Log.Error("export", ex); NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Close, Title = "Backup failed", Value = ex.Message }); }
        }

        static void ImportBackup()
        {
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Crest backup|*.zip" };
                if (NotchWindow.Instance != null) NotchWindow.Instance.DragOut = true;
                bool ok = dlg.ShowDialog() == true;
                if (NotchWindow.Instance != null) NotchWindow.Instance.DragOut = false;
                if (!ok) return;
                using (var zip = System.IO.Compression.ZipFile.OpenRead(dlg.FileName))
                {
                    if (zip.GetEntry("settings.json") == null) { NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Close, Title = "Not a Crest backup" }); return; }
                    foreach (var entry in zip.Entries)
                    {
                        var dest = System.IO.Path.GetFullPath(System.IO.Path.Combine(Storage.Dir, entry.FullName));
                        if (!dest.StartsWith(System.IO.Path.GetFullPath(Storage.Dir), StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Name)) continue;
                        System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(dest)!);
                        System.IO.Compression.ZipFileExtensions.ExtractToFile(entry, dest, true);
                    }
                }
                // Restart so every store reloads cleanly.
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(Environment.ProcessPath!, "--restart") { UseShellExecute = true });
                Application.Current.Shutdown();
            }
            catch (Exception ex) { Log.Error("import", ex); NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Close, Title = "Import failed", Value = ex.Message }); }
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

        static void SliderRow(StackPanel host, string title, string? sub, double min, double max, double value, Action<double> set, Func<double, string> fmt)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var label = new TextBlock { FontSize = 11.5, Foreground = Ui.Res("Text2Brush"), Width = 52, TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Text = fmt(value) };
            var slider = new Slider { Style = Ui.StyleRes("Thin"), Minimum = min, Maximum = max, Value = value, Width = 150, Margin = new Thickness(12, 0, 6, 0), IsSnapToTickEnabled = true, TickFrequency = 1 };
            var save = new Debouncer(TimeSpan.FromMilliseconds(120), () => Changed(true));
            slider.ValueChanged += (s, e) => { set(Math.Round(slider.Value)); label.Text = fmt(Math.Round(slider.Value)); save.Trigger(); };
            slider.MouseDoubleClick += (s, e) => slider.Value = 0;
            DockPanel.SetDock(label, Dock.Right);
            DockPanel.SetDock(slider, Dock.Right);
            row.Children.Add(label);
            row.Children.Add(slider);
            row.Children.Add(Labels(title, sub));
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
