using System;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    /// <summary>
    /// Shelf (NotchNook-style file tray: drop in, drag out anywhere) and Quick Launch (drop apps/folders/links, click to open).
    /// </summary>
    public sealed class TrayView : UserControl, ITabView
    {
        readonly bool _shelf;
        readonly TrayStore _store;
        readonly WrapPanel _tiles = new WrapPanel();
        readonly Border _drop;
        readonly TextBlock _count = new TextBlock();
        readonly Button _clear, _dragAll;
        readonly TextBox? _url;
        Point _downPos;
        TrayItem? _downItem;

        public TrayView(bool shelf)
        {
            _shelf = shelf;
            _store = shelf ? TrayStore.Shelf : TrayStore.Launch;
            _store.EnsureLoaded();
            AllowDrop = true;

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // header
            var header = new DockPanel { Margin = new Thickness(4, 0, 0, 8), LastChildFill = true };
            var right = new StackPanel { Orientation = Orientation.Horizontal };
            DockPanel.SetDock(right, Dock.Right);
            _dragAll = new Button { Style = Ui.StyleRes("Pill"), Content = "Drag all", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Drag every file out at once", Cursor = Cursors.SizeAll };
            _dragAll.PreviewMouseLeftButtonDown += (s, e) => { _downPos = e.GetPosition(this); _downItem = null; };
            _dragAll.PreviewMouseMove += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed && Far(e.GetPosition(this))) DragOut(_store.Items.Where(i => i.Kind == "file" || i.Kind == "folder").ToList());
            };
            _clear = new Button { Style = Ui.StyleRes("Pill"), Content = "Clear" };
            _clear.Click += (s, e) => _store.Clear();
            var paste = new Button { Style = Ui.StyleRes("Pill"), Content = "Paste", Margin = new Thickness(0, 0, 6, 0), ToolTip = "Add what's on the clipboard" };
            paste.Click += (s, e) => PasteFromClipboard();
            var choose = new Button { Style = Ui.StyleRes("Pill"), Content = shelf ? "Choose files" : "Add app", Margin = new Thickness(0, 0, 6, 0) };
            choose.Click += (s, e) => ChooseFiles();
            right.Children.Add(paste);
            right.Children.Add(choose);
            if (shelf) right.Children.Add(_dragAll);
            right.Children.Add(_clear);
            header.Children.Add(right);
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = shelf ? "Shelf" : "Pinned", Style = Ui.StyleRes("H1") });
            _count.Style = Ui.StyleRes("Caption");
            titles.Children.Add(_count);
            header.Children.Add(titles);
            root.Children.Add(header);

            // drop area + tiles
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = _tiles };
            var hint = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
            hint.Children.Add(new TextBlock { Text = shelf ? Glyphs.Folder : Glyphs.Apps, Style = Ui.StyleRes("Icon"), FontSize = 28, Foreground = Ui.Res("Text3Brush") });
            hint.Children.Add(new TextBlock
            {
                Text = shelf ? "Drop files, folders or text here" : "Drop apps, shortcuts, folders or links here",
                Margin = new Thickness(0, 10, 0, 2), FontSize = 13.5, HorizontalAlignment = HorizontalAlignment.Center
            });
            hint.Children.Add(new TextBlock
            {
                Text = shelf ? "They wait here until you drag them into another app." : "Click a tile to open it. Drag tiles to reorder.",
                Style = Ui.StyleRes("Caption"), HorizontalAlignment = HorizontalAlignment.Center
            });
            var layer = new Grid();
            layer.Children.Add(hint);
            layer.Children.Add(scroll);
            _drop = new Border
            {
                CornerRadius = new CornerRadius(18), Background = Ui.Res("CardBrush"), BorderThickness = new Thickness(1.5),
                BorderBrush = Brushes.Transparent, Padding = new Thickness(10), Child = layer
            };
            Grid.SetRow(_drop, 2);
            if (!shelf) root.Children.Add(BuildSearch());
            root.Children.Add(_drop);
            hint.Visibility = Visibility.Visible;
            _store.Items.CollectionChanged += (s, e) => { Rebuild(); hint.Visibility = _store.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed; };
            hint.Visibility = _store.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            if (!shelf)
            {
                _url = new TextBox { Style = Ui.StyleRes("Field"), Tag = "Paste a link and press Enter to pin it", Margin = new Thickness(0, 8, 0, 0) };
                _url.KeyDown += (s, e) =>
                {
                    if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(_url.Text)) return;
                    var t = _url.Text.Trim();
                    if (!t.Contains("://")) t = "https://" + t;
                    _store.Add(TrayItem.FromText(t));
                    _url.Text = "";
                };
                Grid.SetRow(_url, 3);
                root.Children.Add(_url);
            }

            Content = root;
            DragEnter += (s, e) => { SetDropHighlight(true); e.Handled = true; };
            DragOver += (s, e) => { e.Effects = Accepts(e.Data) ? DragDropEffects.Copy | DragDropEffects.Link : DragDropEffects.None; e.Handled = true; };
            DragLeave += (s, e) => SetDropHighlight(false);
            Drop += OnDrop;
            Rebuild();
        }

        static readonly (string Name, string Url)[] Engines =
        {
            ("Google", "https://www.google.com/search?q="), ("YouTube", "https://www.youtube.com/results?search_query="),
            ("GitHub", "https://github.com/search?type=repositories&q="), ("Stack Overflow", "https://stackoverflow.com/search?q="),
            ("ChatGPT", "https://chatgpt.com/?q="), ("Claude", "https://claude.ai/new?q="), ("Perplexity", "https://www.perplexity.ai/search?q="),
            ("Wikipedia", "https://en.wikipedia.org/w/index.php?search=")
        };

        FrameworkElement BuildSearch()
        {
            int engine = 0;
            var box = new TextBox { Style = Ui.StyleRes("Field"), Tag = "Search the web — Enter to search", Background = Brushes.Transparent, Padding = new Thickness(4, 6, 4, 6), FontSize = 14 };
            var go = new Button { Style = Ui.StyleRes("Pill"), Content = "Search ↗", Foreground = Brushes.Black, Padding = new Thickness(14, 6, 14, 6) };
            go.SetResourceReference(BackgroundProperty, "AccentBrush");
            void search()
            {
                var q = box.Text.Trim();
                if (q.Length == 0) return;
                Ui.Open(Engines[engine].Url + Uri.EscapeDataString(q));
                box.Text = "";
                NotchWindow.Instance?.Collapse();
            }
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter) search(); };
            go.Click += (s, e) => search();
            var top = new DockPanel();
            DockPanel.SetDock(go, Dock.Right);
            top.Children.Add(go);
            top.Children.Add(box);
            var chips = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 0; i < Engines.Length; i++)
            {
                var rb = new RadioButton { Style = Ui.StyleRes("Seg"), Content = Engines[i].Name, GroupName = "engines", IsChecked = i == 0, Padding = new Thickness(9, 3, 9, 3), FontSize = 11.5 };
                int idx = i;
                rb.Checked += (s, e) => engine = idx;
                chips.Children.Add(rb);
            }
            var stack = new StackPanel();
            stack.Children.Add(top);
            stack.Children.Add(chips);
            var card = new Border { Style = Ui.StyleRes("Card"), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 0, 8), Child = stack };
            Grid.SetRow(card, 1);
            return card;
        }

        void PasteFromClipboard()
        {
            int added = 0;
            try
            {
                if (Clipboard.ContainsFileDropList())
                    foreach (var f in Clipboard.GetFileDropList()) { if (f != null && _store.Add(TrayItem.FromPath(f))) added++; }
                else if (Clipboard.ContainsText() && !string.IsNullOrWhiteSpace(Clipboard.GetText()))
                    { if (_store.Add(TrayItem.FromText(Clipboard.GetText()))) added++; }
            }
            catch (Exception ex) { Log.Error("paste", ex); }
            NotchWindow.Instance?.ShowBanner(added > 0
                ? new Toast { Glyph = Glyphs.Check, GlyphBrush = Ui.Res("GreenBrush"), Title = added == 1 ? "Added 1 item" : $"Added {added} items" }
                : new Toast { Glyph = Glyphs.Paste, Title = "Nothing to paste", Value = "copy files or a link first" });
        }

        void ChooseFiles()
        {
            var notch = NotchWindow.Instance;
            if (notch != null) notch.DragOut = true; // keep the notch open while the dialog is up
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Multiselect = true,
                    Title = _shelf ? "Add files to the Shelf" : "Add apps or files to Links",
                    Filter = _shelf ? "All files|*.*" : "Apps and shortcuts|*.exe;*.lnk;*.url;*.bat;*.cmd|All files|*.*",
                    InitialDirectory = _shelf ? Environment.GetFolderPath(Environment.SpecialFolder.Desktop) : Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu)
                };
                if (dlg.ShowDialog() == true)
                    foreach (var f in dlg.FileNames) _store.Add(TrayItem.FromPath(f));
            }
            catch (Exception ex) { Log.Error("choose", ex); }
            finally { if (notch != null) notch.DragOut = false; }
        }

        public void OnActivated()
        {
            foreach (var dead in _store.Items.Where(i => !i.Exists).ToList()) _store.Remove(dead);
            UpdateHeader();
        }

        public void OnDeactivated() { }

        static bool Accepts(IDataObject d) => d.GetDataPresent(DataFormats.FileDrop) || d.GetDataPresent(DataFormats.UnicodeText);

        void SetDropHighlight(bool on) => _drop.BorderBrush = on ? (Brush)FindResource("AccentBrush") : Brushes.Transparent;

        void OnDrop(object sender, DragEventArgs e)
        {
            SetDropHighlight(false);
            e.Handled = true;
            if (e.Data.GetDataPresent("CrestInternal")) return;
            int added = 0;
            if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            {
                foreach (var f in files) if (_store.Add(TrayItem.FromPath(f))) added++;
            }
            else if (e.Data.GetData(DataFormats.UnicodeText) is string text && !string.IsNullOrWhiteSpace(text))
            {
                if (_store.Add(TrayItem.FromText(text))) added++;
            }
            if (added > 0)
                NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Check, GlyphBrush = Ui.Res("GreenBrush"), Title = added == 1 ? "Added 1 item" : $"Added {added} items" });
        }

        void UpdateHeader()
        {
            int n = _store.Items.Count;
            _count.Text = n == 0 ? (_shelf ? "Temporary holding spot for files" : "Your favourite apps and sites") : n == 1 ? "1 item" : $"{n} items";
            _clear.Visibility = n > 0 ? Visibility.Visible : Visibility.Collapsed;
            _dragAll.Visibility = _store.Items.Count(i => i.Kind == "file" || i.Kind == "folder") > 1 ? Visibility.Visible : Visibility.Collapsed;
        }

        void Rebuild()
        {
            _tiles.Children.Clear();
            foreach (var item in _store.Items) _tiles.Children.Add(MakeTile(item));
            UpdateHeader();
        }

        FrameworkElement MakeTile(TrayItem item)
        {
            var g = new Grid { Width = 96, Height = 100, Margin = new Thickness(4), Background = Brushes.Transparent, ToolTip = item.Value.Length > 300 ? item.Value.Substring(0, 300) + "…" : item.Value };
            var bg = new Border { CornerRadius = new CornerRadius(14), Background = Ui.Res("Card2Brush"), Opacity = 0 };
            g.Children.Add(bg);

            var stack = new StackPanel { Margin = new Thickness(6, 10, 6, 6) };
            var icon = item.Icon;
            if (icon != null) stack.Children.Add(new Image { Source = icon, Width = 46, Height = 46, Stretch = Stretch.Uniform });
            else stack.Children.Add(new Border
            {
                Width = 46, Height = 46, CornerRadius = new CornerRadius(12), Background = Ui.Res("HoverBrush"),
                Child = new TextBlock { Text = item.Glyph, Style = Ui.StyleRes("Icon"), FontSize = 20 }
            });
            stack.Children.Add(new TextBlock
            {
                Text = item.Name, FontSize = 11.5, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis, MaxHeight = 32, Margin = new Thickness(0, 7, 0, 0)
            });
            g.Children.Add(stack);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 2, 2, 0), Visibility = Visibility.Collapsed };
            if (item.Kind == "file" || item.Kind == "folder")
            {
                var reveal = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.OpenFolder, Width = 22, Height = 22, FontSize = 10, ToolTip = "Show in folder", Background = Ui.Res("HoverBrush") };
                reveal.Click += (s, e) => Ui.Reveal(item.Value);
                actions.Children.Add(reveal);
            }
            var remove = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Close, Width = 22, Height = 22, FontSize = 9, ToolTip = "Remove", Margin = new Thickness(3, 0, 0, 0), Background = Ui.Res("HoverBrush") };
            remove.Click += (s, e) => _store.Remove(item);
            actions.Children.Add(remove);
            g.Children.Add(actions);

            g.MouseEnter += (s, e) => { bg.Opacity = 1; actions.Visibility = Visibility.Visible; };
            g.MouseLeave += (s, e) => { bg.Opacity = 0; actions.Visibility = Visibility.Collapsed; };
            g.PreviewMouseLeftButtonDown += (s, e) =>
            {
                if (Ui.FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
                _downPos = e.GetPosition(this);
                _downItem = item;
                if (e.ClickCount == 2 && _shelf) { Open(item); _downItem = null; }
            };
            g.PreviewMouseMove += (s, e) =>
            {
                if (_downItem != item || e.LeftButton != MouseButtonState.Pressed || !Far(e.GetPosition(this))) return;
                _downItem = null;
                if (_shelf) DragOut(new[] { item }.ToList());
                else Reorder(item);
            };
            g.PreviewMouseLeftButtonUp += (s, e) =>
            {
                if (_downItem == item && !_shelf) Open(item);
                _downItem = null;
            };
            return g;
        }

        bool Far(Point p) => Math.Abs(p.X - _downPos.X) > SystemParameters.MinimumHorizontalDragDistance || Math.Abs(p.Y - _downPos.Y) > SystemParameters.MinimumVerticalDragDistance;

        void Open(TrayItem item)
        {
            if (item.Kind == "text")
            {
                try { Clipboard.SetText(item.Value); } catch { }
                NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Copy, Title = "Copied to clipboard" });
                return;
            }
            Ui.Open(item.Value);
            if (!_shelf) NotchWindow.Instance?.Collapse();
        }

        void DragOut(System.Collections.Generic.List<TrayItem> items)
        {
            if (items.Count == 0) return;
            var data = new DataObject();
            var paths = items.Where(i => i.Kind == "file" || i.Kind == "folder").Select(i => i.Value).ToArray();
            if (paths.Length > 0)
            {
                var sc = new StringCollection();
                sc.AddRange(paths);
                data.SetFileDropList(sc);
            }
            else data.SetText(items[0].Value);
            data.SetData("CrestInternal", "1");

            var notch = NotchWindow.Instance;
            if (notch != null) notch.DragOut = true;
            DragDropEffects result;
            try { result = DragDrop.DoDragDrop(this, data, DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link); }
            catch (Exception ex) { Log.Error("drag out", ex); result = DragDropEffects.None; }
            finally { if (notch != null) notch.DragOut = false; }

            // Moved away by the target (e.g. dropped into another folder with Shift) — forget it.
            foreach (var i in items)
                if ((i.Kind == "file" || i.Kind == "folder") && !File.Exists(i.Value) && !Directory.Exists(i.Value)) _store.Remove(i);
        }

        void Reorder(TrayItem item)
        {
            var data = new DataObject("CrestInternal", "1");
            var notch = NotchWindow.Instance;
            if (notch != null) notch.DragOut = true;
            void over(object s, DragEventArgs e)
            {
                var pos = e.GetPosition(_tiles);
                int idx = 0;
                for (int i = 0; i < _tiles.Children.Count; i++)
                {
                    var child = (FrameworkElement)_tiles.Children[i];
                    var p = child.TranslatePoint(new Point(child.ActualWidth / 2, child.ActualHeight / 2), _tiles);
                    if (pos.Y > p.Y + child.ActualHeight / 2 || (Math.Abs(pos.Y - p.Y) <= child.ActualHeight / 2 && pos.X > p.X)) idx = i + 1;
                }
                int cur = _store.Items.IndexOf(item);
                int target = Math.Min(idx > cur ? idx - 1 : idx, _store.Items.Count - 1);
                if (target != cur && target >= 0) _store.Move(item, target);
                e.Effects = DragDropEffects.Move;
                e.Handled = true;
            }
            _tiles.AllowDrop = true;
            _tiles.DragOver += over;
            try { DragDrop.DoDragDrop(this, data, DragDropEffects.Move); }
            catch { }
            finally
            {
                _tiles.DragOver -= over;
                if (notch != null) notch.DragOut = false;
            }
        }
    }
}
