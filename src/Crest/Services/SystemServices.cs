using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Threading.Tasks;
using System.Windows.Threading;
using Crest.Core;
using Microsoft.Win32;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace Crest.Services
{
    /// <summary>Master volume / mute / peak meter / microphone mute through Core Audio (NAudio).</summary>
    public sealed class AudioService : IMMNotificationClient
    {
        public static AudioService Instance { get; } = new AudioService();
        Dispatcher _ui = null!;
        MMDeviceEnumerator? _enum;
        MMDevice? _device;
        bool _selfChange;

        /// <summary>(volume 0..1, muted) on the UI thread. Not raised for changes made by Crest itself.</summary>
        public event Action<float, bool>? VolumeChanged;

        public void Init(Dispatcher ui)
        {
            _ui = ui;
            try
            {
                _enum = new MMDeviceEnumerator();
                _enum.RegisterEndpointNotificationCallback(this);
                Bind();
            }
            catch (Exception ex) { Log.Error("audio init", ex); }
        }

        void Bind()
        {
            try
            {
                if (_device != null)
                {
                    try { _device.AudioEndpointVolume.OnVolumeNotification -= OnVolume; } catch { }
                    try { _device.Dispose(); } catch { }
                    _device = null;
                }
                if (_enum == null || !_enum.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) return;
                _device = _enum.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                _device.AudioEndpointVolume.OnVolumeNotification += OnVolume;
            }
            catch (Exception ex) { Log.Error("audio bind", ex); _device = null; }
        }

        void OnVolume(AudioVolumeNotificationData data)
        {
            float v = data.MasterVolume;
            bool m = data.Muted;
            _ui.BeginInvoke(new Action(() =>
            {
                if (_selfChange) return;
                VolumeChanged?.Invoke(v, m);
            }));
        }

        void SelfChange(Action a)
        {
            _selfChange = true;
            try { a(); } catch { }
            _ui.BeginInvoke(DispatcherPriority.Background, new Action(() => Ui.Later(150, () => _selfChange = false)));
        }

        public bool Available => _device != null;

        public float Volume
        {
            get { try { return _device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0f; } catch { return 0f; } }
            set { SelfChange(() => { if (_device != null) _device.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0f, 1f); }); }
        }

        public bool Muted
        {
            get { try { return _device?.AudioEndpointVolume.Mute ?? false; } catch { return false; } }
            set { SelfChange(() => { if (_device != null) _device.AudioEndpointVolume.Mute = value; }); }
        }

        public float Peak
        {
            get { try { return _device?.AudioMeterInformation.MasterPeakValue ?? 0f; } catch { return 0f; } }
        }

        public bool MicMuted
        {
            get
            {
                try
                {
                    if (_enum == null || !_enum.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)) return false;
                    using var mic = _enum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                    return mic.AudioEndpointVolume.Mute;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    if (_enum == null || !_enum.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications)) return;
                    using var mic = _enum.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Communications);
                    mic.AudioEndpointVolume.Mute = value;
                }
                catch (Exception ex) { Log.Error("mic mute", ex); }
            }
        }

        public bool HasMic
        {
            get { try { return _enum != null && _enum.HasDefaultAudioEndpoint(DataFlow.Capture, Role.Communications); } catch { return false; } }
        }

        // IMMNotificationClient
        public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
        public void OnDeviceAdded(string pwstrDeviceId) { }
        public void OnDeviceRemoved(string deviceId) { }
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia) _ui?.BeginInvoke(new Action(Bind));
        }
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    }

    /// <summary>Laptop panel brightness through WMI (events + set).</summary>
    public sealed class BrightnessService
    {
        public static BrightnessService Instance { get; } = new BrightnessService();
        Dispatcher _ui = null!;
        ManagementEventWatcher? _watcher;
        int? _pending;
        bool _sending;
        DateTime _selfUntil;

        public bool Supported { get; private set; }
        public int Current { get; private set; } = -1;
        public event Action<int>? Changed;

        public void Init(Dispatcher ui)
        {
            _ui = ui;
            Task.Run(() =>
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\WMI"), new ObjectQuery("SELECT CurrentBrightness FROM WmiMonitorBrightness"));
                    foreach (ManagementObject o in searcher.Get())
                    {
                        Current = Convert.ToInt32(o["CurrentBrightness"]);
                        Supported = true;
                        break;
                    }
                    if (!Supported) return;
                    _watcher = new ManagementEventWatcher(new ManagementScope(@"root\WMI"), new WqlEventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                    _watcher.EventArrived += (s, e) =>
                    {
                        try
                        {
                            int b = Convert.ToInt32(e.NewEvent.Properties["Brightness"].Value);
                            _ui.BeginInvoke(new Action(() =>
                            {
                                Current = b;
                                if (DateTime.Now > _selfUntil) Changed?.Invoke(b);
                            }));
                        }
                        catch { }
                    };
                    _watcher.Start();
                }
                catch (Exception ex) { Supported = false; Log.Write("brightness unsupported: " + ex.Message); }
            });
        }

        /// <summary>Throttled set (safe to call on every slider tick).</summary>
        public void Set(int value)
        {
            if (!Supported) return;
            _pending = Math.Clamp(value, 0, 100);
            _selfUntil = DateTime.Now.AddMilliseconds(800);
            if (_sending) return;
            _sending = true;
            Task.Run(async () =>
            {
                while (_pending.HasValue)
                {
                    int v = _pending.Value;
                    _pending = null;
                    try
                    {
                        using var searcher = new ManagementObjectSearcher(new ManagementScope(@"root\WMI"), new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));
                        foreach (ManagementObject o in searcher.Get())
                            o.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)v });
                        Current = v;
                    }
                    catch (Exception ex) { Log.Error("brightness set", ex); }
                    await Task.Delay(60);
                }
                _sending = false;
            });
        }

        public void Stop() { try { _watcher?.Stop(); _watcher?.Dispose(); } catch { } }
    }

    /// <summary>Battery / AC state with plug, unplug and low-battery events.</summary>
    public sealed class PowerService
    {
        public static PowerService Instance { get; } = new PowerService();
        DispatcherTimer? _timer;
        bool _first = true;
        int _lowNotified = 101;

        public bool HasBattery { get; private set; }
        public int Percent { get; private set; } = 100;
        public bool OnAc { get; private set; } = true;
        public bool Charging => OnAc && Percent < 100;
        public int SecondsLeft { get; private set; } = -1;

        public event Action? Changed;
        public event Action<bool>? AcChanged;
        public event Action<int>? Low;
        public event Action? Full;
        bool _fullNotified;

        public void Init(Dispatcher ui)
        {
            Poll();
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Background, (s, e) => Poll(), ui);
            _timer.Start();
            try { SystemEvents.PowerModeChanged += (s, e) => ui.BeginInvoke(new Action(Poll)); } catch { }
        }

        public void Poll()
        {
            if (!Win32.GetSystemPowerStatus(out var st)) return;
            bool hasBattery = st.BatteryFlag != 128 && st.BatteryFlag != 255 && st.BatteryLifePercent <= 100;
            bool onAc = st.ACLineStatus == 1;
            int pct = st.BatteryLifePercent <= 100 ? st.BatteryLifePercent : 100;
            bool acChanged = !_first && onAc != OnAc;
            bool changed = _first || hasBattery != HasBattery || onAc != OnAc || pct != Percent;
            HasBattery = hasBattery; OnAc = onAc; Percent = pct;
            SecondsLeft = st.BatteryLifeTime;
            _first = false;
            if (changed) Changed?.Invoke();
            if (acChanged && HasBattery) AcChanged?.Invoke(onAc);
            if (onAc) _lowNotified = 101;
            if (onAc && HasBattery && pct >= 100 && !_fullNotified && !_first) { _fullNotified = true; Full?.Invoke(); }
            if (!onAc || pct < 97) _fullNotified = false;
            else if (HasBattery)
            {
                foreach (var threshold in new[] { 20, 10, 5 })
                {
                    if (pct <= threshold && _lowNotified > threshold)
                    {
                        _lowNotified = threshold;
                        Low?.Invoke(pct);
                        break;
                    }
                }
            }
        }
    }

    /// <summary>macOS-style privacy indicators: who is using the microphone / camera right now.</summary>
    public sealed class PrivacyService
    {
        public static PrivacyService Instance { get; } = new PrivacyService();
        DispatcherTimer? _timer;
        const string Root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";

        public bool MicInUse { get; private set; }
        public bool CamInUse { get; private set; }
        public string MicApps { get; private set; } = "";
        public string CamApps { get; private set; } = "";
        public event Action? Changed;

        public void Init(Dispatcher ui)
        {
            _timer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (s, e) => Poll(), ui);
            _timer.Start();
            Poll();
        }

        void Poll()
        {
            if (!SettingsStore.Current.PrivacyDots)
            {
                if (MicInUse || CamInUse) { MicInUse = CamInUse = false; Changed?.Invoke(); }
                return;
            }
            var mic = Scan("microphone");
            var cam = Scan("webcam");
            var capture = Scan("graphicsCaptureProgrammatic").Concat(Scan("graphicsCaptureWithoutBorder")).Distinct().Where(x => !x.Equals("Crest", StringComparison.OrdinalIgnoreCase)).ToList();
            var capText = string.Join(", ", capture);
            if (capText != RecorderService.CaptureApps) { RecorderService.CaptureApps = capText; ActivityHub.Notify(); }
            bool micUse = mic.Count > 0, camUse = cam.Count > 0;
            string micApps = string.Join(", ", mic), camApps = string.Join(", ", cam);
            if (micUse != MicInUse || camUse != CamInUse || micApps != MicApps || camApps != CamApps)
            {
                MicInUse = micUse; CamInUse = camUse; MicApps = micApps; CamApps = camApps;
                Changed?.Invoke();
            }
        }

        static List<string> Scan(string capability)
        {
            var apps = new List<string>();
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(Root + capability);
                if (key == null) return apps;
                foreach (var name in key.GetSubKeyNames())
                {
                    if (name == "NonPackaged")
                    {
                        using var np = key.OpenSubKey(name);
                        if (np == null) continue;
                        foreach (var exe in np.GetSubKeyNames())
                        {
                            using var k = np.OpenSubKey(exe);
                            if (InUse(k)) apps.Add(Path.GetFileNameWithoutExtension(exe.Replace('#', '\\')));
                        }
                    }
                    else
                    {
                        using var k = key.OpenSubKey(name);
                        if (InUse(k))
                        {
                            var n = name;
                            int us = n.IndexOf('_');
                            if (us > 0) n = n.Substring(0, us);
                            int dot = n.LastIndexOf('.');
                            apps.Add(dot >= 0 ? n.Substring(dot + 1) : n);
                        }
                    }
                }
            }
            catch { }
            return apps.Distinct().ToList();
        }

        static bool InUse(RegistryKey? k)
        {
            if (k == null) return false;
            try
            {
                long start = Convert.ToInt64(k.GetValue("LastUsedTimeStart") ?? 0L);
                long stop = Convert.ToInt64(k.GetValue("LastUsedTimeStop") ?? 0L);
                return start > 0 && stop == 0;
            }
            catch { return false; }
        }
    }

    /// <summary>Keeps the PC and display awake (like Amphetamine / caffeinate on macOS).</summary>
    public static class KeepAwake
    {
        public static bool On { get; private set; }

        public static void Set(bool on)
        {
            On = on;
            Win32.SetThreadExecutionState(on
                ? Win32.ES_CONTINUOUS | Win32.ES_SYSTEM_REQUIRED | Win32.ES_DISPLAY_REQUIRED
                : Win32.ES_CONTINUOUS);
        }
    }

    public static class Startup
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string Name = "Crest";

        public static bool Enabled
        {
            get
            {
                try
                {
                    using var k = Registry.CurrentUser.OpenSubKey(RunKey);
                    return k?.GetValue(Name) is string;
                }
                catch { return false; }
            }
            set
            {
                try
                {
                    using var k = Registry.CurrentUser.CreateSubKey(RunKey);
                    if (value) k.SetValue(Name, $"\"{Environment.ProcessPath}\" --startup");
                    else k.DeleteValue(Name, false);
                }
                catch (Exception ex) { Log.Error("startup", ex); }
            }
        }
    }
}
