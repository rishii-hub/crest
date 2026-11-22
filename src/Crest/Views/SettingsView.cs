using System;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Crest.Controls;
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
        public void ScrollTo(double y) => (Content as ScrollViewer)?.ScrollToVerticalOffset(y);
        public void OnDeactivated() { }

        void Build()
        {
            ThemeSection();
            EyesSection();

            // ---- Appearance ----
            var look = Section(_left, "Appearance");
            Seg(look, "Style", new[] { ("Notch", "Notch"), ("Island", "Island") }, S.Style, v => S.Style = v);
            Seg(look, "Size", new[] { ("Small", "Small"), ("Medium", "Medium"), ("Large", "Large") }, S.Size, v => S.Size = v);
            Seg(look, "Visibility", new[] { ("Always", "Always"), ("Smart", "Smart"), ("Minimal", "Minimal"), ("Hidden", "Hidden") }, S.Visibility, v => S.Visibility = v,
                "Smart tucks into a thin line over maximized apps · Hidden shows only events");
            Seg(look, "Resting notch", new[] { ("None", "Clean"), ("Corners", "Time & battery") }, S.RestingInfo, v => S.RestingInfo = v);
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
            Toggle(live, "Focus Assist / Do Not Disturb", "Show a pill when Windows notifications go quiet", S.FocusAssistHud, v => S.FocusAssistHud = v);
            Toggle(live, "Mirror notifications & calls", "Needs the packaged (MSIX) build · " + NotificationMirror.Status, S.MirrorNotifications, v => { S.MirrorNotifications = v; if (v) NotificationMirror.Init(Dispatcher); else NotificationMirror.Stop(); });
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
            Info(about, "Ctrl + Alt + Space", "Search apps, files, settings, math and the web");
            Info(about, "Esc", "Close");
            Info(about, "Swipe a live activity sideways", "Hide it (it keeps running)");
            Info(about, "Pull the notch down", "Pop the tab out as a floating widget");
            Info(about, "Live Activity API", "Named pipe \\\\.\\pipe\\CrestLiveActivity, or crest.exe --activity \"{json}\"");

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

        // ---------- theme ----------
        void ThemeSection()
        {
            var t = Section(_left, "Theme");
            var wrap = new WrapPanel { Margin = new Thickness(0, 8, 0, 2) };
            foreach (var p in ThemeService.Presets) wrap.Children.Add(ThemeTile(p));
            t.Children.Add(wrap);
            if (S.Theme == "Custom") PanelColours(t);
            Accents(t);
            Seg(t, "Background", new[] { ("None", "None"), ("Aurora", "Aurora"), ("Sunset", "Sunset"), ("Ocean", "Ocean"), ("Media", "Your own") }, S.Backdrop,
                v => S.Backdrop = v, "Behind the open notch. Cards turn to glass.", stacked: true);
            if (S.Backdrop == "Media") MediaRows(t);
            Seg(t, "Font", new[] { ("Inter", "Inter"), ("Segoe", "Segoe UI"), ("Bahnschrift", "Bahnschrift") },
                S.Font is "Inter" or "Segoe" or "Bahnschrift" ? S.Font : "", v => S.Font = v);
            FontRow(t);
        }

        void EyesSection()
        {
            var t = Section(_left, "Eyes");
            t.Children.Add(new TextBlock { Text = "The resting notch watches your cursor, blinks, glances around when you're still and dozes off when you're away. Live activities take over while they run.", Style = Ui.StyleRes("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) });
            var wrap = new WrapPanel();
            foreach (var kind in new[] { "Off" }.Concat(NotchEyes.Styles))
            {
                bool sel = S.Eyes == kind;
                UIElement inner = kind == "Off"
                    ? new TextBlock { Text = "Off", FontSize = 11.5, Foreground = Brushes.White, Opacity = 0.7, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
                    : new NotchEyes { Kind = kind, Width = 44, Height = 13, SleepWhenIdle = false, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                var tile = new Border
                {
                    Width = 64, Height = 28, CornerRadius = new CornerRadius(10), Background = Brushes.Black, Child = inner,
                    BorderThickness = new Thickness(sel ? 2 : 1), BorderBrush = sel ? Ui.Res("AccentBrush") : Ui.Res("StrokeBrush")
                };
                var box = new StackPanel { Margin = new Thickness(0, 0, 9, 8), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
                box.Children.Add(tile);
                box.Children.Add(new TextBlock { Text = kind, FontSize = 11.5, Margin = new Thickness(2, 4, 0, 0), Foreground = sel ? Ui.Res("TextBrush") : Ui.Res("Text2Brush"), FontWeight = sel ? FontWeights.SemiBold : FontWeights.Normal });
                var k = kind;
                box.MouseLeftButtonUp += (s, e) =>
                {
                    S.Eyes = k;
                    foreach (StackPanel other in wrap.Children)
                    {
                        bool on = other == box;
                        var b = (Border)other.Children[0];
                        b.BorderThickness = new Thickness(on ? 2 : 1);
                        b.BorderBrush = on ? Ui.Res("AccentBrush") : Ui.Res("StrokeBrush");
                        var l = (TextBlock)other.Children[1];
                        l.Foreground = on ? Ui.Res("TextBrush") : Ui.Res("Text2Brush");
                        l.FontWeight = on ? FontWeights.SemiBold : FontWeights.Normal;
                    }
                    Changed(true);
                };
                wrap.Children.Add(box);
            }
            t.Children.Add(wrap);
            Toggle(t, "Sleep when you're away", "Eyes close after a minute without input and wake up when you're back", S.EyesSleep, v => S.EyesSleep = v);
        }

        FrameworkElement ThemeTile(ThemePreset p)
        {
            bool selected = S.Theme == p.Key;
            string panelHex = p.Key == "Custom" ? S.CustomPanel : p.Panel;
            var panel = ThemeService.Parse(panelHex, Colors.Black);
            bool light = ThemeService.Luma(panel) > 0.5;
            Brush bg = new SolidColorBrush(panel);
            if (p.Backdrop != "None" && p.Backdrop != "")
            {
                var cs = ThemeService.BackdropColors(p.Backdrop);
                var lg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
                lg.GradientStops.Add(new GradientStop(ThemeService.Mix(panel, cs[0], 0.75), 0));
                lg.GradientStops.Add(new GradientStop(ThemeService.Mix(panel, cs[1], 0.6), 0.55));
                lg.GradientStops.Add(new GradientStop(ThemeService.Mix(panel, cs[2], 0.7), 1));
                bg = lg;
            }
            var card = new Border
            {
                Width = 40, Height = 14, CornerRadius = new CornerRadius(4), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(7, 0, 0, 7),
                Background = new SolidColorBrush(p.Backdrop is "None" or "" ? ThemeService.Mix(panel, Colors.White, light ? 0.8 : 0.12) : Color.FromArgb(60, 255, 255, 255))
            };
            var dot = new Ellipse { Width = 9, Height = 9, Fill = Ui.Brush(p.Key == "Custom" ? S.Accent : p.Accent), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 7, 7, 0) };
            var g = new Grid();
            g.Children.Add(card);
            g.Children.Add(dot);
            if (p.Key == "Custom")
                g.Children.Add(new TextBlock { Text = "\uE790", Style = Ui.StyleRes("Icon"), FontSize = 13, Foreground = new SolidColorBrush(light ? Colors.Black : Colors.White), Opacity = 0.8 });
            var swatch = new Border
            {
                Width = 70, Height = 46, CornerRadius = new CornerRadius(11), Background = bg, Child = g,
                BorderThickness = new Thickness(selected ? 2 : 1), BorderBrush = selected ? Ui.Res("AccentBrush") : Ui.Res("StrokeBrush")
            };
            var box = new StackPanel { Margin = new Thickness(0, 0, 8, 8), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent, ToolTip = p.Sub };
            box.Children.Add(swatch);
            box.Children.Add(new TextBlock { Text = p.Name, FontSize = 11.5, Margin = new Thickness(2, 4, 0, 0), Foreground = selected ? Ui.Res("TextBrush") : Ui.Res("Text2Brush"), FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal });
            box.MouseLeftButtonUp += (s, e) => { ThemeService.Choose(p.Key); Changed(true); };
            return box;
        }

        static void PanelColours(StackPanel host)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var wrap = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            foreach (var hex in ThemeService.PanelSwatches)
            {
                bool sel = string.Equals(hex, S.CustomPanel, StringComparison.OrdinalIgnoreCase);
                var sw = new Border
                {
                    Width = 22, Height = 22, CornerRadius = new CornerRadius(11), Background = Ui.Brush(hex), Margin = new Thickness(0, 0, 5, 5), Cursor = System.Windows.Input.Cursors.Hand,
                    BorderThickness = new Thickness(sel ? 2 : 1), BorderBrush = sel ? Ui.Res("AccentBrush") : Ui.Res("StrokeBrush"), ToolTip = hex
                };
                var h = hex;
                sw.MouseLeftButtonUp += (s, e) => { S.CustomPanel = h; Changed(true); };
                wrap.Children.Add(sw);
            }
            var hexBox = new TextBox { Style = Ui.StyleRes("Field"), Text = S.CustomPanel, Width = 86, Tag = "#RRGGBB", Padding = new Thickness(8, 4, 8, 4), FontSize = 12, VerticalAlignment = VerticalAlignment.Top };
            void commit()
            {
                var t = hexBox.Text.Trim();
                if (!t.StartsWith("#")) t = "#" + t;
                if (System.Text.RegularExpressions.Regex.IsMatch(t, "^#[0-9A-Fa-f]{6}$") && !string.Equals(t, S.CustomPanel, StringComparison.OrdinalIgnoreCase)) { S.CustomPanel = t.ToUpperInvariant(); Changed(true); }
            }
            hexBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) commit(); };
            hexBox.LostKeyboardFocus += (s, e) => commit();
            DockPanel.SetDock(hexBox, Dock.Right);
            var left = new StackPanel();
            left.Children.Add(Labels("Panel colour", "Light colours give a light theme automatically"));
            left.Children.Add(wrap);
            row.Children.Add(hexBox);
            row.Children.Add(left);
            host.Children.Add(row);
        }

        static void MediaRows(StackPanel host)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var pick = new Button { Style = Ui.StyleRes("Pill"), Content = string.IsNullOrEmpty(S.BgPath) ? "Choose…" : "Change…", VerticalAlignment = VerticalAlignment.Center };
            pick.Click += (s, e) =>
            {
                var notch = NotchWindow.Instance;
                if (notch != null) notch.DragOut = true;
                try
                {
                    var dlg = new Microsoft.Win32.OpenFileDialog
                    {
                        Title = "Background for the notch",
                        Filter = "Pictures, GIFs and videos|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.gif;*.tif;*.tiff;*.heic;*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.webm;*.mkv|All files|*.*"
                    };
                    if (dlg.ShowDialog() == true) { S.BgPath = dlg.FileName; S.Backdrop = "Media"; Changed(true); }
                }
                finally { if (notch != null) notch.DragOut = false; }
            };
            DockPanel.SetDock(pick, Dock.Right);
            row.Children.Add(pick);
            bool has = System.IO.File.Exists(S.BgPath);
            string name = has ? System.IO.Path.GetFileName(S.BgPath) : "Pick a picture, an animated GIF or a video";
            string kind = !has ? "" : Backdrop.VideoExt.Contains(System.IO.Path.GetExtension(S.BgPath).ToLowerInvariant()) ? "Video · plays muted, only while the notch is open"
                        : S.BgPath.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? "Animated GIF · pauses while the notch is closed" : "Picture";
            row.Children.Add(Labels(name, kind));
            host.Children.Add(row);
            if (!has) return;
            SliderRow(host, "Dim", "Panel colour over the background so text stays readable", 0, 90, S.BgDim, v => S.BgDim = (int)v, v => $"{v:0}%");
            SliderRow(host, "Blur", "Costs some CPU with video", 0, 40, S.BgBlur, v => S.BgBlur = (int)v, v => v == 0 ? "Off" : $"{v:0} px");
        }

        static void FontRow(StackPanel host)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 6) };
            bool custom = S.Font is not ("Inter" or "Segoe" or "Bahnschrift");
            var box = new TextBox { Style = Ui.StyleRes("Field"), Text = custom ? S.Font : "", Tag = "Any installed font", Width = 150, Padding = new Thickness(8, 4, 8, 4), FontSize = 12 };
            var note = new TextBlock { Text = "Or type the name of a font on this PC", FontSize = 11, Foreground = Ui.Res("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };
            void commit()
            {
                var name = box.Text.Trim();
                if (name.Length == 0 || name == S.Font) return;
                bool exists = Fonts.SystemFontFamilies.Any(f => string.Equals(f.Source, name, StringComparison.OrdinalIgnoreCase));
                if (!exists) { note.Text = $"“{name}” isn't installed"; note.Foreground = Ui.Res("RedBrush"); return; }
                S.Font = Fonts.SystemFontFamilies.First(f => string.Equals(f.Source, name, StringComparison.OrdinalIgnoreCase)).Source;
                Changed(true);
            }
            box.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) commit(); };
            box.LostKeyboardFocus += (s, e) => commit();
            DockPanel.SetDock(box, Dock.Right);
            row.Children.Add(box);
            row.Children.Add(note);
            host.Children.Add(row);
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

        static void Seg(StackPanel host, string title, (string Value, string Label)[] options, string current, Action<string> set, string? sub = null, bool stacked = false)
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
            if (stacked)
            {
                // wide control: label on top, options underneath
                var labels = Labels(title, sub);
                DockPanel.SetDock(labels, Dock.Top);
                row.Children.Add(labels);
                seg.Margin = new Thickness(0, 6, 0, 0);
                seg.HorizontalAlignment = HorizontalAlignment.Left;
                row.Children.Add(seg);
                host.Children.Add(row);
                return;
            }
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
                var ring = new Ellipse { Width = 22, Height = 22, StrokeThickness = 2, Stroke = string.Equals(hex, S.Accent, StringComparison.OrdinalIgnoreCase) ? Ui.Res("TextBrush") : Brushes.Transparent };
                var dot = new Ellipse { Width = 16, Height = 16, Fill = Ui.Brush(hex) };
                var g = new Grid { Margin = new Thickness(2, 0, 2, 0), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent, ToolTip = hex };
                g.Children.Add(ring);
                g.Children.Add(dot);
                var h = hex;
                g.MouseLeftButtonUp += (s, e) =>
                {
                    S.Accent = h;
                    foreach (Grid other in swatches.Children) ((Ellipse)other.Children[0]).Stroke = Brushes.Transparent;
                    ring.Stroke = Ui.Res("TextBrush");
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
