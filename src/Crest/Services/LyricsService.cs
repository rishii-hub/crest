using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Crest.Core;

namespace Crest.Services
{
    /// <summary>
    /// Synced lyrics for whatever is playing (Spotify, YouTube in any browser, Apple Music, ...). Windows tells us the
    /// title, artist, length and position; the words come from LRCLIB (lrclib.net), a free, open lyrics database.
    /// Off until you turn it on, because turning it on sends the song's title and artist to lrclib.net. Results are cached.
    /// </summary>
    public static class LyricsService
    {
        public sealed record Line(TimeSpan At, string Text);

        static AppSettings S => SettingsStore.Current;
        static readonly HttpClient Http = CreateHttp();
        static readonly DispatcherTimer Timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        static CancellationTokenSource? _cts;
        static string _key = "";

        /// <summary>off · idle · loading · synced · plain · none · error</summary>
        public static string Status { get; private set; } = "off";
        public static IReadOnlyList<Line> Lines { get; private set; } = Array.Empty<Line>();
        public static string? Plain { get; private set; }
        public static int Index { get; private set; } = -1;
        public static bool HasSynced => Status == "synced" && Lines.Count > 0;
        public static string Current => Index >= 0 && Index < Lines.Count ? Lines[Index].Text : "";
        public static string Next => Index + 1 < Lines.Count ? Lines[Index + 1].Text : "";
        public static string Previous => Index > 0 ? Lines[Index - 1].Text : "";

        /// <summary>Lyrics loaded, cleared or status changed.</summary>
        public static event Action? Changed;
        /// <summary>The current line moved on.</summary>
        public static event Action? LineChanged;

        static HttpClient CreateHttp()
        {
            var h = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            h.DefaultRequestHeaders.UserAgent.ParseAdd("Crest/" + UpdateService.CurrentText + " (https://github.com/rishii-hub/crest)");
            return h;
        }

        static string CacheDir => Path.Combine(Storage.Dir, "lyrics");

        public static void Init()
        {
            Timer.Tick += (s, e) => Advance();
            MediaService.Instance.Changed += Sync;
            SettingsStore.Changed += Sync;
            Sync();
        }

        static void Set(string status, IReadOnlyList<Line>? lines = null, string? plain = null)
        {
            Status = status;
            Lines = lines ?? Array.Empty<Line>();
            Plain = plain;
            Index = -1;
            Changed?.Invoke();
            Advance();
        }

        static bool _demoMode;

        /// <summary>Demo video: timed, made-up lines that follow the demo song's position.</summary>
        internal static void DemoSong(IReadOnlyList<Line> lines)
        {
            _demoMode = true;
            _cts?.Cancel();
            Status = "synced"; Lines = lines; Plain = null; Index = -1;
            Changed?.Invoke();
            Sync();
        }

        static void Sync()
        {
            var md = MediaService.Instance;
            if (!S.Lyrics) { _key = ""; if (Status != "off") Set("off"); Timer.Stop(); return; }
            if (_demoMode)
            {
                if (md.IsPlaying && HasSynced) { if (!Timer.IsEnabled) Timer.Start(); } else Timer.Stop();
                Advance();
                return;
            }
            if (!md.HasSession || string.IsNullOrWhiteSpace(md.Title)) { _key = ""; if (Status != "idle") Set("idle"); Timer.Stop(); return; }
            var key = md.Artist + "\u0001" + md.Title;
            if (key != _key)
            {
                _key = key;
                Load(md.Title, md.Artist, md.Duration);
            }
            if (md.IsPlaying && HasSynced) { if (!Timer.IsEnabled) Timer.Start(); }
            else Timer.Stop();
            Advance();
        }

        static void Advance()
        {
            int idx = -1;
            if (HasSynced)
            {
                // a quarter second early reads better: the line is up as it's sung
                var pos = MediaService.Instance.Position + TimeSpan.FromMilliseconds(250);
                int lo = 0, hi = Lines.Count - 1;
                while (lo <= hi)
                {
                    int mid = (lo + hi) / 2;
                    if (Lines[mid].At <= pos) { idx = mid; lo = mid + 1; } else hi = mid - 1;
                }
            }
            if (idx != Index) { Index = idx; LineChanged?.Invoke(); }
        }

        /// <summary>Tour: show made-up lines in the lyrics widget without a media session.</summary>
        internal static void Demo(IReadOnlyList<Line> lines, int index)
        {
            Status = "synced"; Lines = lines; Plain = null; Index = index;
            Changed?.Invoke(); LineChanged?.Invoke();
        }

        /// <summary>Tour / diagnostics: look a song up and return how many synced lines came back.</summary>
        internal static async Task<int> Probe(string title, string artist, TimeSpan duration)
        {
            var (synced, _) = await Find(title, artist, duration, CancellationToken.None);
            return synced == null ? 0 : Parse(synced).Count;
        }

        // ---------------- lookup ----------------
        static async void Load(string title, string artist, TimeSpan duration)
        {
            _cts?.Cancel();
            var cts = _cts = new CancellationTokenSource();
            Set("loading");
            try
            {
                var (synced, plain) = await Find(title, artist, duration, cts.Token);
                if (cts.IsCancellationRequested || _demoMode) return;
                if (synced != null && Parse(synced) is { Count: > 0 } lines) Set("synced", lines, plain);
                else if (!string.IsNullOrWhiteSpace(plain)) Set("plain", null, plain);
                else Set("none");
                Sync();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (!cts.IsCancellationRequested) { Log.Write("lyrics: " + ex.Message); Set("error"); } }
        }

        sealed class Cached { public string? Synced { get; set; } public string? Plain { get; set; } public DateTime At { get; set; } }

