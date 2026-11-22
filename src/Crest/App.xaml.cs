using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Crest.Core;
using Crest.Services;

namespace Crest
{
    public partial class App : Application
    {
        const string MutexName = "Crest.Notch.SingleInstance";
        const string ShowEventName = "Crest.Notch.Show";

        Mutex? _mutex;
        EventWaitHandle? _showEvent;
        TrayIcon? _tray;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool restart = Array.IndexOf(e.Args, "--restart") >= 0;
            _mutex = new Mutex(false, MutexName);
            bool first;
            try { first = _mutex.WaitOne(restart ? 8000 : 0); }
            catch (AbandonedMutexException) { first = true; }
            if (!first)
            {
                // Already running: ask the existing instance to open, then quit.
                try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
                Shutdown();
                return;
            }

            DispatcherUnhandledException += (s, ex) => { Log.Error("ui", ex.Exception); ex.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) => Log.Write("fatal: " + ex.ExceptionObject);
            TaskScheduler.UnobservedTaskException += (s, ex) => { Log.Error("task", ex.Exception); ex.SetObserved(); };

            SettingsStore.Load();
            Ui.ApplyAccent(SettingsStore.Current.Accent);

            var notch = new NotchWindow();
            notch.Show();
            _tray = new TrayIcon(notch);

            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            ThreadPool.RegisterWaitForSingleObject(_showEvent, (state, timedOut) =>
                Dispatcher.BeginInvoke(new Action(() => notch.Expand("Home"))), null, -1, false);

            bool startup = Array.IndexOf(e.Args, "--startup") >= 0;
            int tour = Array.IndexOf(e.Args, "--tour");
            if (tour >= 0)
            {
                var dir = tour + 1 < e.Args.Length ? e.Args[tour + 1] : "shots";
                SettingsStore.Current.FirstRun = false;
                Ui.Later(800, () => notch.RunTour(dir));
            }
            else if (SettingsStore.Current.FirstRun)
            {
                SettingsStore.Current.FirstRun = false;
                SettingsStore.Save();
                Ui.Later(900, notch.Welcome);
            }
            else if (!startup)
            {
                Ui.Later(600, () => notch.ShowToast(new Toast { Kind = "hello", Glyph = Glyphs.Check, GlyphBrush = Ui.Res("GreenBrush"), Title = "Crest is running", Value = "Ctrl+Alt+N", Duration = TimeSpan.FromSeconds(2) }));
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                Debouncer.FlushAll();
                SettingsStore.SaveNow();
                KeepAwake.Set(false);
                _tray?.Dispose();
                try { _mutex?.ReleaseMutex(); } catch { }
            }
            catch { }
            base.OnExit(e);
        }
    }
}
