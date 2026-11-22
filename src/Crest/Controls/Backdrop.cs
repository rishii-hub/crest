using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using Crest.Core;

namespace Crest.Controls
{
    /// <summary>
    /// What sits behind the open notch: a slow moving gradient (Aurora / Sunset / Ocean), or your own picture,
    /// animated GIF or video. Everything is paused while the notch is closed, so it costs nothing at rest.
    /// </summary>
    public sealed class Backdrop : Grid
    {
        static AppSettings S => SettingsStore.Current;
        public static readonly string[] VideoExt = { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".webm", ".mkv" };
        public static readonly string[] ImageExt = { ".png", ".jpg", ".jpeg", ".bmp", ".webp", ".tif", ".tiff", ".heic", ".gif" };

        readonly Grid _media = new Grid();
        readonly Border _dim = new Border();
        readonly Image _image = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        readonly DispatcherTimer _gifTimer = new DispatcherTimer(DispatcherPriority.Render);
        readonly List<Ellipse> _blobs = new List<Ellipse>();
        MediaElement? _video;
        List<(BitmapSource Frame, int Ms)>? _frames;
        int _frame;
        string _config = "";
        bool _running;

        public Backdrop()
        {
            IsHitTestVisible = false;
            Children.Add(_media);
            Children.Add(_dim);
            RenderOptions.SetBitmapScalingMode(_image, BitmapScalingMode.HighQuality);
            _gifTimer.Tick += (s, e) => NextGifFrame();
        }

        public bool HasContent { get; private set; }

        /// <summary>Re-read the settings. Cheap when nothing changed.</summary>
        public void Configure()
        {
            string mode = S.Backdrop;
            if (mode == "Media" && !File.Exists(S.BgPath)) mode = "None";
            string cfg = $"{mode}|{S.BgPath}|{ThemeService.PanelHex}";
            Color panel = ThemeService.Panel;
            _dim.Background = new SolidColorBrush(Color.FromArgb((byte)Math.Round(Math.Clamp(S.BgDim, 0, 95) * 2.55), panel.R, panel.G, panel.B));
            _dim.Visibility = mode == "Media" ? Visibility.Visible : Visibility.Collapsed;
            _media.Effect = mode == "Media" && S.BgBlur > 0 ? new BlurEffect { Radius = Math.Clamp(S.BgBlur, 0, 40), RenderingBias = RenderingBias.Performance } : null;
            if (cfg == _config) return;
            _config = cfg;

            bool wasRunning = _running;
            Stop();
            ClearMedia();
            HasContent = mode != "None";
            Visibility = HasContent ? Visibility.Visible : Visibility.Collapsed;
            if (mode == "None") return;

            if (mode == "Media") LoadMedia(S.BgPath);
            else BuildBlobs(ThemeService.BackdropColors(mode), ThemeService.IsLight);
            if (wasRunning) Start();
        }

        void ClearMedia()
        {
            _media.Children.Clear();
            _blobs.Clear();
            _frames = null;
            _image.Source = null;
            if (_video != null) { try { _video.Stop(); _video.Close(); } catch { } _video = null; }
        }

