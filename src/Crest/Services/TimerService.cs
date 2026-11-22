using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Threading;
using Crest.Core;

namespace Crest.Services
{
    public enum PomoPhase { Focus, Break, LongBreak }

    public sealed class TimerAlert
    {
        public string Glyph = Glyphs.Stopwatch;
        public bool Emoji;
        public string Title = "";
        public string Value = "";
    }

    /// <summary>Pomodoro, countdown, stopwatch and water tracker. Runs independently of the UI so live activities keep ticking.</summary>
    public sealed class TimerService
    {
        public static TimerService Instance { get; } = new TimerService();
        static AppSettings S => SettingsStore.Current;

        DispatcherTimer _tick = null!;
        DateTime _lastWater = DateTime.Now;

        // Pomodoro
        public PomoPhase Phase { get; private set; } = PomoPhase.Focus;
        public int Cycle { get; private set; } = 1;
        public bool PomoRunning { get; private set; }
        DateTime _pomoEnd;
        TimeSpan _pomoLeft;

        // Countdown
        public bool CdRunning { get; private set; }
        public TimeSpan CdTotal { get; private set; }
        DateTime _cdEnd;
        TimeSpan _cdLeft;

        // Stopwatch
        readonly Stopwatch _sw = new Stopwatch();
        public List<TimeSpan> Laps { get; } = new List<TimeSpan>();

        public event Action? Tick;
        public event Action? StateChanged;
        public event Action<TimerAlert>? Alert;

        public void Init(Dispatcher ui)
        {
            _tick = new DispatcherTimer(DispatcherPriority.Normal, ui) { Interval = TimeSpan.FromMilliseconds(250) };
            _tick.Tick += (s, e) => OnTick();
            _pomoLeft = PhaseLength(Phase);
            CdTotal = TimeSpan.FromMinutes(Math.Max(1, S.CountdownMin));
            _cdLeft = CdTotal;
            RollDay();
            UpdateTicking();
        }

        // ---------- Pomodoro ----------
        public TimeSpan PhaseLength(PomoPhase p) => TimeSpan.FromMinutes(p switch
        {
            PomoPhase.Focus => Math.Max(1, S.FocusMin),
            PomoPhase.Break => Math.Max(1, S.BreakMin),
            _ => Math.Max(1, S.LongBreakMin)
        });

        public TimeSpan PomoLeft => PomoRunning ? Max0(_pomoEnd - DateTime.Now) : _pomoLeft;
        public double PomoProgress => 1 - PomoLeft.TotalSeconds / Math.Max(1, PhaseLength(Phase).TotalSeconds);

        public PomoPhase NextPhase => Phase == PomoPhase.Focus ? (Cycle >= Math.Max(1, S.Cycles) ? PomoPhase.LongBreak : PomoPhase.Break) : PomoPhase.Focus;

        public string PhaseName(PomoPhase p) => p == PomoPhase.Focus ? "Focus" : p == PomoPhase.Break ? "Break" : "Long break";

        public int CompletedToday { get { RollDay(); return S.PomoDone; } }

        public void PomoToggle()
        {
            if (PomoRunning) { _pomoLeft = PomoLeft; PomoRunning = false; }
            else { _pomoEnd = DateTime.Now + _pomoLeft; PomoRunning = true; }
            Changed();
        }

        public void PomoReset()
        {
            PomoRunning = false;
            _pomoLeft = PhaseLength(Phase);
            Changed();
        }

        public void PomoResetAll()
        {
            PomoRunning = false;
            Phase = PomoPhase.Focus;
            Cycle = 1;
            _pomoLeft = PhaseLength(Phase);
            Changed();
        }

        public void PomoSkip() => Advance(false);

        /// <summary>Call after the user edits durations.</summary>
        public void PomoSettingsChanged()
        {
            if (!PomoRunning) _pomoLeft = PhaseLength(Phase);
            if (Cycle > Math.Max(1, S.Cycles)) Cycle = Math.Max(1, S.Cycles);
            SettingsStore.Save();
            Changed();
        }

        void Advance(bool natural)
        {
            var finished = Phase;
            if (finished == PomoPhase.Focus)
            {
                if (natural) { RollDay(); S.PomoDone++; SettingsStore.Save(); }
                Phase = NextPhase;
            }
            else
            {
                Cycle = finished == PomoPhase.LongBreak ? 1 : Cycle + 1;
                Phase = PomoPhase.Focus;
            }
            _pomoLeft = PhaseLength(Phase);
            bool run = natural ? S.PomoAutoStart : PomoRunning;
            PomoRunning = run;
            if (run) _pomoEnd = DateTime.Now + _pomoLeft;
            if (natural)
            {
                Alert?.Invoke(new TimerAlert
                {
                    Glyph = finished == PomoPhase.Focus ? "☕" : "🎯",
                    Emoji = true,
                    Title = finished == PomoPhase.Focus ? "Focus done" : "Break over",
                    Value = finished == PomoPhase.Focus ? PhaseName(Phase) + " " + Ui.Clock(_pomoLeft) : "Focus " + Ui.Clock(_pomoLeft)
                });
                Ui.PlaySound("Alarm01.wav");
            }
            Changed();
        }