        static async Task<(string? Synced, string? Plain)> Find(string title, string artist, TimeSpan duration, CancellationToken ct)
        {
            var (t, a) = Clean(title, artist);
            string file = Path.Combine(CacheDir, Hash(a + "|" + t) + ".json");
            try
            {
                if (File.Exists(file))
                {
                    var c = JsonSerializer.Deserialize<Cached>(File.ReadAllText(file));
                    // a miss is retried after three days, in case someone has added the song since
                    if (c != null && (c.Synced != null || c.Plain != null || DateTime.UtcNow - c.At < TimeSpan.FromDays(3))) return (c.Synced, c.Plain);
                }
            }
            catch { }

            (string?, string?) result = (null, null);
            if (duration > TimeSpan.FromSeconds(10))
                result = await Get($"https://lrclib.net/api/get?track_name={Uri.EscapeDataString(t)}&artist_name={Uri.EscapeDataString(a)}&duration={(int)Math.Round(duration.TotalSeconds)}", ct);
            if (result.Item1 == null)
            {
                var best = await Search($"https://lrclib.net/api/search?track_name={Uri.EscapeDataString(t)}&artist_name={Uri.EscapeDataString(a)}", duration, ct)
                        ?? await Search($"https://lrclib.net/api/search?q={Uri.EscapeDataString(a + " " + t)}", duration, ct);
                if (best != null) result = best.Value;
            }
            try
            {
                Directory.CreateDirectory(CacheDir);
                File.WriteAllText(file, JsonSerializer.Serialize(new Cached { Synced = result.Item1, Plain = result.Item2, At = DateTime.UtcNow }));
            }
            catch { }
            return result;
        }

        static async Task<(string?, string?)> Get(string url, CancellationToken ct)
        {
            using var res = await Http.GetAsync(url, ct);
            if (res.StatusCode == HttpStatusCode.NotFound) return (null, null);
            res.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            return (Str(doc.RootElement, "syncedLyrics"), Str(doc.RootElement, "plainLyrics"));
        }

        static async Task<(string?, string?)?> Search(string url, TimeSpan duration, CancellationToken ct)
        {
            using var res = await Http.GetAsync(url, ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            (string?, string?)? best = null;
            double bestScore = double.MaxValue;
            foreach (var e in doc.RootElement.EnumerateArray().Take(20))
            {
                var synced = Str(e, "syncedLyrics");
                var plain = Str(e, "plainLyrics");
                if (synced == null && plain == null) continue;
                double d = e.TryGetProperty("duration", out var dd) && dd.ValueKind == JsonValueKind.Number ? dd.GetDouble() : 0;
                double score = (synced == null ? 1000 : 0) + (duration > TimeSpan.Zero && d > 0 ? Math.Abs(d - duration.TotalSeconds) : 30);
                if (duration > TimeSpan.Zero && d > 0 && Math.Abs(d - duration.TotalSeconds) > 15) score += 500; // probably a different version
                if (score < bestScore) { bestScore = score; best = (synced, plain); }
            }
            return best;
        }

        static string? Str(JsonElement e, string name) =>
            e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;

        static readonly Regex Noise = new Regex(@"\s*[\(\[\{][^\)\]\}]*\b(official|video|audio|lyrics?|visuali[sz]er|hd|hq|4k|mv|m/v|remaster(ed)?|explicit|full song|music video|from)\b[^\)\]\}]*[\)\]\}]", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        static readonly Regex Feat = new Regex(@"\s*[\(\[]?\b(feat\.?|ft\.?|featuring)\s[^\)\]]*[\)\]]?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>YouTube-style titles ("Artist - Song (Official Video)") and channel names ("Artist - Topic", "ArtistVEVO") cleaned up.</summary>
        public static (string Title, string Artist) Clean(string title, string artist)
        {
            string t = Noise.Replace(title, "");
            t = Feat.Replace(t, "");
            t = Regex.Replace(t, @"\s*\|.*$", "");          // "Song | Movie name | Singer"
            string a = Regex.Replace(artist ?? "", @"\s*-\s*Topic$|VEVO$|\s*Official$", "", RegexOptions.IgnoreCase).Trim();
            int dash = t.IndexOf(" - ", StringComparison.Ordinal);
            if (dash > 0)
            {
                string left = t[..dash].Trim(), right = t[(dash + 3)..].Trim();
                // "Artist - Song" uploaded by a channel that isn't the artist
                if (a.Length == 0 || !right.Contains(a, StringComparison.OrdinalIgnoreCase)) { a = left; t = right; }
                else t = left;
            }
            return (t.Trim(' ', '-', '"', '“', '”'), a);
        }

        static readonly Regex Stamp = new Regex(@"\[(\d{1,3}):(\d{1,2})(?:[.:](\d{1,3}))?\]", RegexOptions.Compiled);

        public static List<Line> Parse(string lrc)
        {
            var lines = new List<Line>();
            foreach (var raw in lrc.Split('\n'))
            {
                var ms = Stamp.Matches(raw);
                if (ms.Count == 0) continue;
                string text = raw[(ms[^1].Index + ms[^1].Length)..].Trim();
                foreach (Match m in ms)
                {
                    int min = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), sec = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    string f = m.Groups[3].Success ? m.Groups[3].Value : "0";
                    int milli = f.Length == 1 ? int.Parse(f) * 100 : f.Length == 2 ? int.Parse(f) * 10 : int.Parse(f);
                    lines.Add(new Line(new TimeSpan(0, 0, min, sec, milli), text.Length == 0 ? "♪" : text));
                }
            }
            lines.Sort((x, y) => x.At.CompareTo(y.At));
            return lines;
        }

        static string Hash(string s) => Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(s.ToLowerInvariant())))[..16];
    }
}
