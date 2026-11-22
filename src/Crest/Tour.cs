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

                await Task.Delay(1500);
                Expand("Home");
                PinButton.IsChecked = true;
                await Task.Delay(1500);
                foreach (var (key, _, _) in Tabs)
                {
                    if (key == "Timers") { TimerService.Instance.PomoToggle(); }
                    SelectTab(key);
                    await Task.Delay(key == "Weather" ? 3000 : 1300);
                    Capture("tab-" + key.ToLowerInvariant());
                    if (key == "Timers") { TimerService.Instance.PomoToggle(); TimerService.Instance.PomoReset(); }
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
                await Task.Delay(1500);
                TimerService.Instance.CdSet(25);
                TimerService.Instance.CdToggle();
                await Task.Delay(1200); Capture("live-timer");
                TimerService.Instance.CdReset();

                SettingsStore.Update(x => x.Style = "Island");
                await Task.Delay(1200); Capture("style-island");
                Expand("Home");
                await Task.Delay(1300); Capture("style-island-expanded");
                Collapse();
                SettingsStore.Update(x => { x.Style = "Notch"; x.IdleLook = "Line"; });
                await Task.Delay(1400); Capture("idle-line");
                SettingsStore.Update(x => x.IdleLook = "Notch");
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
