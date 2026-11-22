using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Crest.Core;
using NAudio.CoreAudioApi;
using WinDev = global::Windows.Devices.Enumeration;

namespace Crest.Services
{
    // =====================================================================================
    //  Focus Assist / Do Not Disturb — reads the shell's quiet-hours state (WNF) every 2 s.
    // =====================================================================================
    public static class FocusAssistService
    {
        [DllImport("ntdll.dll")]
        static extern int NtQueryWnfStateData(ref ulong stateName, IntPtr typeId, IntPtr explicitScope, out uint changeStamp, [Out] byte[] buffer, ref uint bufferSize);

        const ulong WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED = 0x0D83063EA3BF1C75;
        static int _last = -1;
        public static int State { get; private set; } = -1; // 0 off, 1 priority only, 2 alarms only, -1 unknown
        public static event Action<int>? Changed;

        public static void Init(Dispatcher ui)
        {
            var t = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (s, e) => Poll(), ui);
            t.Start();
            Poll();
        }

        static void Poll()
        {
            try
            {
                ulong name = WNF_SHEL_QUIETHOURS_ACTIVE_PROFILE_CHANGED;
                var buf = new byte[4];
                uint size = 4;
                if (NtQueryWnfStateData(ref name, IntPtr.Zero, IntPtr.Zero, out _, buf, ref size) != 0 || size < 4) return;
                int v = BitConverter.ToInt32(buf, 0);
                State = v;
                if (_last != -1 && v != _last) Changed?.Invoke(v);
                _last = v;
            }
            catch { }
        }

