using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Core;
using NAudio.CoreAudioApi;
using NAudio.Dsp;
using NAudio.Wave;

namespace Crest.Services
{
    // =====================================================================================
    //  Real-time audio spectrum (WASAPI loopback + FFT). Runs only while something shows it.
    // =====================================================================================
    public static class AudioViz
    {
        public const int Bands = 28;
        const int FftSize = 1024, FftM = 10;
        static WasapiLoopbackCapture? _cap;
        static int _users;
        static readonly float[] _buffer = new float[FftSize];
        static int _filled;
        static readonly Complex[] _fft = new Complex[FftSize];
        static readonly float[] _bands = new float[Bands];
        static readonly object Gate = new object();
        static int _sampleRate = 48000;

        /// <summary>Demo video: animated, made-up levels instead of real audio.</summary>
        public static bool DemoMode;

        public static void Acquire()
        {
            if (_users++ > 0 || DemoMode) return;
            try
            {
                _cap = new WasapiLoopbackCapture();
                _sampleRate = _cap.WaveFormat.SampleRate;
                _cap.DataAvailable += OnData;
                _cap.StartRecording();
            }
            catch (Exception ex) { Log.Write("audio viz unavailable: " + ex.Message); _cap = null; }
        }

        public static void Release()
        {
            if (_users == 0 || --_users > 0) return;
            try { _cap?.StopRecording(); _cap?.Dispose(); } catch { }
            _cap = null;
            lock (Gate) Array.Clear(_bands);
        }

        /// <summary>Copy of the current band levels (0..1).</summary>
        public static float[] Snapshot()
        {
            if (DemoMode)
            {
                double t = Environment.TickCount64 / 1000.0;
                double beat = Math.Pow(Math.Max(0, Math.Sin(t * Math.PI * 2 * 1.9)), 6); // ~114 bpm thump
                var b = new float[Bands];
                for (int i = 0; i < Bands; i++)
                {
                    double tilt = 1 - i / (double)Bands * 0.55;
                    double wob = 0.5 + 0.5 * Math.Sin(t * (2.3 + i * 0.37) + i * 1.7) * Math.Sin(t * (1.1 + i * 0.13));
                    b[i] = (float)Math.Clamp((0.25 + 0.5 * wob) * tilt + beat * 0.35 * tilt, 0, 1);
                }
                return b;
            }
            lock (Gate) return (float[])_bands.Clone();
        }

        static void OnData(object? sender, WaveInEventArgs e)
        {
            var cap = _cap;
            if (cap == null) return;
            var wf = cap.WaveFormat;
            int ch = Math.Max(1, wf.Channels);
            bool isFloat = wf.Encoding == WaveFormatEncoding.IeeeFloat || (wf.Encoding == WaveFormatEncoding.Extensible && wf.BitsPerSample == 32);
            int bytesPerSample = wf.BitsPerSample / 8;
            int frame = bytesPerSample * ch;
            for (int i = 0; i + frame <= e.BytesRecorded; i += frame)
            {
                float sum = 0;
                for (int c = 0; c < ch; c++)
                {
                    int o = i + c * bytesPerSample;
                    sum += isFloat ? BitConverter.ToSingle(e.Buffer, o) : bytesPerSample == 2 ? BitConverter.ToInt16(e.Buffer, o) / 32768f : 0;
                }
                _buffer[_filled++] = sum / ch;
                if (_filled == FftSize) { Analyse(); _filled = 0; }
            }
        }

