using System;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Core;

namespace Crest.Controls
{
    /// <summary>
    /// A pair of eyes for the resting notch. They follow the cursor, blink, glance around when you're still,
    /// react when the cursor comes close (pupils widen / cat slits open) and fall asleep when you're away.
    /// Only ticks while visible: ~30 fps when awake, 4 fps when asleep.
    /// </summary>
    public sealed class NotchEyes : FrameworkElement
    {
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);

        public static readonly string[] Styles = { "Pill", "Bubble", "Honey", "Jade", "Robot" };

        readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        readonly Random _rnd = new Random();
        string _kind = "Pill";
        double _lx, _ly, _near, _sleep, _blinkLid;
        Point _glance;
        DateTime _nextBlink = DateTime.Now.AddSeconds(2), _blinkStart = DateTime.MinValue, _nextGlance = DateTime.Now, _lastMove = DateTime.Now;
        bool _doubleBlink;
        POINT _lastCursor;
        double _drawn = double.NaN;

        public bool SleepWhenIdle { get; set; } = true;
        public Brush NotchBrush { get; set; } = Brushes.Black;

        public string Kind
        {
            get => _kind;
            set { if (_kind != value) { _kind = value; InvalidateVisual(); } }
        }

        public NotchEyes()
        {
            IsHitTestVisible = false;
            _timer.Tick += (s, e) => Tick();
            IsVisibleChanged += (s, e) => { if (IsVisible) { _lastMove = DateTime.Now; _timer.Start(); } else _timer.Stop(); };
        }

        /// <summary>Close the eyes immediately (tour / screenshots).</summary>
        public void ForceSleep(bool sleep) { _sleep = sleep ? 1 : 0; _lastMove = sleep ? DateTime.Now.AddHours(-1) : DateTime.Now; InvalidateVisual(); }

        void Tick()
        {
            var now = DateTime.Now;
            if (!GetCursorPos(out var c)) return;
            if (c.X != _lastCursor.X || c.Y != _lastCursor.Y) { _lastCursor = c; _lastMove = now; }

            double idleSec = (now - _lastMove).TotalSeconds;
            var li = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
            if (GetLastInputInfo(ref li)) idleSec = Math.Min(idleSec, (Environment.TickCount - (int)li.dwTime) / 1000.0);
            bool asleep = SleepWhenIdle && idleSec > 60;

            // Where the cursor is relative to the eyes, in device pixels.
            Point centre;
            try { centre = PointToScreen(new Point(ActualWidth / 2, ActualHeight / 2)); } catch { return; }
            double scale = Math.Max(1, PresentationSource.FromVisual(this)?.CompositionTarget?.TransformToDevice.M11 ?? 1);
            double dx = c.X - centre.X, dy = c.Y - centre.Y, dist = Math.Sqrt(dx * dx + dy * dy) / scale;

            double tx = Math.Clamp(dx / scale / 260, -1, 1), ty = Math.Clamp(dy / scale / 160, -1, 1);
            if (idleSec > 4 && !asleep)
            {
                // Bored: glance around now and then.
                if (now >= _nextGlance)
                {
                    _glance = _rnd.NextDouble() < 0.35 ? new Point(0, 0) : new Point(_rnd.NextDouble() * 1.6 - 0.8, _rnd.NextDouble() * 1.0 - 0.3);
                    _nextGlance = now.AddSeconds(1.2 + _rnd.NextDouble() * 2.2);
                }
                tx = _glance.X; ty = _glance.Y;
            }
            double nearTarget = Math.Clamp(1 - dist / 420, 0, 1);

            _lx += (tx - _lx) * 0.28;
            _ly += (ty - _ly) * 0.28;
            _near += (nearTarget - _near) * 0.12;
            _sleep += ((asleep ? 1 : 0) - _sleep) * (asleep ? 0.03 : 0.25);

            // Blinks: every 2.5–6 s, sometimes twice.
            if (!asleep && now >= _nextBlink && _blinkStart == DateTime.MinValue)
            {
                _blinkStart = now;
                _doubleBlink = _rnd.NextDouble() < 0.15;
            }
            if (_blinkStart != DateTime.MinValue)
            {
                double t = (now - _blinkStart).TotalMilliseconds / 170.0;
                if (t >= (_doubleBlink ? 2.2 : 1)) { _blinkStart = DateTime.MinValue; _blinkLid = 0; _nextBlink = now.AddSeconds(2.5 + _rnd.NextDouble() * 3.5); }
                else _blinkLid = t <= 1 ? Math.Sin(Math.PI * t) : t >= 1.2 ? Math.Sin(Math.PI * (t - 1.2)) : 0;
            }

            _timer.Interval = TimeSpan.FromMilliseconds(_sleep > 0.97 ? 250 : 33);
            double sig = Math.Round(_lx, 3) * 7 + Math.Round(_ly, 3) * 13 + Math.Round(_near, 3) * 17 + Math.Round(_sleep, 3) * 19 + Math.Round(_blinkLid, 3) * 23
                         + (_sleep > 0.8 ? now.Second : 0);
            if (sig != _drawn) { _drawn = sig; InvalidateVisual(); }
        }

