using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Crest.Core;

namespace Crest.Services
{
    /// <summary>
    /// Self-updater: checks GitHub Releases, downloads the matching exe, swaps it in after Crest exits and relaunches.
    /// No background service, no installer — one HTTPS request to api.github.com per check.
    /// </summary>
    public static class UpdateService
    {
        const string Api = "https://api.github.com/repos/rishii-hub/crest/releases/latest";
        static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        static DispatcherTimer? _timer;

        public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

        public static Version? Latest { get; private set; }
        public static string? LatestTag { get; private set; }
        public static string? AssetUrl { get; private set; }
        public static long AssetSize { get; private set; }
        public static string? Notes { get; private set; }
        public static bool Busy { get; private set; }
        public static bool Available => Latest != null && Latest > Current && AssetUrl != null;

        /// <summary>Raised on the UI thread when a newer release is found or the state changes.</summary>
        public static event Action? Changed;

        static UpdateService()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("Crest-Updater/" + CurrentText);
            Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        }

        /// <summary>Check shortly after start, then every 12 hours (if enabled).</summary>
        public static void Start(Dispatcher ui)
        {
            Ui.Later(45_000, () => { if (SettingsStore.Current.AutoUpdateCheck) _ = CheckAsync(); });
            _timer = new DispatcherTimer(TimeSpan.FromHours(12), DispatcherPriority.Background, (s, e) =>
            {
                if (SettingsStore.Current.AutoUpdateCheck) _ = CheckAsync();
            }, ui);
            _timer.Start();
        }

        /// <summary>True if the running exe is the self-contained build (bundles .NET).</summary>
        static bool SelfContained
        {
            get
            {
                try { return new FileInfo(Environment.ProcessPath!).Length > 45L * 1024 * 1024; }
                catch { return true; }
            }
        }

        public static async Task<bool> CheckAsync()
        {
            try
            {
                var json = await Http.GetStringAsync(Api);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var tag = root.GetProperty("tag_name").GetString() ?? "";
                if (!Version.TryParse(tag.TrimStart('v', 'V'), out var v)) return false;
                string want = SelfContained ? "Crest-win-x64.exe" : "Crest-win-x64-small.exe";
                string? url = null;
                long size = 0;
                foreach (var a in root.GetProperty("assets").EnumerateArray())
                {
                    if (a.GetProperty("name").GetString() == want)
                    {
                        url = a.GetProperty("browser_download_url").GetString();
                        size = a.GetProperty("size").GetInt64();
                    }
                }
                Latest = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
                LatestTag = tag;
                AssetUrl = url;
                AssetSize = size;
                Notes = root.TryGetProperty("body", out var b) ? b.GetString() : null;
                SettingsStore.Current.LastUpdateCheck = DateTime.Now;
                SettingsStore.Save();
                Changed?.Invoke();
                return Available;
            }
            catch (Exception ex)
            {
                Log.Write("update check failed: " + ex.Message);
                return false;
            }
        }

        /// <summary>Download the new exe, then hand over to a tiny script that swaps files once Crest has exited.</summary>
        public static async Task<string?> InstallAsync(IProgress<double>? progress = null)
        {
            if (!Available || Busy) return "No update available.";
            Busy = true;
            Changed?.Invoke();
            try
            {
                var target = Environment.ProcessPath!;
                var dir = Path.Combine(Path.GetTempPath(), "Crest-update");
                Directory.CreateDirectory(dir);
                var newExe = Path.Combine(dir, "Crest-" + LatestTag + ".exe");

                using (var resp = await Http.GetAsync(AssetUrl, HttpCompletionOption.ResponseHeadersRead))
                {
                    resp.EnsureSuccessStatusCode();
                    long total = resp.Content.Headers.ContentLength ?? AssetSize;
                    await using var src = await resp.Content.ReadAsStreamAsync();
                    await using var dst = File.Create(newExe);
                    var buffer = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = await src.ReadAsync(buffer)) > 0)
                    {
                        await dst.WriteAsync(buffer.AsMemory(0, n));
                        done += n;
                        if (total > 0) progress?.Report(done / (double)total);
                    }
                }
                if (AssetSize > 0 && new FileInfo(newExe).Length != AssetSize) return "Download was incomplete. Try again.";

                // Make sure we can write next to the current exe before quitting.
                var probe = Path.Combine(Path.GetDirectoryName(target)!, ".crest-write-test");
                try { File.WriteAllText(probe, ""); File.Delete(probe); }
                catch { return "Crest's folder isn't writable. Move Crest.exe somewhere like Documents and try again."; }

                int pid = Environment.ProcessId;
                var script = Path.Combine(dir, "apply-update.cmd");
                var sb = new StringBuilder();
                sb.AppendLine("@echo off");
                sb.AppendLine(":wait");
                sb.AppendLine($"tasklist /FI \"PID eq {pid}\" 2>nul | find \"{pid}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)");
                sb.AppendLine("set tries=0");
                sb.AppendLine(":copy");
                sb.AppendLine($"copy /y \"{newExe}\" \"{target}\" >nul && goto done");
                sb.AppendLine("set /a tries+=1");
                sb.AppendLine("if %tries% geq 15 goto done");
                sb.AppendLine("timeout /t 1 /nobreak >nul");
                sb.AppendLine("goto copy");
                sb.AppendLine(":done");
                sb.AppendLine($"start \"\" \"{target}\" --restart --updated");
                sb.AppendLine("del \"%~f0\"");
                File.WriteAllText(script, sb.ToString(), Encoding.ASCII);

                Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"\"{script}\"\"") { CreateNoWindow = true, UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden });
                Log.Write($"update: installing {LatestTag} over {target}");
                Application.Current.Shutdown();
                return null;
            }
            catch (Exception ex)
            {
                Log.Error("update install", ex);
                return "Update failed: " + ex.Message;
            }
            finally
            {
                Busy = false;
                Changed?.Invoke();
            }
        }
    }
}
