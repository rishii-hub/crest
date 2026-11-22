using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Crest.Core;

namespace Crest.Services
{
    public sealed class SpotItem
    {
        public string Kind = "app";       // app | uwp | recent | setting | math | unit | web | assistant
        public string Name = "";
        public string Sub = "";
        public string Target = "";        // path / uri / text to copy
        public object? Entry;             // UWP AppListEntry
        public int Score;
        public string Glyph = Glyphs.Apps;
    }

    /// <summary>Spotlight-style index: Start-menu apps, Store apps, recent files, Settings pages, math, units, web.</summary>
    public static class SpotlightService
    {
        static List<SpotItem>? _apps;
        static Task? _indexing;
        static DateTime _indexedAt;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static readonly (string Name, string Uri)[] SettingsPages =
        {
            ("Display", "ms-settings:display"), ("Sound", "ms-settings:sound"), ("Bluetooth & devices", "ms-settings:bluetooth"),
            ("Wi-Fi", "ms-settings:network-wifi"), ("Network & internet", "ms-settings:network"), ("VPN", "ms-settings:network-vpn"),
            ("Mobile hotspot", "ms-settings:network-mobilehotspot"), ("Battery & power", "ms-settings:powersleep"), ("Battery saver", "ms-settings:batterysaver"),
            ("Installed apps", "ms-settings:appsfeatures"), ("Default apps", "ms-settings:defaultapps"), ("Startup apps", "ms-settings:startupapps"),
            ("Personalization", "ms-settings:personalization"), ("Background", "ms-settings:personalization-background"), ("Colors", "ms-settings:colors"),
            ("Themes", "ms-settings:themes"), ("Taskbar", "ms-settings:taskbar"), ("Notifications", "ms-settings:notifications"),
            ("Do not disturb / Focus", "ms-settings:quiethours"), ("Storage", "ms-settings:storagesense"), ("Windows Update", "ms-settings:windowsupdate"),
            ("Privacy & security", "ms-settings:privacy"), ("Microphone privacy", "ms-settings:privacy-microphone"), ("Camera privacy", "ms-settings:privacy-webcam"),
            ("Location", "ms-settings:privacy-location"), ("Mouse", "ms-settings:mousetouchpad"), ("Touchpad", "ms-settings:devices-touchpad"),
            ("Typing & keyboard", "ms-settings:typing"), ("Printers & scanners", "ms-settings:printers"), ("Date & time", "ms-settings:dateandtime"),
            ("Language & region", "ms-settings:regionlanguage"), ("Your info", "ms-settings:yourinfo"), ("Sign-in options", "ms-settings:signinoptions"),
            ("Night light", "ms-settings:nightlight"), ("About this PC", "ms-settings:about"), ("Clipboard settings", "ms-settings:clipboard"),
            ("Multitasking", "ms-settings:multitasking"), ("Game Bar", "ms-settings:gaming-gamebar"), ("Recovery", "ms-settings:recovery"),
            ("Control Panel", "control.exe"), ("Task Manager", "taskmgr.exe"), ("Device Manager", "devmgmt.msc")
        };

        public static void EnsureIndex()
        {
            if (_indexing != null && DateTime.Now - _indexedAt < TimeSpan.FromMinutes(10)) return;
            _indexedAt = DateTime.Now;
            _indexing = Task.Run(async () =>
            {
                var list = new List<SpotItem>();
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var root in new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
                })
                {
                    if (!Directory.Exists(root)) continue;
                    IEnumerable<string> files;
                    try { files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Where(f => f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase)).ToList(); }
                    catch { continue; }
                    foreach (var f in files)
                    {
                        var name = Path.GetFileNameWithoutExtension(f);
                        var low = name.ToLowerInvariant();
                        if (low.Contains("uninstall") || low.Contains("readme") || low.Contains("help") && low.Length > 12 || !seen.Add(name)) continue;
                        list.Add(new SpotItem { Kind = "app", Name = name, Target = f, Sub = "App" });
                    }
                }
                try
                {
                    var pm = new global::Windows.Management.Deployment.PackageManager();
                    foreach (var pkg in pm.FindPackagesForUser(string.Empty))
                    {
                        try
                        {
                            if (pkg.IsFramework || pkg.IsResourcePackage) continue;
                            foreach (var entry in await pkg.GetAppListEntriesAsync())
                            {
                                var name = entry.DisplayInfo.DisplayName;
                                if (string.IsNullOrWhiteSpace(name) || !seen.Add(name)) continue;
                                list.Add(new SpotItem { Kind = "uwp", Name = name, Entry = entry, Sub = "App" });
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex) { Log.Write("store apps index: " + ex.Message); }
                _apps = list;
            });
        }

