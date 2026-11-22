using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Crest.Core;

namespace Crest.Services
{
    public sealed class ClipItem : INotifyPropertyChanged
    {
        bool _pinned;
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Kind { get; set; } = "text"; // text | image | files
        public string? Text { get; set; }
        public List<string>? Files { get; set; }
        public string? ImagePath { get; set; }
        public DateTime Time { get; set; } = DateTime.Now;
        public bool Pinned { get => _pinned; set { _pinned = value; Raise(nameof(Pinned)); Raise(nameof(PinGlyph)); } }

        [JsonIgnore] public BitmapSource? Image { get; set; }
        [JsonIgnore] public ImageSource? Thumb => Kind == "image" ? Image : Kind == "files" && Files?.Count > 0 ? ShellIcons.Get(Files[0], 48) : null;
        [JsonIgnore] public string Glyph => Kind == "files" ? Glyphs.Folder : Kind == "image" ? "" : IsLink ? Glyphs.Link : Glyphs.Document;
        [JsonIgnore] public string PinGlyph => Pinned ? Glyphs.Unpin : Glyphs.Pin;
        [JsonIgnore] public bool IsLink => Kind == "text" && Text != null && (Text.StartsWith("http://") || Text.StartsWith("https://")) && !Text.Contains('\n');
        [JsonIgnore]
        public string Preview
        {
            get
            {
                if (Kind == "image") return Image != null ? $"Image  {Image.PixelWidth}×{Image.PixelHeight}" : "Image";
                if (Kind == "files") return Files == null ? "" : Files.Count == 1 ? Path.GetFileName(Files[0]) : $"{Files.Count} files — {string.Join(", ", Files.Take(3).Select(Path.GetFileName))}";
                var t = (Text ?? "").Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
                while (t.Contains("  ")) t = t.Replace("  ", " ");
                return t.Length > 220 ? t.Substring(0, 220) + "…" : t;
            }
        }
        [JsonIgnore]
        public string Subtitle
        {
            get
            {
                var what = Kind == "image" ? "Image" : Kind == "files" ? "Files" : IsLink ? "Link" : $"{(Text ?? "").Length} chars";
                return $"{what}  ·  {Ui.Ago(Time)}";
            }
        }

        public void Touch() { Raise(nameof(Subtitle)); }

        public event PropertyChangedEventHandler? PropertyChanged;
        void Raise(string p) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    /// <summary>Clipboard history (text, images, files) with pinning. Respects password-manager "don't record" flags.</summary>
    public sealed class ClipboardService
    {
        public static ClipboardService Instance { get; } = new ClipboardService();
        const int MaxItems = 80;
        static readonly string ImgDir = Path.Combine(Storage.Dir, "clips");

        public ObservableCollection<ClipItem> Items { get; } = new ObservableCollection<ClipItem>();
        IntPtr _hwnd;
        bool _listening;
        DateTime _ignoreUntil;
        Debouncer? _saver;
        Debouncer? _reader;

        public void Init(HwndSource source)
        {
            _hwnd = source.Handle;
            _saver = new Debouncer(TimeSpan.FromMilliseconds(800), Save);
            _reader = new Debouncer(TimeSpan.FromMilliseconds(90), Read);
            Load();
            source.AddHook(WndProc);
            Apply();
        }

        /// <summary>Start/stop listening according to settings.</summary>
        public void Apply()
        {
            bool want = SettingsStore.Current.ClipboardHistory;
            if (want && !_listening) _listening = Win32.AddClipboardFormatListener(_hwnd);
            else if (!want && _listening) { Win32.RemoveClipboardFormatListener(_hwnd); _listening = false; }
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Win32.WM_CLIPBOARDUPDATE && _listening) _reader?.Trigger();
            return IntPtr.Zero;
        }