        public static string Describe(int v) => v switch { 0 => "Off", 1 => "Priority only", 2 => "Alarms only", _ => "On" };
    }

    // =====================================================================================
    //  Audio output devices: list + switch the default (IPolicyConfig, as EarTrumpet/SoundSwitch do)
    // =====================================================================================
    public static class AudioOutputs
    {
        [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IPolicyConfig
        {
            [PreserveSig] int GetMixFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr format);
            [PreserveSig] int GetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr format);
            [PreserveSig] int ResetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int SetDeviceFormat([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr endpointFormat, IntPtr mixFormat);
            [PreserveSig] int GetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, int def, IntPtr defaultPeriod, IntPtr minPeriod);
            [PreserveSig] int SetProcessingPeriod([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr period);
            [PreserveSig] int GetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
            [PreserveSig] int SetShareMode([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr mode);
            [PreserveSig] int GetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
            [PreserveSig] int SetPropertyValue([MarshalAs(UnmanagedType.LPWStr)] string id, IntPtr key, IntPtr value);
            [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
            [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
        }

        [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
        class CPolicyConfigClient { }

        public static List<(string Id, string Name, bool IsDefault)> List()
        {
            var list = new List<(string, string, bool)>();
            try
            {
                using var en = new MMDeviceEnumerator();
                string def = "";
                try { if (en.HasDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) using (var d = en.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia)) def = d.ID; } catch { }
                foreach (var d in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                {
                    list.Add((d.ID, d.FriendlyName, d.ID == def));
                    d.Dispose();
                }
            }
            catch (Exception ex) { Log.Error("audio outputs", ex); }
            return list;
        }

        public static bool SetDefault(string id)
        {
            try
            {
                var cfg = (IPolicyConfig)new CPolicyConfigClient();
                for (int role = 0; role < 3; role++) cfg.SetDefaultEndpoint(id, role);
                Marshal.ReleaseComObject(cfg);
                return true;
            }
            catch (Exception ex) { Log.Error("set default output", ex); return false; }
        }

        /// <summary>Switch to the next active output; returns its name.</summary>
        public static string? Cycle()
        {
            var l = List();
            if (l.Count < 2) return null;
            int i = l.FindIndex(x => x.IsDefault);
            var next = l[(i + 1) % l.Count];
            return SetDefault(next.Id) ? next.Name : null;
        }
    }

    // =====================================================================================
    //  Bluetooth battery (where the driver exposes DEVPKEY_Bluetooth_Battery)
    // =====================================================================================
    public static class BluetoothBattery
    {
        const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

        public static async Task<int?> ForContainerAsync(Guid container)
        {
            try
            {
                var aqs = $"System.Devices.ContainerId:=\"{{{container}}}\"";
                var devices = await WinDev.DeviceInformation.FindAllAsync(aqs, new[] { BatteryKey }, WinDev.DeviceInformationKind.Device);
                foreach (var d in devices)
                    if (d.Properties.TryGetValue(BatteryKey, out var v) && v != null)
                    {
                        int pct = Convert.ToInt32(v);
                        if (pct >= 0 && pct <= 100) return pct;
                    }
            }
            catch (Exception ex) { Log.Write("bt battery: " + ex.Message); }
            return null;
        }
    }

    // =====================================================================================
    //  LAN Drop — send files from a phone's browser to this PC. Off by default; PIN-protected;
    //  every transfer must be accepted in the notch.
    // =====================================================================================
    public static class LanDropService
    {
        public const int BasePort = 52070;
        static TcpListener? _listener;
        static CancellationTokenSource? _cts;
        static Dispatcher _ui = null!;
        static readonly Dictionary<string, (int Fails, DateTime Until)> Lockouts = new Dictionary<string, (int, DateTime)>();
        static readonly Dictionary<string, Incoming> Pending = new Dictionary<string, Incoming>();
        static int _seq;

        sealed class Incoming
        {
            public string Id = "", Name = "", From = "";
            public long Size, Done;
            public bool Accepted;
            public TaskCompletionSource<bool> Decision = new TaskCompletionSource<bool>();
        }

        public static bool Running => _listener != null;
        public static int Port { get; private set; }
        public static string Pin { get; private set; } = "";
        public static string Folder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "Crest Drop");
        public static event Action? Changed;

        public static void Init(Dispatcher ui)
        {
            _ui = ui;
            ActivityHub.Register(Activities);
            if (SettingsStore.Current.LanDrop) Start();
        }

        static IEnumerable<LiveActivity> Activities()
        {
            foreach (var p in Pending.Values.ToList())
            {
                var inc = p;
                yield return new LiveActivity
                {
                    Id = "drop-" + p.Id, Priority = 98, Glyph = Glyphs.Phone, GlyphBrush = Ui.Res("BlueBrush"), Pulse = !p.Accepted,
                    Title = p.Accepted ? "Receiving " + p.Name : p.Name, Value = p.Accepted ? $"{(p.Size > 0 ? p.Done * 100 / p.Size : 0)}%" : Ui.Bytes(p.Size),
                    Progress = p.Accepted && p.Size > 0 ? p.Done / (double)p.Size : null, Tab = "Tools", Signature = p.Id,
                    Actions = p.Accepted ? new List<(string, string)>() : new List<(string, string)> { ("accept", "Accept"), ("decline", "Decline") },
                    OnAction = a => inc.Decision.TrySetResult(a == "accept")
                };
            }
        }

        public static string? Start()
        {
            if (_listener != null) return null;
            Pin = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("000000");
            for (int port = BasePort; port < BasePort + 10; port++)
            {
                try
                {
                    var l = new TcpListener(IPAddress.Any, port);
                    l.Start();
                    _listener = l;
                    Port = port;
                    break;
                }
                catch { }
            }
            if (_listener == null) return "No free port between 52070 and 52079.";
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            var listener = _listener;
            Task.Run(async () =>
            {
                while (!ct.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await listener.AcceptTcpClientAsync(ct); }
                    catch { break; }
                    _ = Task.Run(() => Serve(client, ct));
                }
            });
            Changed?.Invoke();
            return null;
        }

        public static void Stop()
        {
            try { _cts?.Cancel(); _listener?.Stop(); } catch { }
            _listener = null;
            foreach (var p in Pending.Values) p.Decision.TrySetResult(false);
            Changed?.Invoke();
        }

        public static string? Url
        {
            get
            {
                var ip = LocalIp();
                return ip == null || !Running ? null : $"http://{ip}:{Port}/?pin={Pin}";
            }
        }

        static string? LocalIp()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()
                         .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback && n.NetworkInterfaceType != NetworkInterfaceType.Tunnel)
                         .OrderByDescending(n => n.GetIPProperties().GatewayAddresses.Count > 0))
                    foreach (var a in ni.GetIPProperties().UnicastAddresses)
                        if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)) return a.Address.ToString();
            }
            catch { }
            return null;
        }

        public static BitmapSource? Qr()
        {
            var url = Url;
            if (url == null) return null;
            try
            {
                using var gen = new QRCoder.QRCodeGenerator();
                using var data = gen.CreateQrCode(url, QRCoder.QRCodeGenerator.ECCLevel.M);
                var png = new QRCoder.PngByteQRCode(data).GetGraphic(6);
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.StreamSource = new MemoryStream(png);
                bi.EndInit();
                bi.Freeze();
                return bi;
            }
            catch (Exception ex) { Log.Error("qr", ex); return null; }
        }

        static async Task Serve(TcpClient client, CancellationToken ct)
        {
            using (client)
            {
                client.ReceiveTimeout = 30000;
                var stream = client.GetStream();
                string remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
                try
                {
                    var (method, path, headers, leftover) = await ReadHead(stream, ct);
                    if (method == "GET")
                    {
                        await Respond(stream, 200, "text/html; charset=utf-8", Page());
                        return;
                    }
                    if (method != "POST" || !path.StartsWith("/upload")) { await Respond(stream, 404, "text/plain", "Not found"); return; }

                    lock (Lockouts)
                        if (Lockouts.TryGetValue(remote, out var lo) && lo.Until > DateTime.Now) { _ = Respond(stream, 429, "application/json", "{\"ok\":false,\"error\":\"Too many wrong PINs. Wait a minute.\"}"); return; }
                    headers.TryGetValue("x-pin", out var pin);
                    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(pin ?? ""), Encoding.UTF8.GetBytes(Pin)))
                    {
                        lock (Lockouts)
                        {
                            Lockouts.TryGetValue(remote, out var lo);
                            int fails = lo.Fails + 1;
                            Lockouts[remote] = (fails, fails >= 5 ? DateTime.Now.AddMinutes(1) : DateTime.MinValue);
                        }
                        await Respond(stream, 403, "application/json", "{\"ok\":false,\"error\":\"Wrong PIN\"}");
                        return;
                    }
                    headers.TryGetValue("x-name", out var rawName);
                    var name = SafeName(Uri.UnescapeDataString(rawName ?? "file"));
                    long size = headers.TryGetValue("content-length", out var cl) && long.TryParse(cl, out var n) ? n : -1;
                    if (size <= 0 || size > 8L * 1024 * 1024 * 1024) { await Respond(stream, 413, "application/json", "{\"ok\":false,\"error\":\"Empty or too large\"}"); return; }

                    var inc = new Incoming { Id = Interlocked.Increment(ref _seq).ToString(), Name = name, Size = size, From = remote };
                    await _ui.InvokeAsync(() => { Pending[inc.Id] = inc; ActivityHub.Notify(); Ui.PlaySound("Windows Notify System Generic.wav"); });
                    var decided = await Task.WhenAny(inc.Decision.Task, Task.Delay(TimeSpan.FromSeconds(60), ct));
                    bool ok = decided == inc.Decision.Task && inc.Decision.Task.Result;
                    if (!ok)
                    {
                        await _ui.InvokeAsync(() => { Pending.Remove(inc.Id); ActivityHub.Notify(); });
                        await Respond(stream, 403, "application/json", "{\"ok\":false,\"error\":\"Declined on the PC\"}");
                        return;
                    }
                    await _ui.InvokeAsync(() => { inc.Accepted = true; ActivityHub.Notify(); });

                    Directory.CreateDirectory(Folder);
                    var dest = Unique(Path.Combine(Folder, name));
                    long done = 0;
                    var lastUi = DateTime.MinValue;
                    await using (var fs = File.Create(dest))
                    {
                        if (leftover.Length > 0) { await fs.WriteAsync(leftover, ct); done += leftover.Length; }
                        var buf = new byte[256 * 1024];
                        while (done < size)
                        {
                            int r = await stream.ReadAsync(buf.AsMemory(0, (int)Math.Min(buf.Length, size - done)), ct);
                            if (r <= 0) break;
                            await fs.WriteAsync(buf.AsMemory(0, r), ct);
                            done += r;
                            if (DateTime.Now - lastUi > TimeSpan.FromMilliseconds(250))
                            {
                                lastUi = DateTime.Now;
                                long d = done;
                                _ = _ui.BeginInvoke(new Action(() => { inc.Done = d; ActivityHub.Notify(); }));
                            }
                        }
                    }
                    bool complete = done == size;
                    if (!complete) try { File.Delete(dest); } catch { }
                    await _ui.InvokeAsync(() =>
                    {
                        Pending.Remove(inc.Id);
                        ActivityHub.Notify();
                        NotchWindow.Instance?.ShowToast(new Toast
                        {
                            Kind = "drop", Glyph = complete ? Glyphs.Check : Glyphs.Close, GlyphBrush = Ui.Res(complete ? "GreenBrush" : "RedBrush"),
                            Title = complete ? "Received" : "Transfer failed", Value = name, Duration = TimeSpan.FromSeconds(3)
                        });
                        if (complete) TrayStore.Shelf.Add(TrayItem.FromPath(dest));
                    });
                    await Respond(stream, complete ? 200 : 500, "application/json", complete ? "{\"ok\":true}" : "{\"ok\":false,\"error\":\"Incomplete\"}");
                }
                catch (Exception ex) { Log.Write("lan drop: " + ex.Message); }
            }
        }

        static async Task<(string Method, string Path, Dictionary<string, string> Headers, byte[] Leftover)> ReadHead(NetworkStream s, CancellationToken ct)
        {
            var buf = new byte[16 * 1024];
            int len = 0, end = -1;
            while (end < 0)
            {
                if (len == buf.Length) throw new IOException("header too large");
                int r = await s.ReadAsync(buf.AsMemory(len, buf.Length - len), ct);
                if (r <= 0) throw new IOException("closed");
                len += r;
                for (int i = Math.Max(0, len - r - 3); i + 3 < len; i++)
                    if (buf[i] == '\r' && buf[i + 1] == '\n' && buf[i + 2] == '\r' && buf[i + 3] == '\n') { end = i; break; }
            }
            var head = Encoding.ASCII.GetString(buf, 0, end).Split("\r\n");
            var first = head[0].Split(' ');
            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var h in head.Skip(1))
            {
                int c = h.IndexOf(':');
                if (c > 0) headers[h.Substring(0, c).Trim().ToLowerInvariant()] = h.Substring(c + 1).Trim();
            }
            var leftover = buf.AsSpan(end + 4, len - end - 4).ToArray();
            return (first[0], first.Length > 1 ? first[1] : "/", headers, leftover);
        }

        static async Task Respond(NetworkStream s, int code, string type, string body)
        {
            var b = Encoding.UTF8.GetBytes(body);
            var head = $"HTTP/1.1 {code} {(code == 200 ? "OK" : "Error")}\r\nContent-Type: {type}\r\nContent-Length: {b.Length}\r\nConnection: close\r\nCache-Control: no-store\r\n\r\n";
            await s.WriteAsync(Encoding.ASCII.GetBytes(head));
            await s.WriteAsync(b);
        }

        static string SafeName(string n)
        {
            n = Path.GetFileName(n.Replace('\\', '/').Split('/').Last());
            foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            n = n.Trim().TrimStart('.');
            return string.IsNullOrEmpty(n) ? "file" : n.Length > 120 ? n.Substring(n.Length - 120) : n;
        }

        static string Unique(string path)
        {
            if (!File.Exists(path)) return path;
            var dir = Path.GetDirectoryName(path)!;
            var stem = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            for (int i = 2; ; i++) { var p = Path.Combine(dir, $"{stem} ({i}){ext}"); if (!File.Exists(p)) return p; }
        }

        static string Page() => @"<!doctype html><html><head><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'>
