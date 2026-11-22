using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using System.Windows.Media;
using Crest.Core;

namespace Crest.Services
{
    /// <summary>A file, folder, link or text snippet on the Shelf or in Quick Launch.</summary>
    public sealed class TrayItem
    {
        public string Kind { get; set; } = "file"; // file | folder | url | text
        public string Value { get; set; } = "";     // path, url or text
        public string Name { get; set; } = "";
        public DateTime Added { get; set; } = DateTime.Now;

        [JsonIgnore] public ImageSource? Icon => Kind == "file" || Kind == "folder" ? ShellIcons.Get(Value, 96) : null;
        [JsonIgnore] public string Glyph => Kind == "url" ? Glyphs.Globe : Kind == "text" ? Glyphs.Note : Kind == "folder" ? Glyphs.Folder : Glyphs.Document;
        [JsonIgnore] public bool Exists => Kind == "url" || Kind == "text" || File.Exists(Value) || Directory.Exists(Value);

        public static TrayItem FromPath(string path)
        {
            bool dir = Directory.Exists(path);
            var name = dir ? new DirectoryInfo(path).Name : Path.GetFileName(path);
            if (!dir && path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(name)) name = path;
            return new TrayItem { Kind = dir ? "folder" : "file", Value = path, Name = name };
        }

        public static TrayItem FromText(string text)
        {
            var t = text.Trim();
            if ((t.StartsWith("http://") || t.StartsWith("https://")) && !t.Contains(' ') && !t.Contains('\n'))
            {
                string name = t;
                try { name = new Uri(t).Host.Replace("www.", ""); } catch { }
                return new TrayItem { Kind = "url", Value = t, Name = name };
            }
            var first = t.Split('\n')[0].Trim();
            return new TrayItem { Kind = "text", Value = text, Name = first.Length > 40 ? first.Substring(0, 40) + "…" : first };
        }
    }

    public sealed class TrayStore
    {
        public static TrayStore Shelf { get; } = new TrayStore("shelf.json");
        public static TrayStore Launch { get; } = new TrayStore("launch.json");

        readonly string _file;
        Debouncer? _saver;
        bool _loaded;
        public ObservableCollection<TrayItem> Items { get; } = new ObservableCollection<TrayItem>();

        TrayStore(string file) { _file = file; }

        public void EnsureLoaded()
        {
            if (_loaded) return;
            _loaded = true;
            _saver = new Debouncer(TimeSpan.FromMilliseconds(500), () => Storage.Save(_file, Items.ToList()));
            foreach (var i in Storage.Load(_file, () => new List<TrayItem>()))
                if (i.Exists) Items.Add(i);
        }

        public bool Add(TrayItem item)
        {
            EnsureLoaded();
            if (Items.Any(x => x.Kind == item.Kind && string.Equals(x.Value, item.Value, StringComparison.OrdinalIgnoreCase))) return false;
            Items.Add(item);
            _saver!.Trigger();
            return true;
        }

        public void Remove(TrayItem item)
        {
            Items.Remove(item);
            _saver?.Trigger();
        }

        public void Clear()
        {
            Items.Clear();
            _saver?.Trigger();
        }

        public void Move(TrayItem item, int index)
        {
            int from = Items.IndexOf(item);
            if (from < 0) return;
            index = Math.Clamp(index, 0, Items.Count - 1);
            Items.Move(from, index);
            _saver?.Trigger();
        }
    }

    public sealed class TodoItem : INotifyPropertyChanged
    {
        string _text = "";
        bool _done;
        public string Text { get => _text; set { _text = value; Raise(nameof(Text)); } }
        public bool Done { get => _done; set { _done = value; Raise(nameof(Done)); } }
        public event PropertyChangedEventHandler? PropertyChanged;
        void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public sealed class NotesData
    {
        public string Text { get; set; } = "";
        public List<TodoItem> Todos { get; set; } = new List<TodoItem>();
    }

    public static class NotesStore
    {
        static Debouncer? _saver;
        public static NotesData Data { get; private set; } = new NotesData();

        public static void Load() => Data = Storage.Load("notes.json", () => new NotesData());

        public static void Save()
        {
            _saver ??= new Debouncer(TimeSpan.FromMilliseconds(600), () => Storage.Save("notes.json", Data));
            _saver.Trigger();
        }
    }
}
