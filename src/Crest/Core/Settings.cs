using System;
using System.Collections.Generic;

namespace Crest.Core
{
    public sealed class AppSettings
    {
        // Appearance
        public string Style { get; set; } = "Notch";        // Notch | Island
        public string Size { get; set; } = "Medium";        // Small | Medium | Large
        public string IdleLook { get; set; } = "Notch";     // Notch | Line
        public string Accent { get; set; } = "#FF9F0A";

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
    }

    public static class SettingsStore
    {
        const string File = "settings.json";
        static Debouncer? _saver;

        public static AppSettings Current { get; private set; } = new AppSettings();

        /// <summary>Raised after a user-facing setting changed (appearance/behaviour). UI thread.</summary>
        public static event Action? Changed;

        public static void Load() => Current = Storage.Load(File, () => new AppSettings());

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