        public static int AppCount => _apps?.Count ?? 0;

        public static List<SpotItem> Query(string q)
        {
            q = q.Trim();
            var results = new List<SpotItem>();
            if (q.Length == 0) return results;
            var counts = SettingsStore.Current.LaunchCounts;

            // Math
            if (Calc.TryEval(q, out var value))
                results.Add(new SpotItem { Kind = "math", Name = "= " + Calc.Format(value), Sub = "Enter to copy", Target = Calc.Format(value), Score = 5000, Glyph = Glyphs.Calculator });

            // Units  ("10 km to mi", "72 f in c", "100 usd to inr")
            var unit = UnitQuery(q);
            if (unit != null) results.Add(unit);

            string low = q.ToLowerInvariant();
            foreach (var a in _apps ?? new List<SpotItem>())
            {
                int s = Score(a.Name, low);
                if (s <= 0) continue;
                counts.TryGetValue(a.Name, out var used);
                results.Add(new SpotItem { Kind = a.Kind, Name = a.Name, Sub = a.Sub, Target = a.Target, Entry = a.Entry, Score = s + 150 + Math.Min(used, 20) * 25 });
            }
            foreach (var (name, uri) in SettingsPages)
            {
                int s = Score(name, low);
                if (s > 0) results.Add(new SpotItem { Kind = "setting", Name = name, Sub = "Settings", Target = uri, Score = s, Glyph = Glyphs.Settings });
            }
            try
            {
                var recent = Environment.GetFolderPath(Environment.SpecialFolder.Recent);
                foreach (var f in new DirectoryInfo(recent).GetFiles("*.lnk").OrderByDescending(f => f.LastWriteTime).Take(150))
                {
                    var name = Path.GetFileNameWithoutExtension(f.Name);
                    int s = Score(name, low);
                    if (s > 0) results.Add(new SpotItem { Kind = "recent", Name = name, Sub = "Recent · " + Ui.Ago(f.LastWriteTime), Target = f.FullName, Score = s - 100, Glyph = Glyphs.Document });
                }
            }
            catch { }

            var top = results.OrderByDescending(r => r.Score).Take(8).ToList();
            if (SettingsStore.Current.AiProvider != "Off")
                top.Add(new SpotItem { Kind = "assistant", Name = $"Ask the assistant: “{q}”", Sub = "Assistant", Target = q, Glyph = Glyphs.Chat });
            top.Add(new SpotItem { Kind = "web", Name = $"Search the web for “{q}”", Sub = "Google", Target = "https://www.google.com/search?q=" + Uri.EscapeDataString(q), Glyph = Glyphs.Globe });
            return top;
        }