<title>Send to PC · Crest</title><style>
body{margin:0;font-family:-apple-system,system-ui,sans-serif;background:#000;color:#f5f5f7;display:flex;min-height:100vh;align-items:center;justify-content:center}
.c{width:min(420px,92vw);background:#161618;border-radius:22px;padding:24px}
h1{font-size:20px;margin:0 0 4px}p{color:#a1a1a6;margin:0 0 18px;font-size:14px}
input{width:100%;box-sizing:border-box;background:#242426;color:#fff;border:0;border-radius:12px;padding:12px;font-size:16px;margin-bottom:12px}
button{width:100%;background:#ff9f0a;color:#000;border:0;border-radius:12px;padding:14px;font-size:16px;font-weight:600}
.f{margin:6px 0;font-size:14px}.bar{height:6px;background:#333;border-radius:3px;overflow:hidden;margin-top:4px}.bar i{display:block;height:100%;width:0;background:#30d158}
</style></head><body><div class='c'><h1>Send to this PC</h1><p>Pick files. Accept them on the PC's notch.</p>
<input id='pin' inputmode='numeric' maxlength='6' placeholder='PIN shown on the PC'><input id='f' type='file' multiple>
<button onclick='go()'>Send</button><div id='l'></div></div>
<script>
const q=new URLSearchParams(location.search);if(q.get('pin'))document.getElementById('pin').value=q.get('pin');
function send(file,i){return new Promise(r=>{const x=new XMLHttpRequest();x.open('POST','/upload');
x.setRequestHeader('X-Pin',document.getElementById('pin').value);x.setRequestHeader('X-Name',encodeURIComponent(file.name));
const row=document.createElement('div');row.className='f';row.innerHTML=file.name+' — waiting for PC…<div class=bar><i></i></div>';document.getElementById('l').appendChild(row);
x.upload.onprogress=e=>{row.querySelector('i').style.width=(e.loaded/e.total*100)+'%'};
x.onload=()=>{let j={};try{j=JSON.parse(x.responseText)}catch(e){};row.firstChild.textContent=file.name+(j.ok?' — sent ✓':' — '+(j.error||'failed'));r()};
x.onerror=()=>{row.firstChild.textContent=file.name+' — network error';r()};x.send(file)})}
async function go(){const fs=document.getElementById('f').files;for(let i=0;i<fs.length;i++)await send(fs[i],i)}
</script></body></html>";
    }

    // =====================================================================================
    //  Mirrored Windows notifications (needs package identity: the MSIX build).
    // =====================================================================================
    public static class NotificationMirror
    {
        static readonly HashSet<uint> Seen = new HashSet<uint>();
        public static string Status { get; private set; } = "Not started";

        public static bool HasIdentity
        {
            get
            {
                try { return global::Windows.ApplicationModel.Package.Current?.Id != null; }
                catch { return false; }
            }
        }

        static DispatcherTimer? _poll;

        public static void Stop()
        {
            _poll?.Stop();
            _poll = null;
            Status = HasIdentity ? "Off" : "Needs the packaged (MSIX) build of Crest";
        }

        public static async void Init(Dispatcher ui)
        {
            if (_poll != null) return;
            if (!HasIdentity) { Status = "Needs the packaged (MSIX) build of Crest"; return; }
            if (!SettingsStore.Current.MirrorNotifications) { Status = "Off"; return; }
            try
            {
                var listener = global::Windows.UI.Notifications.Management.UserNotificationListener.Current;
                var access = await listener.RequestAccessAsync();
                if (access != global::Windows.UI.Notifications.Management.UserNotificationListenerAccessStatus.Allowed) { Status = "Access denied in Windows Settings → Notifications"; return; }
                Status = "Mirroring";
                foreach (var n in await listener.GetNotificationsAsync(global::Windows.UI.Notifications.NotificationKinds.Toast)) Seen.Add(n.Id);
                if (_poll != null) return;
                var t = _poll = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, async (s, e) =>
                {
                    try
                    {
                        foreach (var n in await listener.GetNotificationsAsync(global::Windows.UI.Notifications.NotificationKinds.Toast))
                        {
                            if (!Seen.Add(n.Id)) continue;
                            string app = n.AppInfo?.DisplayInfo?.DisplayName ?? "App";
                            var binding = n.Notification?.Visual?.GetBinding(global::Windows.UI.Notifications.KnownNotificationBindings.ToastGeneric);
                            var texts = binding?.GetTextElements().Select(x => x.Text).Where(x => !string.IsNullOrWhiteSpace(x)).ToList() ?? new List<string>();
                            string title = texts.FirstOrDefault() ?? app;
                            string body = texts.Skip(1).FirstOrDefault() ?? "";
                            bool call = (title + " " + body).IndexOf("call", StringComparison.OrdinalIgnoreCase) >= 0;
                            NotchWindow.Instance?.ShowToast(new Toast
                            {
                                Kind = "notif", Glyph = call ? Glyphs.Phone : Glyphs.Message, GlyphBrush = Ui.Res(call ? "GreenBrush" : "BlueBrush"),
                                Title = call ? title : $"{app} · {title}", Value = call ? app : (body.Length > 34 ? body.Substring(0, 34) + "…" : body),
                                Duration = TimeSpan.FromSeconds(call ? 12 : 4)
                            });
                        }
                    }
                    catch { }
                }, ui);
                t.Start();
            }
            catch (Exception ex) { Status = "Unavailable: " + ex.Message; Log.Error("notification mirror", ex); }
        }
    }
}
