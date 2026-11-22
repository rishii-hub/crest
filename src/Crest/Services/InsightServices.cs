using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows.Threading;
using Crest.Core;

namespace Crest.Services
{
    /// <summary>
    /// Local screen-time tracking: which app is in the foreground, sampled every 2 s, ignoring idle time.
    /// Only the executable is recorded — never window titles or content. Kept for 8 days.
    /// </summary>
    public sealed class ScreenTimeService
    {
        public static ScreenTimeService Instance { get; } = new ScreenTimeService();
        const int TickSeconds = 2;
        const uint IdleLimitMs = 3 * 60 * 1000;

        DispatcherTimer? _timer;
        Debouncer? _saver;
        Dictionary<string, Dictionary<string, int>> _days = new Dictionary<string, Dictionary<string, int>>();
        readonly Dictionary<string, string> _names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> _paths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        bool _limitNotified;

        public event Action<int>? LimitReached;

        static string Today => DateTime.Now.ToString("yyyy-MM-dd");

        public void Init(Dispatcher ui)
        {
            _days = Storage.Load("screentime.json", () => new Dictionary<string, Dictionary<string, int>>());
            _saver = new Debouncer(TimeSpan.FromSeconds(30), Save);
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(TickSeconds), DispatcherPriority.Background, (s, e) => Sample(), ui);
            _timer.Start();
        }

        void Sample()
        {
            try
            {
                if (Win32.IdleMs() > IdleLimitMs && !MediaService.Instance.IsPlaying) return;
                var fg = Win32.GetForegroundWindow();
                if (fg == IntPtr.Zero) return;
                var path = Win32.WindowProcessPath(fg);
                if (string.IsNullOrEmpty(path)) return;
                var exe = Path.GetFileNameWithoutExtension(path);
                if (exe.Equals("LockApp", StringComparison.OrdinalIgnoreCase)) return;
                if (exe.Equals("explorer", StringComparison.OrdinalIgnoreCase) && Win32.ClassName(fg) is "Progman" or "WorkerW" or "Shell_TrayWnd") return;
                _paths[exe] = path;
                if (!_days.TryGetValue(Today, out var day)) { day = new Dictionary<string, int>(); _days[Today] = day; Trim(); _limitNotified = false; }
                day[exe] = (day.TryGetValue(exe, out var v) ? v : 0) + TickSeconds;
                _saver?.Trigger();

                int limit = SettingsStore.Current.ScreenLimitMin;
                if (limit > 0 && !_limitNotified && TotalSeconds(Today) >= limit * 60)
                {
                    _limitNotified = true;
                    LimitReached?.Invoke(limit);
                }
            }
            catch (Exception ex) { Log.Error("screentime", ex); }
        }

        void Trim()
        {
            foreach (var k in _days.Keys.OrderByDescending(k => k).Skip(8).ToList()) _days.Remove(k);
        }

        void Save() => Storage.Save("screentime.json", _days);

        public int TotalSeconds(string day) => _days.TryGetValue(day, out var d) ? d.Values.Sum() : 0;

        public List<(string Exe, int Seconds)> Apps(string day) =>
            _days.TryGetValue(day, out var d) ? d.OrderByDescending(x => x.Value).Select(x => (x.Key, x.Value)).ToList() : new List<(string, int)>();

        /// <summary>Last 7 days, oldest first: (date, seconds).</summary>
        public List<(DateTime Day, int Seconds)> Week()
        {
            var list = new List<(DateTime, int)>();
            for (int i = 6; i >= 0; i--)
            {
                var d = DateTime.Today.AddDays(-i);
                list.Add((d, TotalSeconds(d.ToString("yyyy-MM-dd"))));
            }
            return list;
        }

        public string FriendlyName(string exe)
        {
            if (_names.TryGetValue(exe, out var n)) return n;
            n = exe;
            try
            {
                if (_paths.TryGetValue(exe, out var p) && File.Exists(p))
                {
                    var desc = FileVersionInfo.GetVersionInfo(p).FileDescription;
                    if (!string.IsNullOrWhiteSpace(desc) && desc.Length <= 40) n = desc.Trim();
                }
            }
            catch { }
            if (n == exe && n.Length > 0) n = char.ToUpper(n[0]) + n.Substring(1);
            _names[exe] = n;
            return n;
        }

        public string? PathOf(string exe) => _paths.TryGetValue(exe, out var p) ? p : null;

        public void Flush() => _saver?.Flush();

