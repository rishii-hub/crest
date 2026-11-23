using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Crest.Controls;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    public sealed record WidgetDef(string Key, string Name, int W, int H, string Glyph, string Sub);

    /// <summary>
    /// Widgets and the island pages that hold them. A page is a grid four columns wide and up to two rows tall;
    /// every widget can be any size from 1×1 to 4×2 and widgets are packed in order, left to right, top to bottom.
    /// </summary>
    public static class WidgetCatalog
    {
        static AppSettings S => SettingsStore.Current;
        public const int Cols = 4, Rows = 2;
        public const double Unit = 150, UnitH = 134, Gap = 10;

        public static readonly WidgetDef[] All =
        {
            new("Music", "Music", 2, 1, Glyphs.Music, "Album art, controls and a seek bar"),
            new("Lyrics", "Lyrics", 2, 1, "\uE8D2", "Synced lyrics for whatever is playing"),
            new("Glance", "At a glance", 1, 1, Glyphs.Home, "Time, weather, battery and what's next"),
            new("Clock", "Clock", 1, 1, "\uE917", "Big time and date"),
            new("Weather", "Weather", 1, 1, Glyphs.Cloud, "Now; wider shows the hours, taller the week"),
            new("Battery", "Battery", 1, 1, Glyphs.Battery, "Charge and power state"),
            new("Timer", "Timer", 1, 1, Glyphs.Stopwatch, "Tap to start 5 or 25 minutes"),
            new("System", "System", 1, 1, Glyphs.Chart, "CPU and memory"),
            new("Calendar", "Calendar", 1, 1, Glyphs.Calendar, "This month"),
            new("Toggles", "Quick toggles", 1, 1, "\uE9E9", "Mute, mic, screenshot, lock"),
            new("Countdown", "Countdown", 1, 1, "\uE787", "Days until a date you choose"),
            new("Note", "Note", 1, 1, Glyphs.Note, "Your notes and to-dos at a glance"),
            new("ScreenTime", "Screen time", 1, 1, "\uE823", "Today so far"),
        };

        public static readonly (int W, int H)[] Sizes = { (1, 1), (2, 1), (3, 1), (4, 1), (1, 2), (2, 2), (3, 2), (4, 2) };
        public static readonly string[] PageGlyphs = { Glyphs.Home, "\uE80F", Glyphs.Music, Glyphs.Chart, Glyphs.Calendar, Glyphs.Cloud, "\uE734", "\uEB51", "\uE7FC", "\uE821", "\uE943", "\uE7BE" };

        public static WidgetDef? Get(string key) => All.FirstOrDefault(w => w.Key == key);
        public static IslandPageDef? Page(string? id) => id == null ? null : S.WidgetPages.FirstOrDefault(p => p.Id == id);

        /// <summary>First run on 1.8: turn the old single Now page into the first widget page.</summary>
        public static void EnsurePages()
        {
            if (S.WidgetPages.Count > 0) return;
            var home = new IslandPageDef { Id = "Home", Name = "Now", Glyph = Glyphs.Home };
            foreach (var k in S.NowWidgets)
                if (Get(k) is WidgetDef d) home.Widgets.Add(new WidgetSlot { Kind = k, W = d.W, H = d.H });
            if (home.Widgets.Count == 0) { home.Widgets.Add(new WidgetSlot { Kind = "Music", W = 2 }); home.Widgets.Add(new WidgetSlot { Kind = "Glance" }); }
            S.WidgetPages.Add(home);
            SettingsStore.Save();
        }

        public static IslandPageDef NewPage(string name = "Widgets")
        {
            int n = 2;
            string unique = name;
            while (S.WidgetPages.Any(p => p.Name == unique)) unique = $"{name} {n++}";
            var page = new IslandPageDef { Name = unique, Glyph = PageGlyphs[S.WidgetPages.Count % PageGlyphs.Length] };
            S.WidgetPages.Add(page);
            S.IslandPages.Add(page.Id);
            return page;
        }

        public static double Width(int w) => w * Unit + (w - 1) * Gap;
        public static double Height(int h) => h * UnitH + (h - 1) * Gap;

        /// <summary>Where each widget goes. Widgets that don't fit anywhere come back with Col = -1.</summary>
        public static List<(WidgetSlot Slot, int Col, int Row)> Pack(IslandPageDef page)
        {
            var used = new bool[Rows, Cols];
            var result = new List<(WidgetSlot, int, int)>();
            foreach (var w in page.Widgets)
            {
                int ww = Math.Clamp(w.W, 1, Cols), hh = Math.Clamp(w.H, 1, Rows);
                (int c, int r) spot = (-1, -1);
                for (int r = 0; r + hh <= Rows && spot.c < 0; r++)
                    for (int c = 0; c + ww <= Cols && spot.c < 0; c++)
                    {
                        bool free = true;
                        for (int y = r; y < r + hh && free; y++)
                            for (int x = c; x < c + ww && free; x++)
                                if (used[y, x]) free = false;
                        if (free) spot = (c, r);
                    }
                if (spot.c >= 0)
                    for (int y = spot.r; y < spot.r + hh; y++)
                        for (int x = spot.c; x < spot.c + ww; x++)
                            used[y, x] = true;
                result.Add((w, spot.c, spot.r));
            }
            return result;
        }

        /// <summary>Content size of a page: only as wide and tall as its widgets need.</summary>
        public static Size PageSize(IslandPageDef page)
        {
            int cols = 1, rows = 1;
            foreach (var (slot, c, r) in Pack(page))
                if (c >= 0) { cols = Math.Max(cols, c + Math.Clamp(slot.W, 1, Cols)); rows = Math.Max(rows, r + Math.Clamp(slot.H, 1, Rows)); }
            return new Size(Width(cols), Height(rows));
        }

        public static Widget Create(string key) => key switch
        {
            "Music" => new MusicWidget(),
            "Lyrics" => new LyricsWidget(),
            "Clock" => new ClockWidget(),
            "Weather" => new WeatherWidget(),
            "Battery" => new BatteryWidget(),
            "Timer" => new TimerWidget(),
            "System" => new SystemWidget(),
            "Calendar" => new CalendarWidget(),
            "Toggles" => new TogglesWidget(),
            "Countdown" => new CountdownWidget(),
            "Note" => new NoteWidget(),
            "ScreenTime" => new ScreenTimeWidget(),
            _ => new GlanceWidget(),
        };

        /// <summary>Right-click menu for a widget, on the island or in the editor.</summary>
        public static ContextMenu Menu(IslandPageDef page, WidgetSlot slot, Action? changed = null)
        {
            var menu = new ContextMenu();
            void Commit() { SettingsStore.Update(_ => { }); changed?.Invoke(); }
            var size = new MenuItem { Header = "Size" };
            foreach (var (w, h) in Sizes)
            {
                var item = new MenuItem { Header = $"{w} × {h}", IsCheckable = true, IsChecked = slot.W == w && slot.H == h };
                item.Click += (s, e) => { slot.W = w; slot.H = h; Commit(); };
                size.Items.Add(item);
            }
            menu.Items.Add(size);
            var move = new MenuItem { Header = "Move to page" };
            foreach (var p in S.WidgetPages)
            {
                var target = p;
                var item = new MenuItem { Header = p.Name, IsEnabled = p != page };
                item.Click += (s, e) => { page.Widgets.Remove(slot); target.Widgets.Add(slot); if (!S.IslandPages.Contains(target.Id)) S.IslandPages.Add(target.Id); Commit(); };
                move.Items.Add(item);
            }
            var fresh = new MenuItem { Header = "New page" };
            fresh.Click += (s, e) => { var np = NewPage(); page.Widgets.Remove(slot); np.Widgets.Add(slot); Commit(); };
            move.Items.Add(new Separator());
            move.Items.Add(fresh);
            menu.Items.Add(move);
            var left = new MenuItem { Header = "Move earlier", IsEnabled = page.Widgets.IndexOf(slot) > 0 };
            left.Click += (s, e) => { int i = page.Widgets.IndexOf(slot); page.Widgets.RemoveAt(i); page.Widgets.Insert(i - 1, slot); Commit(); };
            var right = new MenuItem { Header = "Move later", IsEnabled = page.Widgets.IndexOf(slot) < page.Widgets.Count - 1 };
            right.Click += (s, e) => { int i = page.Widgets.IndexOf(slot); page.Widgets.RemoveAt(i); page.Widgets.Insert(i + 1, slot); Commit(); };
            menu.Items.Add(left);
            menu.Items.Add(right);
            var remove = new MenuItem { Header = "Remove" };
            remove.Click += (s, e) => { page.Widgets.Remove(slot); Commit(); };
            menu.Items.Add(remove);
            menu.Items.Add(new Separator());
            var edit = new MenuItem { Header = "Customise pages and widgets…" };
            edit.Click += (s, e) => AppWindow.ShowPage("Island");
            menu.Items.Add(edit);
            return menu;
        }

        /// <summary>"Add widget" menu for a page.</summary>
        public static ContextMenu AddMenu(IslandPageDef page, Action? changed = null)
        {
            var menu = new ContextMenu();
            foreach (var d in All)
            {
                var def = d;
                var item = new MenuItem { Header = d.Name, ToolTip = d.Sub };
                item.Click += (s, e) => { page.Widgets.Add(new WidgetSlot { Kind = def.Key, W = def.W, H = def.H }); SettingsStore.Update(_ => { }); changed?.Invoke(); };
                menu.Items.Add(item);
            }
            return menu;
        }
    }

    /// <summary>A card on the Now page. Start/Stop bracket the time it's visible, so hidden widgets cost nothing.</summary>
    public abstract class Widget : Border
    {
        readonly DispatcherTimer _tick;
        protected Widget(int tickMs = 1000)
        {
            Style = Ui.StyleRes("Card");
            CornerRadius = new CornerRadius(16);
            Padding = new Thickness(12, 10, 12, 10);
            ClipToBounds = true;
            _tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(tickMs) };
            _tick.Tick += (s, e) => Tick();
        }
        public virtual void Start() { Tick(); _tick.Start(); }
        /// <summary>Called with the widget's size in grid cells (1–4 wide, 1–2 tall) so it can lay itself out.</summary>
        public virtual void Resize(int w, int h) { }
        public virtual void Stop() => _tick.Stop();
        protected virtual void Tick() { }

        protected static TextBlock Label(string text, double size = 11, string brush = "Text2Brush", FontWeight? weight = null) =>
            new TextBlock { Text = text, FontSize = size, Foreground = Ui.Res(brush), FontWeight = weight ?? FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        protected static TextBlock Big(string text, double size)
        {
            var t = new TextBlock { Text = text, FontSize = size, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            t.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
            return t;
        }
        protected static TextBlock Icon(string glyph, double size, string brush = "Text2Brush")
        {
            var t = new TextBlock { Text = glyph, FontSize = size, Foreground = Ui.Res(brush) };
            t.Style = Ui.StyleRes("Icon");
            return t;
        }
        protected void OpenOnClick(string tab)
        {
            Cursor = Cursors.Hand;
            MouseLeftButtonUp += (s, e) => { if (e.OriginalSource is DependencyObject d && FindButton(d)) return; NotchWindow.Instance?.SelectTab(tab); };
        }
        static bool FindButton(DependencyObject d)
        {
            for (var x = d; x != null; x = VisualTreeHelper.GetParent(x)) { if (x is ButtonBase) return true; if (x is Widget) return false; }
            return false;
        }
    }

    // ------------------------------------------------------------------ the board
    /// <summary>One widget page of the island: your widgets, packed into a 4 × 2 grid.</summary>
    public sealed class WidgetBoard : UserControl, ITabView
    {
        readonly string _pageId;
        readonly Canvas _canvas = new Canvas { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        readonly List<Widget> _widgets = new List<Widget>();
        string _sig = "";
        bool _active;

        public WidgetBoard(string pageId)
        {
            _pageId = pageId;
            Content = _canvas;
            Build();
            SettingsStore.Changed += () => { if (Signature() != _sig) Build(); };
        }

        string Signature()
        {
            var p = WidgetCatalog.Page(_pageId);
            return p == null ? "" : string.Join(",", p.Widgets.Select(w => $"{w.Id}:{w.Kind}:{w.W}x{w.H}"));
        }

        void Build()
        {
            foreach (var w in _widgets) w.Stop();
            _widgets.Clear();
            _canvas.Children.Clear();
            _sig = Signature();
            var page = WidgetCatalog.Page(_pageId);
            if (page == null) return;
            var size = WidgetCatalog.PageSize(page);
            _canvas.Width = size.Width;
            _canvas.Height = size.Height;
            if (page.Widgets.Count == 0)
            {
                var add = new Button { Style = Ui.StyleRes("Pill"), Content = "+ Add a widget", Padding = new Thickness(14, 6, 14, 6), Width = WidgetCatalog.Width(1), Height = 36 };
                add.Click += (s, e) => { var m = WidgetCatalog.AddMenu(page); m.PlacementTarget = add; Hold(m); m.IsOpen = true; };
                Canvas.SetTop(add, (WidgetCatalog.UnitH - 36) / 2);
                _canvas.Children.Add(add);
                return;
            }
            foreach (var (slot, col, row) in WidgetCatalog.Pack(page))
            {
                if (col < 0) continue; // doesn't fit; the editor says so
                Widget w;
                try { w = WidgetCatalog.Create(slot.Kind); }
                catch (Exception ex) { Log.Error("widget " + slot.Kind, ex); continue; }
                int ww = Math.Clamp(slot.W, 1, WidgetCatalog.Cols), hh = Math.Clamp(slot.H, 1, WidgetCatalog.Rows);
                w.Width = WidgetCatalog.Width(ww);
                w.Height = WidgetCatalog.Height(hh);
                w.Resize(ww, hh);
                Canvas.SetLeft(w, col * (WidgetCatalog.Unit + WidgetCatalog.Gap));
                Canvas.SetTop(w, row * (WidgetCatalog.UnitH + WidgetCatalog.Gap));
                var s2 = slot;
                w.ContextMenuOpening += (s, e) => { var m = WidgetCatalog.Menu(page, s2); Hold(m); w.ContextMenu = m; };
                w.ContextMenu = new ContextMenu(); // placeholder so right-click raises ContextMenuOpening
                _widgets.Add(w);
                _canvas.Children.Add(w);
                if (_active) w.Start();
            }
        }

        /// <summary>Keep the island open while a menu from it is showing.</summary>
        static void Hold(ContextMenu m)
        {
            var notch = NotchWindow.Instance;
            if (notch == null) return;
            notch.DragOut = true;
            m.Closed += (s, e) => notch.DragOut = false;
        }

        public void OnActivated() { _active = true; foreach (var w in _widgets) w.Start(); }
        public void OnDeactivated() { _active = false; foreach (var w in _widgets) w.Stop(); }
    }

    // ------------------------------------------------------------------ widgets
    public sealed class MusicWidget : Widget
    {
        readonly Border _art = new Border { Width = 58, Height = 58, CornerRadius = new CornerRadius(12) };
        readonly TextBlock _artGlyph = new TextBlock { Text = "", FontSize = 20 };
        readonly TextBlock _title = new TextBlock { FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _artist = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly TextBlock _pos = new TextBlock { FontSize = 10, Width = 30 }, _dur = new TextBlock { FontSize = 10, Width = 30, TextAlignment = TextAlignment.Right };
        readonly Slider _seek = new Slider { Minimum = 0, Maximum = 1, Margin = new Thickness(4, 0, 4, 0) };
        readonly Button _prev = new Button(), _play = new Button(), _next = new Button();
        readonly SpectrumBars _spectrum = new SpectrumBars { Height = 14, Width = 34, Bars = 6, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
        readonly Grid _seekRow = new Grid();
        readonly StackPanel _controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(-6, 4, 0, 0) };
        readonly StackPanel _launch = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
        bool _seeking, _running;

        public MusicWidget() : base(500)
        {
            _artGlyph.Style = Ui.StyleRes("Icon");
            _artGlyph.Foreground = Ui.Res("Text3Brush");
            _artist.Foreground = Ui.Res("Text2Brush");
            _pos.Foreground = _dur.Foreground = Ui.Res("Text3Brush");
            _pos.Style = _dur.Style = Ui.StyleRes("Num");
            _seek.Style = Ui.StyleRes("Thin");
            _art.Background = Ui.Res("Card2Brush");
            _art.Child = _artGlyph;

            foreach (var (b, g) in new[] { (_prev, Glyphs.Prev), (_play, Glyphs.Play), (_next, Glyphs.Next) })
            {
                b.Style = Ui.StyleRes(b == _play ? "PrimaryButton" : "IconButton");
                b.Content = g;
                b.Width = b.Height = b == _play ? 32 : 30;
                b.FontSize = 13;
                b.Margin = new Thickness(b == _play ? 6 : 0, 0, b == _play ? 6 : 0, 0);
            }
            _prev.Click += async (s, e) => await MediaService.Instance.PreviousAsync();
            _next.Click += async (s, e) => await MediaService.Instance.NextAsync();
            _play.Click += async (s, e) => await MediaService.Instance.TogglePlayPauseAsync();
            _seek.PreviewMouseLeftButtonDown += (s, e) => _seeking = true;
            _seek.PreviewMouseLeftButtonUp += async (s, e) => { _seeking = false; await MediaService.Instance.SeekAsync(TimeSpan.FromSeconds(_seek.Value)); };
            _controls.Children.Add(_prev); _controls.Children.Add(_play); _controls.Children.Add(_next);

            // nothing playing: offer the usual players instead of dead controls
            foreach (var (name, glyph, color, target) in new[]
            {
                ("Spotify", "", "#1DB954", "spotify:"),
                ("YouTube Music", "", "#FF0033", "https://music.youtube.com"),
                ("Media Player", "", "#0A84FF", "mswindowsmusic:"),
            })
            {
                var b = new Button { Style = Ui.StyleRes("IconButton"), Width = 30, Height = 30, Margin = new Thickness(0, 0, 6, 0), ToolTip = "Open " + name, FontSize = 12 };
                b.Content = new TextBlock { Text = glyph, Style = Ui.StyleRes("Icon"), Foreground = Ui.Brush(color) };
                b.Background = Ui.Res("Card2Brush");
                b.Click += (s, e) => Ui.Open(target);
                _launch.Children.Add(b);
            }

            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var titleRow = new DockPanel();
            DockPanel.SetDock(_spectrum, Dock.Right);
            titleRow.Children.Add(_spectrum);
            titleRow.Children.Add(_title);
            text.Children.Add(titleRow);
            text.Children.Add(_artist);
            text.Children.Add(_controls);
            text.Children.Add(_launch);

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

            var dock = new DockPanel();
            DockPanel.SetDock(_seekRow, Dock.Bottom);
            dock.Children.Add(_seekRow);
            dock.Children.Add(top);
            Child = dock;
            Padding = new Thickness(10, 9, 12, 8);
            MediaService.Instance.Changed += () => { if (_running) Refresh(); };
        }

        public override void Start() { _running = true; Refresh(); base.Start(); }
        public override void Stop() { _running = false; _spectrum.Running = false; base.Stop(); }

        bool _small;
        public override void Resize(int w, int h)
        {
            _small = w == 1;
            double art = h >= 2 ? (w == 1 ? 110 : 150) : w == 1 ? 46 : 58;
            _art.Width = _art.Height = art;
            _art.CornerRadius = new CornerRadius(art / 4.6);
            _artGlyph.FontSize = art / 2.8;
            _title.FontSize = h >= 2 || w >= 3 ? 15.5 : 13.5;
            _prev.Visibility = _next.Visibility = _small ? Visibility.Collapsed : Visibility.Visible;
            for (int i = 1; i < _launch.Children.Count; i++) _launch.Children[i].Visibility = _small && h == 1 ? Visibility.Collapsed : Visibility.Visible;
            _spectrum.Width = w >= 3 ? 60 : 34;
            if (h >= 2 && w == 1)
            {
                // tall and narrow: art on top, text underneath
                DockPanel.SetDock(_art, Dock.Top);
                _art.HorizontalAlignment = HorizontalAlignment.Center;
                ((FrameworkElement)_title.Parent).Margin = new Thickness(0);
                ((FrameworkElement)((FrameworkElement)_title.Parent).Parent).Margin = new Thickness(0, 8, 0, 0);
            }
            if (_small && h == 1) _seekRow.Margin = new Thickness(0, 4, 0, 0);
        }

        static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : $"{(int)t.TotalMinutes}:{t.Seconds:00}";

        void Refresh()
        {
            var md = MediaService.Instance;
            bool has = md.HasSession && !string.IsNullOrEmpty(md.Title);
            _title.Text = has ? md.Title : "Nothing playing";
            _artist.Text = has ? (string.IsNullOrEmpty(md.Artist) ? md.AppName : md.Artist) : "Open a player:";
            _controls.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            _launch.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            _play.Content = md.IsPlaying ? Glyphs.Pause : Glyphs.Play;
            _prev.IsEnabled = md.CanPrev;
            _next.IsEnabled = md.CanNext;
            _seekRow.Visibility = has && md.Duration > TimeSpan.Zero && !(_small && Height < 200) ? Visibility.Visible : Visibility.Collapsed;
            _seek.IsEnabled = md.CanSeek;
            _dur.Text = Fmt(md.Duration);
            var tint = new SolidColorBrush(md.ArtColor ?? Ui.AccentColor);
            _seek.Foreground = tint;
            _spectrum.Brush = tint;
            _spectrum.Running = _running && md.IsPlaying;
            _spectrum.Visibility = md.IsPlaying ? Visibility.Visible : Visibility.Collapsed;
            if (md.Art != null) { _art.Background = new ImageBrush(md.Art) { Stretch = Stretch.UniformToFill }; _artGlyph.Visibility = Visibility.Collapsed; }
            else { _art.Background = Ui.Res("Card2Brush"); _artGlyph.Visibility = Visibility.Visible; }
            Tick();
        }

        protected override void Tick()
        {
            var md = MediaService.Instance;
            if (_seeking || !md.HasSession || md.Duration <= TimeSpan.Zero) return;
            var pos = md.Position;
            _seek.Maximum = Math.Max(1, md.Duration.TotalSeconds);
            _seek.Value = Math.Min(_seek.Maximum, pos.TotalSeconds);
            _pos.Text = Fmt(pos);
        }
    }

    public sealed class LyricsWidget : Widget
    {
        readonly TextBlock _prev = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.45 };
        readonly TextBlock _cur = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 42, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 4) };
        readonly TextBlock _next = new TextBlock { FontSize = 11.5, TextTrimming = TextTrimming.CharacterEllipsis, Opacity = 0.45 };
        readonly TextBlock _head = new TextBlock { FontSize = 10.5, TextTrimming = TextTrimming.CharacterEllipsis };
        readonly StackPanel _lines = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        readonly StackPanel _off = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        readonly TranslateTransform _shift = new TranslateTransform();
        bool _running;

        public LyricsWidget() : base(1000)
        {
            _head.Foreground = Ui.Res("Text3Brush");
            _cur.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            _lines.RenderTransform = _shift;
            _lines.Children.Add(_prev);
            _lines.Children.Add(_cur);
            _lines.Children.Add(_next);

            _off.Children.Add(new TextBlock { Text = "Lyrics", FontSize = 15, FontWeight = FontWeights.SemiBold });
            _off.Children.Add(new TextBlock { Text = "Synced lyrics for Spotify, YouTube and anything else playing. Looks songs up on lrclib.net.", FontSize = 11, Foreground = Ui.Res("Text2Brush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 8) });
            var on = new Button { Style = Ui.StyleRes("Pill"), Content = "Turn on lyrics", HorizontalAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 4, 12, 4) };
            on.Click += (s, e) => SettingsStore.Update(x => x.Lyrics = true);
            _off.Children.Add(on);

            var dock = new DockPanel();
            DockPanel.SetDock(_head, Dock.Top);
            dock.Children.Add(_head);
            var g = new Grid();
            g.Children.Add(_lines);
            g.Children.Add(_off);
            dock.Children.Add(g);
            Child = dock;
            LyricsService.Changed += () => { if (_running) Render(false); };
            LyricsService.LineChanged += () => { if (_running) Render(true); };
        }

        public override void Start() { _running = true; Render(false); base.Start(); }
        public override void Stop() { _running = false; base.Stop(); }

        int _context = 1;
        public override void Resize(int w, int h)
        {
            _cur.FontSize = h >= 2 ? (w >= 3 ? 22 : 18) : w == 1 ? 13 : w >= 3 ? 17 : 15;
            _cur.MaxHeight = _cur.FontSize * (h >= 2 ? 4.2 : 2.8);
            _prev.FontSize = _next.FontSize = h >= 2 ? 13 : 11.5;
            _context = w == 1 && h == 1 ? 0 : 1;
            _prev.Visibility = _next.Visibility = _context == 0 ? Visibility.Collapsed : Visibility.Visible;
            if (h >= 2) { _prev.TextWrapping = _next.TextWrapping = TextWrapping.Wrap; _prev.MaxHeight = _next.MaxHeight = 40; }
        }

        void Render(bool animate)
        {
            var md = MediaService.Instance;
            bool off = LyricsService.Status == "off";
            _off.Visibility = off ? Visibility.Visible : Visibility.Collapsed;
            _lines.Visibility = off ? Visibility.Collapsed : Visibility.Visible;
            _head.Visibility = off ? Visibility.Collapsed : Visibility.Visible;
            if (off) return;
            _head.Text = md.HasSession && md.Title.Length > 0 ? $"{md.Title} · {md.Artist}" : "Lyrics";
            switch (LyricsService.Status)
            {
                case "synced":
                    _prev.Text = LyricsService.Previous;
                    _cur.Text = LyricsService.Index < 0 ? "♪" : LyricsService.Current;
                    _next.Text = LyricsService.Index < 0 ? (LyricsService.Lines.Count > 0 ? LyricsService.Lines[0].Text : "") : LyricsService.Next;
                    _cur.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
                    break;
                case "plain":
                    _prev.Text = "";
                    _cur.Text = string.Join(" / ", (LyricsService.Plain ?? "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Take(3));
                    _next.Text = "Only unsynced lyrics for this one";
                    break;
                case "loading": Message("Finding lyrics…"); break;
                case "none": Message("No lyrics found for this song"); break;
                case "error": Message("Couldn't reach lrclib.net"); break;
                default: Message("Play a song to see its lyrics"); break;
            }
            if (animate)
            {
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                _shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
                _cur.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, TimeSpan.FromMilliseconds(320)));
            }
        }

        void Message(string text)
        {
            _prev.Text = ""; _next.Text = "";
            _cur.Text = text;
            _cur.SetResourceReference(TextBlock.ForegroundProperty, "Text2Brush");
        }
    }

    public sealed class GlanceWidget : Widget
    {
        readonly TextBlock _clock = Big("", 28), _date = Label("", 11.5);
        readonly StackPanel _chips = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };

        public GlanceWidget() : base(1000)
        {
            Background = Brushes.Transparent; // reads as part of the island, not a card
            Padding = new Thickness(4, 6, 0, 4);
            _clock.FontWeight = FontWeights.Light;
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(_clock);
            s.Children.Add(_date);
            s.Children.Add(_chips);
            Child = s;
            WeatherService.Instance.Updated += () => { if (IsVisible) Chips(); };
        }

        protected override void Tick()
        {
            var now = DateTime.Now;
            _clock.Text = Ui.ShortTime(now);
            _date.Text = now.ToString("ddd, d MMM", CultureInfo.CurrentCulture);
            if (now.Second % 10 == 0 || _chips.Children.Count == 0) Chips();
        }

        void Chips()
        {
            _chips.Children.Clear();
            var w = WeatherService.Instance;
            if (w.HasLocation && w.Data != null)
            {
                var city = SettingsStore.Current.City;
                string place = string.IsNullOrWhiteSpace(city) || city.Equals("Current location", StringComparison.OrdinalIgnoreCase) ? WeatherService.Describe(w.Data.Code) : city;
                Chip(Emoji(WeatherService.Symbol(w.Data.Code, w.Data.IsDay)), $"{Math.Round(w.Data.Temp)}° {place}", "Weather");
            }
            var p = PowerService.Instance;
            if (p.HasBattery)
                Chip(new BatteryGlyph { Level = p.Percent, Charging = p.Charging, Width = 16, Height = 9, VerticalAlignment = VerticalAlignment.Center }, $"{p.Percent}%" + (p.Charging ? " · charging" : ""), "System");
            var t = TimerService.Instance;
            if (t.CdRunning) Chip(Icon(Glyphs.Stopwatch, 11.5), "Timer " + Ui.ClockCeil(t.CdLeft), "Timers");
            else if (t.PomoRunning) Chip(Icon(Glyphs.Stopwatch, 11.5), $"{t.PhaseName(t.Phase)} {Ui.ClockCeil(t.PomoLeft)}", "Timers");
            else if (AlarmService.NextRing() is DateTime next && next - DateTime.Now < TimeSpan.FromHours(18))
                Chip(Icon(Glyphs.Alarm, 11.5), "Alarm " + next.ToString("HH:mm"), "Timers");
        }

        static TextBlock Emoji(string e)
        {
            var t = new TextBlock { Text = e, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center };
            t.SetResourceReference(TextBlock.FontFamilyProperty, "EmojiFont");
            return t;
        }

        void Chip(FrameworkElement glyph, string text, string target)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1), Background = Brushes.Transparent, Cursor = Cursors.Hand };
            var box = new Border { Width = 20, Child = glyph, VerticalAlignment = VerticalAlignment.Center };
            glyph.HorizontalAlignment = HorizontalAlignment.Left;
            DockPanel.SetDock(box, Dock.Left);
            row.Children.Add(box);
            row.Children.Add(Label(text, 11.5));
            row.MouseLeftButtonUp += (s, e) => { e.Handled = true; NotchWindow.Instance?.SelectTab(target); };
            _chips.Children.Add(row);
        }
    }

    public sealed class ClockWidget : Widget
    {
        readonly TextBlock _time = Big("", 34), _sec = Label("", 12, "Text3Brush"), _date = Label("", 11.5), _day = Label("", 11, "Text3Brush");
        public override void Resize(int w, int h)
        {
            _time.FontSize = h >= 2 ? (w >= 2 ? 72 : 44) : w >= 2 ? 46 : 34;
            _sec.FontSize = _time.FontSize * 0.36;
            if (w >= 2 || h >= 2) { foreach (UIElement c in ((StackPanel)Child).Children) if (c is FrameworkElement f) f.HorizontalAlignment = HorizontalAlignment.Center; }
        }
        public ClockWidget() : base(1000)
        {
            _time.FontWeight = FontWeights.Light;
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(_time);
            _sec.VerticalAlignment = VerticalAlignment.Bottom;
            _sec.Margin = new Thickness(3, 0, 0, 7);
            row.Children.Add(_sec);
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(row); s.Children.Add(_date); s.Children.Add(_day);
            Child = s;
            OpenOnClick("Clock");
        }
        protected override void Tick()
        {
            var n = DateTime.Now;
            _time.Text = Ui.ShortTime(n);
            _sec.Text = n.ToString("ss");
            _date.Text = n.ToString("dddd", CultureInfo.CurrentCulture);
            _day.Text = n.ToString("d MMMM", CultureInfo.CurrentCulture) + $" · week {ISOWeek.GetWeekOfYear(n)}";
        }
    }

    public sealed class WeatherWidget : Widget
    {
        readonly TextBlock _sym = new TextBlock { FontSize = 26 }, _temp = Big("", 28), _desc = Label("", 11.5), _hl = Label("", 11, "Text3Brush"), _place = Label("", 10.5, "Text3Brush");
        readonly UniformGrid _hours = new UniformGrid { Rows = 1, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        readonly StackPanel _days = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
        int _w = 1, _h = 1;
        public override void Resize(int w, int h)
        {
            _w = w; _h = h;
            _hours.Visibility = w >= 2 ? Visibility.Visible : Visibility.Collapsed;
            _hours.Columns = Math.Max(1, (w - 1) * 3 + 1);
            _days.Visibility = h >= 2 ? Visibility.Visible : Visibility.Collapsed;
        }
        public WeatherWidget() : base(60_000)
        {
            _sym.SetResourceReference(TextBlock.FontFamilyProperty, "EmojiFont");
            _temp.FontWeight = FontWeights.Light;
            var top = new DockPanel();
            DockPanel.SetDock(_sym, Dock.Right);
            _sym.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(_sym);
            top.Children.Add(_temp);
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Width = 126 };
            s.Children.Add(_place); s.Children.Add(top); s.Children.Add(_desc); s.Children.Add(_hl);
            var row = new DockPanel();
            DockPanel.SetDock(s, Dock.Left);
            row.Children.Add(s);
            row.Children.Add(_hours);
            var all = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            all.Children.Add(row);
            all.Children.Add(_days);
            Child = all;
            _hours.Visibility = _days.Visibility = Visibility.Collapsed;
            OpenOnClick("Weather");
            WeatherService.Instance.Updated += () => { if (IsVisible) Tick(); };
        }
        protected override void Tick()
        {
            var w = WeatherService.Instance;
            if (!w.HasLocation || w.Data == null) { _place.Text = ""; _temp.Text = "--°"; _sym.Text = "🌤️"; _desc.Text = "Set a city"; _hl.Text = "Click to open Weather"; return; }
            var d = w.Data;
            _place.Text = string.IsNullOrWhiteSpace(SettingsStore.Current.City) ? "Here" : SettingsStore.Current.City;
            _temp.Text = $"{Math.Round(d.Temp)}°";
            _sym.Text = WeatherService.Symbol(d.Code, d.IsDay);
            _desc.Text = WeatherService.Describe(d.Code);
            _hl.Text = d.Days.Count > 0 ? $"H {Math.Round(d.Days[0].Max)}°  L {Math.Round(d.Days[0].Min)}°" : $"Feels {Math.Round(d.Feels)}°";
            _hours.Children.Clear();
            if (_w >= 2)
                foreach (var hr in d.Hours.Take(_hours.Columns))
                {
                    var c = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    var t = Label(hr.Time, 10.5, "Text3Brush"); t.HorizontalAlignment = HorizontalAlignment.Center;
                    var e = new TextBlock { Text = hr.Symbol, FontSize = 16, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) };
                    e.SetResourceReference(TextBlock.FontFamilyProperty, "EmojiFont");
                    var v = Label(hr.Temp, 12, "TextBrush", FontWeights.SemiBold); v.HorizontalAlignment = HorizontalAlignment.Center;
                    c.Children.Add(t); c.Children.Add(e); c.Children.Add(v);
                    _hours.Children.Add(c);
                }
            _days.Children.Clear();
            if (_h >= 2)
                foreach (var day in d.Days.Take(5))
                {
                    var r = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                    var hi = Label($"{Math.Round(day.Max)}°", 12, "TextBrush", FontWeights.SemiBold); hi.Width = 34; hi.TextAlignment = TextAlignment.Right;
                    var lo = Label($"{Math.Round(day.Min)}°", 12, "Text3Brush"); lo.Width = 34; lo.TextAlignment = TextAlignment.Right;
                    DockPanel.SetDock(hi, Dock.Right); DockPanel.SetDock(lo, Dock.Right);
                    r.Children.Add(hi); r.Children.Add(lo);
                    var e = new TextBlock { Text = day.Symbol, FontSize = 14, Width = 26 };
                    e.SetResourceReference(TextBlock.FontFamilyProperty, "EmojiFont");
                    DockPanel.SetDock(e, Dock.Left);
                    r.Children.Add(e);
                    r.Children.Add(Label(day.Day, 12, "TextBrush"));
                    _days.Children.Add(r);
                }
        }
    }

    public sealed class BatteryWidget : Widget
    {
        readonly RingProgress _ring = new RingProgress { Width = 66, Height = 66, Thickness = 5 };
        readonly TextBlock _pct = Big("", 17), _state = Label("", 11);
        public BatteryWidget() : base(5000)
        {
            var g = new Grid { Width = 66, Height = 66, HorizontalAlignment = HorizontalAlignment.Left };
            g.Children.Add(_ring);
            _pct.HorizontalAlignment = HorizontalAlignment.Center; _pct.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(_pct);
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(g);
            _state.Margin = new Thickness(0, 8, 0, 0);
            s.Children.Add(_state);
            Child = s;
            PowerService.Instance.Changed += () => { if (IsVisible) Tick(); };
        }
        protected override void Tick()
        {
            var p = PowerService.Instance;
            if (!p.HasBattery) { _ring.Progress = 1; _ring.RingBrush = Ui.Res("GreenBrush"); _pct.Text = "AC"; _state.Text = "Plugged in, no battery"; return; }
            _ring.Progress = p.Percent / 100.0;
            _ring.RingBrush = Ui.Res(p.Charging || p.OnAc ? "GreenBrush" : p.Percent <= 20 ? "RedBrush" : p.Percent <= 40 ? "OrangeBrush" : "TextBrush");
            _pct.Text = $"{p.Percent}%";
            _state.Text = p.Charging ? "Charging" : p.OnAc ? "Plugged in" : "On battery";
        }
    }

    public sealed class TimerWidget : Widget
    {
        static TimerService T => TimerService.Instance;
        readonly RingProgress _ring = new RingProgress { Width = 70, Height = 70, Thickness = 4.5 };
        readonly TextBlock _time = Big("", 16), _glyph = new TextBlock { FontSize = 10 };
        public TimerWidget() : base(500)
        {
            _ring.SetResourceReference(RingProgress.RingBrushProperty, "AccentBrush");
            _glyph.Style = Ui.StyleRes("Icon");
            _glyph.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            var c = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            _time.HorizontalAlignment = HorizontalAlignment.Center;
            _glyph.HorizontalAlignment = HorizontalAlignment.Center;
            c.Children.Add(_time); c.Children.Add(_glyph);
            var ring = new Grid { Width = 70, Height = 70, Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "Start / pause" };
            ring.Children.Add(_ring); ring.Children.Add(c);
            ring.MouseLeftButtonUp += (s, e) => { if (T.CdLeft <= TimeSpan.Zero) T.CdSet(5); T.CdToggle(); Tick(); };
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            foreach (var m in new[] { 5, 25 })
            {
                int mm = m;
                var b = new Button { Style = Ui.StyleRes("Pill"), Content = m + "m", Padding = new Thickness(9, 2, 9, 2), FontSize = 11, Margin = new Thickness(0, 0, 5, 0) };
                b.Click += (s, e) => { T.CdSet(mm); if (!T.CdRunning) T.CdToggle(); Tick(); };
                chips.Children.Add(b);
            }
            var reset = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Refresh, Width = 24, Height = 24, FontSize = 10, ToolTip = "Reset" };
            reset.Click += (s, e) => { T.CdReset(); Tick(); };
            chips.Children.Add(reset);
            var s2 = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            ring.HorizontalAlignment = HorizontalAlignment.Left;
            s2.Children.Add(ring); s2.Children.Add(chips);
            Child = s2;
        }
        protected override void Tick()
        {
            _ring.Progress = T.CdProgress;
            _time.Text = Ui.ClockCeil(T.CdLeft);
            _glyph.Text = T.CdRunning ? Glyphs.Pause : Glyphs.Play;
        }
    }

    public sealed class SystemWidget : Widget
    {
        readonly RingProgress _cpu = new RingProgress { Width = 50, Height = 50, Thickness = 4 }, _ram = new RingProgress { Width = 50, Height = 50, Thickness = 4 };
        readonly TextBlock _cpuT = Label("", 11, "TextBrush", FontWeights.SemiBold), _ramT = Label("", 11, "TextBrush", FontWeights.SemiBold);
        long _idle, _kernel, _user;
        public override void Resize(int w, int h)
        {
            double size = h >= 2 ? 96 : w >= 2 ? 64 : 50;
            foreach (var r in new[] { _cpu, _ram }) { r.Width = r.Height = size; ((FrameworkElement)r.Parent).Width = ((FrameworkElement)r.Parent).Height = size; r.Thickness = size / 12; }
            _cpuT.FontSize = _ramT.FontSize = size / 4.4;
            foreach (var r in new[] { _cpu, _ram }) ((FrameworkElement)((FrameworkElement)r.Parent).Parent).Margin = new Thickness(w >= 2 ? 14 : 4, 0, w >= 2 ? 14 : 4, 0);
        }
        public SystemWidget() : base(1500)
        {
            _cpu.SetResourceReference(RingProgress.RingBrushProperty, "AccentBrush");
            _ram.SetResourceReference(RingProgress.RingBrushProperty, "CyanBrush");
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            row.Children.Add(Gauge(_cpu, _cpuT, "CPU"));
            row.Children.Add(Gauge(_ram, _ramT, "RAM"));
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(row);
            Child = s;
            OpenOnClick("System");
        }
        static FrameworkElement Gauge(RingProgress r, TextBlock t, string name)
        {
            var g = new Grid { Width = 50, Height = 50 };
            g.Children.Add(r);
            t.HorizontalAlignment = HorizontalAlignment.Center; t.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(t);
            var s = new StackPanel { Margin = new Thickness(4, 0, 4, 0) };
            s.Children.Add(g);
            var l = Label(name, 10.5, "Text3Brush");
            l.HorizontalAlignment = HorizontalAlignment.Center;
            l.Margin = new Thickness(0, 5, 0, 0);
            s.Children.Add(l);
            return s;
        }
        protected override void Tick()
        {
            if (Win32.GetSystemTimes(out var idle, out var kernel, out var user))
            {
                long di = idle - _idle, total = (kernel - _kernel) + (user - _user);
                if (_kernel != 0 && total > 0) { double cpu = Math.Clamp(1 - di / (double)total, 0, 1); _cpu.Progress = cpu; _cpuT.Text = $"{cpu * 100:0}%"; }
                _idle = idle; _kernel = kernel; _user = user;
            }
            var m = new Win32.MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
            if (Win32.GlobalMemoryStatusEx(ref m)) { _ram.Progress = m.dwMemoryLoad / 100.0; _ramT.Text = $"{m.dwMemoryLoad}%"; }
        }
    }

    public sealed class CalendarWidget : Widget
    {
        readonly TextBlock _month = Label("", 11, "AccentBrush", FontWeights.SemiBold);
        readonly UniformGrid _grid = new UniformGrid { Columns = 7 };
        readonly StackPanel _today = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 16, 0), Visibility = Visibility.Collapsed };
        double _cell = 9;
        int _day = -1;
        public override void Resize(int w, int h)
        {
            _cell = h >= 2 ? 13 : 9;
            _today.Visibility = w >= 2 ? Visibility.Visible : Visibility.Collapsed;
            _day = -1;
        }
        public CalendarWidget() : base(60_000)
        {
            Padding = new Thickness(10, 8, 10, 8);
            _month.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(_month);
            _grid.Margin = new Thickness(0, 3, 0, 0);
            s.Children.Add(_grid);
            var row = new DockPanel();
            DockPanel.SetDock(_today, Dock.Left);
            row.Children.Add(_today);
            row.Children.Add(s);
            Child = row;
            OpenOnClick("Clock");
        }
        protected override void Tick()
        {
            var today = DateTime.Today;
            if (today.Day == _day) return;
            _day = today.Day;
            _month.Text = today.ToString("MMMM", CultureInfo.CurrentCulture).ToUpperInvariant();
            _today.Children.Clear();
            var dn = Label(today.ToString("dddd", CultureInfo.CurrentCulture), 12, "AccentBrush", FontWeights.SemiBold);
            dn.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            _today.Children.Add(dn);
            _today.Children.Add(Big(today.Day.ToString(), 46));
            _grid.Children.Clear();
            var first = (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            for (int i = 0; i < 7; i++)
            {
                var name = CultureInfo.CurrentCulture.DateTimeFormat.ShortestDayNames[(first + i) % 7];
                _grid.Children.Add(new TextBlock { Text = name[..1], FontSize = _cell - 0.5, Foreground = Ui.Res("Text3Brush"), HorizontalAlignment = HorizontalAlignment.Center });
            }
            var start = new DateTime(today.Year, today.Month, 1);
            int offset = ((int)start.DayOfWeek - first + 7) % 7;
            for (int i = 0; i < offset; i++) _grid.Children.Add(new TextBlock());
            for (int d = 1; d <= DateTime.DaysInMonth(today.Year, today.Month); d++)
            {
                var t = new TextBlock { Text = d.ToString(), FontSize = _cell, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
                if (d == today.Day)
                {
                    t.Foreground = Brushes.Black; t.FontWeight = FontWeights.Bold;
                    var b = new Border { Width = _cell * 1.7, Height = _cell * 1.7, CornerRadius = new CornerRadius(_cell * 0.85), Child = t };
                    b.SetResourceReference(BackgroundProperty, "AccentBrush");
                    _grid.Children.Add(b);
                }
                else
                {
                    t.Foreground = Ui.Res(d < today.Day ? "Text3Brush" : "TextBrush");
                    _grid.Children.Add(t);
                }
            }
        }
    }

    public sealed class TogglesWidget : Widget
    {
        readonly ToggleButton _mute = new ToggleButton(), _mic = new ToggleButton();
        readonly UniformGrid _g = new UniformGrid { Columns = 2, Rows = 2 };
        public override void Resize(int w, int h)
        {
            // 1×1: 2×2 tiles · 2×1 and wider: one row of four · tall: 2×2 bigger tiles
            if (h == 1 && w >= 2) { _g.Columns = 4; _g.Rows = 1; }
            else { _g.Columns = 2; _g.Rows = 2; }
            foreach (ButtonBase b in _g.Children) ((TextBlock)b.Content).FontSize = h >= 2 ? 20 : 15;
        }
        public TogglesWidget() : base(2000)
        {
            Padding = new Thickness(8);
            var g = _g;
            Tile(g, _mute, "", "Mute speakers", () => AudioService.Instance.Muted = _mute.IsChecked == true);
            Tile(g, _mic, "", "Mute microphone", () => AudioService.Instance.MicMuted = _mic.IsChecked == true);
            var snip = new Button(); Tile(g, snip, "", "Screenshot", () => { NotchWindow.Instance?.Collapse(); Ui.Later(350, () => Ui.Open("ms-screenclip:")); });
            var lck = new Button(); Tile(g, lck, "", "Lock PC", () => Win32.LockWorkStation());
            Child = g;
        }
        static void Tile(UniformGrid g, ButtonBase b, string glyph, string tip, Action click)
        {
            b.Style = Ui.StyleRes(b is ToggleButton ? "TileToggle" : "Tile");
            b.Content = new TextBlock { Text = glyph, Style = Ui.StyleRes("Icon"), FontSize = 15 };
            b.ToolTip = tip;
            b.Margin = new Thickness(3);
            b.Height = double.NaN;
            b.Click += (s, e) => click();
            g.Children.Add(b);
        }
        protected override void Tick()
        {
            var a = AudioService.Instance;
            _mute.IsChecked = a.Muted;
            _mic.IsEnabled = a.HasMic;
            _mic.IsChecked = a.MicMuted;
        }
    }

    public sealed class CountdownWidget : Widget
    {
        readonly TextBlock _num = Big("", 34), _unit = Label("", 11.5), _label = Label("", 11, "Text3Brush");
        public CountdownWidget() : base(60_000)
        {
            _num.SetResourceReference(TextBlock.ForegroundProperty, "AccentBrush");
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(_num); s.Children.Add(_unit); s.Children.Add(_label);
            Child = s;
            Cursor = Cursors.Hand;
            MouseLeftButtonUp += (s2, e) => AppWindow.ShowPage("Island");
            ToolTip = "Set the date in Crest → Island";
        }
        protected override void Tick()
        {
            var S = SettingsStore.Current;
            if (!DateTime.TryParseExact(S.CountdownDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            { _num.Text = "—"; _unit.Text = "Pick a date"; _label.Text = "Crest → Island → Widgets"; return; }
            int days = (date.Date - DateTime.Today).Days;
            _num.Text = Math.Abs(days).ToString();
            _unit.Text = days == 0 ? "Today!" : days > 0 ? (days == 1 ? "day until" : "days until") : (days == -1 ? "day since" : "days since");
            _label.Text = string.IsNullOrWhiteSpace(S.CountdownLabel) ? date.ToString("d MMM yyyy") : S.CountdownLabel;
        }
    }

    public sealed class NoteWidget : Widget
    {
        readonly TextBlock _text = new TextBlock { FontSize = 11.5, TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis };
        int _lines = 4;
        public override void Resize(int w, int h) { _lines = h >= 2 ? 11 : 4; _text.FontSize = w >= 2 ? 12.5 : 11.5; }
        public NoteWidget() : base(5000)
        {
            var head = Label("NOTES", 10, "Text3Brush", FontWeights.SemiBold);
            var dock = new DockPanel();
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            _text.Margin = new Thickness(0, 4, 0, 0);
            dock.Children.Add(_text);
            Child = dock;
            OpenOnClick("Notes");
        }
        protected override void Tick()
        {
            var d = NotesStore.Data;
            var todos = d.Todos.Where(t => !t.Done).Take(_lines).Select(t => "○ " + t.Text).ToList();
            var text = d.Text.Trim();
            _text.Text = todos.Count > 0 ? string.Join("\n", todos) : text.Length > 0 ? text : "Nothing yet. Click to write.";
            _text.Foreground = Ui.Res(todos.Count > 0 || text.Length > 0 ? "TextBrush" : "Text3Brush");
        }
    }

    public sealed class ScreenTimeWidget : Widget
    {
        readonly TextBlock _total = Big("", 26), _top = Label("", 11), _sub = Label("Screen time today", 10.5, "Text3Brush");
        public ScreenTimeWidget() : base(30_000)
        {
            var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            s.Children.Add(_sub); s.Children.Add(_total); s.Children.Add(_top);
            Child = s;
            OpenOnClick("ScreenTime");
        }
        protected override void Tick()
        {
            var st = ScreenTimeService.Instance;
            var day = DateTime.Today.ToString("yyyy-MM-dd");
            _total.Text = ScreenTimeService.Format(st.TotalSeconds(day));
            var top = st.Apps(day).FirstOrDefault();
            _top.Text = top.Exe == null ? "" : $"Most: {st.FriendlyName(top.Exe)}";
        }
    }
}
