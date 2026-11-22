using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Crest.Core;
using GSMTC = global::Windows.Media.Control;
using WF = global::Windows.Foundation;

namespace Crest.Services
{
    /// <summary>Now Playing via Windows' System Media Transport Controls (Spotify, browsers, Apple Music, VLC, ...).</summary>
    public sealed class MediaService
    {
        public static MediaService Instance { get; } = new MediaService();

        Dispatcher _ui = null!;
        GSMTC.GlobalSystemMediaTransportControlsSessionManager? _mgr;
        GSMTC.GlobalSystemMediaTransportControlsSession? _session;
        readonly List<GSMTC.GlobalSystemMediaTransportControlsSession> _watched = new List<GSMTC.GlobalSystemMediaTransportControlsSession>();
        WF.TypedEventHandler<GSMTC.GlobalSystemMediaTransportControlsSession, GSMTC.MediaPropertiesChangedEventArgs>? _onProps;
        WF.TypedEventHandler<GSMTC.GlobalSystemMediaTransportControlsSession, GSMTC.PlaybackInfoChangedEventArgs>? _onPlayback;
        WF.TypedEventHandler<GSMTC.GlobalSystemMediaTransportControlsSession, GSMTC.TimelinePropertiesChangedEventArgs>? _onTimeline;
        int _propsVersion;

        public bool HasSession => _session != null;
        public bool IsPlaying { get; private set; }
        public string Title { get; private set; } = "";
        public string Artist { get; private set; } = "";
        public string AppName { get; private set; } = "";
        public BitmapSource? Art { get; private set; }
        public Color? ArtColor { get; private set; }
        public bool CanSeek { get; private set; }
        public bool CanNext { get; private set; }
        public bool CanPrev { get; private set; }
        public TimeSpan Duration { get; private set; }
        TimeSpan _position;
        DateTimeOffset _positionAt;

        /// <summary>Raised on the UI thread whenever anything about now-playing changes.</summary>
        public event Action? Changed;

        public TimeSpan Position
        {
            get
            {
                var p = _position;
                if (IsPlaying) p += DateTimeOffset.Now - _positionAt;
                if (Duration > TimeSpan.Zero && p > Duration) p = Duration;
                return p < TimeSpan.Zero ? TimeSpan.Zero : p;
            }
        }

        public async void Init(Dispatcher ui)
        {
            _ui = ui;
            _onProps = (s, e) => _ui.BeginInvoke(new Action(async () => { if (Same(s, _session)) await RefreshPropsAsync(); }));
            _onPlayback = (s, e) => _ui.BeginInvoke(new Action(Pick));
            _onTimeline = (s, e) => _ui.BeginInvoke(new Action(() => { if (Same(s, _session)) { RefreshTimeline(); Changed?.Invoke(); } }));
            try
            {
                _mgr = await GSMTC.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                _mgr.CurrentSessionChanged += (s, e) => _ui.BeginInvoke(new Action(Pick));
                _mgr.SessionsChanged += (s, e) => _ui.BeginInvoke(new Action(Pick));
                Pick();
            }
            catch (Exception ex) { Log.Error("media init", ex); }
        }

        /// <summary>Choose the session to show: a playing one wins over the "current" one (e.g. Spotify playing while a paused YouTube tab is focused).</summary>
        void Pick()
        {
            if (_mgr == null) return;
            IReadOnlyList<GSMTC.GlobalSystemMediaTransportControlsSession> sessions;
            try { sessions = _mgr.GetSessions(); } catch { return; }

            foreach (var s in _watched.Except(sessions).ToList())
            {
                try { s.PlaybackInfoChanged -= _onPlayback; s.MediaPropertiesChanged -= _onProps; s.TimelinePropertiesChanged -= _onTimeline; } catch { }
                _watched.Remove(s);
            }
            foreach (var s in sessions.Except(_watched).ToList())
            {
                try { s.PlaybackInfoChanged += _onPlayback; s.MediaPropertiesChanged += _onProps; s.TimelinePropertiesChanged += _onTimeline; } catch { }
                _watched.Add(s);
            }

            GSMTC.GlobalSystemMediaTransportControlsSession? chosen = null;
            try { chosen = _mgr.GetCurrentSession(); } catch { }
            if (chosen == null || !IsSessionPlaying(chosen))
            {
                var playing = sessions.FirstOrDefault(IsSessionPlaying);
                if (playing != null) chosen = playing;
                else if (_session != null && sessions.Contains(_session) && chosen == null) chosen = _session;
            }

            bool switched = !Same(chosen, _session);
            _session = chosen;
            RefreshPlayback();
            RefreshTimeline();
            if (switched || string.IsNullOrEmpty(Title)) _ = RefreshPropsAsync();
            else Changed?.Invoke();
        }

