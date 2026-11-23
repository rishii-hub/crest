using System;
using System.Collections.Generic;
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
            if (_updateBtn == null) return;
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

        public SettingsView(string page = "All")
        {
            _page = page;
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

        /// <summary>Which settings page this view shows: Appearance | Island | General | All.</summary>
        readonly string _page = "All";
        bool On(string p) => _page == "All" || _page == p;

        void Build()
        {
            if (On("Appearance"))
            {
                ThemeSection(_left);
                EyesSection(_left);
                AppearanceSection(_right);
            }
            if (On("Island"))
            {
                PagesEditor(_left);
                BehaviourSection(_right);
                LiveSection(_right);
            }
            if (On("General"))
            {
                GeneralSections();
            }
        }

        void AppearanceSection(StackPanel col)
        {
            var look = Section(col, "Notch");
            Seg(look, "Style", new[] { ("Notch", "Notch"), ("Island", "Island") }, S.Style, v => S.Style = v);
            Seg(look, "Size", new[] { ("Small", "Small"), ("Medium", "Medium"), ("Large", "Large") }, S.Size, v => S.Size = v);
            Seg(look, "Visibility", new[] { ("Always", "Always"), ("Smart", "Smart"), ("Minimal", "Minimal"), ("Hidden", "Hidden") }, S.Visibility, v => S.Visibility = v,
                "Smart tucks into a thin line over maximized apps · Hidden shows only events", stacked: true);
            Seg(look, "Resting notch", new[] { ("None", "Clean"), ("Corners", "Time & battery") }, S.RestingInfo, v => S.RestingInfo = v);
            Toggle(look, "Outline", "Hairline around the notch", S.Outline, v => S.Outline = v);
            Seg(look, "Shadow", new[] { ("None", "None"), ("Subtle", "Subtle"), ("Medium", "Medium"), ("Deep", "Deep") }, S.Shadow, v => S.Shadow = v, "Depth under the open notch", stacked: true);
            Seg(look, "Music colour", new[] { ("Album", "Album art"), ("Accent", "Accent"), ("Rainbow", "Rainbow") }, S.EqColor, v => S.EqColor = v);
            Toggle(look, "Tab animations", "Sliding pill and direction-aware transitions", S.TabAnimations, v => S.TabAnimations = v);
            Toggle(look, "OLED black", "Pure #000 with no outline or shadow, so it melts into the bezel", S.Oled, v => S.Oled = v);
            Toggle(look, "Camera cutout", "Draw a lens in the notch", S.CameraCutout, v => S.CameraCutout = v);
            SliderRow(look, "Horizontal offset", "Line the notch up with your webcam", -400, 400, S.NotchOffset, v => S.NotchOffset = (int)v, v => v == 0 ? "Centre" : $"{v:+0;-0} px");
        }

        /// <summary>Island pages and their widgets: add pages, rename, reorder, switch off; resize and move every widget.</summary>
        void PagesEditor(StackPanel col)
        {
            var sec = Section(col, "Island pages & widgets");
            var body = new StackPanel();
            sec.Children.Add(body);

            void Commit() { SettingsStore.Update(_ => { }); Render(); }

            Button MiniButton(string glyph, string tip, bool enabled, Action act)
            {
                var b = new Button { Style = Ui.StyleRes("IconButton"), Content = glyph, Width = 26, Height = 26, FontSize = 10, ToolTip = tip, IsEnabled = enabled };
                b.Click += (s2, e2) => act();
                return b;
            }

            void Render()
            {
                body.Children.Clear();
                body.Children.Add(new TextBlock
                {
                    Text = "Make as many pages as you like. Each is a 4 × 2 grid: size any widget from 1 × 1 up to 4 × 2 and the island fits itself around them. You can also right-click a widget on the island.",
                    Style = Ui.StyleRes("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 10)
                });

                var order = S.IslandPages.Where(k => NotchWindow.IsIslandPage(k)).Distinct().ToList();
                var all = order.Concat(S.WidgetPages.Select(p => p.Id).Where(id => !order.Contains(id)))
                               .Concat(NotchWindow.IslandTabs.Select(t => t.Key).Where(k => !order.Contains(k))).ToList();

                foreach (var key in all)
                {
                    var page = WidgetCatalog.Page(key);
                    var builtin = NotchWindow.IslandTabs.FirstOrDefault(t => t.Key == key);
                    bool on = order.Contains(key);
                    int pos = order.IndexOf(key);

                    var card = new Border { Background = Ui.Res("Card2Brush"), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 8, 8), Margin = new Thickness(0, 0, 0, 8), Opacity = on ? 1 : 0.6 };
                    var stack = new StackPanel();
                    card.Child = stack;

                    // header: icon · name · reorder · on/off · delete
                    var head = new DockPanel();
                    var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    right.Children.Add(MiniButton(Glyphs.Left, "Earlier on the island", on && pos > 0, () => { var l = order.ToList(); (l[pos - 1], l[pos]) = (l[pos], l[pos - 1]); S.IslandPages = l; Commit(); }));
                    right.Children.Add(MiniButton(Glyphs.Right, "Later on the island", on && pos < order.Count - 1, () => { var l = order.ToList(); (l[pos + 1], l[pos]) = (l[pos], l[pos + 1]); S.IslandPages = l; Commit(); }));
                    var sw = new CheckBox { Style = Ui.StyleRes("Switch"), IsChecked = on, Margin = new Thickness(6, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Show on the island" };
                    sw.Click += (s2, e2) =>
                    {
                        if (sw.IsChecked == true) S.IslandPages = order.Append(key).ToList();
                        else if (order.Count > 1) S.IslandPages = order.Where(k => k != key).ToList();
                        Commit();
                    };
                    right.Children.Add(sw);
                    if (page != null)
                        right.Children.Add(MiniButton(Glyphs.Delete, "Delete this page", S.WidgetPages.Count > 1 || order.Count > 1, () =>
                        {
                            S.WidgetPages.Remove(page);
                            S.IslandPages = order.Where(k => k != key).ToList();
                            if (S.IslandPages.Count == 0) S.IslandPages.Add(NotchWindow.IslandTabs[0].Key);
                            Commit();
                        }));
                    DockPanel.SetDock(right, Dock.Right);
                    head.Children.Add(right);

                    if (page != null)
                    {
                        var icon = new Button { Style = Ui.StyleRes("IconButton"), Content = page.Glyph, Width = 28, Height = 28, FontSize = 13, ToolTip = "Change the icon", Margin = new Thickness(0, 0, 6, 0) };
                        icon.SetResourceReference(ForegroundProperty, "AccentBrush");
                        icon.Click += (s2, e2) =>
                        {
                            var m = new ContextMenu();
                            foreach (var g in WidgetCatalog.PageGlyphs)
                            {
                                var gg = g;
                                var it = new MenuItem { Header = new TextBlock { Text = g, Style = Ui.StyleRes("Icon"), FontSize = 14 } };
                                it.Click += (s3, e3) => { page.Glyph = gg; Commit(); };
                                m.Items.Add(it);
                            }
                            m.PlacementTarget = icon; m.IsOpen = true;
                        };
                        DockPanel.SetDock(icon, Dock.Left);
                        head.Children.Add(icon);
                        var name = new TextBox { Style = Ui.StyleRes("Field"), Text = page.Name, Padding = new Thickness(8, 3, 8, 3), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
                        name.LostKeyboardFocus += (s2, e2) => { var n = name.Text.Trim(); if (n.Length > 0 && n != page.Name) { page.Name = n; SettingsStore.Update(_ => { }); } };
                        name.KeyDown += (s2, e2) => { if (e2.Key == System.Windows.Input.Key.Enter) System.Windows.Input.Keyboard.ClearFocus(); };
                        head.Children.Add(name);
                    }
                    else
                    {
                        var g = new TextBlock { Text = builtin.Glyph, Style = Ui.StyleRes("Icon"), FontSize = 13, Width = 28, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Res("Text2Brush") };
                        DockPanel.SetDock(g, Dock.Left);
                        head.Children.Add(g);
                        head.Children.Add(Labels(builtin.Title, builtin.Sub));
                    }
                    stack.Children.Add(head);

                    if (page != null)
                    {
                        // to-scale preview of the page
                        const double u = 30, gap = 4;
                        var packed = WidgetCatalog.Pack(page);
                        var preview = new Canvas { Width = WidgetCatalog.Cols * u + (WidgetCatalog.Cols - 1) * gap, Height = WidgetCatalog.Rows * u + gap };
                        for (int r = 0; r < WidgetCatalog.Rows; r++)
                            for (int c = 0; c < WidgetCatalog.Cols; c++)
                            {
                                var cell = new Border { Width = u, Height = u, CornerRadius = new CornerRadius(6), BorderBrush = Ui.Res("StrokeBrush"), BorderThickness = new Thickness(1) };
                                Canvas.SetLeft(cell, c * (u + gap)); Canvas.SetTop(cell, r * (u + gap));
                                preview.Children.Add(cell);
                            }
                        foreach (var (slot, c, r) in packed)
                        {
                            if (c < 0) continue;
                            var d = WidgetCatalog.Get(slot.Kind);
                            int w = Math.Clamp(slot.W, 1, 4), h = Math.Clamp(slot.H, 1, 2);
                            var block = new Border
                            {
                                Width = w * u + (w - 1) * gap, Height = h * u + (h - 1) * gap, CornerRadius = new CornerRadius(6), Background = Ui.Res("HoverBrush"),
                                Child = new TextBlock { Text = d?.Glyph ?? "?", Style = Ui.StyleRes("Icon"), FontSize = 11 }, ToolTip = $"{d?.Name} · {w} × {h}"
                            };
                            Canvas.SetLeft(block, c * (u + gap)); Canvas.SetTop(block, r * (u + gap));
                            preview.Children.Add(block);
                        }
                        stack.Children.Add(new Border { Background = Brushes.Black, CornerRadius = new CornerRadius(10), Padding = new Thickness(6), Margin = new Thickness(0, 8, 0, 6), HorizontalAlignment = HorizontalAlignment.Left, Child = preview });

                        foreach (var (slot, c, r) in packed)
                        {
                            var d = WidgetCatalog.Get(slot.Kind);
                            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                            var sizeBtn = new Button { Style = Ui.StyleRes("Pill"), Content = $"{slot.W} × {slot.H}  ▾", Padding = new Thickness(9, 2, 9, 2), FontSize = 11.5, ToolTip = "Size" };
                            var sl = slot;
                            sizeBtn.Click += (s2, e2) =>
                            {
                                var m = new ContextMenu();
                                foreach (var (w, h) in WidgetCatalog.Sizes)
                                {
                                    var it = new MenuItem { Header = $"{w} × {h}", IsCheckable = true, IsChecked = sl.W == w && sl.H == h };
                                    it.Click += (s3, e3) => { sl.W = w; sl.H = h; Commit(); };
                                    m.Items.Add(it);
                                }
                                m.PlacementTarget = sizeBtn; m.IsOpen = true;
                            };
                            btns.Children.Add(sizeBtn);
                            var more = MiniButton("\uE712", "Move, reorder or remove", true, () => { });
                            more.Click += (s2, e2) => { var m = WidgetCatalog.Menu(page, sl, Render); m.PlacementTarget = more; m.IsOpen = true; };
                            btns.Children.Add(more);
                            DockPanel.SetDock(btns, Dock.Right);
                            row.Children.Add(btns);
                            var g = new TextBlock { Text = d?.Glyph ?? "?", Style = Ui.StyleRes("Icon"), FontSize = 12.5, Width = 24, VerticalAlignment = VerticalAlignment.Center };
                            g.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
                            DockPanel.SetDock(g, Dock.Left);
                            row.Children.Add(g);
                            row.Children.Add(Labels(d?.Name ?? slot.Kind, c < 0 ? "Doesn't fit on this page: make it or something else smaller, or move it" : null));
                            stack.Children.Add(row);
                        }
                        var add = new Button { Style = Ui.StyleRes("Pill"), Content = "+ Add widget", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Padding = new Thickness(12, 4, 12, 4) };
                        add.Click += (s2, e2) => { var m = WidgetCatalog.AddMenu(page, Render); m.PlacementTarget = add; m.IsOpen = true; };
                        stack.Children.Add(add);
                    }
                    body.Children.Add(card);
                }

                var newPage = new Button { Style = Ui.StyleRes("Pill"), Content = "+ New widget page", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 5, 12, 5) };
                newPage.Click += (s2, e2) => { var p = WidgetCatalog.NewPage(); p.Widgets.Add(new WidgetSlot { Kind = "Clock" }); Commit(); };
                var reset = ConfirmButton("Reset pages & widgets", "Click again to reset", () => { SettingsStore.ResetIsland(); Render(); });
                reset.Margin = new Thickness(8, 0, 0, 0);
                reset.ToolTip = "Back to one Now page with music and at-a-glance";
                var buttons = new WrapPanel();
                buttons.Children.Add(newPage);
                buttons.Children.Add(reset);
                body.Children.Add(buttons);

                if (S.WidgetPages.Any(p => p.Widgets.Any(w => w.Kind == "Countdown")))
                {
                    var cd = new DockPanel { Margin = new Thickness(0, 12, 0, 4) };
                    var date = new TextBox { Style = Ui.StyleRes("Field"), Text = S.CountdownDate, Tag = "yyyy-mm-dd", Width = 110, Padding = new Thickness(8, 4, 8, 4), FontSize = 12, Margin = new Thickness(6, 0, 0, 0) };
                    var label = new TextBox { Style = Ui.StyleRes("Field"), Text = S.CountdownLabel, Tag = "Countdown to… (e.g. Exams)", Padding = new Thickness(8, 4, 8, 4), FontSize = 12 };
                    void commit()
                    {
                        S.CountdownLabel = label.Text.Trim();
                        if (date.Text.Trim().Length == 0 || DateTime.TryParseExact(date.Text.Trim(), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out _))
                            S.CountdownDate = date.Text.Trim();
                        SettingsStore.Update(_ => { });
                    }
                    date.LostKeyboardFocus += (s2, e2) => commit();
                    label.LostKeyboardFocus += (s2, e2) => commit();
                    DockPanel.SetDock(date, Dock.Right);
                    cd.Children.Add(date);
                    cd.Children.Add(label);
                    body.Children.Add(new TextBlock { Text = "COUNTDOWN WIDGET", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("Text3Brush"), Margin = new Thickness(0, 12, 0, 0) });
                    body.Children.Add(cd);
                }
            }
            Render();
        }

        void BehaviourSection(StackPanel col)
        {
            var beh = Section(col, "Behaviour");
            Toggle(beh, "Open on hover", "Otherwise click the notch", S.ExpandOnHover, v => S.ExpandOnHover = v);
            Seg(beh, "Hover delay", new[] { ("60", "Instant"), ("150", "Normal"), ("350", "Relaxed") }, S.HoverDelayMs.ToString(),
                v => S.HoverDelayMs = int.Parse(v));
            Toggle(beh, "Hide in full screen", "Games, videos, presentations", S.HideInFullscreen, v => S.HideInFullscreen = v);
            Toggle(beh, "Hide from screen sharing", "Invisible in screenshots, recordings and calls", S.HideFromCapture, v => S.HideFromCapture = v);
            Seg(beh, "Show on", new[] { ("Primary", "Main display"), ("Cursor", "Display with cursor") }, S.Display, v => S.Display = v);
            _startup = Toggle(beh, "Start with Windows", null, Startup.Enabled, v => Startup.Enabled = v, saves: false);
        }

        void LiveSection(StackPanel col)
        {
            var live = Section(col, "Live activities");
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
            Toggle(live, "Mirror notifications & calls", NotificationMirror.HasIdentity ? "Windows notifications and calls as pills · " + NotificationMirror.Status : "Needs the packaged (MSIX) build of Crest", S.MirrorNotifications, v => { S.MirrorNotifications = v; if (v) NotificationMirror.Init(Dispatcher); else NotificationMirror.Stop(); });
            Toggle(live, "Lyrics", "Synced lyrics for whatever is playing. Sends the song's title and artist to lrclib.net, nothing else.", S.Lyrics, v => S.Lyrics = v);
            Toggle(live, "Lyrics in the notch", "Show the current line in the closed notch while music plays", S.LyricsInNotch, v => S.LyricsInNotch = v);
            Toggle(live, "Sounds", "Chime when timers finish", S.Sounds, v => S.Sounds = v);
        }

        void GeneralSections()
        {
            var clip = Section(_left, "Clipboard");
            Toggle(clip, "Clipboard history", "Skips anything password managers mark private", S.ClipboardHistory, v => S.ClipboardHistory = v);

            var backup = Section(_left, "Backup");
            Info(backup, "Everything stays on this PC", "%AppData%\\Crest");
            var brow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            brow.Children.Add(LinkButton("Export backup…", ExportBackup));
            brow.Children.Add(LinkButton("Import backup…", ImportBackup));
            backup.Children.Add(brow);

            var reset = Section(_left, "Reset");
            reset.Children.Add(new TextBlock
            {
                Text = "Theme, island, widgets, live activities and behaviour go back to default. Your notes, clipboard, shelf, alarms, scripts, weather city and assistant setup stay. Click a button twice to confirm.",
                Style = Ui.StyleRes("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 2)
            });
            var rrow = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var all = ConfirmButton("Reset all settings", "Click again to reset everything", () => { SettingsStore.ResetAll(); AppWindow.Current?.Reload(); });
            rrow.Children.Add(all);
            var isl = ConfirmButton("Reset island only", "Click again to reset the island", () => { SettingsStore.ResetIsland(); AppWindow.Current?.Reload(); });
            isl.Margin = new Thickness(8, 0, 0, 0);
            rrow.Children.Add(isl);
            reset.Children.Add(rrow);

            var about = Section(_left, "Shortcuts");
            Info(about, "Ctrl + Alt + N", "Open / close the island");
            Info(about, "Ctrl + Alt + V", "Clipboard in the island");
            Info(about, "Ctrl + Alt + P", "Start / pause Pomodoro");
            Info(about, "Ctrl + Alt + Space", "Search apps, files, settings, math, web");
            Info(about, "Esc", "Close");
            Info(about, "Swipe a live activity sideways", "Hide it (it keeps running)");
            Info(about, "Pull the island down", "Pop the page out as a floating widget");
            Info(about, "Live Activity API", "\\\\.\\pipe\\CrestLiveActivity · crest.exe --activity");

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
            links.Children.Add(LinkButton("Quit Crest", () => Application.Current.Shutdown()));
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
        void ThemeSection(StackPanel col)
        {
            var t = Section(col, "Theme");
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

        void EyesSection(StackPanel col)
        {
            var t = Section(col, "Eyes");
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

        /// <summary>A button that asks "Sure?" on the first click and acts on the second (within 4 s). No modal dialogs from the island.</summary>
        static Button ConfirmButton(string text, string confirm, Action act)
        {
            var b = new Button { Style = Ui.StyleRes("Pill"), Content = text, HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 5, 12, 5) };
            bool armed = false;
            var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            timer.Tick += (s, e) => { timer.Stop(); armed = false; b.Content = text; b.ClearValue(ForegroundProperty); };
            b.Click += (s, e) =>
            {
                if (!armed) { armed = true; b.Content = confirm; b.Foreground = Ui.Res("RedBrush"); timer.Start(); return; }
                timer.Stop(); armed = false; b.Content = text; b.ClearValue(ForegroundProperty);
                act();
            };
            return b;
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