        static void Analyse()
        {
            for (int i = 0; i < FftSize; i++)
            {
                float w = (float)(0.54 - 0.46 * Math.Cos(2 * Math.PI * i / (FftSize - 1)));
                _fft[i].X = _buffer[i] * w;
                _fft[i].Y = 0;
            }
            FastFourierTransform.FFT(true, FftM, _fft);
            double binHz = _sampleRate / (double)FftSize;
            double fMin = 40, fMax = Math.Min(16000, _sampleRate / 2.0);
            var next = new float[Bands];
            for (int b = 0; b < Bands; b++)
            {
                double f0 = fMin * Math.Pow(fMax / fMin, b / (double)Bands), f1 = fMin * Math.Pow(fMax / fMin, (b + 1) / (double)Bands);
                int i0 = Math.Max(1, (int)(f0 / binHz)), i1 = Math.Max(i0 + 1, (int)(f1 / binHz));
                double peak = 0;
                for (int i = i0; i < i1 && i < FftSize / 2; i++)
                {
                    double m = Math.Sqrt(_fft[i].X * _fft[i].X + _fft[i].Y * _fft[i].Y);
                    if (m > peak) peak = m;
                }
                double db = 20 * Math.Log10(peak + 1e-9);
                next[b] = (float)Math.Clamp((db + 72) / 66, 0, 1);
            }
            lock (Gate)
                for (int b = 0; b < Bands; b++)
                    _bands[b] = next[b] > _bands[b] ? next[b] : _bands[b] * 0.82f + next[b] * 0.18f;
        }
    }

    // =====================================================================================
    //  Alarms with snooze / stop pills
    // =====================================================================================
    public static class AlarmService
    {
        static DispatcherTimer? _timer;
        static System.Media.SoundPlayer? _player;
        static DateTime _ringStart, _snoozeUntil = DateTime.MaxValue;
        static AlarmItem? _snoozed;
        public static AlarmItem? Ringing { get; private set; }
        public static event Action? Changed;
        static AppSettings S => SettingsStore.Current;

        public static void Init(Dispatcher ui)
        {
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => Tick(), ui);
            _timer.Start();
            ActivityHub.Register(Activities);
        }

        static IEnumerable<LiveActivity> Activities()
        {
            var r = Ringing;
            if (r == null) yield break;
            yield return new LiveActivity
            {
                Id = "alarm", Priority = 100, Glyph = "⏰", Emoji = true, Pulse = true, Title = string.IsNullOrWhiteSpace(r.Label) ? "Alarm" : r.Label,
                Value = $"{r.Hour:00}:{r.Minute:00}", ValueBrush = Ui.Res("OrangeBrush"), Tab = "Timers", Signature = r.Id + _ringStart.Ticks,
                Actions = { ("snooze", "Snooze"), ("stop", "Stop") },
                OnAction = a => { if (a == "snooze") Snooze(); else Stop(); }
            };
        }

        static void Tick()
        {
            var now = DateTime.Now;
            if (Ringing != null && now - _ringStart > TimeSpan.FromMinutes(5)) Stop();
            if (_snoozed != null && now >= _snoozeUntil) { var a = _snoozed; _snoozed = null; _snoozeUntil = DateTime.MaxValue; Ring(a); return; }
            if (Ringing != null || now.Second > 2) return;
            string stamp = now.ToString("yyyy-MM-dd HH:mm");
            foreach (var a in S.Alarms)
            {
                if (!a.Enabled || a.Hour != now.Hour || a.Minute != now.Minute || a.LastFired == stamp) continue;
                if (a.Days != 0 && (a.Days & (1 << (int)now.DayOfWeek)) == 0) continue;
                a.LastFired = stamp;
                if (a.Days == 0) a.Enabled = false;
                SettingsStore.Save();
                Ring(a);
                break;
            }
        }

        public static void Ring(AlarmItem a)
        {
            Ringing = a;
            _ringStart = DateTime.Now;
            try
            {
                var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Media", "Alarm01.wav");
                if (File.Exists(path)) { _player = new System.Media.SoundPlayer(path); _player.PlayLooping(); }
            }
            catch { }
            ActivityHub.Notify();
            Changed?.Invoke();
        }