        static bool Same(GSMTC.GlobalSystemMediaTransportControlsSession? a, GSMTC.GlobalSystemMediaTransportControlsSession? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a == null || b == null) return false;
            try { return a.SourceAppUserModelId == b.SourceAppUserModelId; } catch { return false; }
        }

        static bool IsSessionPlaying(GSMTC.GlobalSystemMediaTransportControlsSession s)
        {
            try { return s.GetPlaybackInfo()?.PlaybackStatus == GSMTC.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing; }
            catch { return false; }
        }

        void RefreshPlayback()
        {
            if (_session == null) { IsPlaying = false; CanSeek = CanNext = CanPrev = false; return; }
            try
            {
                var info = _session.GetPlaybackInfo();
                IsPlaying = info.PlaybackStatus == GSMTC.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                CanSeek = info.Controls.IsPlaybackPositionEnabled;
                CanNext = info.Controls.IsNextEnabled;
                CanPrev = info.Controls.IsPreviousEnabled;
                AppName = FriendlyApp(_session.SourceAppUserModelId);
            }
            catch { IsPlaying = false; }
        }

        void RefreshTimeline()
        {
            if (_session == null) { Duration = TimeSpan.Zero; _position = TimeSpan.Zero; return; }
            try
            {
                var tl = _session.GetTimelineProperties();
                Duration = tl.EndTime - tl.StartTime;
                _position = tl.Position - tl.StartTime;
                _positionAt = tl.LastUpdatedTime.Year < 2000 ? DateTimeOffset.Now : tl.LastUpdatedTime;
                if (_positionAt > DateTimeOffset.Now) _positionAt = DateTimeOffset.Now;
            }
            catch { }
        }

        async Task RefreshPropsAsync()
        {
            int version = ++_propsVersion;
            var session = _session;
            if (session == null)
            {
                Title = Artist = AppName = "";
                Art = null; ArtColor = null; Duration = TimeSpan.Zero;
                Changed?.Invoke();
                return;
            }
            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                if (version != _propsVersion) return;
                Title = props?.Title ?? "";
                Artist = props?.Artist ?? "";
                if (string.IsNullOrEmpty(Artist)) Artist = props?.AlbumArtist ?? "";
                BitmapSource? art = null;
                if (props?.Thumbnail != null)
                {
                    try
                    {
                        using var ras = await props.Thumbnail.OpenReadAsync();
                        using var stream = ras.AsStreamForRead();
                        var ms = new MemoryStream();
                        await stream.CopyToAsync(ms);
                        ms.Position = 0;
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.DecodePixelWidth = 240;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        bi.Freeze();
                        art = bi;
                    }
                    catch (Exception ex) { Log.Error("media art", ex); }
                }
                if (version != _propsVersion) return;
                Art = art;
                ArtColor = ColorUtil.Dominant(art);
            }
            catch (Exception ex) { Log.Error("media props", ex); }
            RefreshPlayback();
            RefreshTimeline();
            Changed?.Invoke();
        }

        public async Task TogglePlayPauseAsync() { try { if (_session != null) await _session.TryTogglePlayPauseAsync(); } catch { } }
        public async Task NextAsync() { try { if (_session != null) await _session.TrySkipNextAsync(); } catch { } }
        public async Task PreviousAsync() { try { if (_session != null) await _session.TrySkipPreviousAsync(); } catch { } }

        public async Task SeekAsync(TimeSpan to)
        {
            try
            {
                if (_session == null || !CanSeek) return;
                _position = to; _positionAt = DateTimeOffset.Now;
                await _session.TryChangePlaybackPositionAsync(to.Ticks);
            }
            catch { }
        }

        public static string FriendlyApp(string? aumid)
        {
            if (string.IsNullOrEmpty(aumid)) return "";
            var s = aumid;
            int bang = s.LastIndexOf('!');
            if (bang >= 0)
            {
                var pkg = s.Substring(0, bang);
                int us = pkg.IndexOf('_');
                if (us > 0) pkg = pkg.Substring(0, us);
                int dot = pkg.LastIndexOf('.');
                s = dot >= 0 ? pkg.Substring(dot + 1) : pkg;
            }
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = s.Substring(0, s.Length - 4);
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["chrome"] = "Chrome", ["msedge"] = "Edge", ["firefox"] = "Firefox", ["spotify"] = "Spotify", ["SpotifyMusic"] = "Spotify",
                ["ZuneMusic"] = "Media Player", ["AppleMusicWin"] = "Apple Music", ["vlc"] = "VLC", ["brave"] = "Brave", ["opera"] = "Opera",
                ["308046B0AF4A39CB"] = "Firefox"
            };
            if (map.TryGetValue(s, out var nice)) return nice;
            return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;
        }
    }
}
