using System;
using System.Collections.Generic;

namespace Crest.Core
{
    public sealed class AppSettings
    {
        // Appearance
        public string Style { get; set; } = "Notch";        // Notch | Island
        public string Size { get; set; } = "Medium";        // Small | Medium | Large
        public string IdleLook { get; set; } = "Notch";     // legacy (Notch | Line) — migrated to Visibility
        public string Visibility { get; set; } = "";        // Always | Smart | Minimal | Hidden
        public string RestingInfo { get; set; } = "None";   // None | Corners (time left, battery right)
        public bool Outline { get; set; } = false;
        public string Shadow { get; set; } = "Medium";      // None | Subtle | Medium | Deep
        public string EqColor { get; set; } = "Album";      // Album | Accent | Rainbow
        public bool TabAnimations { get; set; } = true;
        public bool Oled { get; set; } = false;             // pure #000, no outline/shadow — blends into OLED bezels
        public bool CameraCutout { get; set; } = false;     // draw a camera lens in the notch
        public int NotchOffset { get; set; } = 0;           // DIP offset from screen centre to line up with a physical cutout
        public double? FloatLeft { get; set; }
        public double? FloatTop { get; set; }
        public List<string> HiddenTabs { get; set; } = new List<string> { "Message" };   // legacy
        public List<string> NowWidgets { get; set; } = new List<string> { "Music", "Glance" };   // legacy (1.7), migrated into WidgetPages
        /// <summary>Your widget pages on the island. Each has its own widgets, each widget its own size.</summary>
        public List<IslandPageDef> WidgetPages { get; set; } = new List<IslandPageDef>();
        public string CountdownLabel { get; set; } = "";
        public string CountdownDate { get; set; } = "";   // yyyy-MM-dd
        public bool Lyrics { get; set; } = false;           // sends title + artist to lrclib.net, so opt-in
        public bool LyricsInNotch { get; set; } = true;
        public List<string> IslandPages { get; set; } = new List<string> { "Home", "Shelf", "Timers" };
        public string AppPage { get; set; } = "Home";
        public double? AppLeft { get; set; }
        public double? AppTop { get; set; }
        public double AppWidth { get; set; } = 1060;
        public double AppHeight { get; set; } = 700;
        public string Accent { get; set; } = "#FF9F0A";

        // Themes
        public string Theme { get; set; } = "Dark";          // Dark | Light | Graphite | Midnight | Forest | Vivid | Sunset | Custom
        public string CustomPanel { get; set; } = "#101014"; // Custom theme panel colour (light or dark is detected from it)
        public string Backdrop { get; set; } = "None";       // None | Aurora | Sunset | Ocean | Media
        public string BgPath { get; set; } = "";             // picture, GIF or video for Backdrop = Media
        public int BgDim { get; set; } = 45;                  // % of panel colour laid over the media
        public int BgBlur { get; set; } = 0;                  // px
        public string Font { get; set; } = "Inter";           // Inter | Segoe | Bahnschrift | any installed family name
        public string Eyes { get; set; } = "Off";             // Off | Pill | Bubble | Honey | Jade | Robot
        public bool EyesSleep { get; set; } = true;           // close the eyes when you're away

        // Behaviour
        public bool ExpandOnHover { get; set; } = true;
        public int HoverDelayMs { get; set; } = 150;
        public bool HideInFullscreen { get; set; } = true;
        public bool HideFromCapture { get; set; } = false;
        public string Display { get; set; } = "Primary";    // Primary | Cursor

        // Live activities
        public bool MediaActivity { get; set; } = true;
        public bool TimerActivity { get; set; } = true;
        public bool VolumeHud { get; set; } = true;
        public bool BrightnessHud { get; set; } = true;
        public bool BatteryAlerts { get; set; } = true;
        public bool BluetoothAlerts { get; set; } = true;
        public bool CapsLockHud { get; set; } = true;
        public bool PrivacyDots { get; set; } = true;
        public bool Sounds { get; set; } = true;
        public bool CopiedHud { get; set; } = true;
        public bool CopiedHideContent { get; set; } = true;

        // Screen time
        public int ScreenLimitMin { get; set; } = 0;

