using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Crest.Core;
using Crest.Services;

namespace Crest
{
    /// <summary>
    /// `Crest.exe --tour [dir]` walks through every tab and live activity and saves screenshots.
    /// Used by CI as a visual smoke test and to regenerate README images. Uses a throwaway data folder in CI.
    /// </summary>
    public partial class NotchWindow
    {
        string _tourDir = "shots";

        public async void RunTour(string dir)
        {
            try
            {
                _tourDir = Path.GetFullPath(dir);
                Directory.CreateDirectory(_tourDir);
                Log.Write("tour: start → " + _tourDir);

                // Demo content so every view has something to show.
                var s = SettingsStore.Current;
                s.City = "Bhubaneswar"; s.Lat = 20.2961; s.Lon = 85.8245;
                _ = WeatherService.Instance.RefreshAsync();
                TrayStore.Shelf.Add(TrayItem.FromPath(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)));
                TrayStore.Shelf.Add(TrayItem.FromPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe")));
                TrayStore.Shelf.Add(TrayItem.FromText("https://github.com/rishii-hub/crest"));
                TrayStore.Shelf.Add(TrayItem.FromText("Meeting notes: ship v1 on Friday"));
                foreach (var app in new[] { @"System32\notepad.exe", @"explorer.exe", @"System32\calc.exe", @"System32\cmd.exe" })
                    TrayStore.Launch.Add(TrayItem.FromPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), app)));
                TrayStore.Launch.Add(TrayItem.FromText("https://github.com"));
                NotesStore.Data.Text = "Crest notes\n\n• Hover the top of the screen to open\n• Drop files on the notch to shelve them\n• Ctrl+Alt+V for clipboard history";
                NotesStore.Data.Todos.Add(new TodoItem { Text = "Star Crest on GitHub" });
                NotesStore.Data.Todos.Add(new TodoItem { Text = "Try the Pomodoro timer" });
                NotesStore.Data.Todos.Add(new TodoItem { Text = "Install Crest", Done = true });
                foreach (var t in new[] { "SELECT * FROM jobs WHERE status = 'QUEUED' LIMIT 50;", "https://open-meteo.com", "Crest — the notch Windows never had" })
                {
                    try { Clipboard.SetText(t); } catch { }
                    await Task.Delay(250);
                }

                s.HiddenTabs.Clear();
                s.Alarms.Add(new AlarmItem { Hour = 7, Minute = 30, Label = "Gym", Days = 0x3E });
                s.Alarms.Add(new AlarmItem { Hour = 9, Minute = 0, Label = "Standup", Days = 0x3E, Enabled = false });
                s.Automations.Add(new AutomationItem { Name = "Backup notes", Path = @"C:\Scripts\backup.ps1" });
                SpotlightService.EnsureIndex();
                BuildTabs();
                await Task.Delay(1500);
                Expand("Home");
                PinButton.IsChecked = true;
                await Task.Delay(1500);
                foreach (var (key, _, _, _) in Tabs)
                {
                    if (key == "Timers") { TimerService.Instance.PomoToggle(); }
                    if (key == "Tools") LanDropService.Start();
                    SelectTab(key);
                    await Task.Delay(key == "Weather" || key == "Units" || key == "AI" ? 3000 : 1300);
                    Capture("tab-" + key.ToLowerInvariant());
                    if (key == "Timers") { TimerService.Instance.PomoToggle(); TimerService.Instance.PomoReset(); }
                }

                // Alarms panel inside Timers
                SelectTab("Timers");
                if (_views.TryGetValue("Timers", out var tv) && tv is Views.TimersView timers) timers.AlarmMode.IsChecked = true;
                await Task.Delay(900); Capture("tab-alarms");

                // Spotlight hub: math, then apps/settings
                PinButton.IsChecked = false;
                Collapse();
                await Task.Delay(700);
                OpenSpotlight();
                await Task.Delay(800);
                if (_views.TryGetValue("Search", out var sv) && sv is Views.SpotlightView spot)
                {
                    spot.SetQuery("(12*7+16)/4");
                    await Task.Delay(900); Capture("search-math");
                    spot.SetQuery("10 km to mi");
                    await Task.Delay(900); Capture("search-units");
                    spot.SetQuery("note");
                    await Task.Delay(1500); Capture("search-apps");
                    spot.SetQuery("bluetooth");
                    await Task.Delay(900); Capture("search-settings");
                }