        void Read()
        {
            if (DateTime.Now < _ignoreUntil) return;
            IDataObject? data = null;
            for (int i = 0; i < 6 && data == null; i++)
            {
                try { data = Clipboard.GetDataObject(); }
                catch (COMException) { System.Threading.Thread.Sleep(25); }
                catch { return; }
            }
            if (data == null) return;
            try
            {
                if (data.GetDataPresent("ExcludeClipboardContentFromMonitorProcessing") || data.GetDataPresent("Clipboard Viewer Ignore")) return;
                if (data.GetDataPresent("CanIncludeInClipboardHistory") && data.GetData("CanIncludeInClipboardHistory") is MemoryStream ms)
                {
                    var b = ms.ToArray();
                    if (b.Length >= 4 && BitConverter.ToInt32(b, 0) == 0) return;
                }

                ClipItem? item = null;
                if (data.GetDataPresent(DataFormats.FileDrop) && data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
                    item = new ClipItem { Kind = "files", Files = files.ToList() };
                else if (data.GetDataPresent(DataFormats.UnicodeText) && data.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrWhiteSpace(text))
                    item = new ClipItem { Kind = "text", Text = text.Length > 200_000 ? text.Substring(0, 200_000) : text };
                else if (data.GetDataPresent(DataFormats.Bitmap))
                {
                    var img = Clipboard.GetImage();
                    if (img != null)
                    {
                        var copy = new WriteableBitmap(img);
                        copy.Freeze();
                        item = new ClipItem { Kind = "image", Image = copy };
                    }
                }
                if (item != null) Add(item);
            }
            catch (Exception ex) { Log.Error("clipboard read", ex); }
        }

        void Add(ClipItem item)
        {
            var dup = Items.FirstOrDefault(x => x.Kind == item.Kind && x.Kind != "image" &&
                (x.Text == item.Text) && (x.Files == null && item.Files == null || x.Files != null && item.Files != null && x.Files.SequenceEqual(item.Files)));
            if (dup != null)
            {
                dup.Time = DateTime.Now;
                dup.Touch();
                if (!dup.Pinned) { Items.Remove(dup); Items.Insert(FirstUnpinnedIndex(), dup); }
                _saver?.Trigger();
                return;
            }
            Items.Insert(FirstUnpinnedIndex(), item);
            while (Items.Count(x => !x.Pinned) > MaxItems)
            {
                var last = Items.Last(x => !x.Pinned);
                Items.Remove(last);
            }
            _saver?.Trigger();
        }

        int FirstUnpinnedIndex()
        {
            int i = 0;
            while (i < Items.Count && Items[i].Pinned) i++;
            return i;
        }

        public void Copy(ClipItem item)
        {
            _ignoreUntil = DateTime.Now.AddMilliseconds(600);
            try
            {
                if (item.Kind == "text" && item.Text != null) Clipboard.SetText(item.Text);
                else if (item.Kind == "image" && item.Image != null) Clipboard.SetImage(item.Image);
                else if (item.Kind == "files" && item.Files != null)
                {
                    var sc = new StringCollection();
                    sc.AddRange(item.Files.ToArray());
                    Clipboard.SetFileDropList(sc);
                }
            }
            catch (Exception ex) { Log.Error("clipboard copy", ex); return; }
            item.Time = DateTime.Now;
            item.Touch();
            if (!item.Pinned) { Items.Remove(item); Items.Insert(FirstUnpinnedIndex(), item); }
            _saver?.Trigger();
        }

        public void TogglePin(ClipItem item)
        {
            item.Pinned = !item.Pinned;
            Items.Remove(item);
            Items.Insert(item.Pinned ? 0 : FirstUnpinnedIndex(), item);
            _saver?.Trigger();
        }

        public void Remove(ClipItem item)
        {
            Items.Remove(item);
            if (item.ImagePath != null) try { File.Delete(item.ImagePath); } catch { }
            _saver?.Trigger();
        }

        public void ClearUnpinned()
        {
            foreach (var i in Items.Where(x => !x.Pinned).ToList()) Items.Remove(i);
            _saver?.Trigger();
        }

        void Load()
        {
            var list = Storage.Load("clipboard.json", () => new List<ClipItem>());
            foreach (var i in list)
            {
                if (i.Kind == "image")
                {
                    if (i.ImagePath == null || !File.Exists(i.ImagePath)) continue;
                    try
                    {
                        var bi = new BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = BitmapCacheOption.OnLoad;
                        bi.UriSource = new Uri(i.ImagePath);
                        bi.EndInit();
                        bi.Freeze();
                        i.Image = bi;
                    }
                    catch { continue; }
                }
                Items.Add(i);
            }
        }

        void Save()
        {
            // Images only persist when pinned (keeps disk use tiny and avoids storing screenshots by accident).
            try
            {
                foreach (var i in Items.Where(x => x.Kind == "image" && x.Pinned && x.ImagePath == null && x.Image != null))
                {
                    Directory.CreateDirectory(ImgDir);
                    var path = Path.Combine(ImgDir, i.Id + ".png");
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(i.Image!));
                    using var fs = File.Create(path);
                    enc.Save(fs);
                    i.ImagePath = path;
                }
            }
            catch (Exception ex) { Log.Error("clip image save", ex); }
            var toSave = Items.Where(x => x.Kind != "image" || (x.Pinned && x.ImagePath != null)).ToList();
            Storage.Save("clipboard.json", toSave);
        }
    }
}
