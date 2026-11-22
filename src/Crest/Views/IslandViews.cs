using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Controls;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    // =====================================================================================
    //  Compact pages for the island. Small, glanceable, two or three controls at most.
    //  The full versions live in the Crest app window.
    // =====================================================================================

    /// <summary>Now playing + time, weather, battery and what's next.</summary>
    public sealed class IslandHome : UserControl, ITabView
    {
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        readonly Border _art = new Border { Width = 62, Height = 62, CornerRadius = new CornerRadius(13) };
        readonly TextBlock _artGlyph = new TextBlock { Text = "", FontSize = 22 };
        readonly TextBlock _title = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _artist = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _pos = new TextBlock { FontSize = 10, Width = 32 }, _dur = new TextBlock { FontSize = 10, Width = 32, TextAlignment = TextAlignment.Right };
        readonly Slider _seek = new Slider { Minimum = 0, Maximum = 1, Margin = new Thickness(4, 0, 4, 0) };
        readonly Button _prev = new Button(), _play = new Button(), _next = new Button();
        readonly SpectrumBars _spectrum = new SpectrumBars { Height = 14, Width = 46, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        readonly Grid _seekRow = new Grid();
        readonly TextBlock _clock = new TextBlock { FontSize = 30, FontWeight = FontWeights.Light };
        readonly TextBlock _date = new TextBlock { FontSize = 11.5 };
        readonly StackPanel _chips = new StackPanel();
        bool _seeking, _active;

        public IslandHome()
        {
            _artGlyph.Style = Ui.StyleRes("Icon");
            _artGlyph.Foreground = Ui.Res("Text3Brush");
            _artist.Foreground = Ui.Res("Text2Brush");
            _pos.Foreground = _dur.Foreground = Ui.Res("Text3Brush");
            _pos.Style = _dur.Style = Ui.StyleRes("Num");
            _date.Foreground = Ui.Res("Text2Brush");
            _clock.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            _clock.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
            _seek.Style = Ui.StyleRes("Thin");
            _art.Background = Ui.Res("Card2Brush");
            _art.Child = _artGlyph;

            // ---- media card
            foreach (var (b, g) in new[] { (_prev, Glyphs.Prev), (_play, Glyphs.Play), (_next, Glyphs.Next) })
            {
                b.Style = Ui.StyleRes(b == _play ? "PrimaryButton" : "IconButton");
                b.Content = g;
                b.Width = b.Height = b == _play ? 32 : 30;
                b.FontSize = b == _play ? 13 : 13;
                b.Margin = new Thickness(b == _play ? 6 : 0, 0, b == _play ? 6 : 0, 0);
            }
            _prev.Click += async (s, e) => await MediaService.Instance.PreviousAsync();
            _next.Click += async (s, e) => await MediaService.Instance.NextAsync();
            _play.Click += async (s, e) => await MediaService.Instance.TogglePlayPauseAsync();
            _seek.PreviewMouseLeftButtonDown += (s, e) => _seeking = true;
            _seek.PreviewMouseLeftButtonUp += async (s, e) => { _seeking = false; await MediaService.Instance.SeekAsync(TimeSpan.FromSeconds(_seek.Value)); };

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var titleRow = new DockPanel();
            DockPanel.SetDock(_spectrum, Dock.Right);
            titleRow.Children.Add(_spectrum);
            titleRow.Children.Add(_title);
            text.Children.Add(titleRow);
            text.Children.Add(_artist);
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 4, 0, 0) };
            controls.Children.Add(_prev); controls.Children.Add(_play); controls.Children.Add(_next);
            text.Children.Add(controls);

            var top = new DockPanel();
            DockPanel.SetDock(_art, Dock.Left);
            top.Children.Add(_art);
            top.Children.Add(text);

            _seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _seekRow.ColumnDefinitions.Add(new ColumnDefinition());
            _seekRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _seekRow.Margin = new Thickness(0, 6, 0, 0);
            Grid.SetColumn(_seek, 1); Grid.SetColumn(_dur, 2);
            _seekRow.Children.Add(_pos); _seekRow.Children.Add(_seek); _seekRow.Children.Add(_dur);

            var media = new DockPanel();
            DockPanel.SetDock(_seekRow, Dock.Bottom);
            media.Children.Add(_seekRow);
            media.Children.Add(top);
            var mediaCard = new Border { Style = Ui.StyleRes("Card"), CornerRadius = new CornerRadius(16), Padding = new Thickness(10, 9, 12, 8), Child = media };

            // ---- glance column
            var glance = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 4, 0) };
            glance.Children.Add(_clock);
            glance.Children.Add(_date);
            _chips.Margin = new Thickness(0, 8, 0, 0);
            glance.Children.Add(_chips);
            glance.Cursor = Cursors.Hand;
            glance.MouseLeftButtonUp += (s, e) => NotchWindow.Instance?.SelectTab("Clock");

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(mediaCard);
            Grid.SetColumn(glance, 1);
            grid.Children.Add(glance);
            Content = grid;

            _timer.Tick += (s, e) => Tick();
            MediaService.Instance.Changed += () => { if (IsVisible) RefreshMedia(); };
            WeatherService.Instance.Updated += () => { if (IsVisible) RefreshChips(); };
        }

        public void OnActivated() { _active = true; RefreshMedia(); RefreshChips(); Tick(); _timer.Start(); }
        public void OnDeactivated() { _active = false; _timer.Stop(); _spectrum.Running = false; }

        static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

        void RefreshMedia()
        {
            var md = MediaService.Instance;
            bool has = md.HasSession && !string.IsNullOrEmpty(md.Title);
            _title.Text = has ? md.Title : "Nothing playing";
            _artist.Text = has ? (string.IsNullOrEmpty(md.Artist) ? md.AppName : md.Artist) : "Play something anywhere";
            _play.Content = md.IsPlaying ? Glyphs.Pause : Glyphs.Play;
            _play.IsEnabled = md.HasSession;
            _prev.IsEnabled = md.HasSession && md.CanPrev;
            _next.IsEnabled = md.HasSession && md.CanNext;
            _seekRow.Visibility = md.HasSession && md.Duration > TimeSpan.Zero ? Visibility.Visible : Visibility.Hidden;
            _seek.IsEnabled = md.CanSeek;
            _dur.Text = Fmt(md.Duration);
            var tint = new SolidColorBrush(md.ArtColor ?? Ui.AccentColor);
            _seek.Foreground = tint;
            _spectrum.Brush = tint;
            _spectrum.Running = _active && md.IsPlaying;
            if (md.Art != null) { _art.Background = new ImageBrush(md.Art) { Stretch = Stretch.UniformToFill }; _artGlyph.Visibility = Visibility.Collapsed; }
            else { _art.Background = Ui.Res("Card2Brush"); _artGlyph.Visibility = Visibility.Visible; }
            Tick();
        }

        void Tick()
        {
            var now = DateTime.Now;
            _clock.Text = now.ToString(CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H") ? "HH:mm" : "h:mm", CultureInfo.CurrentCulture);
            _date.Text = now.ToString("ddd, d MMM", CultureInfo.CurrentCulture);
            var md = MediaService.Instance;
            if (!_seeking && md.HasSession && md.Duration > TimeSpan.Zero)
            {
                var pos = md.Position;
                _seek.Maximum = Math.Max(1, md.Duration.TotalSeconds);
                _seek.Value = Math.Min(_seek.Maximum, pos.TotalSeconds);
                _pos.Text = Fmt(pos);
            }
            if (now.Second % 10 == 0) RefreshChips();
        }

        void RefreshChips()
        {
            _chips.Children.Clear();
            var w = WeatherService.Instance;
            if (w.HasLocation && w.Data != null)
                Chip(WeatherService.Symbol(w.Data.Code, w.Data.IsDay), $"{Math.Round(w.Data.Temp)}° {SettingsStore.Current.City}", true, "Weather");
            var p = PowerService.Instance;
            if (p.HasBattery) Chip(p.Charging ? "" : "", $"{p.Percent}%" + (p.Charging ? " · charging" : ""), false, "System");
            var t = TimerService.Instance;
            if (t.CdRunning) Chip(Glyphs.Stopwatch, "Timer " + Ui.ClockCeil(t.CdLeft), false, "Timers");
            else if (t.PomoRunning) Chip(Glyphs.Stopwatch, $"{t.PhaseName(t.Phase)} {Ui.ClockCeil(t.PomoLeft)}", false, "Timers");
            else if (AlarmService.NextRing() is DateTime next && next - DateTime.Now < TimeSpan.FromHours(18))
                Chip(Glyphs.Alarm, "Alarm " + next.ToString("HH:mm"), false, "Timers");
        }

        void Chip(string glyph, string text, bool emoji, string target)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var g = new TextBlock { Text = glyph, FontSize = emoji ? 13 : 11.5, Width = 18, VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Res("Text2Brush") };
            if (emoji) g.SetResourceReference(TextBlock.FontFamilyProperty, "EmojiFont"); else g.Style = Ui.StyleRes("Icon");
            g.HorizontalAlignment = HorizontalAlignment.Left;
            row.Children.Add(g);
            row.Children.Add(new TextBlock { Text = text, FontSize = 11.5, Foreground = Ui.Res("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 140, Margin = new Thickness(4, 0, 0, 0) });
            row.MouseLeftButtonUp += (s, e) => { e.Handled = true; NotchWindow.Instance?.SelectTab(target); };
            _chips.Children.Add(row);
        }
    }

    /// <summary>A quick countdown or a Pomodoro, nothing else.</summary>
    public sealed class IslandTimers : UserControl, ITabView
    {
        static TimerService T => TimerService.Instance;
        readonly RingProgress _ring = new RingProgress { Width = 104, Height = 104, Thickness = 5 };
        readonly TextBlock _big = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        readonly TextBlock _label = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
        readonly Button _play = new Button(), _reset = new Button(), _extra = new Button();
        readonly WrapPanel _presets = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        readonly RadioButton _timerMode = new RadioButton(), _focusMode = new RadioButton();
        bool FocusMode => _focusMode.IsChecked == true;

        public IslandTimers()
        {
            _ring.SetResourceReference(RingProgress.RingBrushProperty, "AccentBrush");
            _big.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            _big.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
            _label.Foreground = Ui.Res("Text2Brush");
            var centre = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            centre.Children.Add(_big);
            centre.Children.Add(_label);
            var ring = new Grid { Width = 104, Height = 104, VerticalAlignment = VerticalAlignment.Center };
            ring.Children.Add(_ring);
            ring.Children.Add(centre);

            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (rb, label) in new[] { (_timerMode, "Timer"), (_focusMode, "Focus") })
            {
                rb.Style = Ui.StyleRes("Seg");
                rb.Content = label;
                rb.GroupName = "island-timer-mode";
                rb.Padding = new Thickness(12, 3, 12, 3);
                rb.FontSize = 11.5;
                rb.Checked += (s, e) => Render();
                seg.Children.Add(rb);
            }
            var segBox = new Border { Background = Ui.Res("Card2Brush"), CornerRadius = new CornerRadius(9), Padding = new Thickness(2), Child = seg, HorizontalAlignment = HorizontalAlignment.Left };

            foreach (var m in new[] { 1, 5, 10, 15, 25, 45 })
            {
                var b = new Button { Style = Ui.StyleRes("Pill"), Content = m + "m", Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 4, 4), FontSize = 11.5 };
                int mm = m;
                b.Click += (s, e) => { T.CdSet(mm); if (!T.CdRunning) T.CdToggle(); Render(); };
                _presets.Children.Add(b);
            }

            foreach (var b in new[] { _reset, _play, _extra })
            {
                b.Style = Ui.StyleRes(b == _play ? "PrimaryButton" : "IconButton");
                b.Width = b.Height = b == _play ? 36 : 30;
                b.FontSize = 13;
                b.Margin = new Thickness(0, 0, 6, 0);
                if (b != _play) b.Background = Ui.Res("Card2Brush");
            }
            _play.SetResourceReference(BackgroundProperty, "AccentBrush");
            _play.Foreground = Brushes.Black;
            _reset.Content = Glyphs.Refresh;
            _reset.Click += (s, e) => { if (FocusMode) T.PomoReset(); else T.CdReset(); Render(); };
            _play.Click += (s, e) => { if (FocusMode) T.PomoToggle(); else T.CdToggle(); Render(); };
            _extra.Click += (s, e) => { if (FocusMode) T.PomoSkip(); else T.CdSet((int)Math.Ceiling(T.CdLeft.TotalMinutes) + 1); Render(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            buttons.Children.Add(_reset); buttons.Children.Add(_play); buttons.Children.Add(_extra);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
            right.Children.Add(segBox);
            right.Children.Add(_presets);
            right.Children.Add(buttons);

            var dock = new DockPanel { Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(ring, Dock.Left);
            dock.Children.Add(ring);
            dock.Children.Add(right);
            Content = dock;

            T.Tick += () => { if (IsVisible) Render(); };
            T.StateChanged += () => { if (IsVisible) Render(); };
        }

        public void OnActivated()
        {
            if (T.PomoRunning && !T.CdRunning) _focusMode.IsChecked = true; else if (_focusMode.IsChecked != true) _timerMode.IsChecked = true;
            Render();
        }
        public void OnDeactivated() { }

        void Render()
        {
            _presets.Visibility = FocusMode ? Visibility.Collapsed : Visibility.Visible;
            if (FocusMode)
            {
                _ring.Progress = T.PomoProgress;
                _big.Text = Ui.ClockCeil(T.PomoLeft);
                _label.Text = $"{T.PhaseName(T.Phase)} {T.Cycle}/{Math.Max(1, SettingsStore.Current.Cycles)}";
                _play.Content = T.PomoRunning ? Glyphs.Pause : Glyphs.Play;
                _extra.Content = Glyphs.Next;
                _extra.ToolTip = "Skip to the next phase";
            }
            else
            {
                _ring.Progress = T.CdProgress;
                _big.Text = Ui.ClockCeil(T.CdLeft);
                _label.Text = T.CdRunning ? "Running" : T.CdLeft <= TimeSpan.Zero ? "Done" : "Timer";
                _play.Content = T.CdRunning ? Glyphs.Pause : Glyphs.Play;
                _extra.Content = "+1";
                _extra.FontFamily = (FontFamily)FindResource("UiFont");
                _extra.ToolTip = "Add a minute";
            }
            if (FocusMode) _extra.FontFamily = (FontFamily)FindResource("IconFont");
        }
    }

    /// <summary>The last few things you copied. Click one to copy it again.</summary>
    public sealed class IslandClipboard : UserControl, ITabView
    {
        readonly StackPanel _list = new StackPanel();

        public IslandClipboard()
        {
            var head = new DockPanel { Margin = new Thickness(4, 0, 2, 6) };
            var all = new Button { Style = Ui.StyleRes("Pill"), Content = "Full history", Padding = new Thickness(10, 3, 10, 3), FontSize = 11.5 };
            all.Click += (s, e) => NotchWindow.Instance?.SelectTab("Clipboard");
            DockPanel.SetDock(all, Dock.Right);
            head.Children.Add(all);
            head.Children.Add(new TextBlock { Text = "Click to copy again", Style = Ui.StyleRes("Caption"), VerticalAlignment = VerticalAlignment.Center });
            var dock = new DockPanel();
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            dock.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = dock;
            ClipboardService.Instance.Items.CollectionChanged += (s, e) => { if (IsVisible) Render(); };
        }

        public void OnActivated() => Render();
        public void OnDeactivated() { }

        void Render()
        {
            _list.Children.Clear();
            var items = ClipboardService.Instance.Items.Take(8).ToList();
            if (items.Count == 0)
                _list.Children.Add(new TextBlock { Text = "Nothing copied yet.", Style = Ui.StyleRes("Caption"), Margin = new Thickness(4, 10, 0, 0) });
            foreach (var it in items)
            {
                var item = it;
                var row = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                var dp = new DockPanel();
                FrameworkElement icon = item.Thumb != null
                    ? new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Background = new ImageBrush(item.Thumb) { Stretch = Stretch.UniformToFill } }
                    : new TextBlock { Text = item.Glyph, Style = Ui.StyleRes("Icon"), FontSize = 12, Width = 22, Foreground = Ui.Res("Text2Brush") };
                icon.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(icon, Dock.Left);
                dp.Children.Add(icon);
                var when = new TextBlock { Text = Ui.Ago(item.Time), FontSize = 10.5, Foreground = Ui.Res("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(when, Dock.Right);
                dp.Children.Add(when);
                dp.Children.Add(new TextBlock { Text = item.Preview, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                row.Child = dp;
                row.MouseEnter += (s, e) => row.Background = Ui.Res("Card2Brush");
                row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
                row.MouseLeftButtonUp += (s, e) =>
                {
                    ClipboardService.Instance.Copy(item);
                    NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Copy, GlyphBrush = Ui.Res("GreenBrush"), Title = "Copied" });
                };
                _list.Children.Add(row);
            }
        }
    }
}
