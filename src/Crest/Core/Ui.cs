using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using System.Windows.Threading;

namespace Crest.Core
{
    public static class Glyphs
    {
        public const string Home = "", Settings = "", Play = "", Pause = "", Prev = "", Next = "";
        public const string Volume0 = "", Volume1 = "", Volume2 = "", Volume3 = "", Mute = "";
        public const string Brightness = "", Clipboard = "", Note = "", Stopwatch = "", Folder = "";
        public const string Apps = "", Chart = "", Cloud = "", Calendar = "", Music = "";
        public const string Mic = "", Camera = "", Lock = "", Bluetooth = "", Headphone = "";
        public const string Keyboard = "", Cut = "", Bulb = "", Delete = "", Pin = "", Unpin = "";
        public const string Copy = "", Add = "", Remove = "", Close = "", Refresh = "", Search = "";
        public const string Left = "", Right = "", Globe = "", Document = "", OpenFolder = "";
        public const string Reset = "", Skip = "", Flag = "", Check = "", Location = "", Link = "";
        public const string Battery = "", Power = "", Mail = "";
        public const string Calculator = "\uE8EF", ScreenTime = "\uE823", Robot = "\uE99A", Message = "\uE8BD", Swap = "\uE8AB", Paste = "\uE77F", OpenFile = "\uE8E5", Wifi = "\uE701", Download = "\uE896", Chat = "\uE8F2", Tools = "\uE90F", Record = "\uE7C8", Alarm = "\uEA8F", Script = "\uE756", Phone = "\uE8EA", Dnd = "\uE708";
    }

    public static class Ui
    {
        public static Brush Brush(string hex)
        {
            try
            {
                var b = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
                b.Freeze();
                return b;
            }
            catch { return Brushes.White; }
        }

        public static Brush Res(string key) => (Brush)Application.Current.FindResource(key);

        public static Style StyleRes(string key) => (Style)Application.Current.FindResource(key);

        public static void Later(int ms, Action action)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (s, e) => { t.Stop(); try { action(); } catch (Exception ex) { Log.Error("later", ex); } };
            t.Start();
        }

        public static T? FindAncestor<T>(DependencyObject? d) where T : DependencyObject
        {
            while (d != null)
            {
                if (d is T t) return t;
                d = d is Visual || d is Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return null;
        }

        public static string Clock(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes:00}:{t.Seconds:00}";
        }

        /// <summary>"14:05" or "2:05" following the user's 12/24-hour setting, without AM/PM.</summary>
        public static string ShortTime(DateTime t) =>
            t.ToString(System.Globalization.CultureInfo.CurrentCulture.DateTimeFormat.ShortTimePattern.Contains("H") ? "HH:mm" : "h:mm", System.Globalization.CultureInfo.CurrentCulture);

        public static string ClockCeil(TimeSpan t)
        {
            if (t < TimeSpan.Zero) t = TimeSpan.Zero;
            return Clock(TimeSpan.FromSeconds(Math.Ceiling(t.TotalSeconds)));
        }

        public static string Ago(DateTime when)
        {
            var d = DateTime.Now - when;
            if (d.TotalSeconds < 60) return "now";
            if (d.TotalMinutes < 60) return $"{(int)d.TotalMinutes}m ago";
            if (d.TotalHours < 24) return $"{(int)d.TotalHours}h ago";
            return $"{(int)d.TotalDays}d ago";
        }

        public static string Bytes(double b)
        {
            string[] u = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            while (b >= 1024 && i < u.Length - 1) { b /= 1024; i++; }
            return i == 0 ? $"{b:0} {u[i]}" : $"{b:0.#} {u[i]}";
        }

        public static void Open(string target)
        {
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("open " + target, ex); }
        }

        public static void Reveal(string path)
        {
            try { Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true }); }
            catch (Exception ex) { Log.Error("reveal", ex); }
        }

        public static void ApplyAccent(string hex)
        {
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(hex);
                Application.Current.Resources["AccentBrush"] = new SolidColorBrush(c);
            }
            catch { }
        }

        public static Color AccentColor
        {
            get
            {
                if (Application.Current.TryFindResource("AccentBrush") is SolidColorBrush b) return b.Color;
                return Color.FromRgb(0xFF, 0x9F, 0x0A);
            }
        }

        public static void PlaySound(string file)
        {
            if (!SettingsStore.Current.Sounds) return;
            try
            {
                var path = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", file);
                if (System.IO.File.Exists(path)) new System.Media.SoundPlayer(path).Play();
                else System.Media.SystemSounds.Asterisk.Play();
            }
            catch { }
        }
    }
}
