using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Services;

namespace Crest.Controls
{
    /// <summary>Real-time spectrum of what's playing (WASAPI loopback FFT). Only captures while visible and Running.</summary>
    public sealed class SpectrumBars : FrameworkElement
    {
        readonly DispatcherTimer _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };
        readonly float[] _shown = new float[AudioViz.Bands];
        bool _acquired, _running;
        public Brush Brush { get; set; } = Brushes.White;
        /// <summary>Draw this many bars (bands are averaged together); 0 = one per band.</summary>
        public int Bars { get; set; }

        public SpectrumBars()
        {
            IsHitTestVisible = false;
            _timer.Tick += (s, e) => Step();
            IsVisibleChanged += (s, e) => Sync();
            Unloaded += (s, e) => { _running = false; Sync(); };
        }

        /// <summary>True while media is playing and the host view is active.</summary>
        public bool Running { get => _running; set { _running = value; Sync(); } }

        void Sync()
        {
            bool want = _running && IsVisible;
            if (want && !_acquired) { AudioViz.Acquire(); _acquired = true; _timer.Start(); }
            else if (!want && _acquired) { AudioViz.Release(); _acquired = false; }
            // when stopping, the timer keeps ticking until the bars have fallen, then stops itself
        }

        void Step()
        {
            var target = _acquired ? AudioViz.Snapshot() : new float[AudioViz.Bands];
            bool any = false;
            for (int i = 0; i < _shown.Length; i++)
            {
                float t = i < target.Length ? target[i] : 0;
                _shown[i] = t > _shown[i] ? _shown[i] + (t - _shown[i]) * 0.6f : _shown[i] * 0.86f;
                if (_shown[i] > 0.01f) any = true;
            }
            if (!_acquired && !any) _timer.Stop();
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0 || h <= 0) return;
            if (!_acquired && Array.TrueForAll(_shown, v => v < 0.01f)) return; // idle: draw nothing
            int bands = _shown.Length;
            int n = Bars > 0 ? Math.Min(Bars, bands) : bands;
            // bars and gaps always fit inside the control, whatever its width
            double slot = w / n, gap = Math.Min(3, slot * 0.35), bw = Math.Max(1, slot - gap);
            for (int i = 0; i < n; i++)
            {
                int from = i * bands / n, to = Math.Max(from + 1, (i + 1) * bands / n);
                double v = 0;
                for (int k = from; k < to; k++) v = Math.Max(v, _shown[k]);
                v = Math.Clamp(v, 0, 1);
                double bh = Math.Max(2, v * h);
                dc.PushOpacity(0.35 + 0.65 * v);
                dc.DrawRoundedRectangle(Brush, null, new Rect(i * slot + gap / 2, (h - bh) / 2, bw, bh), bw / 2, bw / 2);
                dc.Pop();
            }
        }
    }
}
