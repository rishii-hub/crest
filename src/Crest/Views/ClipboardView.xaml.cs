using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    public partial class ClipboardView : UserControl, ITabView
    {
        readonly ICollectionView _view;
        string _filter = "All";
        readonly (string Key, string Label)[] _filters = { ("All", "All"), ("text", "Text"), ("image", "Images"), ("link", "Links"), ("files", "Files") };

        public ClipboardView()
        {
            InitializeComponent();
            var svc = ClipboardService.Instance;
            _view = CollectionViewSource.GetDefaultView(svc.Items);
            _view.Filter = Filter;
            List.ItemsSource = _view;

            foreach (var (key, label) in _filters)
            {
                var rb = new RadioButton { Style = Ui.StyleRes("Seg"), GroupName = "clip-filter", IsChecked = key == "All", Tag = key, Padding = new Thickness(10, 4, 10, 4) };
                var k = key;
                rb.Checked += (s, e) => { _filter = k; _view.Refresh(); SelectFirst(); UpdateState(); };
                Filters.Children.Add(rb);
            }

            SearchBox.TextChanged += (s, e) => { _view.Refresh(); SelectFirst(); UpdateState(); };
            SearchBox.KeyDown += (s, e) => { if (e.Key == Key.Enter && List.SelectedItem is ClipItem c) CopyItem(c); };
            List.SelectionChanged += (s, e) => ShowPreview();
            List.MouseDoubleClick += (s, e) => { if (List.SelectedItem is ClipItem c) CopyItem(c); };
            CopyBtn.Click += (s, e) => { if (List.SelectedItem is ClipItem c) CopyItem(c); };
            PinBtn.Click += (s, e) => { if (List.SelectedItem is ClipItem c) { svc.TogglePin(c); List.SelectedItem = c; ShowPreview(); } };
            DeleteBtn.Click += (s, e) =>
            {
                if (List.SelectedItem is not ClipItem c) return;
                int i = List.SelectedIndex;
                svc.Remove(c);
                if (List.Items.Count > 0) List.SelectedIndex = Math.Min(i, List.Items.Count - 1);
            };
            ClearBtn.Click += (s, e) => svc.ClearUnpinned();
            svc.Items.CollectionChanged += (s, e) => { UpdateState(); if (List.SelectedItem == null) SelectFirst(); };
            UpdateState();
        }

        public void OnActivated()
        {
            foreach (var i in ClipboardService.Instance.Items) i.Touch();
            SelectFirst();
            UpdateState();
        }

        public void OnDeactivated()
        {
            if (SearchBox.Text.Length > 0) SearchBox.Text = "";
        }

        void SelectFirst()
        {
            if (List.Items.Count > 0) { List.SelectedIndex = 0; List.ScrollIntoView(List.Items[0]); }
            ShowPreview();
        }

        bool Filter(object o)
        {
            if (o is not ClipItem c) return false;
            if (_filter == "link" && !c.IsLink) return false;
            if (_filter == "text" && (c.Kind != "text" || c.IsLink)) return false;
            if (_filter != "All" && _filter != "link" && _filter != "text" && c.Kind != _filter) return false;
            var q = SearchBox?.Text?.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            if (c.Text != null && c.Text.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            if (c.Files != null) foreach (var f in c.Files) if (f.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void UpdateState()
        {
            var items = ClipboardService.Instance.Items;
            int total = items.Count;
            foreach (RadioButton rb in Filters.Children)
            {
                var key = (string)rb.Tag;
                int n = key == "All" ? total : key == "link" ? items.Count(i => i.IsLink) : key == "text" ? items.Count(i => i.Kind == "text" && !i.IsLink) : items.Count(i => i.Kind == key);
                var label = _filters.First(f => f.Key == key).Label;
                rb.Content = n > 0 ? $"{label}  {n}" : label;
            }
            bool off = !SettingsStore.Current.ClipboardHistory;
            EmptyTitle.Text = off ? "Clipboard history is off" : _view.IsEmpty && total > 0 ? "Nothing here" : "Copy something — it shows up here";
            EmptySub.Text = off ? "Turn it on in Settings → Clipboard." : "Text, images and files. Double-click an entry to copy it again. Pin the ones you reuse.";
            bool empty = _view.IsEmpty;
            EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
            List.Visibility = PreviewCard.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;
            ClearBtn.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
            NotchWindow.Instance?.UpdateHeader();
        }

        void ShowPreview()
        {
            if (List.SelectedItem is not ClipItem c) { PreviewText.Text = ""; PreviewImage.Source = null; PreviewKind.Text = ""; PreviewStats.Text = ""; return; }
            PreviewKind.Text = c.Subtitle;
            PinBtn.Content = c.PinGlyph;
            if (c.Kind == "image")
            {
                PreviewText.Text = "";
                PreviewImage.Source = c.Image;
                PreviewStats.Text = c.Image != null ? $"{c.Image.PixelWidth} × {c.Image.PixelHeight}" : "";
            }
            else
            {
                PreviewImage.Source = null;
                var text = c.Kind == "files" ? string.Join("\n", c.Files ?? new System.Collections.Generic.List<string>()) : c.Text ?? "";
                PreviewText.Text = text.Length > 6000 ? text.Substring(0, 6000) + "…" : text;
                int words = text.Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries).Length;
                PreviewStats.Text = c.Kind == "files" ? $"{c.Files?.Count ?? 0} file(s)" : $"{words} words · {text.Length} chars";
            }
        }

        void CopyItem(ClipItem item)
        {
            ClipboardService.Instance.Copy(item);
            List.SelectedItem = item;
            NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Copy, Title = "Copied", Value = "paste with Ctrl+V" });
        }
    }
}
