using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using Crest.Controls;
using Crest.Core;

namespace Crest
{
    /// <summary>
    /// A tab detached from the notch ("pull down" / pop-out): a persistent, draggable widget that stays above other windows.
    /// Unlike the notch it is a normal activatable window, so typing works immediately.
    /// </summary>
    public sealed class FloatWindow : Window
    {
        static readonly Dictionary<string, FloatWindow> Windows = new Dictionary<string, FloatWindow>();
        readonly FrameworkElement _view;
        readonly string _key;

        public static void Open(string key, Point? near)
        {
            if (Windows.TryGetValue(key, out var existing)) { existing.Activate(); return; }
            var w = new FloatWindow(key);
            var s = SettingsStore.Current;
            if (s.FloatLeft.HasValue && s.FloatTop.HasValue && Windows.Count == 0 && near == null) { w.Left = s.FloatLeft.Value; w.Top = s.FloatTop.Value; }
            else if (near.HasValue) { w.Left = near.Value.X - 380; w.Top = Math.Max(8, near.Value.Y - 20); }
            else { w.WindowStartupLocation = WindowStartupLocation.CenterScreen; }
            if (Windows.Count > 0 && near == null) { w.Left += 30 * Windows.Count; w.Top += 30 * Windows.Count; }
            Windows[key] = w;
            w.Show();
        }

        FloatWindow(string key)
        {
            _key = key;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "Crest";
            FontFamily = (FontFamily)FindResource("UiFont");
            Foreground = Ui.Res("TextBrush");
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            var tab = NotchWindow.AllTabs.FirstOrDefault(t => t.Key == key);
            _view = NotchWindow.CreateViewFor(key);
            _view.Width = 708;
            _view.Height = 336;

            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10), Background = Brushes.Transparent, Cursor = Cursors.SizeAll };
            var close = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Close, Width = 28, Height = 28, FontSize = 10, ToolTip = "Close widget  (Esc)" };
            close.Click += (s, e) => Close();
            var back = new Button { Style = Ui.StyleRes("IconButton"), Content = "", Width = 28, Height = 28, FontSize = 11, ToolTip = "Back into the notch" };
            back.Click += (s, e) => { Close(); NotchWindow.Instance?.Expand(_key); };
            DockPanel.SetDock(close, Dock.Right);
            DockPanel.SetDock(back, Dock.Right);
            header.Children.Add(close);
            header.Children.Add(back);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(new Border
            {
                Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Brushes.White, Margin = new Thickness(0, 0, 9, 0),
                Child = new TextBlock { Text = tab.Glyph ?? "", Style = Ui.StyleRes("Icon"), FontSize = 11, Foreground = Brushes.Black }
            });
            title.Children.Add(new TextBlock { Text = tab.Title ?? key, FontFamily = (FontFamily)FindResource("DisplayFont"), FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            if (!string.IsNullOrEmpty(tab.Sub))
                title.Children.Add(new TextBlock { Text = "  ·  " + tab.Sub, FontSize = 12, Foreground = Ui.Res("Text2Brush"), VerticalAlignment = VerticalAlignment.Center });
            header.Children.Add(title);
            header.MouseLeftButtonDown += (s, e) =>
            {
                if (Ui.FindAncestor<Button>(e.OriginalSource as DependencyObject) != null) return;
                try { DragMove(); } catch { }
                SettingsStore.Current.FloatLeft = Left;
                SettingsStore.Current.FloatTop = Top;
                SettingsStore.Save();
            };

            var stack = new DockPanel();
            DockPanel.SetDock(header, Dock.Top);
            stack.Children.Add(header);
            stack.Children.Add(_view);

            var card = new Border
            {
                Background = Brushes.Black, CornerRadius = new CornerRadius(26), Padding = new Thickness(18, 12, 18, 16),
                BorderBrush = SettingsStore.Current.Oled ? null : Ui.Brush("#26FFFFFF"), BorderThickness = new Thickness(SettingsStore.Current.Oled ? 0 : 1),
                Child = stack, Margin = new Thickness(26),
                Effect = SettingsStore.Current.Oled ? null : new DropShadowEffect { BlurRadius = 30, ShadowDepth = 8, Direction = 270, Opacity = 0.55, Color = Colors.Black },
                RenderTransformOrigin = new Point(0.5, 0),
                RenderTransform = new ScaleTransform(0.92, 0.92)
            };
            Content = card;

            Loaded += (s, e) =>
            {
                (_view as ITabView)?.OnActivated();
                var ease = new SpringEase { Damping = 0.7 };
                ((ScaleTransform)card.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(460)) { EasingFunction = ease });
                ((ScaleTransform)card.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(460)) { EasingFunction = ease });
                card.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180)));
                // keep it on screen
                var wa = SystemParameters.WorkArea;
                if (Left + ActualWidth > wa.Right) Left = Math.Max(wa.Left, wa.Right - ActualWidth);
                if (Left < wa.Left) Left = wa.Left;
                if (Top + ActualHeight > wa.Bottom) Top = Math.Max(wa.Top, wa.Bottom - ActualHeight);
            };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Closed += (s, e) =>
            {
                (_view as ITabView)?.OnDeactivated();
                Windows.Remove(_key);
            };
        }
    }
}
