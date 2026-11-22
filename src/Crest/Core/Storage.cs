using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;

namespace Crest.Core
{
    /// <summary>JSON persistence in %AppData%\Crest. Writes are atomic (temp file + move).</summary>
    public static class Storage
    {
        public static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Crest");
        static readonly JsonSerializerOptions Opts = new JsonSerializerOptions { WriteIndented = true };

        public static T Load<T>(string file, Func<T> fallback)
        {
            try
            {
                var path = Path.Combine(Dir, file);
                if (File.Exists(path))
                {
                    var value = JsonSerializer.Deserialize<T>(File.ReadAllText(path), Opts);
                    if (value != null) return value;
                }
            }
            catch (Exception ex) { Log.Error("load " + file, ex); }
            return fallback();
        }

        public static void Save<T>(string file, T data)
        {
            try
            {
                Directory.CreateDirectory(Dir);
                var path = Path.Combine(Dir, file);
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonSerializer.Serialize(data, Opts));
                File.Move(tmp, path, true);
            }
            catch (Exception ex) { Log.Error("save " + file, ex); }
        }
    }

    public static class Log
    {
        static readonly object Gate = new object();

        public static void Error(string context, Exception ex) => Write(context + ": " + ex);

        public static void Write(string line)
        {
            try
            {
                lock (Gate)
                {
                    Directory.CreateDirectory(Storage.Dir);
                    var path = Path.Combine(Storage.Dir, "crest.log");
                    if (File.Exists(path) && new FileInfo(path).Length > 512 * 1024) File.Delete(path);
                    File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}");
                }
            }
            catch { }
        }
    }

    /// <summary>Runs an action once after calls stop for the given delay. UI thread only.</summary>
    public sealed class Debouncer
    {
        static readonly List<Debouncer> All = new List<Debouncer>();
        readonly DispatcherTimer _timer;
        readonly Action _action;

        public Debouncer(TimeSpan delay, Action action)
        {
            _action = action;
            _timer = new DispatcherTimer { Interval = delay };
            _timer.Tick += (s, e) => { _timer.Stop(); Run(); };
            All.Add(this);
        }

        public void Trigger() { _timer.Stop(); _timer.Start(); }

        public void Flush() { if (_timer.IsEnabled) { _timer.Stop(); Run(); } }

        void Run() { try { _action(); } catch (Exception ex) { Log.Error("debounce", ex); } }

        public static void FlushAll() { foreach (var d in All.ToArray()) d.Flush(); }
    }
}
