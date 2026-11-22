using System;
using System.ComponentModel;
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

        public ClipboardView()
        {
            InitializeComponent();
            var svc = ClipboardService.Instance;
            _view = CollectionViewSource.GetDefaultView(svc.Items);
            _view.Filter = Filter;
            List.ItemsSource = _view;
            SearchBox.TextChanged += (s, e) => { _view.Refresh(); UpdateState(); };
            SearchBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && !_view.IsEmpty)
                {
                    foreach (var o in _view) { if (o is ClipItem c) { CopyAndClose(c); break; } }
                }
            };
            ClearBtn.Click += (s, e) => svc.ClearUnpinned();
            svc.Items.CollectionChanged += (s, e) => UpdateState();
            UpdateState();
        }

        public void OnActivated()
        {
            foreach (var i in ClipboardService.Instance.Items) i.Touch();
            Scroller.ScrollToTop();
            UpdateState();
        }

        public void OnDeactivated()
        {
            if (SearchBox.Text.Length > 0) SearchBox.Text = "";
        }

        bool Filter(object o)
        {
            var q = SearchBox?.Text?.Trim();
            if (string.IsNullOrEmpty(q)) return true;
            if (o is not ClipItem c) return false;
            if (c.Text != null && c.Text.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            if (c.Files != null) foreach (var f in c.Files) if (f.Contains(q, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        void UpdateState()
        {
            int total = ClipboardService.Instance.Items.Count;
            CountText.Text = total == 0 ? "" : total == 1 ? "1 item" : $"{total} items";
            bool off = !SettingsStore.Current.ClipboardHistory;
            EmptyTitle.Text = off ? "Clipboard history is off" : _view.IsEmpty && total > 0 ? "No matches" : "Copy something — it shows up here";
            EmptySub.Text = off ? "Turn it on in Settings → Clipboard." : "Text, images and files. Click an entry to copy it again. Pin the ones you reuse.";
            EmptyState.Visibility = _view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
            ClearBtn.Visibility = total > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        void CopyAndClose(ClipItem item)
        {
            ClipboardService.Instance.Copy(item);
            NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Copy, Title = "Copied", Value = "paste with Ctrl+V" });
        }

        void OnRowClick(object sender, MouseButtonEventArgs e)
        {
            if (Ui.FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
            if ((sender as FrameworkElement)?.DataContext is ClipItem item) CopyAndClose(item);
        }

        void OnPin(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ClipItem item) ClipboardService.Instance.TogglePin(item);
            e.Handled = true;
        }

        void OnDelete(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is ClipItem item) ClipboardService.Instance.Remove(item);
            e.Handled = true;
        }
    }
}