        public static void Snooze()
        {
            if (Ringing == null) return;
            _snoozed = Ringing;
            _snoozeUntil = DateTime.Now.AddMinutes(9);
            StopSound();
            Ringing = null;
            ActivityHub.Notify();
            Changed?.Invoke();
            NotchWindow.Instance?.ShowToast(new Toast { Kind = "alarm", Glyph = "💤", Emoji = true, Title = "Snoozed", Value = _snoozeUntil.ToString("HH:mm"), Duration = TimeSpan.FromSeconds(2) });
        }

        public static void Stop()
        {
            StopSound();
            Ringing = null;
            _snoozed = null;
            _snoozeUntil = DateTime.MaxValue;
            ActivityHub.Notify();
            Changed?.Invoke();
        }

        static void StopSound() { try { _player?.Stop(); _player?.Dispose(); } catch { } _player = null; }

        public static DateTime? NextRing()
        {
            DateTime? best = null;
            var now = DateTime.Now;
            foreach (var a in S.Alarms.Where(x => x.Enabled))
                for (int d = 0; d < 8; d++)
                {
                    var t = now.Date.AddDays(d).AddHours(a.Hour).AddMinutes(a.Minute);
                    if (t <= now) continue;
                    if (a.Days != 0 && (a.Days & (1 << (int)t.DayOfWeek)) == 0) continue;
                    if (best == null || t < best) best = t;
                    break;
                }
            return best;
        }
    }

    // =====================================================================================
    //  Voice memos (WASAPI microphone → WAV) and screen recording
    // =====================================================================================
    public static class RecorderService
    {
        static WasapiCapture? _cap;
        static WaveFileWriter? _writer;
        static DateTime _start;
        static string? _path;
        public static bool Recording => _cap != null;
        public static float Level { get; private set; }
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), "Crest Memos");
        public static event Action? Changed;

        // Screen recording via Xbox Game Bar (Win+Alt+R) + detection of apps using Windows screen capture
        public static bool GameBarRecording { get; private set; }
        static DateTime _screenStart;
        public static string CaptureApps { get; set; } = "";

        public static void Init(Dispatcher ui)
        {
            ActivityHub.Register(Activities);
            var t = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => { if (Recording || GameBarRecording || CaptureApps.Length > 0) ActivityHub.Notify(); }, ui);
            t.Start();
        }

        static IEnumerable<LiveActivity> Activities()
        {
            if (Recording)
                yield return new LiveActivity
                {
                    Id = "memo", Priority = 90, Glyph = Glyphs.Record, GlyphBrush = Ui.Res("RedBrush"), Pulse = true, Title = "Recording",
                    Value = Ui.Clock(DateTime.Now - _start), ValueBrush = Ui.Res("RedBrush"), Tab = "Tools", Signature = _start.Ticks.ToString(),
                    Actions = { ("stop", "Stop") }, OnAction = a => Stop()
                };
            if (GameBarRecording)
                yield return new LiveActivity
                {
                    Id = "screenrec", Priority = 88, Glyph = Glyphs.Record, GlyphBrush = Ui.Res("RedBrush"), Pulse = true, Title = "Screen recording",
                    Value = Ui.Clock(DateTime.Now - _screenStart), ValueBrush = Ui.Res("RedBrush"), Tab = "Tools", Signature = _screenStart.Ticks.ToString(),
                    Actions = { ("stop", "Stop") }, OnAction = a => ToggleGameBar()
                };
            else if (CaptureApps.Length > 0)
                yield return new LiveActivity
                {
                    Id = "capture", Priority = 85, Glyph = Glyphs.Record, GlyphBrush = Ui.Res("RedBrush"), Title = "Screen capture", Value = CaptureApps,
                    ValueBrush = Ui.Res("Text2Brush"), Tab = "Tools", Signature = CaptureApps
                };
        }

        public static string? Start()
        {
            if (Recording) return null;
            try
            {
                Directory.CreateDirectory(Folder);
                _path = Path.Combine(Folder, $"Memo {DateTime.Now:yyyy-MM-dd HHmmss}.wav");
                var cap = new WasapiCapture();
                var writer = new WaveFileWriter(_path, cap.WaveFormat);
                var fmt = cap.WaveFormat;
                cap.DataAvailable += (s, a) =>
                {
                    try
                    {
                        writer.Write(a.Buffer, 0, a.BytesRecorded);
                        float peak = 0;
                        if (fmt.Encoding == WaveFormatEncoding.IeeeFloat || fmt.BitsPerSample == 32)
                            for (int i = 0; i + 4 <= a.BytesRecorded; i += 4) peak = Math.Max(peak, Math.Abs(BitConverter.ToSingle(a.Buffer, i)));
                        else if (fmt.BitsPerSample == 16)
                            for (int i = 0; i + 2 <= a.BytesRecorded; i += 2) peak = Math.Max(peak, Math.Abs(BitConverter.ToInt16(a.Buffer, i) / 32768f));
                        Level = peak;
                    }
                    catch { }
                };
                cap.RecordingStopped += (s, a) => { try { writer.Dispose(); cap.Dispose(); } catch { } };
                cap.StartRecording();
                _cap = cap;
                _writer = writer;
                _start = DateTime.Now;
                ActivityHub.Notify();
                Changed?.Invoke();
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("memo start", ex);
                _cap = null;
                return "Couldn't start recording — is a microphone connected and allowed for desktop apps?";
            }
        }

        public static void Stop()
        {
            var cap = _cap;
            if (cap == null) return;
            _cap = null;
            try { cap.StopRecording(); } catch { }
            Level = 0;
            var dur = DateTime.Now - _start;
            ActivityHub.Notify();
            Changed?.Invoke();
            NotchWindow.Instance?.ShowToast(new Toast { Kind = "memo", Glyph = Glyphs.Check, GlyphBrush = Ui.Res("GreenBrush"), Title = "Memo saved", Value = Ui.Clock(dur), Duration = TimeSpan.FromSeconds(2.5) });
        }

        public static IEnumerable<FileInfo> Memos()
        {
            try { return new DirectoryInfo(Folder).Exists ? new DirectoryInfo(Folder).GetFiles("*.wav").OrderByDescending(f => f.CreationTime).ToList() : Enumerable.Empty<FileInfo>(); }
            catch { return Enumerable.Empty<FileInfo>(); }
        }

        [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

        /// <summary>Start/stop Xbox Game Bar recording of the active app (Win+Alt+R).</summary>
        public static void ToggleGameBar()
        {
            const byte WIN = 0x5B, ALT = 0x12, R = 0x52;
            const uint UP = 0x2;
            keybd_event(WIN, 0, 0, UIntPtr.Zero);
            keybd_event(ALT, 0, 0, UIntPtr.Zero);
            keybd_event(R, 0, 0, UIntPtr.Zero);
            keybd_event(R, 0, UP, UIntPtr.Zero);
            keybd_event(ALT, 0, UP, UIntPtr.Zero);
            keybd_event(WIN, 0, UP, UIntPtr.Zero);
            GameBarRecording = !GameBarRecording;
            if (GameBarRecording) _screenStart = DateTime.Now;
            ActivityHub.Notify();
            Changed?.Invoke();
        }
    }

    // =====================================================================================
    //  Automations: run your own scripts with live progress in the notch
    // =====================================================================================
    public static class AutomationService
    {
        sealed class Run { public Process P = null!; public DateTime Start; public double? Progress; public string Last = ""; }
        static readonly Dictionary<string, Run> Running = new Dictionary<string, Run>();
        static readonly Regex ProgressRx = new Regex(@"(?:^|##\s*)progress\s*[:=]?\s*(\d{1,3}(?:\.\d+)?)\s*%?|\[(\d+)\s*/\s*(\d+)\]|^(\d{1,3})%$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static Dispatcher _ui = null!;
        public static event Action? Changed;

        public static void Init(Dispatcher ui)
        {
            _ui = ui;
            ActivityHub.Register(Activities);
            var t = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => { if (Running.Count > 0) ActivityHub.Notify(); }, ui);
            t.Start();
        }

        public static bool IsRunning(string id) => Running.ContainsKey(id);

        static IEnumerable<LiveActivity> Activities()
        {
            foreach (var (id, r) in Running.ToList())
            {
                var item = SettingsStore.Current.Automations.FirstOrDefault(a => a.Id == id);
                var aid = id;
                yield return new LiveActivity
                {
                    Id = "auto-" + id, Priority = 70, Glyph = Glyphs.Script, Title = item?.Name ?? "Script",
                    Value = r.Progress.HasValue ? $"{r.Progress:0}%" : Ui.Clock(DateTime.Now - r.Start),
                    Progress = r.Progress / 100.0, Tab = "Tools", Signature = r.Start.Ticks.ToString(),
                    Actions = { ("stop", "Stop") }, OnAction = a => Cancel(aid)
                };
            }
        }

        public static string? Start(AutomationItem item)
        {
            if (Running.ContainsKey(item.Id)) return "Already running.";
            if (!File.Exists(item.Path)) return "Script not found: " + item.Path;
            var ext = Path.GetExtension(item.Path).ToLowerInvariant();
            ProcessStartInfo psi;
            bool capture = true;
            switch (ext)
            {
                case ".ps1":
                    psi = new ProcessStartInfo("powershell.exe", $"-NoProfile -NonInteractive -File \"{item.Path}\" {item.Args}");
                    break;
                case ".bat":
                case ".cmd":
                    psi = new ProcessStartInfo("cmd.exe", $"/c \"\"{item.Path}\" {item.Args}\"");
                    break;
                case ".exe":
                    psi = new ProcessStartInfo(item.Path, item.Args);
                    break;
                default: // .ahk, .py, .vbs, shortcuts… — open with the associated program
                    psi = new ProcessStartInfo(item.Path, item.Args) { UseShellExecute = true };
                    capture = false;
                    break;
            }
            if (capture)
            {
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.WorkingDirectory = Path.GetDirectoryName(item.Path) ?? "";
            }
            try
            {
                var p = new Process { StartInfo = psi, EnableRaisingEvents = true };
                var run = new Run { P = p, Start = DateTime.Now };
                if (capture)
                {
                    p.OutputDataReceived += (s, e) => OnLine(item.Id, e.Data);
                    p.ErrorDataReceived += (s, e) => OnLine(item.Id, e.Data);
                }
                p.Exited += (s, e) => _ui.BeginInvoke(new Action(() => Finished(item, run)));
                p.Start();
                if (capture) { p.BeginOutputReadLine(); p.BeginErrorReadLine(); }
                Running[item.Id] = run;
                ActivityHub.Notify();
                Changed?.Invoke();
                return null;
            }
            catch (Exception ex) { Log.Error("automation", ex); return "Couldn't start: " + ex.Message; }
        }

        static void OnLine(string id, string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            var m = ProgressRx.Match(line.Trim());
            double? pct = null;
            if (m.Success)
            {
                if (m.Groups[1].Success) pct = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
                else if (m.Groups[2].Success && double.TryParse(m.Groups[3].Value, out var tot) && tot > 0) pct = double.Parse(m.Groups[2].Value) / tot * 100;
                else if (m.Groups[4].Success) pct = double.Parse(m.Groups[4].Value);
            }
            _ui.BeginInvoke(new Action(() =>
            {
                if (!Running.TryGetValue(id, out var r)) return;
                r.Last = line.Trim();
                if (pct.HasValue) r.Progress = Math.Clamp(pct.Value, 0, 100);
            }));
        }

        static void Finished(AutomationItem item, Run run)
        {
            Running.Remove(item.Id);
            int code = -1;
            try { code = run.P.ExitCode; } catch { }
            ActivityHub.Notify();
            Changed?.Invoke();
            bool ok = code == 0;
            NotchWindow.Instance?.ShowToast(new Toast
            {
                Kind = "auto", Glyph = ok ? Glyphs.Check : Glyphs.Close, GlyphBrush = ok ? Ui.Res("GreenBrush") : Ui.Res("RedBrush"),
                Title = item.Name, Value = ok ? "Done" : $"Exit {code}", ValueBrush = ok ? Ui.Res("GreenBrush") : Ui.Res("RedBrush"), Duration = TimeSpan.FromSeconds(3)
            });
            try { run.P.Dispose(); } catch { }
        }

        public static void Cancel(string id)
        {
            if (!Running.TryGetValue(id, out var r)) return;
            try { r.P.Kill(true); } catch { }
        }
    }

    // =====================================================================================
    //  Third-party Live Activities over a local named pipe: \\.\pipe\CrestLiveActivity
    //  Newline-delimited JSON. Only the current Windows user can connect.
    // =====================================================================================
    public static class PipeService
    {
        public const string PipeName = "CrestLiveActivity";
        sealed class Ext
        {
            public LiveActivity A = new LiveActivity();
            public StreamWriter? Client;
            public DateTime Started = DateTime.Now;
            public bool Elapsed;
            public DateTime? Expires;
        }
        static readonly Dictionary<string, Ext> Items = new Dictionary<string, Ext>();
        static Dispatcher _ui = null!;
        static CancellationTokenSource? _cts;

        public static void Init(Dispatcher ui)
        {
            _ui = ui;
            ActivityHub.Register(() => Items.Values.Select(Render).ToList());
            var t = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (s, e) => Sweep(), ui);
            t.Start();
            _cts = new CancellationTokenSource();
            Task.Run(() => Listen(_cts.Token));
        }

        public static void Stop() => _cts?.Cancel();

        static LiveActivity Render(Ext e)
        {
            if (e.Elapsed) e.A.Value = Ui.Clock(DateTime.Now - e.Started);
            return e.A;
        }

        static void Sweep()
        {
            bool changed = false;
            foreach (var (id, e) in Items.ToList())
                if (e.Expires.HasValue && DateTime.Now >= e.Expires) { Items.Remove(id); changed = true; }
            if (changed || Items.Values.Any(x => x.Elapsed)) ActivityHub.Notify();
        }

        static async Task Listen(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream server;
                try
                {
                    server = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(ct);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Log.Error("pipe listen", ex); await Task.Delay(2000, ct).ContinueWith(_ => { }); continue; }
                _ = Task.Run(() => Serve(server, ct));
            }
        }

        static async Task Serve(NamedPipeServerStream pipe, CancellationToken ct)
        {
            using (pipe)
            {
                var reader = new StreamReader(pipe, Encoding.UTF8);
                var writer = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true };
                try
                {
                    string? line;
                    while (!ct.IsCancellationRequested && (line = await reader.ReadLineAsync()) != null)
                    {
                        if (line.Length > 16_384) continue;
                        string reply;
                        try { reply = await _ui.InvokeAsync(() => Apply(line, writer)); }
                        catch (Exception ex) { reply = Err(ex.Message); }
                        try { await writer.WriteLineAsync(reply); } catch { break; }
                    }
                }
                catch { }
                await _ui.InvokeAsync(() => { foreach (var e in Items.Values.Where(x => x.Client == writer)) e.Client = null; });
            }
        }

        static string Ok(string id) => JsonSerializer.Serialize(new { ok = true, id });
        static string Err(string msg) => JsonSerializer.Serialize(new { ok = false, error = msg });

        /// <summary>Apply one JSON command. Also used in-process (tour, CLI).</summary>
        public static string Apply(string json, StreamWriter? client = null)
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string op = Str(r, "op") ?? "show";
            string id = Clip(Str(r, "id") ?? "default", 40);
            switch (op)
            {
                case "end":
                    Items.Remove(id);
                    ActivityHub.Notify();
                    return Ok(id);
                case "toast":
                    NotchWindow.Instance?.ShowToast(new Toast
                    {
                        Kind = "ext-" + id, Glyph = Str(r, "glyph") ?? Glyphs.Message, Emoji = IsEmoji(Str(r, "glyph")),
                        GlyphBrush = Color(r), Title = Clip(Str(r, "title") ?? "", 60), Value = Clip(Str(r, "value") ?? "", 30),
                        Duration = TimeSpan.FromSeconds(Math.Clamp(Num(r, "seconds") ?? 3, 1, 30))
                    });
                    return Ok(id);
                case "show":
                case "update":
                    if (!Items.TryGetValue(id, out var e)) { e = new Ext(); Items[id] = e; }
                    var a = e.A;
                    a.Id = "ext-" + id;
                    if (Str(r, "glyph") is string g) { a.Glyph = Clip(g, 4); a.Emoji = IsEmoji(g); }
                    else if (string.IsNullOrEmpty(a.Glyph)) a.Glyph = Glyphs.Apps;
                    if (Str(r, "title") is string t) a.Title = Clip(t, 60);
                    if (Str(r, "value") is string v) { a.Value = Clip(v, 30); e.Elapsed = false; }
                    if (r.TryGetProperty("elapsed", out var el) && el.ValueKind == JsonValueKind.True) { e.Elapsed = true; e.Started = DateTime.Now; }
                    if (r.TryGetProperty("progress", out var p))
                        a.Progress = p.ValueKind == JsonValueKind.Number ? Math.Clamp(p.GetDouble(), 0, 1) : null;
                    if (Color(r) is Brush br) { a.GlyphBrush = br; a.ValueBrush = br; }
                    a.Priority = (int)Math.Clamp(Num(r, "priority") ?? (a.Priority == 0 ? 50 : a.Priority), 0, 95);
                    a.Signature = (Str(r, "signature") ?? e.Started.Ticks.ToString());
                    a.Tab = Str(r, "tab");
                    if (Num(r, "ttl") is double ttl) e.Expires = DateTime.Now.AddSeconds(Math.Clamp(ttl, 1, 86400));
                    if (client != null) e.Client = client;
                    if (r.TryGetProperty("actions", out var acts) && acts.ValueKind == JsonValueKind.Array)
                    {
                        a.Actions.Clear();
                        foreach (var x in acts.EnumerateArray().Take(2))
                            a.Actions.Add((Clip(Str(x, "id") ?? "action", 30), Clip(Str(x, "label") ?? "OK", 16)));
                        var ext = e;
                        var itemId = id;
                        bool dismiss = Bool(r, "dismissOnAction");
                        a.OnAction = action =>
                        {
                            var w = ext.Client;
                            if (w != null) _ = Task.Run(async () => { try { await w.WriteLineAsync(JsonSerializer.Serialize(new { @event = "action", id = itemId, action })); } catch { } });
                            if (dismiss) { Items.Remove(itemId); ActivityHub.Notify(); }
                        };
                    }
                    ActivityHub.Notify();
                    return Ok(id);
                default:
                    return Err("unknown op: " + op);
            }
        }

        static string? Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        static double? Num(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
        static bool Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
        static string Clip(string s, int max) => s.Length > max ? s.Substring(0, max) : s;
        static Brush? Color(JsonElement r) => Str(r, "color") is string c && c.StartsWith("#") ? Ui.Brush(c) : null;
        static bool IsEmoji(string? g) => !string.IsNullOrEmpty(g) && (char.IsSurrogate(g[0]) || g[0] > 0x2000 && g[0] < 0xE000);

        /// <summary>`Crest.exe --activity "{json}"` → forward to the running instance.</summary>
        public static bool SendFromCli(string json)
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                client.Connect(2000);
                using var w = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
                using var rd = new StreamReader(client);
                w.WriteLine(json.Replace("\r", "").Replace("\n", " "));
                var reply = rd.ReadLine();
                Console.WriteLine(reply);
                return true;
            }
            catch { return false; }
        }
    }
}