        // Message banner
        public string MsgAnim { get; set; } = "Type";       // Type | Scroll | Pulse
        public string MsgColor { get; set; } = "#FFFFFF";
        public int MsgSeconds { get; set; } = 5;

        // Units
        public string UnitCategory { get; set; } = "Currency";

        // Alarms
        public List<AlarmItem> Alarms { get; set; } = new List<AlarmItem>();

        // Assistant (strictly opt-in: nothing is sent anywhere until a provider is configured)
        public string AiProvider { get; set; } = "Off";      // Off | Ollama | OpenAI | Anthropic
        public string AiEndpoint { get; set; } = "";
        public string AiModel { get; set; } = "";
        public string AiKeyProtected { get; set; } = "";     // DPAPI-encrypted for the current Windows user

        // Automations
        public List<AutomationItem> Automations { get; set; } = new List<AutomationItem>();

        // Phase 3
        public bool FocusAssistHud { get; set; } = true;
        public bool LanDrop { get; set; } = false;
        public bool MirrorNotifications { get; set; } = true;
        public Dictionary<string, int> LaunchCounts { get; set; } = new Dictionary<string, int>();

        // Clipboard
        public bool ClipboardHistory { get; set; } = true;

        // Weather
        public string TempUnit { get; set; } = "C";
        public string? City { get; set; }
        public double? Lat { get; set; }
        public double? Lon { get; set; }

        // Clock
        public List<string> WorldClocks { get; set; } = new List<string> { "Pacific Standard Time", "GMT Standard Time", "Tokyo Standard Time" };

        // Timers
        public int FocusMin { get; set; } = 25;
        public int BreakMin { get; set; } = 5;
        public int LongBreakMin { get; set; } = 15;
        public int Cycles { get; set; } = 4;
        public bool PomoAutoStart { get; set; } = true;
        public string PomoDate { get; set; } = "";
        public int PomoDone { get; set; }
        public int CountdownMin { get; set; } = 15;
        public int WaterGoal { get; set; } = 8;
        public int WaterReminderMin { get; set; } = 0;
        public string WaterDate { get; set; } = "";
        public int WaterCount { get; set; }

        // UI state
        public string LastTab { get; set; } = "Home";
        public bool FirstRun { get; set; } = true;

        // Updates
        public bool AutoUpdateCheck { get; set; } = true;
        public DateTime? LastUpdateCheck { get; set; }
        public string NotifiedVersion { get; set; } = "";
    }

    public sealed class IslandPageDef
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Name { get; set; } = "Widgets";
        public string Glyph { get; set; } = "\uE80F";
        public List<WidgetSlot> Widgets { get; set; } = new List<WidgetSlot>();
    }

    public sealed class WidgetSlot
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
        public string Kind { get; set; } = "Clock";
        public int W { get; set; } = 1;   // 1–4 columns
        public int H { get; set; } = 1;   // 1–2 rows
    }

    public sealed class AlarmItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public int Hour { get; set; } = 7;
        public int Minute { get; set; }
        public string Label { get; set; } = "Alarm";
        public bool Enabled { get; set; } = true;
        /// <summary>Bitmask of weekdays (bit 0 = Sunday). 0 = ring once.</summary>
        public int Days { get; set; }
        public string LastFired { get; set; } = "";
    }

    public sealed class AutomationItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N").Substring(0, 8);
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Args { get; set; } = "";
    }

    public static class SettingsStore
    {
        const string File = "settings.json";
        static Debouncer? _saver;

        public static AppSettings Current { get; private set; } = new AppSettings();

        /// <summary>Raised after a user-facing setting changed (appearance/behaviour). UI thread.</summary>
        public static event Action? Changed;

        public static void Load()
        {
            Current = Storage.Load(File, () => new AppSettings());
            if (string.IsNullOrEmpty(Current.Visibility)) Current.Visibility = Current.IdleLook == "Line" ? "Minimal" : "Always";
        }

        public static void Save()
        {
            _saver ??= new Debouncer(TimeSpan.FromMilliseconds(400), () => Storage.Save(File, Current));
            _saver.Trigger();
        }

        public static void SaveNow() => Storage.Save(File, Current);

        public static void Update(Action<AppSettings> mutate)
        {
            mutate(Current);
            Save();
            Changed?.Invoke();
        }
    }
}
