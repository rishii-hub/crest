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

        public ClockView()
        {
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // calendar
            var cal = new StackPanel();
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
            var calCard = new Border { Style = Ui.StyleRes("Card"), Child = cal };
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

        public void OnActivated()
        {
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
}