        static int Score(string name, string q)
        {
            var n = name.ToLowerInvariant();
            if (n == q) return 1000;
            if (n.StartsWith(q)) return 800 - Math.Min(n.Length, 100);
            var words = n.Split(new[] { ' ', '-', '_', '.', '(' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Any(w => w.StartsWith(q))) return 600;
            var initials = new string(words.Select(w => w[0]).ToArray());
            if (q.Length >= 2 && initials.StartsWith(q)) return 550;
            if (n.Contains(q)) return 400;
            int i = 0;
            foreach (var c in n) if (i < q.Length && c == q[i]) i++;
            return i == q.Length && q.Length >= 3 ? 150 : 0;
        }

        static readonly Regex UnitRx = new Regex(@"^(-?[\d.,]+)\s*([a-zA-Z°²³/ ]+?)\s+(?:to|in|->)\s+([a-zA-Z°²³/ ]+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        static SpotItem? UnitQuery(string q)
        {
            var m = UnitRx.Match(q);
            if (!m.Success || !double.TryParse(m.Groups[1].Value.Replace(",", ""), NumberStyles.Float, Inv, out var v)) return null;
            string from = Norm(m.Groups[2].Value), to = Norm(m.Groups[3].Value);
            var r = UnitMath.Convert(v, from, to);
            if (r == null)
            {
                if (UnitMath.IsCurrency(from) && UnitMath.IsCurrency(to))
                    return new SpotItem { Kind = "unit", Name = $"{Calc.Format(v)} {from.ToUpperInvariant()} → {to.ToUpperInvariant()}", Sub = "Open Units for live rates", Target = "units", Score = 4900, Glyph = Glyphs.Calculator };
                return null;
            }
            var shown = Calc.Format(Math.Round(r.Value, Math.Abs(r.Value) >= 1 ? 4 : 8)); // 4 decimals is plenty for a conversion
            return new SpotItem { Kind = "math", Name = $"= {shown} {to}", Sub = $"{Calc.Format(v)} {from} → {to} · Enter to copy", Target = shown, Score = 4900, Glyph = Glyphs.Calculator };
        }

        static string Norm(string u) => u.Trim();

        public static void Launch(SpotItem item)
        {
            var counts = SettingsStore.Current.LaunchCounts;
            switch (item.Kind)
            {
                case "uwp":
                    if (item.Entry is global::Windows.ApplicationModel.Core.AppListEntry e) _ = e.LaunchAsync();
                    break;
                case "app":
                case "recent":
                case "setting":
                case "web":
                    Ui.Open(item.Target);
                    break;
            }
            if (item.Kind == "app" || item.Kind == "uwp")
            {
                counts[item.Name] = (counts.TryGetValue(item.Name, out var c) ? c : 0) + 1;
                SettingsStore.Save();
            }
        }
    }

    /// <summary>Tiny safe expression evaluator: + − × ÷ ^ % ( ), sqrt sin cos tan log ln abs round, pi, e.</summary>
    public static class Calc
    {
        public static bool TryEval(string s, out double value)
        {
            value = 0;
            s = s.Replace("×", "*").Replace("÷", "/").Replace("−", "-").Replace(",", "").Trim();
            if (s.Length < 2 || !s.Any(char.IsDigit) && !s.Contains("pi")) return false;
            if (!Regex.IsMatch(s, @"^[\d\s.+\-*/^%()a-z]+$", RegexOptions.IgnoreCase)) return false;
            if (!Regex.IsMatch(s, @"[+\-*/^%(]|sqrt|sin|cos|tan|log|ln|abs|round|pi")) return false;
            try
            {
                var p = new Parser(s.ToLowerInvariant());
                value = p.Expr();
                p.Ws();
                return p.Done && double.IsFinite(value);
            }
            catch { return false; }
        }

        public static string Format(double v)
        {
            if (Math.Abs(v - Math.Round(v)) < 1e-10 && Math.Abs(v) < 1e15) return Math.Round(v).ToString("#,0", CultureInfo.InvariantCulture);
            if (Math.Abs(v) >= 1e12 || Math.Abs(v) < 1e-6) return v.ToString("G8", CultureInfo.InvariantCulture);
            return v.ToString("#,0.########", CultureInfo.InvariantCulture);
        }

        sealed class Parser
        {
            readonly string _s;
            int _i;
            public Parser(string s) { _s = s; }
            public bool Done => _i >= _s.Length;
            public void Ws() { while (_i < _s.Length && _s[_i] == ' ') _i++; }
            bool Eat(char c) { Ws(); if (_i < _s.Length && _s[_i] == c) { _i++; return true; } return false; }

            public double Expr()
            {
                double v = Term();
                while (true)
                {
                    if (Eat('+')) v += Term();
                    else if (Eat('-')) v -= Term();
                    else return v;
                }
            }

            double Term()
            {
                double v = Power();
                while (true)
                {
                    if (Eat('*')) v *= Power();
                    else if (Eat('/')) v /= Power();
                    else if (Eat('%')) { Ws(); if (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '(')) v %= Power(); else v /= 100; }
                    else return v;
                }
            }

            double Power()
            {
                double b = Unary();
                return Eat('^') ? Math.Pow(b, Power()) : b;
            }

            double Unary()
            {
                if (Eat('-')) return -Unary();
                if (Eat('+')) return Unary();
                return Atom();
            }

            double Atom()
            {
                Ws();
                if (Eat('(')) { var v = Expr(); if (!Eat(')')) throw new FormatException(); return v; }
                int start = _i;
                if (_i < _s.Length && char.IsLetter(_s[_i]))
                {
                    while (_i < _s.Length && char.IsLetter(_s[_i])) _i++;
                    var name = _s.Substring(start, _i - start);
                    if (name == "pi") return Math.PI;
                    if (name == "e") return Math.E;
                    var arg = Atom();
                    return name switch
                    {
                        "sqrt" => Math.Sqrt(arg), "sin" => Math.Sin(arg * Math.PI / 180), "cos" => Math.Cos(arg * Math.PI / 180),
                        "tan" => Math.Tan(arg * Math.PI / 180), "log" => Math.Log10(arg), "ln" => Math.Log(arg), "abs" => Math.Abs(arg),
                        "round" => Math.Round(arg), _ => throw new FormatException()
                    };
                }
                while (_i < _s.Length && (char.IsDigit(_s[_i]) || _s[_i] == '.')) _i++;
                if (start == _i) throw new FormatException();
                return double.Parse(_s.Substring(start, _i - start), CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Shared unit tables (Units tab + Spotlight).</summary>
    public static class UnitMath
    {
        public static readonly Dictionary<string, (string Unit, double Factor)[]> Linear = new Dictionary<string, (string, double)[]>
        {
            ["Length"] = new[] { ("mm", 0.001), ("cm", 0.01), ("m", 1), ("km", 1000), ("in", 0.0254), ("ft", 0.3048), ("yd", 0.9144), ("mi", 1609.344) },
            ["Mass"] = new[] { ("mg", 1e-6), ("g", 0.001), ("kg", 1), ("t", 1000), ("oz", 0.028349523125), ("lb", 0.45359237) },
            ["Data"] = new[] { ("bit", 0.125), ("B", 1), ("KB", 1e3), ("MB", 1e6), ("GB", 1e9), ("TB", 1e12), ("KiB", 1024), ("MiB", 1048576), ("GiB", 1073741824) },
            ["Volume"] = new[] { ("mL", 0.001), ("L", 1), ("m³", 1000), ("tsp", 0.00492892159375), ("tbsp", 0.01478676478125), ("cup", 0.2365882365), ("fl oz", 0.0295735295625), ("gal", 3.785411784) },
            ["Speed"] = new[] { ("m/s", 1), ("km/h", 1 / 3.6), ("mph", 0.44704), ("knot", 0.514444444), ("ft/s", 0.3048) },
            ["Time"] = new[] { ("ms", 0.001), ("s", 1), ("min", 60), ("h", 3600), ("day", 86400), ("week", 604800), ("year", 31557600) },
            ["Area"] = new[] { ("cm²", 1e-4), ("m²", 1), ("ha", 1e4), ("km²", 1e6), ("ft²", 0.09290304), ("acre", 4046.8564224) },
        };
        public static readonly string[] Temps = { "°C", "°F", "K" };
        public static readonly string[] Currencies = { "USD", "EUR", "GBP", "INR", "JPY", "CNY", "AUD", "CAD", "CHF", "SGD" };

        static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["c"] = "°C", ["celsius"] = "°C", ["f"] = "°F", ["fahrenheit"] = "°F", ["k"] = "K", ["kelvin"] = "K",
            ["meter"] = "m", ["meters"] = "m", ["metre"] = "m", ["km"] = "km", ["kilometers"] = "km", ["miles"] = "mi", ["mile"] = "mi",
            ["inch"] = "in", ["inches"] = "in", ["feet"] = "ft", ["foot"] = "ft", ["yards"] = "yd", ["kgs"] = "kg", ["kilograms"] = "kg",
            ["grams"] = "g", ["pounds"] = "lb", ["lbs"] = "lb", ["ounces"] = "oz", ["liters"] = "L", ["litres"] = "L", ["l"] = "L", ["ml"] = "mL",
            ["gallons"] = "gal", ["kmh"] = "km/h", ["kph"] = "km/h", ["hours"] = "h", ["hour"] = "h", ["hr"] = "h", ["minutes"] = "min",
            ["seconds"] = "s", ["sec"] = "s", ["days"] = "day", ["weeks"] = "week", ["years"] = "year", ["gb"] = "GB", ["mb"] = "MB", ["kb"] = "KB", ["tb"] = "TB",
            ["m2"] = "m²", ["km2"] = "km²", ["ft2"] = "ft²", ["acres"] = "acre"
        };

        public static string Canon(string u) => Aliases.TryGetValue(u.Trim(), out var c) ? c : u.Trim();
        public static bool IsCurrency(string u) => Currencies.Contains(u.Trim().ToUpperInvariant());

        public static double? Convert(double v, string from, string to)
        {
            from = Canon(from); to = Canon(to);
            if (Temps.Contains(from) && Temps.Contains(to))
            {
                double c = from == "°C" ? v : from == "°F" ? (v - 32) * 5 / 9 : v - 273.15;
                return to == "°C" ? c : to == "°F" ? c * 9 / 5 + 32 : c + 273.15;
            }
            foreach (var table in Linear.Values)
            {
                var a = table.FirstOrDefault(x => string.Equals(x.Unit, from, StringComparison.OrdinalIgnoreCase));
                var b = table.FirstOrDefault(x => string.Equals(x.Unit, to, StringComparison.OrdinalIgnoreCase));
                if (a.Unit != null && b.Unit != null) return v * a.Factor / b.Factor;
            }
            return null;
        }
    }

    /// <summary>
    /// Opt-in assistant: local Ollama, any OpenAI-compatible endpoint, or Anthropic — with a key the user supplies.
    /// Crest sends nothing until the user configures a provider and asks something.
    /// </summary>
    public static class AssistantService
    {
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        const string SystemPrompt = "You are a concise assistant living in the notch of a Windows PC. Answer briefly and plainly; use short lists only when needed.";
        static AppSettings S => SettingsStore.Current;

        public static bool Configured => S.AiProvider != "Off" && (S.AiProvider == "Ollama" || !string.IsNullOrEmpty(S.AiKeyProtected)) && !string.IsNullOrWhiteSpace(S.AiModel);

        public static string DefaultEndpoint(string provider) => provider switch
        {
            "Ollama" => "http://localhost:11434",
            "OpenAI" => "https://api.openai.com/v1",
            "Anthropic" => "https://api.anthropic.com",
            _ => ""
        };

        public static string Endpoint => string.IsNullOrWhiteSpace(S.AiEndpoint) ? DefaultEndpoint(S.AiProvider) : S.AiEndpoint.TrimEnd('/');

        public static void SetKey(string key)
        {
            if (string.IsNullOrEmpty(key)) { S.AiKeyProtected = ""; return; }
            var enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(key), null, DataProtectionScope.CurrentUser);
            S.AiKeyProtected = Convert.ToBase64String(enc);
        }

        static string Key()
        {
            if (string.IsNullOrEmpty(S.AiKeyProtected)) return "";
            try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(S.AiKeyProtected), null, DataProtectionScope.CurrentUser)); }
            catch { return ""; }
        }