                PinButton.IsChecked = false;
                Collapse();
                await Task.Delay(1400);
                Capture("idle-notch");

                ShowToast(new Toast { Kind = "vol", Glyph = Glyphs.Volume2, Value = "62", Level = 0.62, Duration = TimeSpan.FromSeconds(3) });
                await Task.Delay(900); Capture("live-volume");
                ShowToast(new Toast { Kind = "power", Title = "Charging", Value = "76%", ValueBrush = Ui.Res("GreenBrush"), Battery = 76, Charging = true, Duration = TimeSpan.FromSeconds(3) });
                await Task.Delay(900); Capture("live-charging");
                ShowToast(new Toast { Kind = "bt", Glyph = Glyphs.Headphone, GlyphBrush = Ui.Res("BlueBrush"), Title = "Nothing Ear (a)", Value = "Connected", ValueBrush = Ui.Res("GreenBrush"), Duration = TimeSpan.FromSeconds(3) });
                await Task.Delay(900); Capture("live-bluetooth");
                ShowToast(new Toast { Kind = "caps", Glyph = Glyphs.Keyboard, Title = "Caps Lock", Value = "On", ValueBrush = (Brush)FindResource("AccentBrush"), Duration = TimeSpan.FromSeconds(1.5) });
                await Task.Delay(900); Capture("live-capslock");
                ShowToast(new Toast { Kind = "copied", Glyph = Glyphs.Copy, Title = "Copied", Value = "Text", ValueBrush = Ui.Res("Text2Brush"), Duration = TimeSpan.FromSeconds(2) });
                await Task.Delay(900); Capture("live-copied");
                await Task.Delay(1500);
                Flash("Ship it on Friday 🚀", Brushes.White, 4);
                await Task.Delay(2200); Capture("live-message");
                await Task.Delay(2500);
                TimerService.Instance.CdSet(25);
                TimerService.Instance.CdToggle();
                await Task.Delay(1200); Capture("live-timer");
                TimerService.Instance.CdReset();

                // Alarm ringing with Snooze / Stop
                AlarmService.Ring(new AlarmItem { Hour = DateTime.Now.Hour, Minute = DateTime.Now.Minute, Label = "Wake up" });
                await Task.Delay(1200); Capture("live-alarm");
                AlarmService.Stop();
                await Task.Delay(700);

                // Third-party Live Activity through the named-pipe API (same code path as the pipe server)
                PipeService.Apply("{\"id\":\"deploy\",\"glyph\":\"🚀\",\"title\":\"Deploying crest\",\"value\":\"3/5\",\"progress\":0.6,\"color\":\"#30D158\",\"actions\":[{\"id\":\"cancel\",\"label\":\"Cancel\"}]}");
                await Task.Delay(1300); Capture("live-pipe");
                Expand("Home");
                await Task.Delay(1000);
                Collapse();
                PipeService.Apply("{\"op\":\"end\",\"id\":\"deploy\"}");
                await Task.Delay(700);

                // Voice memo (real capture when a microphone exists, otherwise skipped)
                if (RecorderService.Start() is null)
                {
                    await Task.Delay(2200); Capture("live-memo");
                    RecorderService.Stop();
                    await Task.Delay(900);
                }
                else Log.Write("tour: no microphone, memo skipped");

                // Screen capture indicator (as reported by Windows' capability access store)
                s.PrivacyDots = false; // pause the real poll so it doesn't clear the demo value
                RecorderService.CaptureApps = "OBS Studio";
                ActivityHub.Notify();
                await Task.Delay(900); Capture("live-screen-capture");
                s.PrivacyDots = true;
                RecorderService.CaptureApps = "";
                ActivityHub.Notify();
                await Task.Delay(700);

