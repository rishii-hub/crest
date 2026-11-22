using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Core;

namespace Crest.Views
{
    /// <summary>Month calendar plus world clocks (any Windows time zone).</summary>
    public sealed class ClockView : UserControl, ITabView
    {
        static AppSettings S => SettingsStore.Current;
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        readonly UniformGrid _days = new UniformGrid { Columns = 7 };
        readonly TextBlock _month = new TextBlock();
        readonly StackPanel _clocks = new StackPanel();
        readonly TextBox _zoneSearch = new TextBox();
        readonly StackPanel _zoneResults = new StackPanel();
        readonly Border _addPanel;
        DateTime _shown = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
        readonly StackPanel _calPanel = new StackPanel();
        readonly Grid _clockPanel = new Grid { Visibility = Visibility.Collapsed };
        readonly AnalogClock _analog = new AnalogClock { Width = 170, Height = 170 };
        readonly TextBlock _digital = new TextBlock(), _digitalSub = new TextBlock();
        static bool _showClock, _analogMode = true;

        public ClockView()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // calendar
            var cal = _calPanel;
            var head = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            var next = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Right, Width = 26, Height = 26, FontSize = 10 };
            var prev = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Left, Width = 26, Height = 26, FontSize = 10 };
            var today = new Button { Style = Ui.StyleRes("Pill"), Content = "Today", Padding = new Thickness(8, 2, 8, 2), FontSize = 11, Margin = new Thickness(0, 0, 4, 0) };
            next.Click += (s, e) => { _shown = _shown.AddMonths(1); BuildMonth(); };
            prev.Click += (s, e) => { _shown = _shown.AddMonths(-1); BuildMonth(); };
            today.Click += (s, e) => { _shown = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1); BuildMonth(); };
            DockPanel.SetDock(next, Dock.Right);
            DockPanel.SetDock(prev, Dock.Right);
            DockPanel.SetDock(today, Dock.Right);
            head.Children.Add(next);
            head.Children.Add(prev);
            head.Children.Add(today);
            _month.Style = Ui.StyleRes("H1");
            _month.VerticalAlignment = VerticalAlignment.Center;
            head.Children.Add(_month);
            cal.Children.Add(head);
            var names = new UniformGrid { Columns = 7, Margin = new Thickness(0, 0, 0, 4) };
            var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            for (int i = 0; i < 7; i++)
            {
                var dow = (DayOfWeek)(((int)first + i) % 7);
                names.Children.Add(new TextBlock
                {
                    Text = CultureInfo.CurrentCulture.DateTimeFormat.GetShortestDayName(dow), FontSize = 10.5,
                    Foreground = Ui.Res("Text3Brush"), HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            cal.Children.Add(names);
            cal.Children.Add(_days);
            // Big clock (digital with seconds, or analog)
            _digital.FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont");
            _digital.FontSize = 44; _digital.FontWeight = FontWeights.SemiBold;
            _digital.HorizontalAlignment = HorizontalAlignment.Center;
            _digital.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
            _digitalSub.FontSize = 12; _digitalSub.Foreground = Ui.Res("Text2Brush"); _digitalSub.HorizontalAlignment = HorizontalAlignment.Center;
            var dig = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            dig.Children.Add(_digital); dig.Children.Add(_digitalSub);
            _clockPanel.Children.Add(dig);
            _clockPanel.Children.Add(_analog);
            _clockPanel.MouseLeftButtonUp += (s, e) => { _analogMode = !_analogMode; ApplyClockMode(); };
            _clockPanel.Background = Brushes.Transparent;
            _clockPanel.Cursor = Cursors.Hand;
            _clockPanel.ToolTip = "Click to switch analog / digital";

            var calWrap = new DockPanel();
            var modes = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (label, isClock) in new[] { ("Calendar", false), ("Clock", true) })
            {
                var rb = new RadioButton { Style = Ui.StyleRes("Seg"), Content = label, GroupName = "clock-mode", IsChecked = isClock == _showClock, Padding = new Thickness(10, 3, 10, 3), FontSize = 11.5 };
                var ic = isClock;
                rb.Checked += (s, e) => { _showClock = ic; ApplyClockMode(); };
                modes.Children.Add(rb);
            }
            var modeBorder = new Border { Background = Ui.Res("Card2Brush"), CornerRadius = new CornerRadius(9), Padding = new Thickness(2), Child = modes, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(modeBorder, Dock.Top);
            calWrap.Children.Add(modeBorder);
            var stack = new Grid();
            stack.Children.Add(_calPanel);
            stack.Children.Add(_clockPanel);
            calWrap.Children.Add(stack);
            var calCard = new Border { Style = Ui.StyleRes("Card"), Child = calWrap, Padding = new Thickness(14, 10, 14, 10) };
            root.Children.Add(calCard);

            // world clocks
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var rhead = new DockPanel { Margin = new Thickness(4, 0, 0, 8) };
            var add = new Button { Style = Ui.StyleRes("Pill"), Content = "+ Add city", Padding = new Thickness(10, 4, 10, 4) };
            DockPanel.SetDock(add, Dock.Right);
            rhead.Children.Add(add);
            rhead.Children.Add(new TextBlock { Text = "World clock", Style = Ui.StyleRes("H1"), VerticalAlignment = VerticalAlignment.Center });
            right.Children.Add(rhead);
            var scroll = new ScrollViewer { Content = _clocks, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            Grid.SetRow(scroll, 1);
            right.Children.Add(scroll);

            _zoneSearch.Style = Ui.StyleRes("Field");
            _zoneSearch.Tag = "Type a city or time zone…";
            _zoneSearch.TextChanged += (s, e) => SearchZones();
            _zoneSearch.KeyDown += (s, e) => { if (e.Key == Key.Escape) { _addPanel!.Visibility = Visibility.Collapsed; e.Handled = true; } };
            var addStack = new StackPanel();
            addStack.Children.Add(_zoneSearch);
            addStack.Children.Add(new ScrollViewer { Content = _zoneResults, MaxHeight = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) });
            _addPanel = new Border { Style = Ui.StyleRes("Card"), Background = Ui.Res("Card2Brush"), Child = addStack, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 40, 0, 0) };
            Grid.SetRowSpan(_addPanel, 2);
            right.Children.Add(_addPanel);
            add.Click += (s, e) =>
            {
                _addPanel.Visibility = _addPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                _zoneSearch.Text = "";
                SearchZones();
            };

            Grid.SetColumn(right, 2);
            root.Children.Add(right);
            Content = root;
            _timer.Tick += (s, e) => UpdateClocks();
        }

        void ApplyClockMode()
        {
            _calPanel.Visibility = _showClock ? Visibility.Collapsed : Visibility.Visible;
            _clockPanel.Visibility = _showClock ? Visibility.Visible : Visibility.Collapsed;
            _analog.Visibility = _analogMode ? Visibility.Visible : Visibility.Collapsed;
            ((FrameworkElement)_clockPanel.Children[0]).Visibility = _analogMode ? Visibility.Collapsed : Visibility.Visible;
            UpdateClocks();
        }

        public void OnActivated()
        {
            ApplyClockMode();
            BuildMonth();
            BuildClocks();
            _timer.Start();
        }

        public void OnDeactivated()
        {
            _timer.Stop();
            _addPanel.Visibility = Visibility.Collapsed;
        }

        void BuildMonth()
        {
            _month.Text = _shown.ToString("MMMM yyyy", CultureInfo.CurrentCulture);
            _days.Children.Clear();
            var first = CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek;
            int offset = ((int)_shown.DayOfWeek - (int)first + 7) % 7;
            var start = _shown.AddDays(-offset);
            for (int i = 0; i < 42; i++)
            {
                var d = start.AddDays(i);
                bool inMonth = d.Month == _shown.Month;
                bool isToday = d.Date == DateTime.Today;
                var t = new TextBlock
                {
                    Text = d.Day.ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = isToday ? Brushes.Black : inMonth ? Ui.Res("TextBrush") : Ui.Res("Text3Brush"),
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal
                };
                var cell = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(14), Child = t, Margin = new Thickness(0, 1, 0, 1) };
                if (isToday) cell.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
                else if (d.DayOfWeek == DayOfWeek.Sunday || d.DayOfWeek == DayOfWeek.Saturday) t.Opacity = inMonth ? 0.75 : 1;
                _days.Children.Add(cell);
            }
        }

        void BuildClocks()
        {
            _clocks.Children.Clear();
            AddClockRow(TimeZoneInfo.Local, "Local time", false);
            foreach (var id in S.WorldClocks.ToList())
            {
                try { AddClockRow(TimeZoneInfo.FindSystemTimeZoneById(id), null, true); }
                catch { S.WorldClocks.Remove(id); SettingsStore.Save(); }
            }
            UpdateClocks();
        }

        void AddClockRow(TimeZoneInfo tz, string? label, bool removable)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 6, 6), Tag = tz, Background = Brushes.Transparent };
            var bg = new Border { CornerRadius = new CornerRadius(14), Background = Ui.Res("CardBrush") };
            row.Children.Add(bg);
            var inner = new DockPanel { Margin = new Thickness(14, 8, 10, 8) };
            var time = new TextBlock { FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = 24, FontWeight = FontWeights.Light, VerticalAlignment = VerticalAlignment.Center, Typography = { NumeralAlignment = FontNumeralAlignment.Tabular } };
            DockPanel.SetDock(time, Dock.Right);
            if (removable)
            {
                var del = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Close, Width = 22, Height = 22, FontSize = 9, Opacity = 0, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(del, Dock.Right);
                del.Click += (s, e) => { S.WorldClocks.Remove(tz.Id); SettingsStore.Save(); BuildClocks(); };
                row.MouseEnter += (s, e) => del.Opacity = 1;
                row.MouseLeave += (s, e) => del.Opacity = 0;
                inner.Children.Add(del);
            }
            inner.Children.Add(time);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            names.Children.Add(new TextBlock { Text = label ?? CityName(tz), FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
            var sub = new TextBlock { FontSize = 11, Foreground = Ui.Res("Text2Brush") };
            names.Children.Add(sub);
            inner.Children.Add(names);
            row.Children.Add(inner);
            _clocks.Children.Add(row);
        }

        void UpdateClocks()
        {
            var nowUtc = DateTime.UtcNow;
            if (_showClock)
            {
                var n = DateTime.Now;
                bool h24c = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H");
                _digital.Text = n.ToString(h24c ? "HH:mm:ss" : "h:mm:ss", CultureInfo.CurrentCulture);
                var off = TimeZoneInfo.Local.GetUtcOffset(n);
                _digitalSub.Text = n.ToString("dddd, d MMMM yyyy", CultureInfo.CurrentCulture) + $"  ·  UTC{(off < TimeSpan.Zero ? "−" : "+")}{off.Duration().Hours:00}:{off.Duration().Minutes:00}";
                _analog.InvalidateVisual();
            }
            var local = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimeZoneInfo.Local);
            bool h24 = CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H");
            foreach (Grid row in _clocks.Children.OfType<Grid>())
            {
                if (row.Tag is not TimeZoneInfo tz) continue;
                var t = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, tz);
                var inner = (DockPanel)row.Children[1];
                var time = inner.Children.OfType<TextBlock>().First();
                var names = inner.Children.OfType<StackPanel>().First();
                var sub = (TextBlock)names.Children[1];
                time.Text = t.ToString(h24 ? "HH:mm" : "h:mm tt", CultureInfo.CurrentCulture);
                var diff = t - local;
                string day = t.Date == local.Date ? "Today" : t.Date > local.Date ? "Tomorrow" : "Yesterday";
                string off = Math.Abs(diff.TotalMinutes) < 1 ? "" : $", {(diff.TotalMinutes > 0 ? "+" : "−")}{FormatOffset(diff.Duration())}";
                sub.Text = tz == TimeZoneInfo.Local ? local.ToString("dddd, d MMM", CultureInfo.CurrentCulture) : day + off;
            }
        }

        static string FormatOffset(TimeSpan d) => d.Minutes == 0 ? $"{(int)d.TotalHours}h" : $"{(int)d.TotalHours}h {d.Minutes}m";

        static string CityName(TimeZoneInfo tz)
        {
            var dn = tz.DisplayName;
            int p = dn.IndexOf(')');
            var cities = p >= 0 && p + 1 < dn.Length ? dn.Substring(p + 1).Trim() : dn;
            var firstCity = cities.Split(',')[0].Trim();
            return string.IsNullOrEmpty(firstCity) ? tz.Id : firstCity;
        }

        void SearchZones()
        {
            _zoneResults.Children.Clear();
            var q = _zoneSearch.Text.Trim();
            var zones = TimeZoneInfo.GetSystemTimeZones()
                .Where(z => !S.WorldClocks.Contains(z.Id))
                .Where(z => q.Length == 0 || z.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) || z.Id.Contains(q, StringComparison.OrdinalIgnoreCase))
                .Take(40);
            foreach (var z in zones)
            {
                var b = new Button { Style = Ui.StyleRes("Pill"), Background = Brushes.Transparent, Margin = new Thickness(0, 0, 0, 2), HorizontalAlignment = HorizontalAlignment.Stretch };
                b.Content = new TextBlock { Text = z.DisplayName, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 340 };
                var zone = z;
                b.Click += (s, e) =>
                {
                    S.WorldClocks.Add(zone.Id);
                    SettingsStore.Save();
                    _addPanel.Visibility = Visibility.Collapsed;
                    BuildClocks();
                };
                _zoneResults.Children.Add(b);
            }
        }
    }

    /// <summary>Minimal Apple-style analog clock face.</summary>
    public sealed class AnalogClock : FrameworkElement
    {
        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size < 20) return;
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            double r = size / 2 - 1;
            dc.DrawEllipse(Ui.Res("Card2Brush"), null, c, r, r);
            var tick = new Pen(Ui.Res("Text3Brush"), 1.2);
            var hourTick = new Pen(Ui.Res("TextBrush"), 2.4) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            for (int i = 0; i < 60; i++)
            {
                double a = i * Math.PI / 30;
                bool hour = i % 5 == 0;
                double r1 = r - (hour ? 12 : 6), r2 = r - 3;
                dc.DrawLine(hour ? hourTick : tick, new Point(c.X + r1 * Math.Sin(a), c.Y - r1 * Math.Cos(a)), new Point(c.X + r2 * Math.Sin(a), c.Y - r2 * Math.Cos(a)));
            }
            var now = DateTime.Now;
            double sec = now.Second, min = now.Minute + sec / 60, hr = (now.Hour % 12) + min / 60;
            void hand(double angleDeg, double len, double width, Brush brush, double tail)
            {
                double a = angleDeg * Math.PI / 180;
                var pen = new Pen(brush, width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
                dc.DrawLine(pen, new Point(c.X - tail * Math.Sin(a), c.Y + tail * Math.Cos(a)), new Point(c.X + len * Math.Sin(a), c.Y - len * Math.Cos(a)));
            }
            hand(hr * 30, r * 0.5, 5, Ui.Res("TextBrush"), 0);
            hand(min * 6, r * 0.75, 3.5, Ui.Res("TextBrush"), 0);
            hand(sec * 6, r * 0.85, 1.5, (Brush)Application.Current.FindResource("AccentBrush"), r * 0.15);
            dc.DrawEllipse((Brush)Application.Current.FindResource("AccentBrush"), null, c, 3.5, 3.5);
        }
    }
}
