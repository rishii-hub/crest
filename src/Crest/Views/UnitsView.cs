using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    /// <summary>Unit converter: length, mass, temperature, data, volume, speed, time, area and live currency (ECB rates).</summary>
    public sealed class UnitsView : UserControl, ITabView
    {
        static AppSettings S => SettingsStore.Current;

        static readonly Dictionary<string, (string Unit, double Factor)[]> Linear = new Dictionary<string, (string, double)[]>
        {
            ["Length"] = new[] { ("mm", 0.001), ("cm", 0.01), ("m", 1), ("km", 1000), ("in", 0.0254), ("ft", 0.3048), ("yd", 0.9144), ("mi", 1609.344) },
            ["Mass"] = new[] { ("mg", 1e-6), ("g", 0.001), ("kg", 1), ("t", 1000), ("oz", 0.028349523125), ("lb", 0.45359237) },
            ["Data"] = new[] { ("bit", 0.125), ("B", 1), ("KB", 1e3), ("MB", 1e6), ("GB", 1e9), ("TB", 1e12), ("KiB", 1024), ("MiB", 1048576), ("GiB", 1073741824) },
            ["Volume"] = new[] { ("mL", 0.001), ("L", 1), ("m³", 1000), ("tsp", 0.00492892159375), ("tbsp", 0.01478676478125), ("cup", 0.2365882365), ("fl oz", 0.0295735295625), ("gal", 3.785411784) },
            ["Speed"] = new[] { ("m/s", 1), ("km/h", 1 / 3.6), ("mph", 0.44704), ("knot", 0.514444444), ("ft/s", 0.3048) },
            ["Time"] = new[] { ("ms", 0.001), ("s", 1), ("min", 60), ("h", 3600), ("day", 86400), ("week", 604800), ("year", 31557600) },
            ["Area"] = new[] { ("cm²", 1e-4), ("m²", 1), ("ha", 1e4), ("km²", 1e6), ("ft²", 0.09290304), ("acre", 4046.8564224) },
        };
        static readonly string[] Temps = { "°C", "°F", "K" };
        static readonly string[] Currencies = { "USD", "EUR", "GBP", "INR", "JPY", "CNY", "AUD", "CAD", "CHF", "SGD" };
        static readonly string[] Categories = { "Currency", "Length", "Mass", "Temperature", "Data", "Volume", "Speed", "Time", "Area" };

        readonly TextBox _input = new TextBox();
        readonly TextBlock _result = new TextBlock(), _note = new TextBlock();
        readonly WrapPanel _fromUnits = new WrapPanel(), _toUnits = new WrapPanel();
        readonly StackPanel _extra = new StackPanel();
        string _cat = "Currency", _from = "USD", _to = "INR";
        Dictionary<string, double>? _rates;
        string _ratesFrom = "";

        public UnitsView()
        {
            var root = Kit.Cols(0, 1);
            var cats = new StackPanel { Width = 150, Margin = new Thickness(0, 0, 8, 0) };
            foreach (var c in Categories)
            {
                var rb = new RadioButton { Style = Ui.StyleRes("Seg"), Content = c, GroupName = "unit-cat", HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Left, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 0, 2), IsChecked = c == S.UnitCategory };
                var cc = c;
                rb.Checked += (s, e) => SetCategory(cc);
                cats.Children.Add(rb);
            }
            root.Children.Add(Kit.At(cats, 0));

            var main = new StackPanel();
            _input.Style = Ui.StyleRes("Field");
            _input.FontSize = 22;
            _input.Padding = new Thickness(12, 6, 12, 6);
            _input.Text = "1";
            _input.Tag = "Value";
            _input.TextChanged += (s, e) => Convert();
            main.Children.Add(Label("From"));
            main.Children.Add(_input);
            main.Children.Add(new Border { Child = _fromUnits, Margin = new Thickness(0, 6, 0, 0) });

            var swapRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var swap = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Swap, Width = 30, Height = 30, Background = Ui.Res("Card2Brush"), ToolTip = "Swap units" };
            swap.Click += (s, e) => { (_from, _to) = (_to, _from); BuildUnits(); Convert(); };
            var copy = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Copy, Width = 30, Height = 30, FontSize = 12, ToolTip = "Copy result" };
            copy.Click += (s, e) => { try { Clipboard.SetText(_result.Text.Replace(",", "")); } catch { } NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Copy, Title = "Copied", Value = _result.Text }); };
            DockPanel.SetDock(swap, Dock.Left);
            DockPanel.SetDock(copy, Dock.Right);
            swapRow.Children.Add(swap);
            swapRow.Children.Add(copy);
            _result.FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont");
            _result.FontSize = 28;
            _result.FontWeight = FontWeights.SemiBold;
            _result.Margin = new Thickness(12, 0, 0, 0);
            _result.TextTrimming = TextTrimming.CharacterEllipsis;
            swapRow.Children.Add(_result);
            main.Children.Add(swapRow);
            main.Children.Add(new Border { Child = _toUnits, Margin = new Thickness(0, 6, 0, 0) });
            _note.FontSize = 11;
            _note.Foreground = Ui.Res("Text3Brush");
            _note.Margin = new Thickness(0, 6, 0, 0);
            main.Children.Add(_note);
            main.Children.Add(_extra);
            root.Children.Add(Kit.At(Kit.Card(new ScrollViewer { Content = main, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }), 1));
            Content = root;
            SetCategory(S.UnitCategory);
        }

        static TextBlock Label(string t) => new TextBlock { Text = t, FontSize = 11, Foreground = Ui.Res("Text3Brush"), Margin = new Thickness(2, 0, 0, 4) };

        public void OnActivated() => Convert();
        public void OnDeactivated() { }

        IEnumerable<string> Units() => _cat == "Currency" ? Currencies : _cat == "Temperature" ? Temps : Linear[_cat].Select(x => x.Unit);

        void SetCategory(string cat)
        {
            if (!Categories.Contains(cat)) cat = "Currency";
            _cat = cat;
            if (S.UnitCategory != cat) { S.UnitCategory = cat; SettingsStore.Save(); }
            var u = Units().ToList();
            (_from, _to) = cat switch
            {
                "Currency" => ("USD", "INR"),
                "Temperature" => ("°C", "°F"),
                "Length" => ("km", "mi"),
                "Mass" => ("kg", "lb"),
                "Data" => ("GB", "MiB"),
                "Volume" => ("L", "gal"),
                "Speed" => ("km/h", "mph"),
                "Time" => ("h", "min"),
                _ => (u[0], u[1])
            };
            BuildUnits();
            Convert();
        }

        void BuildUnits()
        {
            Fill(_fromUnits, "unit-from", _from, v => { _from = v; Convert(); });
            Fill(_toUnits, "unit-to", _to, v => { _to = v; Convert(); });
        }

        void Fill(WrapPanel panel, string group, string current, Action<string> set)
        {
            panel.Children.Clear();
            foreach (var unit in Units())
            {
                var rb = new RadioButton { Style = Ui.StyleRes("Seg"), Content = unit, GroupName = group, IsChecked = unit == current, Padding = new Thickness(9, 4, 9, 4) };
                var u = unit;
                rb.Checked += (s, e) => set(u);
                panel.Children.Add(rb);
            }
        }

        async void Convert()
        {
            _extra.Children.Clear();
            if (!double.TryParse(_input.Text.Replace(",", ""), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) &&
                !double.TryParse(_input.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out v))
            {
                _result.Text = "—";
                _note.Text = "";
                return;
            }
            if (_cat == "Currency")
            {
                if (_rates == null || _ratesFrom != _from)
                {
                    _note.Text = "Fetching rates…";
                    var r = await RatesService.GetAsync(_from);
                    if (r == null) { _result.Text = "—"; _note.Text = "Couldn't load exchange rates. Check your connection."; return; }
                    _rates = r.Value.Rates; _ratesFrom = _from;
                    _note.Text = $"European Central Bank reference rates · {r.Value.Date}";
                }
                else _note.Text = _note.Text.StartsWith("European") ? _note.Text : "European Central Bank reference rates";
                if (!_rates.TryGetValue(_to, out var rate)) { _result.Text = "—"; return; }
                _result.Text = Fmt(v * rate) + " " + _to;
                foreach (var other in Currencies.Where(c => c != _from && c != _to).Take(4))
                    if (_rates.TryGetValue(other, out var or))
                    {
                        var row = new DockPanel { Margin = new Thickness(0, 4, 0, 0) };
                        var val = new TextBlock { Text = Fmt(v * or) + " " + other, FontSize = 12.5, FontWeight = FontWeights.SemiBold };
                        DockPanel.SetDock(val, Dock.Right);
                        row.Children.Add(val);
                        row.Children.Add(new TextBlock { Text = other, FontSize = 12.5, Foreground = Ui.Res("Text2Brush") });
                        _extra.Children.Add(row);
                    }
                return;
            }
            double result;
            if (_cat == "Temperature")
            {
                double c = _from == "°C" ? v : _from == "°F" ? (v - 32) * 5 / 9 : v - 273.15;
                result = _to == "°C" ? c : _to == "°F" ? c * 9 / 5 + 32 : c + 273.15;
            }
            else
            {
                var table = Linear[_cat];
                double f1 = table.First(x => x.Unit == _from).Factor, f2 = table.First(x => x.Unit == _to).Factor;
                result = v * f1 / f2;
            }
            _result.Text = Fmt(result) + " " + _to;
            _note.Text = $"{Fmt(v)} {_from} = {Fmt(result)} {_to}";
        }

        static string Fmt(double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) return "—";
            double a = Math.Abs(d);
            if (a != 0 && (a >= 1e12 || a < 1e-5)) return d.ToString("G6", CultureInfo.InvariantCulture);
            if (a >= 1000) return d.ToString("#,##0.##", CultureInfo.InvariantCulture);
            return d.ToString("0.######", CultureInfo.InvariantCulture);
        }
    }
}