        // ---------------- gradients ----------------
        void BuildBlobs(Color[] colors, bool light)
        {
            var spots = new[]
            {
                (HorizontalAlignment.Left, VerticalAlignment.Top, new Thickness(-140, -220, 0, 0)),
                (HorizontalAlignment.Right, VerticalAlignment.Top, new Thickness(0, -160, -160, 0)),
                (HorizontalAlignment.Center, VerticalAlignment.Bottom, new Thickness(0, 0, 0, -260)),
            };
            for (int i = 0; i < 3; i++)
            {
                var c = colors[i % colors.Length];
                var brush = new RadialGradientBrush();
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(light ? (byte)150 : (byte)215, c.R, c.G, c.B), 0));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(light ? (byte)60 : (byte)90, c.R, c.G, c.B), 0.55));
                brush.GradientStops.Add(new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 1));
                brush.Freeze();
                var (ha, va, m) = spots[i];
                var e = new Ellipse { Width = 520, Height = 460, Fill = brush, HorizontalAlignment = ha, VerticalAlignment = va, Margin = m, RenderTransform = new TranslateTransform() };
                _blobs.Add(e);
                _media.Children.Add(e);
            }
        }

        // ---------------- picture / GIF / video ----------------
        void LoadMedia(string path)
        {
            string ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (VideoExt.Contains(ext))
                {
                    _video = new MediaElement
                    {
                        LoadedBehavior = MediaState.Manual, UnloadedBehavior = MediaState.Manual, IsMuted = true, Volume = 0,
                        Stretch = Stretch.UniformToFill, ScrubbingEnabled = false, Source = new Uri(path)
                    };
                    _video.MediaEnded += (s, e) => { _video.Position = TimeSpan.Zero; _video.Play(); };
                    _video.MediaFailed += (s, e) => Log.Write("backdrop video failed: " + e.ErrorException?.Message);
                    _media.Children.Add(_video);
                    return;
                }
                _media.Children.Add(_image);
                if (ext == ".gif")
                {
                    string cfg = _config;
                    Task.Run(() => LoadGif(path, 900)).ContinueWith(t =>
                    {
                        if (t.Result == null || cfg != _config) return;
                        _frames = t.Result;
                        _frame = 0;
                        Log.Write($"backdrop: gif {_frames.Count} frames");
                        _image.Source = _frames[0].Frame;
                        if (_running && _frames.Count > 1) { _gifTimer.Interval = TimeSpan.FromMilliseconds(_frames[0].Ms); _gifTimer.Start(); }
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                    return;
                }
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
                bmp.DecodePixelWidth = 1400;
                bmp.UriSource = new Uri(path);
                bmp.EndInit();
                bmp.Freeze();
                _image.Source = bmp;
            }
            catch (Exception ex) { Log.Error("backdrop " + path, ex); }
        }

        /// <summary>Decode and composite every GIF frame (honouring offsets and disposal), scaled so all frames fit in ~64 MB.</summary>
        static List<(BitmapSource, int)>? LoadGif(string path, int maxWidth)
        {
            try
            {
                using var fs = File.OpenRead(path);
                var dec = new GifBitmapDecoder(fs, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                if (dec.Frames.Count == 0) return null;
                int W = dec.Frames[0].PixelWidth, H = dec.Frames[0].PixelHeight;
                try
                {
                    if (dec.Metadata?.GetQuery("/logscrdesc/Width") is ushort lw && lw > 0) W = lw;
                    if (dec.Metadata?.GetQuery("/logscrdesc/Height") is ushort lh && lh > 0) H = lh;
                }
                catch { }
                int count = Math.Min(dec.Frames.Count, 400);
                double scale = Math.Min(1, maxWidth / (double)W);
                double budget = 64.0 * 1024 * 1024;
                double need = count * W * H * 4 * scale * scale;
                if (need > budget) scale *= Math.Sqrt(budget / need);
                int sw = Math.Max(1, (int)(W * scale)), sh = Math.Max(1, (int)(H * scale));

                var canvas = new byte[W * H * 4];
                var list = new List<(BitmapSource, int)>(count);
                for (int i = 0; i < count; i++)
                {
                    var f = dec.Frames[i];
                    int left = 0, top = 0, delay = 10, disposal = 0;
                    if (f.Metadata is BitmapMetadata md)
                    {
                        try { left = Convert.ToInt32(md.GetQuery("/imgdesc/Left") ?? 0); } catch { }
                        try { top = Convert.ToInt32(md.GetQuery("/imgdesc/Top") ?? 0); } catch { }
                        try { delay = Convert.ToInt32(md.GetQuery("/grctlext/Delay") ?? 10); } catch { }
                        try { disposal = Convert.ToInt32(md.GetQuery("/grctlext/Disposal") ?? 0); } catch { }
                    }
                    var conv = new FormatConvertedBitmap(f, PixelFormats.Bgra32, null, 0);
                    int fw = conv.PixelWidth, fh = conv.PixelHeight;
                    var px = new byte[fw * fh * 4];
                    conv.CopyPixels(px, fw * 4, 0);
                    byte[]? restore = disposal == 3 ? (byte[])canvas.Clone() : null;
                    for (int y = 0; y < fh; y++)
                    {
                        int cy = top + y;
                        if (cy < 0 || cy >= H) continue;
                        for (int x = 0; x < fw; x++)
                        {
                            int cx = left + x;
                            if (cx < 0 || cx >= W) continue;
                            int si = (y * fw + x) * 4;
                            if (px[si + 3] == 0) continue;
                            int di = (cy * W + cx) * 4;
                            canvas[di] = px[si]; canvas[di + 1] = px[si + 1]; canvas[di + 2] = px[si + 2]; canvas[di + 3] = px[si + 3];
                        }
                    }
                    BitmapSource full = BitmapSource.Create(W, H, 96, 96, PixelFormats.Bgra32, null, canvas, W * 4);
                    BitmapSource frame = full;
                    if (scale < 1)
                    {
                        var tb = new TransformedBitmap(full, new ScaleTransform(sw / (double)W, sh / (double)H));
                        var small = new byte[sw * sh * 4];
                        tb.CopyPixels(small, sw * 4, 0);
                        frame = BitmapSource.Create(sw, sh, 96, 96, PixelFormats.Bgra32, null, small, sw * 4);
                    }
                    frame.Freeze();
                    list.Add((frame, Math.Max(20, (delay < 2 ? 10 : delay) * 10)));

                    int span = Math.Min(W, left + fw) - Math.Max(0, left);
                    if (disposal == 2 && span > 0)
                        for (int y = Math.Max(0, top); y < Math.Min(H, top + fh); y++)
                            Array.Clear(canvas, (y * W + Math.Max(0, left)) * 4, span * 4);
                    else if (restore != null) canvas = restore;
                }
                return list;
            }
            catch (Exception ex) { Log.Error("gif " + path, ex); return null; }
        }

        void NextGifFrame()
        {
            if (_frames == null || _frames.Count < 2) { _gifTimer.Stop(); return; }
            _frame = (_frame + 1) % _frames.Count;
            _image.Source = _frames[_frame].Frame;
            _gifTimer.Interval = TimeSpan.FromMilliseconds(_frames[_frame].Ms);
        }

        // ---------------- run / pause ----------------
        public void Start()
        {
            if (!HasContent) return;
            _running = true;
            for (int i = 0; i < _blobs.Count; i++)
            {
                var t = (TranslateTransform)_blobs[i].RenderTransform;
                double secs = 9 + i * 2.5;
                t.BeginAnimation(TranslateTransform.XProperty, Loop(i % 2 == 0 ? 160 : -170, secs));
                t.BeginAnimation(TranslateTransform.YProperty, Loop(i == 2 ? -90 : 70, secs * 0.8));
            }
            if (_frames != null && _frames.Count > 1) { _gifTimer.Interval = TimeSpan.FromMilliseconds(_frames[_frame].Ms); _gifTimer.Start(); }
            try { _video?.Play(); } catch { }
        }

        static DoubleAnimation Loop(double to, double secs)
        {
            var a = new DoubleAnimation(0, to, TimeSpan.FromSeconds(secs))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            Timeline.SetDesiredFrameRate(a, 24); // slow drift: 24 fps is plenty and keeps CPU low
            return a;
        }

        public void Stop()
        {
            _running = false;
            foreach (var b in _blobs)
            {
                var t = (TranslateTransform)b.RenderTransform;
                t.BeginAnimation(TranslateTransform.XProperty, null);
                t.BeginAnimation(TranslateTransform.YProperty, null);
            }
            _gifTimer.Stop();
            try { _video?.Pause(); } catch { }
        }
    }
}