                // LAN Drop: a real upload over loopback, accepted from the notch
                if (LanDropService.Running || LanDropService.Start() is null)
                {
                    var pin = LanDropService.Pin;
                    var upload = Task.Run(async () =>
                    {
                        try
                        {
                            using var http = new System.Net.Http.HttpClient();
                            var body = new System.Net.Http.ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("Hello from the Crest tour\n"));
                            var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"http://127.0.0.1:{LanDropService.Port}/upload") { Content = body };
                            req.Headers.Add("X-Pin", pin);
                            req.Headers.Add("X-Name", "holiday-photos.txt");
                            var res = await http.SendAsync(req);
                            Log.Write("tour: lan drop upload " + (int)res.StatusCode);
                        }
                        catch (Exception ex) { Log.Write("tour: lan drop upload failed " + ex.Message); }
                    });
                    await Task.Delay(1500); Capture("live-drop");
                    foreach (var a in ActivityHub.Current()) if (a.Id.StartsWith("drop-")) a.OnAction?.Invoke("accept");
                    await Task.WhenAny(upload, Task.Delay(5000));
                    await Task.Delay(500); Capture("live-drop-received");
                    LanDropService.Stop();
                    await Task.Delay(2500);
                }

                // Bluetooth earbuds with battery level
                ShowToast(new Toast { Kind = "bt", Glyph = Glyphs.Headphone, GlyphBrush = Ui.Res("BlueBrush"), Title = "Nothing Ear (a)", Value = "80%", ValueBrush = Ui.Res("GreenBrush"), Battery = 80, Duration = TimeSpan.FromSeconds(3) });
                await Task.Delay(900); Capture("live-bluetooth-battery");
                ShowToast(new Toast { Kind = "dnd", Glyph = Glyphs.Dnd, GlyphBrush = Ui.Res("PurpleBrush"), Title = "Do not disturb", Value = "Priority only", ValueBrush = Ui.Res("PurpleBrush"), Duration = TimeSpan.FromSeconds(2) });
                await Task.Delay(900); Capture("live-dnd");
                await Task.Delay(1800);

                // Split pill: a running timer + a third-party style activity side by side
                var demo = new LiveActivity { Id = "demo", Priority = 70, Glyph = Glyphs.Download, Title = "Uploading build", Value = "42%", Progress = 0.42, Tab = "Home", Signature = "demo",
                    Actions = { ("cancel", "Cancel") } };
                bool demoOn = true;
                ActivityHub.Register(() => demoOn ? new[] { demo } : Array.Empty<LiveActivity>());
                TimerService.Instance.CdSet(25);
                TimerService.Instance.CdToggle();
                ActivityHub.Notify();
                await Task.Delay(1400); Capture("split-pill");
                Dismiss(Notch, 1);
                await Task.Delay(900); Capture("split-dismissed");
                demoOn = false;
                TimerService.Instance.CdReset();
                ActivityHub.Notify();
                await Task.Delay(600);
                SettingsStore.Update(x => { x.Oled = true; x.CameraCutout = true; x.RestingInfo = "Corners"; });
                await Task.Delay(1200); Capture("oled-cutout");
                SettingsStore.Update(x => { x.Oled = false; x.CameraCutout = false; x.RestingInfo = "None"; });
                FloatWindow.Open("Timers", new Point(SystemParameters.PrimaryScreenWidth / 2, 40));
                await Task.Delay(1300); Capture("floating-widget");
                foreach (Window w in Application.Current.Windows) if (w is FloatWindow) w.Close();
                await Task.Delay(400);

                SettingsStore.Update(x => x.Style = "Island");
                await Task.Delay(1200); Capture("style-island");
                Expand("Home");
                await Task.Delay(1300); Capture("style-island-expanded");
                Collapse();
                SettingsStore.Update(x => { x.Style = "Notch"; x.Visibility = "Minimal"; });
                await Task.Delay(1400); Capture("idle-line");
                SettingsStore.Update(x => { x.Visibility = "Always"; x.RestingInfo = "Corners"; });
                await Task.Delay(1400); Capture("idle-corners");
                SettingsStore.Update(x => x.RestingInfo = "None");
                await Task.Delay(800);

                // Eyes in the resting notch, one per style, then asleep
                foreach (var kind in Controls.NotchEyes.Styles)
                {
                    SettingsStore.Update(x => { x.Eyes = kind; x.EyesSleep = false; });
                    await Task.Delay(900); Capture("eyes-" + kind.ToLowerInvariant());
                }
                SettingsStore.Update(x => { x.EyesSleep = true; x.RestingInfo = "Corners"; });
                await Task.Delay(4500); Capture("eyes-asleep");
                SettingsStore.Update(x => { x.Eyes = "Off"; x.RestingInfo = "None"; });
                await Task.Delay(600);

                // Themes
                foreach (var theme in new[] { "Light", "Vivid", "Midnight", "Sunset" })
                {
                    SettingsStore.Update(x => ThemeService.Choose(theme));
                    Expand("Home");
                    PinButton.IsChecked = true;
                    await Task.Delay(1600); Capture("theme-" + theme.ToLowerInvariant());
                    if (theme == "Light" || theme == "Vivid")
                    {
                        SelectTab("Settings");
                        await Task.Delay(1200); Capture("theme-" + theme.ToLowerInvariant() + "-settings");
                        if (_views.TryGetValue("Settings", out var sv2) && sv2 is Views.SettingsView settings) settings.ScrollTo(260);
                        await Task.Delay(500); Capture("theme-" + theme.ToLowerInvariant() + "-settings-2");
                        SelectTab("Timers");
                        await Task.Delay(1000); Capture("theme-" + theme.ToLowerInvariant() + "-timers");
                    }
                    PinButton.IsChecked = false;
                    Collapse();
                    await Task.Delay(800);
                }

                // Your own background: a picture, then an animated GIF
                var wall = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"Web\Wallpaper\Windows\img0.jpg");
                if (File.Exists(wall))
                {
                    SettingsStore.Update(x => { ThemeService.Choose("Custom"); x.CustomPanel = "#101014"; x.Backdrop = "Media"; x.BgPath = wall; x.BgDim = 35; });
                    Expand("Home"); PinButton.IsChecked = true;
                    await Task.Delay(1600); Capture("theme-picture");
                    PinButton.IsChecked = false; Collapse();
                    await Task.Delay(800);
                }
                var gif = Path.Combine(Path.GetTempPath(), "crest-tour.gif");
                var enc = new System.Windows.Media.Imaging.GifBitmapEncoder();
                for (int i = 0; i < 12; i++)
                {
                    var dv = new DrawingVisual();
                    using (var dc = dv.RenderOpen())
                    {
                        dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(20, 10, 60), Color.FromRgb(120, 20, 90), 45), null, new Rect(0, 0, 320, 180));
                        double a = i / 12.0 * 2 * Math.PI;
                        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(255, 159, 10)), null, new Point(160 + 90 * Math.Cos(a), 90 + 50 * Math.Sin(a)), 28, 28);
                    }
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(320, 180, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(dv);
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                }
                using (var fs = File.Create(gif)) enc.Save(fs);
                SettingsStore.Update(x => { ThemeService.Choose("Custom"); x.CustomPanel = "#1A0B2E"; x.Backdrop = "Media"; x.BgPath = gif; x.BgDim = 25; });
                Expand("Clock"); PinButton.IsChecked = true;
                await Task.Delay(1800); Capture("theme-gif");
                await Task.Delay(350); Capture("theme-gif-2");
                PinButton.IsChecked = false; Collapse();
                SettingsStore.Update(x => { ThemeService.Choose("Dark"); x.Backdrop = "None"; x.BgPath = ""; });
                await Task.Delay(800);
                Log.Write("tour: done");
            }
            catch (Exception ex) { Log.Error("tour", ex); }
            Application.Current.Shutdown();
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")] static extern bool BitBlt(IntPtr hdc, int x, int y, int w, int h, IntPtr src, int sx, int sy, int rop);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        void Capture(string name)
        {
            try
            {
                var b = Win32.MonitorBounds(_monitor);
                double sc = Win32.MonitorScale(_monitor);
                int w = Math.Min(b.Width, (int)(920 * sc)), h = Math.Min(b.Height, (int)(430 * sc));
                int x = b.Left + (b.Width - w) / 2;
                using var bmp = new System.Drawing.Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
                using (var g = System.Drawing.Graphics.FromImage(bmp))
                {
                    IntPtr dst = g.GetHdc();
                    IntPtr src = GetDC(IntPtr.Zero);
                    try { BitBlt(dst, 0, 0, w, h, src, x, b.Top, 0x00CC0020 | 0x40000000); } // SRCCOPY | CAPTUREBLT (includes layered windows)
                    finally { ReleaseDC(IntPtr.Zero, src); g.ReleaseHdc(dst); }
                }
                bmp.Save(Path.Combine(_tourDir, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
                Log.Write("tour: captured " + name);
            }
            catch (Exception ex) { Log.Error("capture " + name, ex); }
        }
    }
}
