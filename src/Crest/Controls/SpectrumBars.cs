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
            int n = _shown.Length;
            double gap = 3, bw = Math.Max(1, (w - gap * (n - 1)) / n);
            for (int i = 0; i < n; i++)
            {
                double v = Math.Clamp(_shown[i], 0, 1);
                double bh = Math.Max(2, v * h);
                dc.PushOpacity(0.35 + 0.65 * v);
                dc.DrawRoundedRectangle(Brush, null, new Rect(i * (bw + gap), (h - bh) / 2, bw, bh), bw / 2, bw / 2);
                dc.Pop();
            }
        }
    }
}
