using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    /// <summary>Current conditions, 12-hour strip and 6-day forecast (Open-Meteo).</summary>
    public sealed class WeatherView : UserControl, ITabView
    {
        static AppSettings S => SettingsStore.Current;
        readonly Grid _main = new Grid();
        readonly StackPanel _setup = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Width = 360 };
        readonly TextBox _search = new TextBox();
        readonly StackPanel _results = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        readonly TextBlock _status = new TextBlock();
        readonly FontFamily _emoji, _display;

        public WeatherView()
        {
            _emoji = (FontFamily)Application.Current.FindResource("EmojiFont");
            _display = (FontFamily)Application.Current.FindResource("DisplayFont");
            BuildSetup();
            var root = new Grid();
            root.Children.Add(_main);
            root.Children.Add(_setup);
            Content = root;
            WeatherService.Instance.Updated += () => { if (IsVisible) Render(); };
        }

        public void OnActivated()
        {
            Render();
            _ = WeatherService.Instance.RefreshIfStaleAsync();
        }

        public void OnDeactivated() { }

        void BuildSetup()
        {
            _setup.Children.Add(new TextBlock { Text = "🌤", FontFamily = _emoji, FontSize = 30, HorizontalAlignment = HorizontalAlignment.Center });
            _setup.Children.Add(new TextBlock { Text = "Where are you?", Style = Ui.StyleRes("H1"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 2) });
            _setup.Children.Add(new TextBlock { Text = "Weather comes from Open-Meteo — free, no account, no tracking.", Style = Ui.StyleRes("Caption"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) });
            _search.Style = Ui.StyleRes("Field");
            _search.Tag = "Search a city and press Enter";
            _search.KeyDown += async (s, e) =>
            {
                if (e.Key != Key.Enter) return;
                _results.Children.Clear();
                _status.Text = "Searching…";
                try
                {
                    var places = await WeatherService.SearchAsync(_search.Text);
                    _status.Text = places.Count == 0 ? "No places found." : "";
                    foreach (var p in places)
                    {
                        var b = new Button { Style = Ui.StyleRes("Pill"), HorizontalContentAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 4), Background = Ui.Res("CardBrush") };
                        var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left };
                        sp.Children.Add(new TextBlock { Text = p.Name, FontSize = 13, FontWeight = FontWeights.SemiBold });
                        sp.Children.Add(new TextBlock { Text = p.Detail, FontSize = 11, Foreground = Ui.Res("Text2Brush") });
                        b.Content = sp;
                        b.HorizontalAlignment = HorizontalAlignment.Stretch;
                        var place = p;
                        b.Click += (s2, e2) => Choose(place);
                        _results.Children.Add(b);
                    }
                }
                catch { _status.Text = "Couldn't search right now. Check your connection."; }
            };
            _setup.Children.Add(_search);
            var locate = new Button { Style = Ui.StyleRes("Pill"), Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            var lp = new StackPanel { Orientation = Orientation.Horizontal };
            lp.Children.Add(new TextBlock { Text = Glyphs.Location, Style = Ui.StyleRes("Icon"), FontSize = 12, Margin = new Thickness(0, 0, 6, 0) });
            lp.Children.Add(new TextBlock { Text = "Use my location" });
            locate.Content = lp;
            locate.Click += async (s, e) =>
            {
                _status.Text = "Locating…";
                var place = await WeatherService.LocateAsync();
                if (place == null) { _status.Text = "Location is off. Enable it in Windows Settings → Privacy → Location, or search a city."; return; }
                Choose(place);
            };
            _setup.Children.Add(locate);
            _status.Style = Ui.StyleRes("Caption");
            _status.HorizontalAlignment = HorizontalAlignment.Center;
            _status.TextWrapping = TextWrapping.Wrap;
            _status.TextAlignment = TextAlignment.Center;
            _status.Margin = new Thickness(0, 8, 0, 0);
            _setup.Children.Add(_status);
            _setup.Children.Add(new ScrollViewer { Content = _results, MaxHeight = 150, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        void Choose(Place p)
        {
            S.City = p.Name;
            S.Lat = p.Lat;
            S.Lon = p.Lon;
            SettingsStore.Save();
            _results.Children.Clear();
            _search.Text = "";
            _status.Text = "";
            Render();
            _ = WeatherService.Instance.RefreshAsync();
        }

        void Render()
        {
            var w = WeatherService.Instance;
            bool has = w.HasLocation;
            _setup.Visibility = has ? Visibility.Collapsed : Visibility.Visible;
            _main.Visibility = has ? Visibility.Visible : Visibility.Collapsed;
            if (!has) return;

            _main.Children.Clear();
            _main.ColumnDefinitions.Clear();
            _main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            _main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            _main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var d = w.Data;
            // left: now
            var left = new StackPanel();
            var head = new DockPanel();
            var change = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Search, Width = 26, Height = 26, FontSize = 11, ToolTip = "Change location" };
            change.Click += (s, e) => { S.Lat = S.Lon = null; S.City = null; SettingsStore.Save(); Render(); };
            var unit = new Button { Style = Ui.StyleRes("Pill"), Content = S.TempUnit == "F" ? "°F" : "°C", Padding = new Thickness(8, 2, 8, 2), FontSize = 11, ToolTip = "Switch units", Margin = new Thickness(0, 0, 4, 0) };
            unit.Click += (s, e) => { S.TempUnit = S.TempUnit == "F" ? "C" : "F"; SettingsStore.Save(); _ = w.RefreshAsync(); };
            DockPanel.SetDock(change, Dock.Right);
            DockPanel.SetDock(unit, Dock.Right);
            head.Children.Add(change);
            head.Children.Add(unit);
            head.Children.Add(new TextBlock { Text = S.City ?? "", Style = Ui.StyleRes("H1"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            left.Children.Add(head);

            if (d == null)
            {
                left.Children.Add(new TextBlock { Text = w.Error ?? "Loading…", Style = Ui.StyleRes("Caption"), Margin = new Thickness(0, 12, 0, 0), TextWrapping = TextWrapping.Wrap });
                _main.Children.Add(Card(left));
                return;
            }

            var now = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            now.Children.Add(new TextBlock { Text = WeatherService.Symbol(d.Code, d.IsDay), FontFamily = _emoji, FontSize = 40, VerticalAlignment = VerticalAlignment.Center });
            now.Children.Add(new TextBlock { Text = $"{Math.Round(d.Temp)}°", FontFamily = _display, FontSize = 52, FontWeight = FontWeights.Light, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
            left.Children.Add(now);
            left.Children.Add(new TextBlock { Text = WeatherService.Describe(d.Code), FontSize = 15, FontWeight = FontWeights.SemiBold });
            left.Children.Add(new TextBlock { Text = $"Feels like {Math.Round(d.Feels)}°", Style = Ui.StyleRes("Caption"), Margin = new Thickness(0, 2, 0, 10) });
            var facts = new Grid();
            facts.ColumnDefinitions.Add(new ColumnDefinition());
            facts.ColumnDefinitions.Add(new ColumnDefinition());
            facts.RowDefinitions.Add(new RowDefinition());
            facts.RowDefinitions.Add(new RowDefinition());
            AddFact(facts, 0, 0, "Humidity", $"{d.Humidity}%");
            AddFact(facts, 1, 0, "Wind", $"{Math.Round(d.Wind)} {(d.Unit == "F" ? "mph" : "km/h")}");
            AddFact(facts, 0, 1, "Sunrise", d.Sunrise);
            AddFact(facts, 1, 1, "Sunset", d.Sunset);
            left.Children.Add(facts);
            left.Children.Add(new TextBlock { Text = $"Updated {Ui.Ago(d.Fetched)}", FontSize = 10.5, Foreground = Ui.Res("Text3Brush"), Margin = new Thickness(0, 8, 0, 0) });
            _main.Children.Add(Card(left));

            // right: hourly + daily
            var right = new Grid();
            right.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(10) });
            right.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var hours = new UniformGrid12();
            foreach (var h in d.Hours)
            {
                var col = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                col.Children.Add(new TextBlock { Text = h.Time, FontSize = 10.5, Foreground = Ui.Res("Text2Brush"), HorizontalAlignment = HorizontalAlignment.Center });
                col.Children.Add(new TextBlock { Text = h.Symbol, FontFamily = _emoji, FontSize = 17, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 4) });
                col.Children.Add(new TextBlock { Text = h.Temp, FontSize = 12.5, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center });
                hours.Children.Add(col);
            }
            right.Children.Add(Card(hours, new Thickness(10, 12, 10, 12)));

            var days = new StackPanel();
            double lo = d.Days.Count > 0 ? d.Days.Min(x => x.Min) : 0, hi = d.Days.Count > 0 ? d.Days.Max(x => x.Max) : 1;
            foreach (var day in d.Days)
            {
                var row = new Grid { Margin = new Thickness(0, 1, 0, 1) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(38) });
                row.Children.Add(Cell(new TextBlock { Text = day.Day, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }, 0));
                row.Children.Add(Cell(new TextBlock { Text = day.Symbol, FontFamily = _emoji, FontSize = 14, VerticalAlignment = VerticalAlignment.Center }, 1));
                row.Children.Add(Cell(new TextBlock { Text = $"{Math.Round(day.Min)}°", FontSize = 12, Foreground = Ui.Res("Text2Brush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center }, 2));
                row.Children.Add(Cell(new RangeBar { Lo = lo, Hi = hi, Min = day.Min, Max = day.Max, Margin = new Thickness(10, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center }, 3));
                row.Children.Add(Cell(new TextBlock { Text = $"{Math.Round(day.Max)}°", FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center }, 4));
                days.Children.Add(row);
            }
            var dayCard = Card(days, new Thickness(14, 8, 14, 8));
            Grid.SetRow(dayCard, 2);
            right.Children.Add(dayCard);
            Grid.SetColumn(right, 2);
            _main.Children.Add(right);
        }

        static UIElement Cell(UIElement e, int col) { Grid.SetColumn(e, col); return e; }

        static void AddFact(Grid g, int col, int row, string label, string value)
        {
            var s = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
            s.Children.Add(new TextBlock { Text = label, FontSize = 10.5, Foreground = Ui.Res("Text3Brush") });
            s.Children.Add(new TextBlock { Text = value, FontSize = 13, FontWeight = FontWeights.SemiBold });
            Grid.SetColumn(s, col);
            Grid.SetRow(s, row);
            g.Children.Add(s);
        }

        static Border Card(UIElement child, Thickness? pad = null) =>
            new Border { Style = Ui.StyleRes("Card"), Padding = pad ?? new Thickness(16, 12, 16, 12), Child = child };
    }

    /// <summary>One-row uniform grid for the hourly strip.</summary>
    public sealed class UniformGrid12 : System.Windows.Controls.Primitives.UniformGrid
    {
        public UniformGrid12() { Rows = 1; }
    }

    /// <summary>Apple Weather-style temperature range bar.</summary>
    public sealed class RangeBar : FrameworkElement
    {
        public double Lo, Hi, Min, Max;
        static readonly Brush Track = Ui.Brush("#22FFFFFF");

        protected override Size MeasureOverride(Size available) => new Size(0, 5);

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = 5;
            if (w <= 0) return;
            dc.DrawRoundedRectangle(Track, null, new Rect(0, 0, w, h), 2.5, 2.5);
            double span = Math.Max(0.1, Hi - Lo);
            double x0 = (Min - Lo) / span * w, x1 = (Max - Lo) / span * w;
            var grad = new LinearGradientBrush(Color.FromRgb(0x64, 0xD2, 0xFF), Color.FromRgb(0xFF, 0x9F, 0x0A), 0);
            grad.Freeze();
            dc.DrawRoundedRectangle(grad, null, new Rect(x0, 0, Math.Max(h, x1 - x0), h), 2.5, 2.5);
        }
    }
}
