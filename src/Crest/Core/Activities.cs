using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace Crest.Core
{
    /// <summary>
    /// A persistent live activity (timer, media, recording, script, alarm, third-party...).
    /// The highest-priority one fills the notch; the second becomes the split "bubble".
    /// </summary>
    public sealed class LiveActivity
    {
        public string Id = "";
        public int Priority;
        public string Glyph = "";
        public bool Emoji;
        public Brush? GlyphBrush;
        public string Title = "";
        public string Value = "";
        public Brush? ValueBrush;
        public double? Progress;
        public bool Pulse;
        public ImageSource? Art;
        public string? Tab;
        /// <summary>Changing this makes a dismissed activity visible again (e.g. a new track, a new timer phase).</summary>
        public string Signature = "";
        public List<(string Id, string Label)> Actions = new List<(string, string)>();
        public Action<string>? OnAction;

        public string Key => Id + "|" + Signature;
    }

    public static class ActivityHub
    {
        static readonly List<Func<IEnumerable<LiveActivity>>> Providers = new List<Func<IEnumerable<LiveActivity>>>();

        /// <summary>Raised (UI thread) when any provider's activities change.</summary>
        public static event Action? Changed;

        public static void Register(Func<IEnumerable<LiveActivity>> provider) => Providers.Add(provider);

        public static void Notify() => Changed?.Invoke();

        public static List<LiveActivity> Current()
        {
            var list = new List<LiveActivity>();
            foreach (var p in Providers)
            {
                try { list.AddRange(p()); }
                catch (Exception ex) { Log.Error("activity provider", ex); }
            }
            return list.OrderByDescending(a => a.Priority).ToList();
        }
    }
}