        public static string Format(int seconds)
        {
            var t = TimeSpan.FromSeconds(seconds);
            if (t.TotalMinutes < 1) return seconds > 0 ? "<1m" : "0m";
            if (t.TotalHours < 1) return $"{(int)t.TotalMinutes}m";
            return t.Minutes == 0 ? $"{(int)t.TotalHours}h" : $"{(int)t.TotalHours}h {t.Minutes}m";
        }
    }

    public sealed class AiDay
    {
        public long Input, Output, CacheWrite, CacheRead;
        public int Messages, ToolCalls;
        public HashSet<string> Sessions = new HashSet<string>();
        public Dictionary<string, long> ByModel = new Dictionary<string, long>();
        public Dictionary<string, long> ByProject = new Dictionary<string, long>();
        public long[] ByHour = new long[24];
        public long Total => Input + Output + CacheWrite;
    }

    public sealed class AiUsage
    {
        public bool ClaudeFound, CodexFound;
        public AiDay ClaudeToday = new AiDay(), CodexToday = new AiDay();
        public long[] ClaudeWeek = new long[7], CodexWeek = new long[7];
        public DateTime Scanned;
    }

    /// <summary>
    /// Token usage for Claude Code (~/.claude/projects/**/*.jsonl) and Codex CLI (~/.codex/sessions/**/*.jsonl),
    /// read from the local logs these tools already write. Nothing is uploaded; no prices are guessed.
    /// </summary>
    public static class AiUsageService
    {
        static readonly string Home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        public static Task<AiUsage> ScanAsync() => Task.Run(Scan);

        static AiUsage Scan()
        {
            var u = new AiUsage { Scanned = DateTime.Now };
            var since = DateTime.Today.AddDays(-6);
            var claudeDirs = new[] { Path.Combine(Home, ".claude", "projects"), Path.Combine(Home, ".config", "claude", "projects") };
            foreach (var dir in claudeDirs.Where(Directory.Exists))
            {
                u.ClaudeFound = true;
                foreach (var f in SafeFiles(dir, since)) ScanClaude(f, u, since);
            }
            var codexDir = Path.Combine(Home, ".codex", "sessions");
            if (Directory.Exists(codexDir))
            {
                u.CodexFound = true;
                foreach (var f in SafeFiles(codexDir, since)) ScanCodex(f, u, since);
            }
            return u;
        }

        static IEnumerable<string> SafeFiles(string dir, DateTime since)
        {
            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(dir, "*.jsonl", SearchOption.AllDirectories).ToList(); }
            catch { yield break; }
            foreach (var f in files)
            {
                DateTime m;
                try { m = File.GetLastWriteTime(f); } catch { continue; }
                if (m >= since) yield return f;
            }
        }

        static IEnumerable<string> Lines(string path)
        {
            var list = new List<string>();
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                string? line;
                while ((line = sr.ReadLine()) != null) list.Add(line);
            }
            catch { }
            return list;
        }

        static bool TryTime(JsonElement root, out DateTime local)
        {
            local = default;
            if (root.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String &&
                DateTime.TryParse(ts.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var utc))
            {
                local = utc.ToLocalTime();
                return true;
            }
            return false;
        }

        static long Num(JsonElement o, string name) =>
            o.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

        static void ScanClaude(string file, AiUsage u, DateTime since)
        {
            var seen = new HashSet<string>();
            string project = Path.GetFileName(Path.GetDirectoryName(file) ?? "") ?? "";
            foreach (var line in Lines(file))
            {
                if (line.Length < 20 || !line.Contains("\"usage\"")) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("type", out var type) || type.GetString() != "assistant") continue;
                    if (!root.TryGetProperty("message", out var msg) || !msg.TryGetProperty("usage", out var usage)) continue;
                    if (!TryTime(root, out var when) || when < since) continue;
                    // Streaming writes the same message several times — count each message id once.
                    var id = msg.TryGetProperty("id", out var mid) ? mid.GetString() ?? "" : "";
                    var req = root.TryGetProperty("requestId", out var rid) ? rid.GetString() ?? "" : "";
                    if (id.Length > 0 && !seen.Add(id + req)) continue;

                    long input = Num(usage, "input_tokens"), output = Num(usage, "output_tokens");
                    long cw = Num(usage, "cache_creation_input_tokens"), cr = Num(usage, "cache_read_input_tokens");
                    int dayIndex = 6 - (int)(DateTime.Today - when.Date).TotalDays;
                    if (dayIndex >= 0 && dayIndex < 7) u.ClaudeWeek[dayIndex] += input + output + cw;
                    if (when.Date != DateTime.Today) continue;

                    var d = u.ClaudeToday;
                    d.Input += input; d.Output += output; d.CacheWrite += cw; d.CacheRead += cr;
                    d.Messages++;
                    d.ByHour[when.Hour] += input + output + cw;
                    if (root.TryGetProperty("sessionId", out var sid) && sid.ValueKind == JsonValueKind.String) d.Sessions.Add(sid.GetString()!);
                    var model = msg.TryGetProperty("model", out var mm) && mm.ValueKind == JsonValueKind.String ? mm.GetString()! : "unknown";
                    if (model != "<synthetic>") d.ByModel[model] = (d.ByModel.TryGetValue(model, out var x) ? x : 0) + input + output + cw;
                    var proj = root.TryGetProperty("cwd", out var cwd) && cwd.ValueKind == JsonValueKind.String ? Path.GetFileName(cwd.GetString()!.TrimEnd('\\', '/')) : project;
                    d.ByProject[proj] = (d.ByProject.TryGetValue(proj, out var y) ? y : 0) + input + output + cw;
                    if (msg.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                        foreach (var c in content.EnumerateArray())
                            if (c.TryGetProperty("type", out var ct) && ct.GetString() == "tool_use") d.ToolCalls++;
                }
                catch { }
            }
        }

        static void ScanCodex(string file, AiUsage u, DateTime since)
        {
            // Each session file reports cumulative totals; take the difference between consecutive token_count events.
            long lastTotal = 0, lastIn = 0, lastOut = 0;
            string? model = null;
            bool counted = false;
            foreach (var line in Lines(file))
            {
                if (line.Length < 20) continue;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("payload", out var p)) continue;
                    if (root.TryGetProperty("type", out var t) && t.GetString() == "turn_context" && p.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String) model = m.GetString();
                    if (!p.TryGetProperty("type", out var pt) || pt.GetString() != "token_count") continue;
                    if (!p.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object) continue;
                    if (!info.TryGetProperty("total_token_usage", out var tot)) continue;
                    long total = Num(tot, "total_tokens"), inp = Num(tot, "input_tokens") - Num(tot, "cached_input_tokens"), outp = Num(tot, "output_tokens");
                    if (total <= lastTotal) continue;
                    long dt = total - lastTotal, di = Math.Max(0, inp - lastIn), dout = Math.Max(0, outp - lastOut);
                    lastTotal = total; lastIn = inp; lastOut = outp;
                    if (!TryTime(root, out var when) || when < since) continue;
                    int dayIndex = 6 - (int)(DateTime.Today - when.Date).TotalDays;
                    if (dayIndex >= 0 && dayIndex < 7) u.CodexWeek[dayIndex] += dt;
                    if (when.Date != DateTime.Today) continue;
                    var d = u.CodexToday;
                    d.Input += di; d.Output += dout;
                    d.Messages++;
                    d.ByHour[when.Hour] += dt;
                    var key = model ?? "codex";
                    d.ByModel[key] = (d.ByModel.TryGetValue(key, out var x) ? x : 0) + dt;
                    if (!counted) { d.Sessions.Add(file); counted = true; }
                }
                catch { }
            }
        }

        public static string Tokens(long n) =>
            n >= 1_000_000_000 ? $"{n / 1e9:0.##}B" : n >= 1_000_000 ? $"{n / 1e6:0.##}M" : n >= 1000 ? $"{n / 1e3:0.#}K" : n.ToString();
    }

    /// <summary>Exchange rates from the European Central Bank via frankfurter.app (free, no key). Cached for 6 hours.</summary>
    public static class RatesService
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        static readonly Dictionary<string, (DateTime At, string Date, Dictionary<string, double> Rates)> Cache = new Dictionary<string, (DateTime, string, Dictionary<string, double>)>();

        public static async Task<(string Date, Dictionary<string, double> Rates)?> GetAsync(string from)
        {
            if (Cache.TryGetValue(from, out var c) && DateTime.Now - c.At < TimeSpan.FromHours(6)) return (c.Date, c.Rates);
            try
            {
                var json = await Http.GetStringAsync("https://api.frankfurter.app/latest?from=" + Uri.EscapeDataString(from));
                using var doc = JsonDocument.Parse(json);
                var rates = new Dictionary<string, double> { [from] = 1 };
                foreach (var p in doc.RootElement.GetProperty("rates").EnumerateObject()) rates[p.Name] = p.Value.GetDouble();
                var date = doc.RootElement.TryGetProperty("date", out var d) ? d.GetString() ?? "" : "";
                Cache[from] = (DateTime.Now, date, rates);
                return (date, rates);
            }
            catch (Exception ex) { Log.Error("rates", ex); return null; }
        }
    }
}