        public static async Task<List<string>> OllamaModelsAsync()
        {
            var list = new List<string>();
            var json = await Http.GetStringAsync(Endpoint + "/api/tags");
            using var doc = JsonDocument.Parse(json);
            foreach (var m in doc.RootElement.GetProperty("models").EnumerateArray())
                if (m.TryGetProperty("name", out var n)) list.Add(n.GetString() ?? "");
            return list;
        }

        /// <summary>Stream a reply. history = (role "user"/"assistant", text).</summary>
        public static async Task StreamAsync(List<(string Role, string Text)> history, Action<string> onDelta, CancellationToken ct)
        {
            HttpRequestMessage req;
            switch (S.AiProvider)
            {
                case "Ollama":
                {
                    var msgs = new List<object> { new { role = "system", content = SystemPrompt } };
                    msgs.AddRange(history.Select(h => (object)new { role = h.Role, content = h.Text }));
                    req = new HttpRequestMessage(HttpMethod.Post, Endpoint + "/api/chat")
                    { Content = Json(new { model = S.AiModel, messages = msgs, stream = true }) };
                    break;
                }
                case "OpenAI":
                {
                    var msgs = new List<object> { new { role = "system", content = SystemPrompt } };
                    msgs.AddRange(history.Select(h => (object)new { role = h.Role, content = h.Text }));
                    req = new HttpRequestMessage(HttpMethod.Post, Endpoint + "/chat/completions")
                    { Content = Json(new { model = S.AiModel, messages = msgs, stream = true }) };
                    req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + Key());
                    break;
                }
                case "Anthropic":
                {
                    req = new HttpRequestMessage(HttpMethod.Post, Endpoint + "/v1/messages")
                    {
                        Content = Json(new { model = S.AiModel, max_tokens = 1024, stream = true, system = SystemPrompt, messages = history.Select(h => new { role = h.Role, content = h.Text }) })
                    };
                    req.Headers.TryAddWithoutValidation("x-api-key", Key());
                    req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                    break;
                }
                default:
                    throw new InvalidOperationException("Choose a provider first.");
            }

            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                var body = await resp.Content.ReadAsStringAsync(ct);
                throw new HttpRequestException($"{(int)resp.StatusCode} {resp.ReasonPhrase}: {(body.Length > 300 ? body.Substring(0, 300) : body)}");
            }
            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var reader = new StreamReader(stream);
            string? line;
            while ((line = await reader.ReadLineAsync(ct)) != null)
            {
                if (line.Length == 0) continue;
                string? delta = null;
                try
                {
                    if (S.AiProvider == "Ollama")
                    {
                        using var d = JsonDocument.Parse(line);
                        if (d.RootElement.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c)) delta = c.GetString();
                    }
                    else if (line.StartsWith("data:"))
                    {
                        var data = line.Substring(5).Trim();
                        if (data == "[DONE]") break;
                        using var d = JsonDocument.Parse(data);
                        var root = d.RootElement;
                        if (S.AiProvider == "OpenAI")
                        {
                            if (root.TryGetProperty("choices", out var ch) && ch.GetArrayLength() > 0 && ch[0].TryGetProperty("delta", out var dl) && dl.TryGetProperty("content", out var ct2) && ct2.ValueKind == JsonValueKind.String)
                                delta = ct2.GetString();
                        }
                        else if (root.TryGetProperty("type", out var t) && t.GetString() == "content_block_delta" && root.GetProperty("delta").TryGetProperty("text", out var tx))
                            delta = tx.GetString();
                    }
                }
                catch { }
                if (!string.IsNullOrEmpty(delta)) onDelta(delta);
            }
        }

        static StringContent Json(object o) => new StringContent(JsonSerializer.Serialize(o), Encoding.UTF8, "application/json");
    }
}
