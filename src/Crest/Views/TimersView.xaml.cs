using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using Crest.Controls;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    public partial class TimersView : UserControl, ITabView
    {
        static AppSettings S => SettingsStore.Current;
        static TimerService T => TimerService.Instance;

        // pomodoro
        TextBlock _pomoHead = null!, _pomoNext = null!, _pomoDone = null!;
        StackPanel _dots = null!;
        Button _pomoPlay = null!;
        CheckBox _autoStart = null!;
        // countdown
        Button _cdPlay = null!;
        Stepper _cdMinutes = null!;
        // stopwatch
        Button _swPlay = null!, _swLap = null!;
        StackPanel _laps = null!;
        // water
        TextBlock _waterBig = null!;
        WrapPanel _drops = null!;

        public TimersView()
        {
            InitializeComponent();
            BuildPomodoro();
            BuildCountdown();
            BuildStopwatch();
            BuildWater();
            BuildAlarms();
            PomoMode.Checked += (s, e) => ShowPanel();
            CdMode.Checked += (s, e) => ShowPanel();
            SwMode.Checked += (s, e) => ShowPanel();
            WaterMode.Checked += (s, e) => ShowPanel();
            AlarmMode.Checked += (s, e) => ShowPanel();
            AlarmService.Changed += () => { if (IsVisible) { _alarmListCount = -1; Refresh(); } };
            T.Tick += () => { if (IsVisible) Refresh(); };
            T.StateChanged += () => { if (IsVisible) Refresh(); };
        }

        public void OnActivated()
        {
            // Jump to whatever is running.
            if (AlarmService.Ringing != null) AlarmMode.IsChecked = true;
            else if (T.CdRunning && !T.PomoRunning) CdMode.IsChecked = true;
            else if (T.SwRunning && !T.PomoRunning && !T.CdRunning) SwMode.IsChecked = true;
            ShowPanel();
        }

        public void OnDeactivated() { }

        void ShowPanel()
        {
            PomoPanel.Visibility = PomoMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            CdPanel.Visibility = CdMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            SwPanel.Visibility = SwMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            WaterPanel.Visibility = WaterMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            AlarmPanel.Visibility = AlarmMode.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            _alarmListCount = -1;
            Refresh();
        }

        // ---------- builders ----------
        static Button Round(string glyph, string tip, bool primary = false)
        {
            var b = new Button { Content = glyph, ToolTip = tip, Margin = new Thickness(5, 0, 5, 0) };
            if (primary)
            {
                b.Style = Ui.StyleRes("PrimaryButton");
                b.Width = b.Height = 52;
                b.FontSize = 18;
                b.SetResourceReference(BackgroundProperty, "AccentBrush");
            }
            else
            {
                b.Style = Ui.StyleRes("IconButton");
                b.Width = b.Height = 40;
                b.Background = Ui.Res("Card2Brush");
                b.FontSize = 14;
            }
            return b;
        }

        static StackPanel Row(params UIElement[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var c in children) p.Children.Add(c);
            return p;
        }

        static UniformGrid Grid2(params UIElement[] children)
        {
            var g = new UniformGrid { Columns = 2, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var c in children) g.Children.Add(c);
            return g;
        }

        void BuildPomodoro()
        {
            _pomoHead = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            _dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 4) };
            _pomoNext = new TextBlock { Style = Ui.StyleRes("Caption"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 10) };
            var reset = Round(Glyphs.Refresh, "Reset this phase (double-click: reset all)");
            reset.Click += (s, e) => T.PomoReset();
            reset.MouseDoubleClick += (s, e) => T.PomoResetAll();
            _pomoPlay = Round(Glyphs.Play, "Start / pause  (Ctrl+Alt+P)", true);
            _pomoPlay.Click += (s, e) => T.PomoToggle();
            var skip = Round(Glyphs.Next, "Skip to next phase");
            skip.Click += (s, e) => T.PomoSkip();

            var focus = new Stepper(v => $"Focus {v}m", S.FocusMin, 1, 180, 5);
            focus.Changed += v => { S.FocusMin = v; T.PomoSettingsChanged(); };
            var brk = new Stepper(v => $"Break {v}m", S.BreakMin, 1, 60, 1);
            brk.Changed += v => { S.BreakMin = v; T.PomoSettingsChanged(); };
            var lng = new Stepper(v => $"Long {v}m", S.LongBreakMin, 1, 90, 5);
            lng.Changed += v => { S.LongBreakMin = v; T.PomoSettingsChanged(); };
            var cyc = new Stepper(v => $"Cycles {v}", S.Cycles, 1, 12, 1);
            cyc.Changed += v => { S.Cycles = v; T.PomoSettingsChanged(); };

            _autoStart = new CheckBox { Style = Ui.StyleRes("Switch"), IsChecked = S.PomoAutoStart, Margin = new Thickness(10, 0, 0, 0) };
            _autoStart.Click += (s, e) => { S.PomoAutoStart = _autoStart.IsChecked == true; SettingsStore.Save(); };
            var auto = Row(new TextBlock { Text = "Auto-start next", FontSize = 12.5, Foreground = Ui.Res("Text2Brush"), VerticalAlignment = VerticalAlignment.Center }, _autoStart);
            auto.Margin = new Thickness(0, 12, 0, 0);
            _pomoDone = new TextBlock { Style = Ui.StyleRes("Caption"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };

            PomoPanel.Children.Add(_pomoHead);
            PomoPanel.Children.Add(_dots);
            PomoPanel.Children.Add(_pomoNext);
            PomoPanel.Children.Add(Row(reset, _pomoPlay, skip));
            PomoPanel.Children.Add(Grid2(focus, brk, lng, cyc));
            PomoPanel.Children.Add(auto);
            PomoPanel.Children.Add(_pomoDone);
        }

        void BuildCountdown()
        {
            var presets = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
            foreach (var m in new[] { 1, 3, 5, 10, 15, 25, 45, 60 })
            {
                var b = new Button { Style = Ui.StyleRes("Pill"), Content = m < 60 ? $"{m}m" : "1h", Margin = new Thickness(3), Padding = new Thickness(11, 5, 11, 5) };
                int mm = m;
                b.Click += (s, e) => { _cdMinutes.Set(mm, false); T.CdSet(mm); };
                presets.Children.Add(b);
            }
            _cdMinutes = new Stepper(v => v >= 60 ? $"{v / 60}h {v % 60:00}m" : $"{v} min", S.CountdownMin, 1, 600, 1) { Width = 200 };
            _cdMinutes.Changed += v => T.CdSet(v);
            var reset = Round(Glyphs.Refresh, "Reset");
            reset.Click += (s, e) => T.CdReset();
            _cdPlay = Round(Glyphs.Play, "Start / pause", true);
            _cdPlay.Click += (s, e) => T.CdToggle();
            var plus = Round("+1", "Add a minute");
            plus.FontFamily = (FontFamily)FindResource("UiFont");
            plus.FontSize = 12.5;
            plus.Click += (s, e) => { _cdMinutes.Set(_cdMinutes.Value + 1, false); T.CdSet(_cdMinutes.Value); };

            CdPanel.Children.Add(new TextBlock { Text = "Presets", Style = Ui.StyleRes("Caption"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
            CdPanel.Children.Add(presets);
            CdPanel.Children.Add(Row(_cdMinutes));
            var controls = Row(reset, _cdPlay, plus);
            controls.Margin = new Thickness(0, 14, 0, 0);
            CdPanel.Children.Add(controls);
        }

        void BuildStopwatch()
        {
            var reset = Round(Glyphs.Refresh, "Reset");
            reset.Click += (s, e) => T.SwReset();
            _swPlay = Round(Glyphs.Play, "Start / pause", true);
            _swPlay.Click += (s, e) => T.SwToggle();
            _swLap = Round(Glyphs.Flag, "Lap");
            _swLap.Click += (s, e) => T.SwLap();
            var controls = Row(reset, _swPlay, _swLap);
            controls.Margin = new Thickness(0, 10, 0, 12);
            controls.VerticalAlignment = VerticalAlignment.Top;
            _laps = new StackPanel();
            var scroll = new ScrollViewer { Content = _laps, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 80, 0, 0) };
            SwPanel.Children.Add(controls);
            SwPanel.Children.Add(scroll);
        }

        void BuildWater()
        {
            _waterBig = new TextBlock { FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 26, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
            _drops = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 10), MaxWidth = 280 };
            var minus = Round(Glyphs.Remove, "Remove a glass");
            minus.Click += (s, e) => T.WaterAdd(-1);
            var plus = Round(Glyphs.Add, "Drank a glass", true);
            plus.Click += (s, e) => T.WaterAdd(1);
            var goal = new Stepper(v => $"Goal {v} glasses", S.WaterGoal, 1, 20, 1) { Width = 200 };
            goal.Changed += v => { S.WaterGoal = v; T.WaterSettingsChanged(); };

            var remind = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            var seg = new Border { Background = Ui.Res("CardBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(2), Child = remind, HorizontalAlignment = HorizontalAlignment.Center };
            foreach (var m in new[] { 0, 30, 60, 90 })
            {
                var rb = new RadioButton { Style = Ui.StyleRes("Seg"), Content = m == 0 ? "Off" : $"{m}m", GroupName = "water-remind", IsChecked = S.WaterReminderMin == m };
                int mm = m;
                rb.Checked += (s, e) => { S.WaterReminderMin = mm; T.WaterSettingsChanged(); };
                remind.Children.Add(rb);
            }

            WaterPanel.Children.Add(_waterBig);
            WaterPanel.Children.Add(_drops);
            WaterPanel.Children.Add(Row(minus, plus));
            var g = Row(goal);
            g.Margin = new Thickness(0, 12, 0, 8);
            WaterPanel.Children.Add(g);
            WaterPanel.Children.Add(new TextBlock { Text = "Remind me every", Style = Ui.StyleRes("Caption"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
            WaterPanel.Children.Add(seg);
        }

        // ---------- alarms ----------
        StackPanel _alarmList = null!;
        Border _alarmEditor = null!;
        int _alarmListCount = -1;
        static readonly string[] DayLetters = { "S", "M", "T", "W", "T", "F", "S" };

        void BuildAlarms()
        {
            // editor: hour / minute steppers, repeat chips, label
            int hour = 7, minute = 0, days = 0;
            var h = new Stepper(v => $"{v:00} h", hour, 0, 23, 1) { Width = 108 };
            h.Changed += v => hour = v;
            var m = new Stepper(v => $"{v:00} m", minute, 0, 55, 5) { Width = 108 };
            m.Changed += v => minute = v;
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 4) };
            for (int i = 0; i < 7; i++)
            {
                int bit = 1 << i;
                var t = new ToggleButton { Style = Ui.StyleRes("Pill"), Content = DayLetters[i], Width = 26, Height = 26, Padding = new Thickness(0), Margin = new Thickness(1, 0, 1, 0), FontSize = 11, ToolTip = ((DayOfWeek)i).ToString() };
                t.Click += (s, e) =>
                {
                    days ^= bit;
                    if ((days & bit) != 0) t.SetResourceReference(Control.BackgroundProperty, "AccentBrush"); else t.ClearValue(Control.BackgroundProperty);
                };
                chips.Children.Add(t);
            }
            var label = new TextBox { Style = Ui.StyleRes("Field"), Text = "Alarm", Margin = new Thickness(0, 4, 0, 6) };
            var add = new Button { Style = Ui.StyleRes("Pill"), Content = "+ Add alarm", HorizontalAlignment = HorizontalAlignment.Left };
            add.Click += (s, e) =>
            {
                S.Alarms.Add(new AlarmItem { Hour = hour, Minute = minute, Days = days, Label = string.IsNullOrWhiteSpace(label.Text) ? "Alarm" : label.Text.Trim(), Enabled = true });
                S.Alarms.Sort((a, b) => (a.Hour * 60 + a.Minute).CompareTo(b.Hour * 60 + b.Minute));
                SettingsStore.Save();
                _alarmListCount = -1;
                Refresh();
            };
            var ed = new StackPanel();
            ed.Children.Add(Row(h, m));
            var rep = new DockPanel { Margin = new Thickness(4, 2, 0, 0) };
            rep.Children.Add(new TextBlock { Text = "Repeat", Style = Ui.StyleRes("Caption"), VerticalAlignment = VerticalAlignment.Center, Width = 50 });
            rep.Children.Add(chips);
            ed.Children.Add(rep);
            ed.Children.Add(label);
            ed.Children.Add(add);
            _alarmEditor = new Border { Child = ed, Padding = new Thickness(0, 8, 0, 0) };
            DockPanel.SetDock(_alarmEditor, Dock.Bottom);
            AlarmPanel.Children.Add(_alarmEditor);

            _alarmList = new StackPanel();
            AlarmPanel.Children.Add(new ScrollViewer { Content = _alarmList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        }

        static string DaysText(int d) => d switch
        {
            0 => "Once",
            0x7F => "Every day",
            0x3E => "Weekdays",
            0x41 => "Weekends",
            _ => string.Join(" ", Enumerable.Range(0, 7).Where(i => (d & (1 << i)) != 0).Select(i => ((DayOfWeek)i).ToString()[..3]))
        };

        void RebuildAlarmList()
        {
            _alarmList.Children.Clear();
            var ringing = AlarmService.Ringing;
            if (ringing != null)
            {
                var bar = Row(new TextBlock { Text = "⏰  " + ringing.Label, FontFamily = (FontFamily)FindResource("EmojiFont"), FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
                var snooze = new Button { Style = Ui.StyleRes("Pill"), Content = "Snooze 9m" };
                snooze.Click += (s, e) => AlarmService.Snooze();
                var stop = new Button { Style = Ui.StyleRes("Pill"), Content = "Stop", Foreground = Ui.Res("RedBrush"), Margin = new Thickness(6, 0, 0, 0) };
                stop.Click += (s, e) => AlarmService.Stop();
                bar.Children.Add(snooze); bar.Children.Add(stop);
                bar.HorizontalAlignment = HorizontalAlignment.Left;
                bar.Margin = new Thickness(0, 0, 0, 6);
                _alarmList.Children.Add(bar);
            }
            if (S.Alarms.Count == 0)
                _alarmList.Children.Add(new TextBlock { Text = "No alarms yet. Alarms ring while Crest is running.", Style = Ui.StyleRes("Caption"), TextWrapping = TextWrapping.Wrap });
            foreach (var a in S.Alarms.ToList())
            {
                var item = a;
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var sw = new CheckBox { Style = Ui.StyleRes("Switch"), IsChecked = a.Enabled, VerticalAlignment = VerticalAlignment.Center };
                sw.Click += (s, e) => { item.Enabled = sw.IsChecked == true; item.LastFired = ""; SettingsStore.Save(); Refresh(); };
                var del = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Close, Width = 22, Height = 22, FontSize = 9, ToolTip = "Delete", Margin = new Thickness(4, 0, 0, 0) };
                del.Click += (s, e) => { S.Alarms.Remove(item); SettingsStore.Save(); _alarmListCount = -1; Refresh(); };
                DockPanel.SetDock(del, Dock.Right); DockPanel.SetDock(sw, Dock.Right);
                row.Children.Add(del); row.Children.Add(sw);
                var txt = new StackPanel { Opacity = a.Enabled ? 1 : 0.5 };
                txt.Children.Add(new TextBlock { Text = $"{a.Hour:00}:{a.Minute:00}", FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 20, FontWeight = FontWeights.SemiBold, Typography = { NumeralAlignment = FontNumeralAlignment.Tabular } });
                txt.Children.Add(new TextBlock { Text = $"{a.Label} · {DaysText(a.Days)}", FontSize = 11, Foreground = Ui.Res("Text2Brush"), TextTrimming = TextTrimming.CharacterEllipsis });
                row.Children.Add(txt);
                _alarmList.Children.Add(row);
            }
            _alarmListCount = S.Alarms.Count;
        }

        // ---------- refresh ----------
        void Refresh()
        {
            // sidebar subtitles
            PomoSub.Text = $"{Ui.ClockCeil(T.PomoLeft)} · {T.PhaseName(T.Phase)} {T.Cycle} of {Math.Max(1, S.Cycles)}" + (T.PomoRunning ? "  ●" : "");
            CdSub.Text = Ui.ClockCeil(T.CdLeft) + (T.CdRunning ? "  ●" : "");
            SwSub.Text = Ui.Clock(T.SwElapsed) + (T.SwRunning ? "  ●" : "");
            WaterSub.Text = $"{T.WaterCount} / {S.WaterGoal}";
            var next = AlarmService.NextRing();
            AlarmSub.Text = AlarmService.Ringing != null ? "Ringing  ●" : next is DateTime nx ? (nx.Date == DateTime.Today ? "Next " : nx.Date == DateTime.Today.AddDays(1) ? "Tomorrow " : nx.ToString("ddd ")) + nx.ToString("HH:mm") : "None set";

            if (AlarmMode.IsChecked == true)
            {
                if (_alarmListCount != S.Alarms.Count) RebuildAlarmList();
                if (AlarmService.Ringing is AlarmItem r) { Ring.Progress = 1; BigTime.Text = $"{r.Hour:00}:{r.Minute:00}"; BigLabel.Text = r.Label; }
                else if (next is DateTime n)
                {
                    var left = n - DateTime.Now;
                    Ring.Progress = Math.Clamp(1 - left.TotalHours / 24, 0, 1);
                    BigTime.Text = n.ToString("HH:mm");
                    BigLabel.Text = left.TotalHours >= 1 ? $"in {(int)left.TotalHours}h {left.Minutes}m" : $"in {Math.Max(1, (int)Math.Ceiling(left.TotalMinutes))} min";
                }
                else { Ring.Progress = 0; BigTime.Text = "--:--"; BigLabel.Text = "No alarm"; }
                return;
            }

            if (PomoMode.IsChecked == true)
            {
                Ring.Progress = T.PomoProgress;
                BigTime.Text = Ui.ClockCeil(T.PomoLeft);
                BigLabel.Text = T.PhaseName(T.Phase);
                _pomoHead.Text = $"{T.PhaseName(T.Phase)} {T.Cycle} of {Math.Max(1, S.Cycles)}";
                _pomoNext.Text = $"{T.PhaseName(T.NextPhase)} {Ui.Clock(T.PhaseLength(T.NextPhase))} next";
                _pomoPlay.Content = T.PomoRunning ? Glyphs.Pause : Glyphs.Play;
                int n = Math.Max(1, S.Cycles);
                if (_dots.Children.Count != n)
                {
                    _dots.Children.Clear();
                    for (int i = 0; i < n; i++) _dots.Children.Add(new Ellipse { Width = 6, Height = 6, Margin = new Thickness(3, 0, 3, 0) });
                }
                for (int i = 0; i < n; i++)
                {
                    var e = (Ellipse)_dots.Children[i];
                    if (i < T.Cycle - 1 || (i == T.Cycle - 1 && T.Phase != PomoPhase.Focus)) e.SetResourceReference(Shape.FillProperty, "AccentBrush");
                    else if (i == T.Cycle - 1) { e.SetResourceReference(Shape.FillProperty, "AccentBrush"); e.Opacity = 0.55; continue; }
                    else e.Fill = Ui.Res("HoverBrush");
                    e.Opacity = 1;
                }
                int done = T.CompletedToday;
                _pomoDone.Text = done == 0 ? "No focus sessions completed today" : done == 1 ? "1 focus session completed today" : $"{done} focus sessions completed today";
            }
            else if (CdMode.IsChecked == true)
            {
                Ring.Progress = T.CdProgress;
                BigTime.Text = Ui.ClockCeil(T.CdLeft);
                BigLabel.Text = T.CdRunning ? "Running" : T.CdLeft <= TimeSpan.Zero ? "Done" : "Timer";
                _cdPlay.Content = T.CdRunning ? Glyphs.Pause : Glyphs.Play;
            }
            else if (SwMode.IsChecked == true)
            {
                var el = T.SwElapsed;
                Ring.Progress = (el.TotalSeconds % 60) / 60.0;
                BigTime.Text = el.TotalHours >= 1 ? Ui.Clock(el) : $"{(int)el.TotalMinutes:00}:{el.Seconds:00}.{el.Milliseconds / 100}";
                BigLabel.Text = T.Laps.Count > 0 ? $"Lap {T.Laps.Count + 1}" : "Stopwatch";
                _swPlay.Content = T.SwRunning ? Glyphs.Pause : Glyphs.Play;
                _swLap.IsEnabled = T.SwRunning;
                if (_laps.Children.Count != T.Laps.Count)
                {
                    _laps.Children.Clear();
                    for (int i = 0; i < T.Laps.Count; i++)
                    {
                        var prev = i + 1 < T.Laps.Count ? T.Laps[i + 1] : TimeSpan.Zero;
                        var row = new DockPanel { Margin = new Thickness(4, 2, 8, 2) };
                        var total = new TextBlock { Text = Ui.Clock(T.Laps[i]), Style = Ui.StyleRes("Num"), Foreground = Ui.Res("Text2Brush"), FontSize = 12 };
                        DockPanel.SetDock(total, Dock.Right);
                        row.Children.Add(total);
                        row.Children.Add(new TextBlock { Text = $"Lap {T.Laps.Count - i}    +{Ui.Clock(T.Laps[i] - prev)}", FontSize = 12, Style = Ui.StyleRes("Num") });
                        _laps.Children.Add(row);
                    }
                }
            }
            else
            {
                int c = T.WaterCount, goal = Math.Max(1, S.WaterGoal);
                Ring.Progress = Math.Min(1, c / (double)goal);
                BigTime.Text = $"{c}/{goal}";
                BigLabel.Text = c >= goal ? "Goal reached 🎉" : "glasses today";
                _waterBig.Text = c >= goal ? "Nicely hydrated" : $"{goal - c} to go";
                if (_drops.Children.Count != goal || _drops.Tag as int? != c)
                {
                    _drops.Children.Clear();
                    for (int i = 0; i < goal; i++)
                        _drops.Children.Add(new TextBlock { Text = "💧", FontFamily = (FontFamily)FindResource("EmojiFont"), FontSize = 16, Margin = new Thickness(2), Opacity = i < c ? 1 : 0.22 });
                    _drops.Tag = c;
                }
            }
        }
    }
}
