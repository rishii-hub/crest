using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace Crest.Core
{
    /// <summary>A theme preset: panel colour, accent and backdrop. Card/text colours are derived from the panel unless given.</summary>
    public sealed record ThemePreset(string Key, string Name, string Panel, string Accent, string Backdrop, string Sub);

    /// <summary>
    /// Builds the colour palette and fonts and puts them in the application resources. Everything in XAML that can change
    /// with the theme uses DynamicResource; code-built views are rebuilt by the notch when the theme changes.
    /// </summary>
    public static class ThemeService
    {
        static AppSettings S => SettingsStore.Current;

        public static readonly ThemePreset[] Presets =
        {
            new("Dark", "Dark", "#000000", "#FF9F0A", "None", "Pure black, like the hardware notch"),
            new("Light", "Light", "#F2F2F7", "#007AFF", "None", "Bright, paper-white panels"),
            new("Graphite", "Graphite", "#1C1C1E", "#64D2FF", "None", "Soft dark grey"),
            new("Midnight", "Midnight", "#070B1A", "#5E5CE6", "None", "Deep navy"),
            new("Forest", "Forest", "#06120D", "#30D158", "None", "Dark green"),
            new("Vivid", "Vivid", "#0A0612", "#FF375F", "Aurora", "Moving aurora behind glass"),
            new("Sunset", "Sunset", "#140A06", "#FF9F0A", "Sunset", "Warm glow behind glass"),
            new("Custom", "Custom", "", "", "", "Your colour, picture, GIF or video"),
        };

        public static readonly string[] PanelSwatches =
            { "#000000", "#101014", "#1C1C1E", "#0B1020", "#1A0B2E", "#2A0A18", "#0E1A14", "#F2F2F7", "#FFFFFF", "#FFF4E6", "#FCE7F3", "#E6F4EA", "#E7F0FF" };

        public static string PanelHex => S.Theme == "Custom" ? S.CustomPanel : (Presets.FirstOrDefault(p => p.Key == S.Theme) ?? Presets[0]).Panel;
        public static Color Panel => Parse(PanelHex, Colors.Black);
        public static bool IsLight => Luma(Panel) > 0.5;
        /// <summary>Glass look: translucent cards so a backdrop shows through.</summary>
        public static bool HasBackdrop => S.Backdrop != "None" && (S.Backdrop != "Media" || System.IO.File.Exists(S.BgPath));

        /// <summary>Key that changes whenever something needing a view rebuild changes.</summary>
        public static string Signature => $"{S.Theme}|{S.CustomPanel}|{HasBackdrop}|{S.Backdrop == "Media"}|{S.Font}";

        public static void Apply()
        {
            var res = Application.Current.Resources;
            Color panel = Panel;
            bool light = IsLight, glass = HasBackdrop;
            var pal = new Dictionary<string, Color>();

            if (light)
            {
                pal["TextBrush"] = Hex("#1C1C1E");
                pal["Text2Brush"] = Hex("#5C5C61");
                pal["Text3Brush"] = Hex("#8A8A8E");
                pal["CardBrush"] = glass ? A(Colors.White, 0.62) : Mix(panel, Colors.White, 0.78);
                pal["Card2Brush"] = glass ? A(Colors.Black, 0.06) : Mix(panel, Colors.Black, 0.06);
                pal["HoverBrush"] = glass ? A(Colors.Black, 0.12) : Mix(panel, Colors.Black, 0.12);
                pal["StrokeBrush"] = A(Colors.Black, 0.10);
                pal["DockBrush"] = glass ? A(Colors.White, 0.62) : Mix(panel, Colors.White, 0.78);
                pal["InkBrush"] = Hex("#1C1C1E");
                pal["OnInkBrush"] = Colors.White;
                pal["OverlayBrush"] = Colors.Black;
                pal["DividerBrush"] = A(Colors.Black, 0.12);
                pal["TrackBrush"] = A(Colors.Black, 0.12);
                pal["ScrollBrush"] = A(Colors.Black, 0.25);
                pal["GreenBrush"] = Hex("#28A745"); pal["RedBrush"] = Hex("#FF3B30"); pal["BlueBrush"] = Hex("#007AFF");
                pal["OrangeBrush"] = Hex("#F08A00"); pal["CyanBrush"] = Hex("#1A9FD6"); pal["PurpleBrush"] = Hex("#AF52DE");
            }
            else
            {
                pal["TextBrush"] = Hex("#F5F5F7");
                pal["Text2Brush"] = Hex("#AEAEB2");
                pal["Text3Brush"] = Hex("#7C7C80");
                bool black = panel.R + panel.G + panel.B < 12;
                pal["CardBrush"] = glass ? A(Colors.White, 0.08) : black ? Hex("#161618") : Mix(panel, Colors.White, 0.07);
                pal["Card2Brush"] = glass ? A(Colors.White, 0.13) : black ? Hex("#242426") : Mix(panel, Colors.White, 0.13);
                pal["HoverBrush"] = glass ? A(Colors.White, 0.20) : black ? Hex("#333336") : Mix(panel, Colors.White, 0.20);
                pal["StrokeBrush"] = glass ? A(Colors.White, 0.12) : black ? Hex("#2E2E31") : Mix(panel, Colors.White, 0.11);
                pal["DockBrush"] = glass ? A(Colors.White, 0.10) : black ? Hex("#161618") : Mix(panel, Colors.White, 0.07);
                pal["InkBrush"] = Colors.White;
                pal["OnInkBrush"] = Colors.Black;
                pal["OverlayBrush"] = Colors.White;
                pal["DividerBrush"] = A(Colors.White, 0.20);
                pal["TrackBrush"] = glass ? A(Colors.White, 0.22) : Hex("#39393D");
                pal["ScrollBrush"] = A(Colors.White, 0.25);
                pal["GreenBrush"] = Hex("#30D158"); pal["RedBrush"] = Hex("#FF453A"); pal["BlueBrush"] = Hex("#0A84FF");
                pal["OrangeBrush"] = Hex("#FF9F0A"); pal["CyanBrush"] = Hex("#64D2FF"); pal["PurpleBrush"] = Hex("#BF5AF2");
            }
            pal["PanelBrush"] = panel;

            foreach (var kv in pal)
            {
                var b = new SolidColorBrush(kv.Value);
                b.Freeze();
                res[kv.Key] = b;
            }
            ApplyFont();
            Ui.ApplyAccent(S.Accent);
        }

        static readonly Uri FontBase = new Uri("pack://application:,,,/");

        public static void ApplyFont()
        {
            var res = Application.Current.Resources;
            string ui, display;
            switch (S.Font)
            {
                case "Segoe":
                    ui = "Segoe UI Variable Text, Segoe UI"; display = "Segoe UI Variable Display, Segoe UI"; break;
                case "Bahnschrift":
                    ui = display = "Bahnschrift, Segoe UI"; break;
                case "Inter":
                case "":
                case null:
                    ui = "./Assets/Fonts/#Inter, Segoe UI Variable Text, Segoe UI";
                    display = "./Assets/Fonts/#Inter Display, Segoe UI Variable Display, Segoe UI"; break;
                default:
                    ui = display = S.Font + ", Segoe UI"; break;
            }
            res["UiFont"] = new FontFamily(FontBase, ui);
            res["DisplayFont"] = new FontFamily(FontBase, display);
        }

        /// <summary>Choose a preset; its accent and backdrop come with it (a picture/GIF/video backdrop is kept).</summary>
        public static void Choose(string key)
        {
            var p = Presets.FirstOrDefault(x => x.Key == key);
            if (p == null) return;
            S.Theme = key;
            if (key == "Custom") return;
            S.Accent = p.Accent;
            if (S.Backdrop != "Media" || p.Backdrop != "None") S.Backdrop = p.Backdrop;
        }

        // ---------- colour helpers ----------
        public static Color Parse(string hex, Color fallback)
        {
            try { return (Color)ColorConverter.ConvertFromString(hex.StartsWith("#") ? hex : "#" + hex); } catch { return fallback; }
        }
        static Color Hex(string h) => (Color)ColorConverter.ConvertFromString(h);
        static Color A(Color c, double a) => Color.FromArgb((byte)Math.Round(a * 255), c.R, c.G, c.B);
        public static Color Mix(Color a, Color b, double t) => Color.FromRgb(
            (byte)Math.Round(a.R + (b.R - a.R) * t), (byte)Math.Round(a.G + (b.G - a.G) * t), (byte)Math.Round(a.B + (b.B - a.B) * t));
        public static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

        /// <summary>Colours of the moving blobs for a gradient backdrop.</summary>
        public static Color[] BackdropColors(string backdrop) => backdrop switch
        {
            "Sunset" => new[] { Hex("#FF6A00"), Hex("#FF2D55"), Hex("#AF52DE") },
            "Ocean" => new[] { Hex("#00C7BE"), Hex("#0A84FF"), Hex("#5E5CE6") },
            _ => new[] { Hex("#7B2FF7"), Hex("#F107A3"), Hex("#0A84FF") }, // Aurora
        };
    }
}