        // ---------- Countdown ----------
        public TimeSpan CdLeft => CdRunning ? Max0(_cdEnd - DateTime.Now) : _cdLeft;
        public double CdProgress => CdTotal.TotalSeconds <= 0 ? 0 : 1 - CdLeft.TotalSeconds / CdTotal.TotalSeconds;

        public void CdSet(int minutes)
        {
            minutes = Math.Clamp(minutes, 1, 600);
            S.CountdownMin = minutes;
            SettingsStore.Save();
            CdTotal = TimeSpan.FromMinutes(minutes);
            CdRunning = false;
            _cdLeft = CdTotal;
            Changed();
        }

        public void CdToggle()
        {
            if (CdRunning) { _cdLeft = CdLeft; CdRunning = false; }
            else
            {
                if (_cdLeft <= TimeSpan.Zero) _cdLeft = CdTotal;
                _cdEnd = DateTime.Now + _cdLeft;
                CdRunning = true;
            }
            Changed();
        }

        public void CdReset() { CdRunning = false; _cdLeft = CdTotal; Changed(); }

        // ---------- Stopwatch ----------
        public bool SwRunning => _sw.IsRunning;
        public TimeSpan SwElapsed => _sw.Elapsed;
        public void SwToggle() { if (_sw.IsRunning) _sw.Stop(); else _sw.Start(); Changed(); }
        public void SwReset() { _sw.Reset(); Laps.Clear(); Changed(); }
        public void SwLap() { if (_sw.IsRunning) { Laps.Insert(0, _sw.Elapsed); Changed(); } }

        // ---------- Water ----------
        public int WaterCount { get { RollDay(); return S.WaterCount; } }

        public void WaterAdd(int delta)
        {
            RollDay();
            S.WaterCount = Math.Clamp(S.WaterCount + delta, 0, 99);
            SettingsStore.Save();
            _lastWater = DateTime.Now;
            if (delta > 0 && S.WaterCount == S.WaterGoal)
                Alert?.Invoke(new TimerAlert { Glyph = "💧", Emoji = true, Title = "Water goal reached", Value = $"{S.WaterCount}/{S.WaterGoal}" });
            Changed();
        }

        public void WaterSettingsChanged() { SettingsStore.Save(); _lastWater = DateTime.Now; UpdateTicking(); Changed(); }

        // ---------- Engine ----------
        /// <summary>The timer the collapsed notch should show, if any.</summary>
        public (string Label, string Time, double Progress)? LiveActivity
        {
            get
            {
                if (PomoRunning) return (PhaseName(Phase), Ui.ClockCeil(PomoLeft), PomoProgress);
                if (CdRunning) return ("Timer", Ui.ClockCeil(CdLeft), CdProgress);
                if (_sw.IsRunning) return ("Stopwatch", Ui.Clock(_sw.Elapsed), 0);
                return null;
            }
        }

        void OnTick()
        {
            if (PomoRunning && DateTime.Now >= _pomoEnd) Advance(true);
            if (CdRunning && DateTime.Now >= _cdEnd)
            {
                CdRunning = false;
                _cdLeft = TimeSpan.Zero;
                Alert?.Invoke(new TimerAlert { Glyph = "⏰", Emoji = true, Title = "Timer done", Value = Ui.Clock(CdTotal) });
                Ui.PlaySound("Alarm01.wav");
                Changed();
            }
            if (S.WaterReminderMin > 0 && WaterCount < S.WaterGoal && DateTime.Now - _lastWater >= TimeSpan.FromMinutes(S.WaterReminderMin))
            {
                _lastWater = DateTime.Now;
                int h = DateTime.Now.Hour;
                if (h >= 8 && h < 23)
                {
                    Alert?.Invoke(new TimerAlert { Glyph = "💧", Emoji = true, Title = "Time for water", Value = $"{WaterCount}/{S.WaterGoal}" });
                    Ui.PlaySound("Windows Notify Calendar.wav");
                }
            }
            Tick?.Invoke();
        }

        void Changed()
        {
            UpdateTicking();
            StateChanged?.Invoke();
            Tick?.Invoke();
        }

        void UpdateTicking()
        {
            if (_tick == null) return;
            bool fast = PomoRunning || CdRunning || _sw.IsRunning;
            _tick.Interval = fast ? TimeSpan.FromMilliseconds(250) : TimeSpan.FromSeconds(20);
            bool need = fast || S.WaterReminderMin > 0;
            if (need && !_tick.IsEnabled) _tick.Start();
            else if (!need && _tick.IsEnabled) _tick.Stop();
        }

        void RollDay()
        {
            var today = DateTime.Now.ToString("yyyy-MM-dd");
            if (S.PomoDate != today) { S.PomoDate = today; S.PomoDone = 0; SettingsStore.Save(); }
            if (S.WaterDate != today) { S.WaterDate = today; S.WaterCount = 0; SettingsStore.Save(); }
        }

        static TimeSpan Max0(TimeSpan t) => t < TimeSpan.Zero ? TimeSpan.Zero : t;
    }
}
