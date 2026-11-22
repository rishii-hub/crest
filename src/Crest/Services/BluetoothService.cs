using System;
using System.Collections.Generic;
using System.Windows.Threading;
using Crest.Core;
using WinBt = global::Windows.Devices.Bluetooth;
using WinDev = global::Windows.Devices.Enumeration;

namespace Crest.Services
{
    /// <summary>AirPods-style "Connected" pop-ups for paired Bluetooth (classic + LE) devices.</summary>
    public sealed class BluetoothService
    {
        public static BluetoothService Instance { get; } = new BluetoothService();
        const string ConnectedProp = "System.Devices.Aep.IsConnected";

        Dispatcher _ui = null!;
        readonly List<WinDev.DeviceWatcher> _watchers = new List<WinDev.DeviceWatcher>();
        readonly Dictionary<string, (string Name, bool Connected)> _devices = new Dictionary<string, (string, bool)>();
        readonly HashSet<WinDev.DeviceWatcher> _ready = new HashSet<WinDev.DeviceWatcher>();

        /// <summary>(device name, connected) on the UI thread.</summary>
        public event Action<string, bool>? Changed;

        public void Init(Dispatcher ui)
        {
            _ui = ui;
            try
            {
                Watch(WinBt.BluetoothDevice.GetDeviceSelectorFromPairingState(true));
                Watch(WinBt.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true));
            }
            catch (Exception ex) { Log.Error("bluetooth init", ex); }
        }

        void Watch(string selector)
        {
            var w = WinDev.DeviceInformation.CreateWatcher(selector, new[] { ConnectedProp }, WinDev.DeviceInformationKind.AssociationEndpoint);
            w.Added += (s, info) =>
            {
                bool c = Read(info.Properties);
                string name = info.Name ?? "Bluetooth device";
                string id = info.Id;
                _ui.BeginInvoke(new Action(() => _devices[id] = (name, c)));
            };
            w.Updated += (s, upd) =>
            {
                if (!upd.Properties.ContainsKey(ConnectedProp)) return;
                bool c = Read(upd.Properties);
                string id = upd.Id;
                _ui.BeginInvoke(new Action(() =>
                {
                    if (!_devices.TryGetValue(id, out var d)) return;
                    if (d.Connected == c) return;
                    _devices[id] = (d.Name, c);
                    if (_ready.Contains(w)) Changed?.Invoke(d.Name, c);
                }));
            };
            w.Removed += (s, upd) => { string id = upd.Id; _ui.BeginInvoke(new Action(() => _devices.Remove(id))); };
            w.EnumerationCompleted += (s, o) => _ui.BeginInvoke(new Action(() => _ready.Add(w)));
            w.Start();
            _watchers.Add(w);
        }

        static bool Read(IReadOnlyDictionary<string, object> props)
        {
            try { return props.TryGetValue(ConnectedProp, out var v) && v is bool b && b; }
            catch { return false; }
        }

        public void Stop()
        {
            foreach (var w in _watchers)
            {
                try { if (w.Status == WinDev.DeviceWatcherStatus.Started || w.Status == WinDev.DeviceWatcherStatus.EnumerationCompleted) w.Stop(); } catch { }
            }
        }

        public static bool LooksLikeAudio(string name)
        {
            var n = name.ToLowerInvariant();
            foreach (var k in new[] { "bud", "pod", "head", "ear", "wh-", "wf-", "jbl", "bose", "sony", "beats", "nothing ear", "speaker", "audio", "boat", "oneplus", "galaxy b" })
                if (n.Contains(k)) return true;
            return false;
        }
    }
}
