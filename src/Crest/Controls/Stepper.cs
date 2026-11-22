using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Crest.Core;

namespace Crest.Controls
{
    /// <summary>"−  Focus 25m  +" stepper pill.</summary>
    public sealed class Stepper : Border
    {
        readonly TextBlock _text;
        readonly Func<int, string> _format;
        int _value;

        public int Min { get; set; }
        public int Max { get; set; } = 999;
        public int Step { get; set; } = 1;
        public event Action<int>? Changed;

        public Stepper(Func<int, string> format, int value, int min, int max, int step = 1)
        {
            _format = format;
            Min = min; Max = max; Step = step;
            _value = Math.Clamp(value, min, max);
            CornerRadius = new CornerRadius(12);
            Background = Ui.Res("CardBrush");
            BorderBrush = Ui.Res("StrokeBrush");
            BorderThickness = new Thickness(1);
            Height = 34;
            Margin = new Thickness(3);

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var minus = MakeButton(Glyphs.Remove);
            minus.Click += (s, e) => Set(_value - Step);
            var plus = MakeButton(Glyphs.Add);
            plus.Click += (s, e) => Set(_value + Step);
            Grid.SetColumn(plus, 2);

            _text = new TextBlock
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 12.5,
                Foreground = Ui.Res("Text2Brush"),
                Text = _format(_value)
            };
            Grid.SetColumn(_text, 1);

            grid.Children.Add(minus);
            grid.Children.Add(_text);
            grid.Children.Add(plus);
            Child = grid;
        }

        public int Value => _value;

        public void Set(int v, bool notify = true)
        {
            v = Math.Clamp(v, Min, Max);
            if (v == _value) { _text.Text = _format(_value); return; }
            _value = v;
            _text.Text = _format(_value);
            if (notify) Changed?.Invoke(_value);
        }

        static Button MakeButton(string glyph) => new Button
        {
            Content = glyph,
            Style = Ui.StyleRes("IconButton"),
            Width = 30,
            Height = 30,
            FontSize = 10,
            Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93))
        };
    }
}
