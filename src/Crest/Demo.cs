using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Crest.Core;
using Crest.Services;
using Crest.Views;

namespace Crest
{
    /// <summary>
    /// `Crest.exe --demo` plays a scripted ~95 second walkthrough for the promo video: a real cursor moves, hovers and clicks,
    /// with captions on a wallpaper behind. The song, its art, the audio levels and the lyrics are made up (CI has no music),
    /// everything else is the real app. CI records the screen with ffmpeg while this runs.
    /// </summary>
    public partial class NotchWindow
    {
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
        [DllImport("user32.dll")] static extern bool GetCursorPos(out POINTD p);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags, int dx, int dy, uint data, UIntPtr extra);
        [StructLayout(LayoutKind.Sequential)] struct POINTD { public int X, Y; }

        Window? _wall;
        TextBlock? _capTitle, _capSub;
        StackPanel? _cap;

        public async void RunDemo()
        {
            try
            {
                Log.Write("demo: start");
                var s = SettingsStore.Current;
                s.City = "Bhubaneswar"; s.Lat = 20.2961; s.Lon = 85.8245;
                _ = WeatherService.Instance.RefreshAsync();
                s.Eyes = "Pill"; s.EyesSleep = false; s.Lyrics = true; s.LyricsInNotch = true;
                s.ExpandOnHover = true; s.HoverDelayMs = 150; s.Shadow = "Medium";
                AudioViz.DemoMode = true;
                NotesStore.Data.Text = "Ship Crest v2 ✨\nRecord the demo\nReply to issues";
                TrayStore.Shelf.Add(TrayItem.FromPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
                TrayStore.Shelf.Add(TrayItem.FromPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe")));
                TrayStore.Shelf.Add(TrayItem.FromText("https://github.com/rishii-hub/crest"));
                SettingsStore.Update(x =>
                {
                    var home = WidgetCatalog.Page("Home")!;
                    home.Widgets = new List<WidgetSlot> { new() { Kind = "Music", W = 2 }, new() { Kind = "Lyrics", W = 2 } };
                    var today = new IslandPageDef { Id = "today", Name = "Today", Glyph = Glyphs.Calendar };
                    today.Widgets = new List<WidgetSlot>
                    {
                        new() { Kind = "Clock", W = 2, H = 2 }, new() { Kind = "Weather", W = 2 }, new() { Kind = "Calendar" }, new() { Kind = "System" },
                    };
                    x.WidgetPages.RemoveAll(p => p.Id == "today");
                    x.WidgetPages.Add(today);
                    x.IslandPages = new List<string> { "Home", "today", "Timers", "Shelf" };
                });

                ShowWallpaper();
                var screen = new Point(SystemParameters.PrimaryScreenWidth, SystemParameters.PrimaryScreenHeight);
                var dpi = VisualTreeHelper.GetDpi(this);
                Point Px(double x, double y) => new Point(x * dpi.DpiScaleX, y * dpi.DpiScaleY);
                var centre = Px(screen.X / 2, screen.Y * 0.55);
                SetCursorPos((int)centre.X, (int)centre.Y);
                await Task.Delay(1200);

                // 1 · the resting notch, with eyes
                await Caption("Crest", "A Dynamic Island for Windows");
                await Glide(Px(screen.X * 0.22, screen.Y * 0.35), 1400);
                await Glide(Px(screen.X * 0.78, screen.Y * 0.30), 1800);
                await Glide(Px(screen.X * 0.5, screen.Y * 0.62), 1200);

                // 2 · music + lyrics as a live activity
                await Caption("Live activities", "Music from any app, with lyrics right in the notch");
                MediaService.Instance.Demo("Midnight Drive", "The Crest Demo Band", Art(), Color.FromRgb(0xFF, 0x5E, 0x7E), TimeSpan.FromSeconds(204));
                LyricsService.DemoSong(DemoLyrics());
                await Task.Delay(7000);

                // 3 · hover to open: widgets
                await Caption("Hover to open", "Your widgets, your pages, any size");
                await Glide(NotchPoint(0, 10), 900);
                await Task.Delay(4200);
                await ClickTab("today");
                await Task.Delay(3600);
                TimerService.Instance.CdSet(25);
                await ClickTab("Timers");
                await Task.Delay(900);
                if (!TimerService.Instance.CdRunning) TimerService.Instance.CdToggle();
                await Task.Delay(1800);
                await ClickTab("Shelf");
                await Task.Delay(2200);
                await ClickTab("Home");
                await Task.Delay(1500);

                // 4 · two activities at once
                await Glide(Px(screen.X * 0.5, screen.Y * 0.6), 700);
                await Caption("Two at once", "A timer and your music, side by side");
                await Task.Delay(4200);

                // 5 · system HUDs
                await Caption("Everything, at a glance", "Volume · AirPods-style battery · charging");
                ShowToast(new Toast { Kind = "vol", Glyph = Glyphs.Volume2, Title = "Volume", Level = 0.64, Duration = TimeSpan.FromSeconds(1.8) });
                await Task.Delay(2200);
                ShowToast(new Toast { Kind = "bt", Glyph = Glyphs.Headphone, GlyphBrush = Ui.Res("BlueBrush"), Title = "Nothing Ear (a)", Value = "80%", ValueBrush = Ui.Res("GreenBrush"), Battery = 80, Duration = TimeSpan.FromSeconds(2.2) });
                await Task.Delay(2600);
                ShowToast(new Toast { Kind = "charge", Glyph = Glyphs.Power, GlyphBrush = Ui.Res("GreenBrush"), Title = "Charging", Value = "64%", ValueBrush = Ui.Res("GreenBrush"), Battery = 64, Charging = true, Duration = TimeSpan.FromSeconds(2.2) });
                await Task.Delay(2600);

                // 6 · search
                await Caption("Ctrl + Alt + Space", "Apps, settings, math and units in one box");
                OpenSpotlight();
                await Task.Delay(700);
                if (_views.TryGetValue("Search", out var sv) && sv is SpotlightView spot)
                {
                    await Type(spot, "notepad");
                    await Task.Delay(1400);
                    await Type(spot, "(12*7+16)/4");
                    await Task.Delay(1400);
                    await Type(spot, "10 km to mi");
                    await Task.Delay(1600);
                }
                Collapse();
                await Task.Delay(900);

                // 7 · themes
                await Caption("Make it yours", "Themes, glass, even your own GIF or video behind it");
                foreach (var theme in new[] { "Vivid", "Light", "Sunset" })
                {
                    SettingsStore.Update(x => ThemeService.Choose(theme));
                    await Glide(NotchPoint(0, 10), 600);
                    await Task.Delay(2600);
                    await Glide(Px(screen.X * 0.5, screen.Y * 0.6), 500);
                    await Task.Delay(700);
                }
                SettingsStore.Update(x => { ThemeService.Choose("Dark"); x.Backdrop = "None"; });

                // 8 · the app: resize widgets
                await Caption("Any widget, any size", "From 1×1 up to 4×2, on as many pages as you like");
                AppWindow.ShowPage("Island");
                var app = AppWindow.Current!;
                app.Topmost = true;
                app.Width = 1060; app.Height = 640;
                app.Left = (screen.X - 1060) / 2; app.Top = screen.Y - 640 - 90;
                await Task.Delay(2200);
                SettingsStore.Update(x => { var h = WidgetCatalog.Page("Home")!; h.Widgets[0].H = 2; h.Widgets[1].H = 2; });
                await Glide(NotchPoint(0, 10), 800);
                await Task.Delay(3200);
                await Glide(Px(screen.X * 0.5, screen.Y * 0.5), 600);
                app.Topmost = false;
                app.Hide();
                await Task.Delay(800);

                // 9 · eyes
                MediaService.Instance.DemoEnd();
                TimerService.Instance.CdReset();
                await Caption("And yes, it has eyes", "They follow you, blink, and nap when you're away");
                foreach (var style in new[] { "Bubble", "Honey", "Jade" })
                {
                    SettingsStore.Update(x => x.Eyes = style);
                    await Glide(Px(screen.X * 0.25, screen.Y * 0.28), 900);
                    await Glide(Px(screen.X * 0.75, screen.Y * 0.4), 1100);
                }
                await Glide(Px(screen.X * 0.5, screen.Y * 0.9), 900);
                Eyes.ForceSleep(true);

                await Caption("Crest · free and open source", "github.com/rishii-hub/crest");
                await Task.Delay(4500);
                Log.Write("demo: done");
            }
            catch (Exception ex) { Log.Error("demo", ex); }
            finally
            {
                AudioViz.DemoMode = false;
                Application.Current.Shutdown();
            }
        }

        static List<LyricsService.Line> DemoLyrics()
        {
            // original lines written for the demo
            var words = new[]
            {
                "Streetlights humming on the edge of town", "We keep the windows open, let the night come down",
                "Every island glowing like it knows our name", "Hold the moment, nothing stays the same",
                "Neon on the glass and the radio low", "Turn it up a little, then let it go",
                "Midnight drive, we don't need a map", "Follow the light at the top of the screen",
                "Midnight drive, and we're never coming back", "Every little island is a place we've been",
            };
            return words.Select((w, i) => new LyricsService.Line(TimeSpan.FromSeconds(0.6 + i * 2.7), w)).ToList();
        }

        /// <summary>Made-up album art: a sunset over a grid.</summary>
        static BitmapSource Art()
        {
            const int n = 300;
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                var sky = new LinearGradientBrush(Color.FromRgb(0x2B, 0x0F, 0x54), Color.FromRgb(0xFF, 0x5E, 0x7E), 90);
                dc.DrawRectangle(sky, null, new Rect(0, 0, n, n));
                var sun = new LinearGradientBrush(Color.FromRgb(0xFF, 0xD3, 0x6E), Color.FromRgb(0xFF, 0x6B, 0x6B), 90);
                dc.DrawEllipse(sun, null, new Point(n / 2, n * 0.58), 82, 82);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0x14, 0x06, 0x2E)), null, new Rect(0, n * 0.62, n, n * 0.38));
                var pen = new Pen(new SolidColorBrush(Color.FromArgb(170, 0xFF, 0x5E, 0xC8)), 2);
                for (int i = 0; i < 7; i++) dc.DrawLine(pen, new Point(0, n * 0.62 + i * i * 2.8 + 4), new Point(n, n * 0.62 + i * i * 2.8 + 4));
                for (int i = -6; i <= 6; i++) dc.DrawLine(pen, new Point(n / 2 + i * 12, n * 0.62), new Point(n / 2 + i * 60, n));
            }
            var rtb = new RenderTargetBitmap(n, n, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            rtb.Freeze();
            return rtb;
        }

        void ShowWallpaper()
        {
            double w = SystemParameters.PrimaryScreenWidth, h = SystemParameters.PrimaryScreenHeight;
            var bg = new Grid();
            bg.Background = new LinearGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromRgb(0x0B, 0x0A, 0x1F), 0),
                new GradientStop(Color.FromRgb(0x24, 0x1B, 0x4D), 0.55),
                new GradientStop(Color.FromRgb(0x3B, 0x1F, 0x4F), 1),
            }, new Point(0, 0), new Point(1, 1));
            foreach (var (c, x, y, r) in new[] { (Color.FromRgb(0x7B, 0x2F, 0xF7), 0.18, 0.75, 520.0), (Color.FromRgb(0xFF, 0x5E, 0x7E), 0.85, 0.65, 460.0), (Color.FromRgb(0x0A, 0x84, 0xFF), 0.55, 1.05, 600.0) })
            {
                var brush = new RadialGradientBrush(Color.FromArgb(120, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
                var e = new System.Windows.Shapes.Ellipse { Width = r * 2, Height = r * 2, Fill = brush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(w * x - r, h * y - r, 0, 0) };
                bg.Children.Add(e);
            }
            _capTitle = new TextBlock { FontSize = 44, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
            _capTitle.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            _capSub = new TextBlock { FontSize = 20, Foreground = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
            _capSub.SetResourceReference(TextBlock.FontFamilyProperty, "UiFont");
            _cap = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, h * 0.12), Opacity = 0, RenderTransform = new TranslateTransform() };
            _cap.Children.Add(_capTitle);
            _cap.Children.Add(_capSub);
            bg.Children.Add(_cap);
            _wall = new Window
            {
                WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, ShowActivated = false, Topmost = true,
                Left = 0, Top = 0, Width = w, Height = h, Content = bg, Background = Brushes.Black
            };
            _wall.Show();
            Topmost = false; Topmost = true; // the notch stays above the wallpaper
        }

        async Task Caption(string title, string sub)
        {
            if (_cap == null || _capTitle == null || _capSub == null) return;
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (_cap.Opacity > 0)
            {
                _cap.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(220)));
                await Task.Delay(240);
            }
            _capTitle.Text = title;
            _capSub.Text = sub;
            ((TranslateTransform)_cap.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(500)) { EasingFunction = ease });
            _cap.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)));
            await Task.Delay(450);
        }

        /// <summary>Screen point (device pixels) inside the notch, offset from its top centre.</summary>
        Point NotchPoint(double dx, double dy)
        {
            var p = Notch.PointToScreen(new Point(Notch.ActualWidth / 2 + dx, dy));
            return p;
        }

        /// <summary>Move the real cursor smoothly (ease in-out), so hovers and the eyes react like they would for a person.</summary>
        async Task Glide(Point to, int ms)
        {
            GetCursorPos(out var from);
            int steps = Math.Max(1, ms / 16);
            for (int i = 1; i <= steps; i++)
            {
                double t = i / (double)steps;
                t = t < 0.5 ? 4 * t * t * t : 1 - Math.Pow(-2 * t + 2, 3) / 2;
                SetCursorPos((int)(from.X + (to.X - from.X) * t), (int)(from.Y + (to.Y - from.Y) * t));
                await Task.Delay(16);
            }
        }

        async Task ClickTab(string key)
        {
            if (!_tabButtons.TryGetValue(key, out var rb)) { SelectTab(key); return; }
            var p = rb.PointToScreen(new Point(rb.ActualWidth / 2, rb.ActualHeight / 2));
            await Glide(p, 450);
            await Task.Delay(120);
            mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero); // left down
            await Task.Delay(70);
            mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero); // left up
            await Task.Delay(200);
        }

        static async Task Type(SpotlightView spot, string text)
        {
            for (int i = 1; i <= text.Length; i++)
            {
                spot.SetQuery(text[..i]);
                await Task.Delay(90);
            }
        }
    }
}
