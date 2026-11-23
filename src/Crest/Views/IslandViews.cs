using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Controls;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    // =====================================================================================
    //  Compact pages for the island. Small, glanceable, two or three controls at most.
    //  The full versions live in the Crest app window.
    // =====================================================================================

    /// <summary>A quick countdown or a Pomodoro, nothing else.</summary>
    public sealed class IslandTimers : UserControl, ITabView
    {
        static TimerService T => TimerService.Instance;
        readonly RingProgress _ring = new RingProgress { Width = 104, Height = 104, Thickness = 5 };
        readonly TextBlock _big = new TextBlock { FontSize = 24, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
        readonly TextBlock _label = new TextBlock { FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center };
        readonly Button _play = new Button(), _reset = new Button(), _extra = new Button();
        readonly WrapPanel _presets = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        readonly RadioButton _timerMode = new RadioButton(), _focusMode = new RadioButton();
        bool FocusMode => _focusMode.IsChecked == true;

        public IslandTimers()
        {
            _ring.SetResourceReference(RingProgress.RingBrushProperty, "AccentBrush");
            _big.SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            _big.Typography.NumeralAlignment = FontNumeralAlignment.Tabular;
            _label.Foreground = Ui.Res("Text2Brush");
            var centre = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            centre.Children.Add(_big);
            centre.Children.Add(_label);
            var ring = new Grid { Width = 104, Height = 104, VerticalAlignment = VerticalAlignment.Center };
            ring.Children.Add(_ring);
            ring.Children.Add(centre);

            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var (rb, label) in new[] { (_timerMode, "Timer"), (_focusMode, "Focus") })
            {
                rb.Style = Ui.StyleRes("Seg");
                rb.Content = label;
                rb.GroupName = "island-timer-mode";
                rb.Padding = new Thickness(12, 3, 12, 3);
                rb.FontSize = 11.5;
                rb.Checked += (s, e) => Render();
                seg.Children.Add(rb);
            }
            var segBox = new Border { Background = Ui.Res("Card2Brush"), CornerRadius = new CornerRadius(9), Padding = new Thickness(2), Child = seg, HorizontalAlignment = HorizontalAlignment.Left };

            foreach (var m in new[] { 1, 5, 10, 15, 25, 45 })
            {
                var b = new Button { Style = Ui.StyleRes("Pill"), Content = m + "m", Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 4, 4), FontSize = 11.5 };
                int mm = m;
                b.Click += (s, e) => { T.CdSet(mm); if (!T.CdRunning) T.CdToggle(); Render(); };
                _presets.Children.Add(b);
            }

            foreach (var b in new[] { _reset, _play, _extra })
            {
                b.Style = Ui.StyleRes(b == _play ? "PrimaryButton" : "IconButton");
                b.Width = b.Height = b == _play ? 36 : 30;
                b.FontSize = 13;
                b.Margin = new Thickness(0, 0, 6, 0);
                if (b != _play) b.Background = Ui.Res("Card2Brush");
            }
            _play.SetResourceReference(BackgroundProperty, "AccentBrush");
            _play.Foreground = Brushes.Black;
            _reset.Content = Glyphs.Refresh;
            _reset.Click += (s, e) => { if (FocusMode) T.PomoReset(); else T.CdReset(); Render(); };
            _play.Click += (s, e) => { if (FocusMode) T.PomoToggle(); else T.CdToggle(); Render(); };
            _extra.Click += (s, e) => { if (FocusMode) T.PomoSkip(); else T.CdSet((int)Math.Ceiling(T.CdLeft.TotalMinutes) + 1); Render(); };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            buttons.Children.Add(_reset); buttons.Children.Add(_play); buttons.Children.Add(_extra);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(18, 0, 0, 0) };
            right.Children.Add(segBox);
            right.Children.Add(_presets);
            right.Children.Add(buttons);

            var dock = new DockPanel { Margin = new Thickness(6, 0, 0, 0) };
            DockPanel.SetDock(ring, Dock.Left);
            dock.Children.Add(ring);
            dock.Children.Add(right);
            Content = dock;

            T.Tick += () => { if (IsVisible) Render(); };
            T.StateChanged += () => { if (IsVisible) Render(); };
        }

        public void OnActivated()
        {
            if (T.PomoRunning && !T.CdRunning) _focusMode.IsChecked = true; else if (_focusMode.IsChecked != true) _timerMode.IsChecked = true;
            Render();
        }
        public void OnDeactivated() { }

        void Render()
        {
            _presets.Visibility = FocusMode ? Visibility.Collapsed : Visibility.Visible;
            if (FocusMode)
            {
                _ring.Progress = T.PomoProgress;
                _big.Text = Ui.ClockCeil(T.PomoLeft);
                _label.Text = $"{T.PhaseName(T.Phase)} {T.Cycle}/{Math.Max(1, SettingsStore.Current.Cycles)}";
                _play.Content = T.PomoRunning ? Glyphs.Pause : Glyphs.Play;
                _extra.Content = Glyphs.Next;
                _extra.ToolTip = "Skip to the next phase";
            }
            else
            {
                _ring.Progress = T.CdProgress;
                _big.Text = Ui.ClockCeil(T.CdLeft);
                _label.Text = T.CdRunning ? "Running" : T.CdLeft <= TimeSpan.Zero ? "Done" : "Timer";
                _play.Content = T.CdRunning ? Glyphs.Pause : Glyphs.Play;
                _extra.Content = "+1";
                _extra.FontFamily = (FontFamily)FindResource("UiFont");
                _extra.ToolTip = "Add a minute";
            }
            if (FocusMode) _extra.FontFamily = (FontFamily)FindResource("IconFont");
        }
    }

    /// <summary>The last few things you copied. Click one to copy it again.</summary>
    public sealed class IslandClipboard : UserControl, ITabView
    {
        readonly StackPanel _list = new StackPanel();

        public IslandClipboard()
        {
            var head = new DockPanel { Margin = new Thickness(4, 0, 2, 6) };
            var all = new Button { Style = Ui.StyleRes("Pill"), Content = "Full history", Padding = new Thickness(10, 3, 10, 3), FontSize = 11.5 };
            all.Click += (s, e) => NotchWindow.Instance?.SelectTab("Clipboard");
            DockPanel.SetDock(all, Dock.Right);
            head.Children.Add(all);
            head.Children.Add(new TextBlock { Text = "Click to copy again", Style = Ui.StyleRes("Caption"), VerticalAlignment = VerticalAlignment.Center });
            var dock = new DockPanel();
            DockPanel.SetDock(head, Dock.Top);
            dock.Children.Add(head);
            dock.Children.Add(new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Content = dock;
            ClipboardService.Instance.Items.CollectionChanged += (s, e) => { if (IsVisible) Render(); };
        }

        public void OnActivated() => Render();
        public void OnDeactivated() { }

        void Render()
        {
            _list.Children.Clear();
            var items = ClipboardService.Instance.Items.Take(8).ToList();
            if (items.Count == 0)
                _list.Children.Add(new TextBlock { Text = "Nothing copied yet.", Style = Ui.StyleRes("Caption"), Margin = new Thickness(4, 10, 0, 0) });
            foreach (var it in items)
            {
                var item = it;
                var row = new Border { CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 0, 0, 2), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                var dp = new DockPanel();
                FrameworkElement icon = item.Thumb != null
                    ? new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(5), Background = new ImageBrush(item.Thumb) { Stretch = Stretch.UniformToFill } }
                    : new TextBlock { Text = item.Glyph, Style = Ui.StyleRes("Icon"), FontSize = 12, Width = 22, Foreground = Ui.Res("Text2Brush") };
                icon.Margin = new Thickness(0, 0, 8, 0);
                DockPanel.SetDock(icon, Dock.Left);
                dp.Children.Add(icon);
                var when = new TextBlock { Text = Ui.Ago(item.Time), FontSize = 10.5, Foreground = Ui.Res("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
                DockPanel.SetDock(when, Dock.Right);
                dp.Children.Add(when);
                dp.Children.Add(new TextBlock { Text = item.Preview, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                row.Child = dp;
                row.MouseEnter += (s, e) => row.Background = Ui.Res("Card2Brush");
                row.MouseLeave += (s, e) => row.Background = Brushes.Transparent;
                row.MouseLeftButtonUp += (s, e) =>
                {
                    ClipboardService.Instance.Copy(item);
                    NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Copy, GlyphBrush = Ui.Res("GreenBrush"), Title = "Copied" });
                };
                _list.Children.Add(row);
            }
        }
    }
}
