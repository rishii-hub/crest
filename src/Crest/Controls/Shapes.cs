using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Crest.Controls
{
    /// <summary>
    /// Critically-tuned spring curve (what macOS / iOS use for the Dynamic Island) as a WPF easing function.
    /// Lower damping = more bounce.
    /// </summary>
    public sealed class SpringEase : EasingFunctionBase
    {
        public static readonly DependencyProperty DampingProperty =
            DependencyProperty.Register(nameof(Damping), typeof(double), typeof(SpringEase), new PropertyMetadata(0.75));

        public double Damping { get => (double)GetValue(DampingProperty); set => SetValue(DampingProperty, value); }

        public SpringEase() { EasingMode = EasingMode.EaseIn; }

        protected override double EaseInCore(double t)
        {
            if (t <= 0) return 0;
            if (t >= 1) return 1;
            double z = Math.Clamp(Damping, 0.3, 0.99);
            double w = 6.8 / z;
            double wd = w * Math.Sqrt(1 - z * z);
            double x = Spring(t, z, w, wd), x1 = Spring(1, z, w, wd);
            return x + (1 - x1) * t;
        }

        static double Spring(double t, double z, double w, double wd) =>
            1 - Math.Exp(-z * w * t) * (Math.Cos(wd * t) + (z * w / wd) * Math.Sin(wd * t));

        protected override Freezable CreateInstanceCore() => new SpringEase();
    }

    /// <summary>The notch silhouette: flush top edge, concave "shoulders" and rounded bottom corners. Island = floating pill.</summary>
    public sealed class NotchShape : FrameworkElement
    {
        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(NotchShape),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(NotchShape),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ShoulderProperty = DependencyProperty.Register(nameof(Shoulder), typeof(double), typeof(NotchShape),
            new FrameworkPropertyMetadata(9.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty BottomRadiusProperty = DependencyProperty.Register(nameof(BottomRadius), typeof(double), typeof(NotchShape),
            new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty IsIslandProperty = DependencyProperty.Register(nameof(IsIsland), typeof(bool), typeof(NotchShape),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
        public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
        public double Shoulder { get => (double)GetValue(ShoulderProperty); set => SetValue(ShoulderProperty, value); }
        public double BottomRadius { get => (double)GetValue(BottomRadiusProperty); set => SetValue(BottomRadiusProperty, value); }
        public bool IsIsland { get => (bool)GetValue(IsIslandProperty); set => SetValue(IsIslandProperty, value); }

        protected override void OnRenderSizeChanged(SizeChangedInfo info)
        {
            base.OnRenderSizeChanged(info);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 1 || h < 1) return;
            double s = IsIsland ? 0 : Math.Min(Shoulder, Math.Min(w / 4, h / 2));
            Geometry g;
            if (IsIsland || h <= 8)
            {
                double bw = Math.Max(1, w - 2 * s);
                double r = IsIsland ? Math.Min(BottomRadius, Math.Min(h / 2, bw / 2)) : h / 2;
                g = new RectangleGeometry(new Rect(s, 0, bw, h), r, r);
            }
            else
            {
                double r = Math.Max(0, Math.Min(BottomRadius, Math.Min((w - 2 * s) / 2, h - s)));
                var sg = new StreamGeometry();
                using (var c = sg.Open())
                {
                    c.BeginFigure(new Point(0, 0), true, true);
                    c.LineTo(new Point(w, 0), true, false);
                    c.QuadraticBezierTo(new Point(w - s, 0), new Point(w - s, s), true, true);
                    c.LineTo(new Point(w - s, h - r), true, false);
                    c.ArcTo(new Point(w - s - r, h), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                    c.LineTo(new Point(s + r, h), true, false);
                    c.ArcTo(new Point(s, h - r), new Size(r, r), 0, false, SweepDirection.Clockwise, true, true);
                    c.LineTo(new Point(s, s), true, false);
                    c.QuadraticBezierTo(new Point(s, 0), new Point(0, 0), true, true);
                }
                g = sg;
            }
            g.Freeze();
            dc.DrawGeometry(Fill, Stroke != null ? new Pen(Stroke, 1) : null, g);
        }
    }

    /// <summary>Circular progress ring (timers).</summary>
    public sealed class RingProgress : FrameworkElement
    {
        public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(nameof(Progress), typeof(double), typeof(RingProgress),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(RingProgress),
            new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty RingBrushProperty = DependencyProperty.Register(nameof(RingBrush), typeof(Brush), typeof(RingProgress),
            new FrameworkPropertyMetadata(Brushes.Orange, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(nameof(TrackBrush), typeof(Brush), typeof(RingProgress),
            new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF)), FrameworkPropertyMetadataOptions.AffectsRender));

        public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
        public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
        public Brush RingBrush { get => (Brush)GetValue(RingBrushProperty); set => SetValue(RingBrushProperty, value); }
        public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }

        protected override void OnRender(DrawingContext dc)
        {
            double size = Math.Min(ActualWidth, ActualHeight);
            if (size <= Thickness) return;
            double r = (size - Thickness) / 2;
            var c = new Point(ActualWidth / 2, ActualHeight / 2);
            dc.DrawEllipse(null, new Pen(TrackBrush, Thickness), c, r, r);
            double p = Math.Clamp(Progress, 0, 1);
            if (p <= 0.0005) return;
            var pen = new Pen(RingBrush, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            if (p >= 0.9995) { dc.DrawEllipse(null, pen, c, r, r); return; }
            double a0 = -Math.PI / 2, a1 = a0 + p * 2 * Math.PI;
            var start = new Point(c.X + r * Math.Cos(a0), c.Y + r * Math.Sin(a0));
            var end = new Point(c.X + r * Math.Cos(a1), c.Y + r * Math.Sin(a1));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.ArcTo(end, new Size(r, r), 0, p > 0.5, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            dc.DrawGeometry(null, pen, g);
        }
    }

    /// <summary>Rolling line chart with soft area fill (CPU / network).</summary>
    public sealed class Sparkline : FrameworkElement
    {
        readonly List<double> _values = new List<double>();
        public int Capacity { get; set; } = 60;
        public double? FixedMax { get; set; }

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(Sparkline),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
        public Brush Stroke { get => (Brush)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }

        public void Push(double v)
        {
            _values.Add(double.IsFinite(v) ? Math.Max(0, v) : 0);
            if (_values.Count > Capacity) _values.RemoveAt(0);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Math.Max(0, w), Math.Max(0, h)));
            if (_values.Count < 2 || w < 2 || h < 2) return;
            double max = FixedMax ?? Math.Max(1e-9, _values.Max() * 1.2);
            double step = w / (Capacity - 1);
            double x0 = w - (_values.Count - 1) * step;
            var pts = new Point[_values.Count];
            for (int i = 0; i < _values.Count; i++)
                pts[i] = new Point(x0 + i * step, h - Math.Min(1, _values[i] / max) * (h - 1.5) - 0.75);

            var line = new StreamGeometry();
            using (var c = line.Open())
            {
                c.BeginFigure(pts[0], false, false);
                c.PolyLineTo(pts.Skip(1).ToList(), true, true);
            }
            line.Freeze();
            var area = new StreamGeometry();
            using (var c = area.Open())
            {
                c.BeginFigure(new Point(pts[0].X, h), true, true);
                c.PolyLineTo(pts.ToList(), true, true);
                c.LineTo(new Point(pts[pts.Length - 1].X, h), true, true);
            }
            area.Freeze();
            var fill = Stroke.Clone();
            fill.Opacity = 0.16;
            fill.Freeze();
            dc.DrawGeometry(fill, null, area);
            dc.DrawGeometry(null, new Pen(Stroke, 1.6) { LineJoin = PenLineJoin.Round }, line);
        }
    }

    /// <summary>Battery icon with level fill and charging bolt.</summary>
    public sealed class BatteryGlyph : FrameworkElement
    {
        public static readonly DependencyProperty LevelProperty = DependencyProperty.Register(nameof(Level), typeof(double), typeof(BatteryGlyph),
            new FrameworkPropertyMetadata(100.0, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty ChargingProperty = DependencyProperty.Register(nameof(Charging), typeof(bool), typeof(BatteryGlyph),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

        public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
        public bool Charging { get => (bool)GetValue(ChargingProperty); set => SetValue(ChargingProperty, value); }

        static readonly Brush Outline = Freeze(new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)));
        static readonly Brush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58)));
        static readonly Brush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)));
        static Brush Freeze(Brush b) { b.Freeze(); return b; }

        protected override Size MeasureOverride(Size available) => new Size(26, 12);

        protected override void OnRender(DrawingContext dc)
        {
            double lvl = Math.Clamp(Level / 100.0, 0, 1);
            dc.DrawRoundedRectangle(null, new Pen(Outline, 1), new Rect(0.5, 0.5, 22, 11), 3.2, 3.2);
            dc.DrawRoundedRectangle(Outline, null, new Rect(23.6, 4, 1.9, 4), 0.9, 0.9);
            var fill = Charging ? Green : lvl <= 0.2 ? Red : Brushes.White;
            dc.DrawRoundedRectangle(fill, null, new Rect(2, 2, Math.Max(1.6, 19 * lvl), 8), 1.6, 1.6);
            if (Charging)
            {
                var bolt = new StreamGeometry();
                using (var c = bolt.Open())
                {
                    c.BeginFigure(new Point(12.6, 1.2), true, true);
                    c.PolyLineTo(new[] { new Point(8.4, 6.6), new Point(11.2, 6.6), new Point(10.0, 10.8), new Point(14.4, 5.2), new Point(11.6, 5.2) }, true, true);
                }
                bolt.Freeze();
                dc.DrawGeometry(Brushes.White, new Pen(Brushes.Black, 0.9), bolt);
            }
        }
    }
}