        static Brush Frozen(Brush b) { b.Freeze(); return b; }
        static readonly Brush HoneyIris = Frozen(new RadialGradientBrush(Color.FromRgb(0xFF, 0xC8, 0x5A), Color.FromRgb(0xB8, 0x5C, 0x00)) { GradientOrigin = new Point(0.45, 0.4) });
        static readonly Brush JadeIris = Frozen(new RadialGradientBrush(Color.FromRgb(0x9B, 0xFF, 0xB0), Color.FromRgb(0x0B, 0x7A, 0x34)) { GradientOrigin = new Point(0.45, 0.4) });
        static readonly Brush Shine = Frozen(new SolidColorBrush(Color.FromArgb(0xE6, 0xFF, 0xFF, 0xFF)));

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w < 4 || h < 4) return;
            double r = h / 2;
            double lid = Math.Max(_blinkLid, _sleep * 0.94);
            foreach (int side in new[] { -1, 1 })
            {
                var c = new Point(w / 2 + side * w * 0.26, h / 2);
                switch (_kind)
                {
                    case "Bubble": Round(dc, c, r, Brushes.White, Brushes.Black, r * (0.46 + 0.08 * _near), r * (0.46 + 0.08 * _near), 0.42, lid); break;
                    case "Honey": Round(dc, c, r, HoneyIris, Brushes.Black, r * (0.26 + 0.34 * _near), r * (0.26 + 0.34 * _near), 0.28, lid); break;
                    case "Jade": Round(dc, c, r, JadeIris, Brushes.Black, r * (0.1 + 0.36 * _near), r * 0.72, 0.3, lid); break;
                    case "Robot": Robot(dc, c, r, lid); break;
                    default: Pill(dc, c, r, lid); break;
                }
            }
            if (_sleep > 0.8)
            {
                // a little "z" drifting up while asleep
                double phase = (DateTime.Now.Second % 3) / 3.0;
                var ft = new FormattedText("z", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), h * (0.5 + 0.25 * phase),
                    new SolidColorBrush(Color.FromArgb((byte)(200 * (1 - phase)), 255, 255, 255)), VisualTreeHelper.GetDpi(this).PixelsPerDip);
                dc.DrawText(ft, new Point(w - ft.Width * 0.6, h * 0.1 - phase * h * 0.5));
            }
        }

        void Pill(DrawingContext dc, Point c, double r, double lid)
        {
            double ew = r * 0.72, eh = r * 1.9 * Math.Max(0.12, 1 - lid);
            var o = new Point(c.X + _lx * r * 0.5, c.Y + _ly * r * 0.22);
            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(o.X - ew / 2, o.Y - eh / 2, ew, eh), ew / 2, Math.Min(ew / 2, eh / 2));
        }

        void Robot(DrawingContext dc, Point c, double r, double lid)
        {
            var acc = Ui.AccentColor;
            double ew = r * 1.6, eh = r * 1.25 * Math.Max(0.14, 1 - lid);
            var o = new Point(c.X + _lx * r * 0.4, c.Y + _ly * r * 0.2);
            var glow = new SolidColorBrush(Color.FromArgb(70, acc.R, acc.G, acc.B));
            dc.DrawRoundedRectangle(glow, null, new Rect(o.X - ew / 2 - 2, o.Y - eh / 2 - 2, ew + 4, eh + 4), 4, 4);
            dc.DrawRoundedRectangle(new SolidColorBrush(acc), null, new Rect(o.X - ew / 2, o.Y - eh / 2, ew, eh), 3, 3);
        }

        void Round(DrawingContext dc, Point c, double r, Brush eye, Brush pupil, double prx, double pry, double travel, double lid)
        {
            var clip = new EllipseGeometry(c, r, r);
            dc.PushClip(clip);
            dc.DrawEllipse(eye, null, c, r, r);
            var p = new Point(c.X + _lx * r * travel * 1.25, c.Y + _ly * r * travel);
            dc.DrawEllipse(pupil, null, p, prx, pry);
            dc.DrawEllipse(Shine, null, new Point(p.X - prx * 0.35 - r * 0.08, p.Y - r * 0.3), r * 0.16, r * 0.16);
            if (lid > 0.01)
            {
                // eyelids in the notch colour: the top one does most of the work, like a real blink
                dc.DrawRectangle(NotchBrush, null, new Rect(c.X - r, c.Y - r, 2 * r, 2 * r * 0.62 * lid));
                dc.DrawRectangle(NotchBrush, null, new Rect(c.X - r, c.Y + r - 2 * r * 0.4 * lid, 2 * r, 2 * r * 0.4 * lid + 0.5));
            }
            dc.Pop();
        }
    }
}
