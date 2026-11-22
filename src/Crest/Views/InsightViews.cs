using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Crest.Controls;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    internal static class Kit
    {
        public static Border Card(UIElement child, string? title = null, Thickness? pad = null)
        {
            UIElement content = child;
            if (title != null)
            {
                var s = new DockPanel();
                var t = new TextBlock { Text = title.ToUpperInvariant(), FontSize = 10.5, FontWeight = FontWeights.SemiBold, Foreground = Ui.Res("Text3Brush"), Margin = new Thickness(0, 0, 0, 8) };
                DockPanel.SetDock(t, Dock.Top);
                s.Children.Add(t);
                s.Children.Add(child);
                content = s;
            }
            return new Border { Style = Ui.StyleRes("Card"), Padding = pad ?? new Thickness(14, 12, 14, 12), Child = content, Margin = new Thickness(4) };
        }

        public static TextBlock Big(string text = "", double size = 26) => new TextBlock
        {
            Text = text, FontFamily = (FontFamily)Application.Current.FindResource("DisplayFont"), FontSize = size, FontWeight = FontWeights.SemiBold,
            Typography = { NumeralAlignment = FontNumeralAlignment.Tabular }
        };

        public static TextBlock Small(string text = "") => new TextBlock { Text = text, FontSize = 11, Foreground = Ui.Res("Text2Brush"), TextTrimming = TextTrimming.CharacterEllipsis };

        public static T At<T>(T e, int col, int row = 0, int colSpan = 1, int rowSpan = 1) where T : UIElement
        {
            Grid.SetColumn(e, col); Grid.SetRow(e, row);
            if (colSpan > 1) Grid.SetColumnSpan(e, colSpan);
            if (rowSpan > 1) Grid.SetRowSpan(e, rowSpan);
            return e;
        }

        public static Grid Cols(params double[] stars)
        {
            var g = new Grid();
            foreach (var w in stars) g.ColumnDefinitions.Add(new ColumnDefinition { Width = w > 0 ? new GridLength(w, GridUnitType.Star) : GridLength.Auto });
            return g;
        }

        public static Grid Rows(Grid g, params double[] stars)
        {
            foreach (var h in stars) g.RowDefinitions.Add(new RowDefinition { Height = h > 0 ? new GridLength(h, GridUnitType.Star) : GridLength.Auto });
            return g;
        }
    }

    /// <summary>Simple vertical bar chart with labels (screen time / AI usage per day or hour).</summary>
    public sealed class Bars : FrameworkElement
    {
        public double[] Values = Array.Empty<double>();
        public string[] Labels = Array.Empty<string>();
        public int Highlight = -1;
        public Brush? BarBrush;
        readonly Brush Dim = Ui.Res("TrackBrush");

        public void Set(double[] values, string[] labels, int highlight) { Values = values; Labels = labels; Highlight = highlight; InvalidateVisual(); }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, Math.Max(0, w), Math.Max(0, h)));
            int n = Values.Length;
            if (n == 0 || w <= 0 || h <= 20) return;
            double labelH = Labels.Length > 0 ? 16 : 0;
            double max = Math.Max(1e-9, Values.Max());
            double slot = w / n, bw = Math.Min(26, slot * 0.55);
            var accent = BarBrush ?? (Brush)Application.Current.FindResource("AccentBrush");
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            for (int i = 0; i < n; i++)
            {
                double bh = Math.Max(3, (h - labelH - 4) * Values[i] / max);
                if (Values[i] <= 0) bh = 3;
                double x = i * slot + (slot - bw) / 2, y = h - labelH - bh;
                dc.DrawRoundedRectangle(i == Highlight ? accent : Dim, null, new Rect(x, y, bw, bh), Math.Min(6, bw / 2), Math.Min(6, bw / 2));
                if (i < Labels.Length && !string.IsNullOrEmpty(Labels[i]))
                {
                    var ft = new FormattedText(Labels[i], System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                        new Typeface("Segoe UI"), 10, i == Highlight ? Ui.Res("TextBrush") : Ui.Res("Text3Brush"), dpi);
                    dc.DrawText(ft, new Point(i * slot + (slot - ft.Width) / 2, h - labelH + 2));
                }
            }
        }
    }

    // ===================================================================== Screen Time
    public sealed class ScreenTimeView : UserControl, ITabView
    {
        readonly TextBlock _total = Kit.Big("", 28), _left = Kit.Big("", 17), _apps = Kit.Big("", 28);
        readonly TextBlock _leftSub = Kit.Small(), _avg = Kit.Small();
        readonly RingProgress _ring = new RingProgress { Width = 40, Height = 40, Thickness = 5 };
        readonly Bars _week = new Bars();
        readonly StackPanel _list = new StackPanel();
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };

        public ScreenTimeView()
        {
            _ring.SetResourceReference(RingProgress.RingBrushProperty, "AccentBrush");
            var root = Kit.Cols(1.45, 1);
            var left = Kit.Rows(new Grid(), 0, 1);

            var today = Kit.Cols(0.8, 1.6, 0.7);
            var a = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            a.Children.Add(_total); a.Children.Add(Kit.Small("Screen time"));
            var limitRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            limitRow.Children.Add(_ring);
            var ls = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            ls.Children.Add(_left);
            var limitStep = new Stepper(v => v == 0 ? "No limit" : $"Limit {ScreenTimeService.Format(v * 60)}", SettingsStore.Current.ScreenLimitMin, 0, 960, 30) { Height = 30, Width = 128, Margin = new Thickness(0, 3, 0, 0) };
            limitStep.Changed += v => { SettingsStore.Current.ScreenLimitMin = v; SettingsStore.Save(); Refresh(); };
            ls.Children.Add(limitStep);
            limitRow.Children.Add(ls);
            var c = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            c.Children.Add(_apps); c.Children.Add(Kit.Small("Apps used"));
            today.Children.Add(Kit.At(a, 0)); today.Children.Add(Kit.At(limitRow, 1)); today.Children.Add(Kit.At(c, 2));
            left.Children.Add(Kit.At(Kit.Card(today, "Today"), 0, 0));

            var weekPanel = new DockPanel();
            DockPanel.SetDock(_avg, Dock.Top);
            _avg.HorizontalAlignment = HorizontalAlignment.Right;
            _avg.Margin = new Thickness(0, -22, 0, 4);
            weekPanel.Children.Add(_avg);
            weekPanel.Children.Add(_week);
            left.Children.Add(Kit.At(Kit.Card(weekPanel, "Last 7 days"), 0, 1));
            root.Children.Add(Kit.At(left, 0));

            var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            root.Children.Add(Kit.At(Kit.Card(scroll, "By app"), 1));
            Content = root;
            _timer.Tick += (s, e) => Refresh();
        }

        public void OnActivated() { ScreenTimeService.Instance.Flush(); Refresh(); _timer.Start(); }
        public void OnDeactivated() => _timer.Stop();

        void Refresh()
        {
            var st = ScreenTimeService.Instance;
            var day = DateTime.Now.ToString("yyyy-MM-dd");
            int total = st.TotalSeconds(day);
            var apps = st.Apps(day);
            _total.Text = ScreenTimeService.Format(total);
            _apps.Text = apps.Count.ToString();
            int limit = SettingsStore.Current.ScreenLimitMin * 60;
            if (limit > 0)
            {
                _ring.Progress = Math.Min(1, total / (double)limit);
                _left.Text = total >= limit ? "Limit reached" : ScreenTimeService.Format(limit - total) + " left";
            }
            else { _ring.Progress = 0; _left.Text = "Daily limit"; }
            _ring.Visibility = limit > 0 ? Visibility.Visible : Visibility.Collapsed;

            var week = st.Week();
            _week.Set(week.Select(w => (double)w.Seconds).ToArray(), week.Select(w => w.Day.ToString("ddd")).ToArray(), 6);
            var used = week.Where(w => w.Seconds > 0).ToList();
            _avg.Text = used.Count > 0 ? $"avg {ScreenTimeService.Format((int)used.Average(w => w.Seconds))}" : "";

            _list.Children.Clear();
            if (apps.Count == 0) _list.Children.Add(Kit.Small("Usage appears here as you work. Only app names are recorded."));
            int max = apps.Count > 0 ? apps[0].Seconds : 1;
            foreach (var (exe, secs) in apps.Take(12))
            {
                var row = Kit.Cols(0, 1, 0);
                row.Margin = new Thickness(0, 0, 6, 8);
                var path = st.PathOf(exe);
                var icon = path != null ? ShellIcons.Get(path, 32) : null;
                row.Children.Add(Kit.At(icon != null ? (FrameworkElement)new Image { Source = icon, Width = 22, Height = 22, Margin = new Thickness(0, 0, 10, 0) }
                    : new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(6), Background = Ui.Res("HoverBrush"), Margin = new Thickness(0, 0, 10, 0) }, 0));
                var mid = new StackPanel();
                mid.Children.Add(new TextBlock { Text = st.FriendlyName(exe), FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                mid.Children.Add(new ProgressLine { Value = secs / (double)max, Brush = Ui.Res("TextBrush"), Margin = new Thickness(0, 4, 0, 0), Height = 4 });
                row.Children.Add(Kit.At(mid, 1));
                var right = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
                right.Children.Add(new TextBlock { Text = ScreenTimeService.Format(secs), FontSize = 12, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Right });
                right.Children.Add(new TextBlock { Text = total > 0 ? $"{secs * 100 / total}%" : "", FontSize = 10.5, Foreground = Ui.Res("Text3Brush"), HorizontalAlignment = HorizontalAlignment.Right });
                row.Children.Add(Kit.At(right, 2));
                _list.Children.Add(row);
            }
        }
    }

    // ===================================================================== AI Coding
    public sealed class AiView : UserControl, ITabView
    {
        readonly TextBlock _tokens = Kit.Big("", 28), _tokSub = Kit.Small(), _msgs = Kit.Big("", 20), _tools = Kit.Big("", 20), _sessions = Kit.Big("", 20);
        readonly TextBlock _status = Kit.Small();
        readonly Bars _hours = new Bars(), _week = new Bars();
        readonly StackPanel _models = new StackPanel(), _projects = new StackPanel();
        readonly RadioButton _all, _claude, _codex;
        AiUsage? _data;

        public AiView()
        {
            var root = Kit.Rows(new Grid(), 0, 1);
            var top = new DockPanel { Margin = new Thickness(4, 0, 4, 4) };
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            _all = new RadioButton { Style = Ui.StyleRes("Seg"), Content = "All", GroupName = "ai-src", IsChecked = true };
            _claude = new RadioButton { Style = Ui.StyleRes("Seg"), Content = "Claude Code", GroupName = "ai-src" };
            _codex = new RadioButton { Style = Ui.StyleRes("Seg"), Content = "Codex", GroupName = "ai-src" };
            foreach (var r in new[] { _all, _claude, _codex }) { r.Checked += (s, e) => Render(); seg.Children.Add(r); }
            var segBorder = new Border { Background = Ui.Res("CardBrush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(2), Child = seg };
            var refresh = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Refresh, Width = 28, Height = 28, FontSize = 12, ToolTip = "Rescan logs", Margin = new Thickness(6, 0, 0, 0) };
            refresh.Click += async (s, e) => await Load();
            DockPanel.SetDock(refresh, Dock.Right);
            DockPanel.SetDock(segBorder, Dock.Right);
            top.Children.Add(refresh);
            top.Children.Add(segBorder);
            _status.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(_status);
            root.Children.Add(top);

            var body = Kit.Rows(Kit.Cols(1.2, 1), 1, 1);
            var today = Kit.Cols(1.1, 1);
            var tokCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var tline = new StackPanel { Orientation = Orientation.Horizontal };
            tline.Children.Add(_tokens);
            tline.Children.Add(new TextBlock { Text = " tokens", FontSize = 13, Foreground = Ui.Res("Text2Brush"), VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, 6) });
            tokCol.Children.Add(tline);
            _tokSub.TextWrapping = TextWrapping.Wrap;
            tokCol.Children.Add(_tokSub);
            today.Children.Add(Kit.At(tokCol, 0));
            var stats = new UniformGrid { Columns = 3, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
            foreach (var (val, label) in new[] { (_msgs, "Messages"), (_tools, "Tool calls"), (_sessions, "Sessions") })
            {
                var sp = new StackPanel();
                sp.Children.Add(val);
                sp.Children.Add(Kit.Small(label));
                stats.Children.Add(sp);
            }
            today.Children.Add(Kit.At(stats, 1));
            body.Children.Add(Kit.At(Kit.Card(today, "Today"), 0, 0));

            var models = new DockPanel();
            var mscroll = new ScrollViewer { Content = _models, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            models.Children.Add(mscroll);
            body.Children.Add(Kit.At(Kit.Card(models, "By model · today"), 1, 0));

            _hours.Height = double.NaN;
            body.Children.Add(Kit.At(Kit.Card(_hours, "Active hours"), 0, 1));
            var wk = Kit.Rows(new Grid(), 1, 0);
            wk.Children.Add(Kit.At(_week, 0, 0));
            var pscroll = new ScrollViewer { Content = _projects, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, Margin = new Thickness(0, 6, 0, 0) };
            wk.Children.Add(Kit.At(pscroll, 0, 1));
            body.Children.Add(Kit.At(Kit.Card(wk, "Last 7 days"), 1, 1));
            root.Children.Add(Kit.At(body, 0, 1));
            Content = root;
        }

        public async void OnActivated()
        {
            if (_data == null || DateTime.Now - _data.Scanned > TimeSpan.FromMinutes(2)) await Load();
            else Render();
        }

        public void OnDeactivated() { }

        async System.Threading.Tasks.Task Load()
        {
            _status.Text = "Reading local logs…";
            try { _data = await AiUsageService.ScanAsync(); }
            catch (Exception ex) { Log.Error("ai scan", ex); }
            Render();
        }

        void Render()
        {
            var d = _data;
            if (d == null) return;
            if (!d.ClaudeFound && !d.CodexFound)
            {
                _status.Text = "No Claude Code or Codex logs found in your user folder.";
            }
            else _status.Text = $"Updated {d.Scanned:t} · from local logs, nothing uploaded";

            bool useClaude = _all.IsChecked == true || _claude.IsChecked == true;
            bool useCodex = _all.IsChecked == true || _codex.IsChecked == true;
            var days = new System.Collections.Generic.List<AiDay>();
            if (useClaude) days.Add(d.ClaudeToday);
            if (useCodex) days.Add(d.CodexToday);

            long total = days.Sum(x => x.Total), cache = days.Sum(x => x.CacheRead);
            _tokens.Text = AiUsageService.Tokens(total);
            _tokSub.Text = $"in {AiUsageService.Tokens(days.Sum(x => x.Input + x.CacheWrite))} · out {AiUsageService.Tokens(days.Sum(x => x.Output))}" +
                           (cache > 0 ? $" · cache reads {AiUsageService.Tokens(cache)}" : "");
            _msgs.Text = days.Sum(x => x.Messages).ToString();
            _tools.Text = useClaude ? d.ClaudeToday.ToolCalls.ToString() : "—";
            _sessions.Text = days.Sum(x => x.Sessions.Count).ToString();

            var hours = new double[24];
            foreach (var x in days) for (int i = 0; i < 24; i++) hours[i] += x.ByHour[i];
            _hours.Set(hours, Enumerable.Range(0, 24).Select(i => i % 6 == 0 ? $"{i:00}:00" : "").ToArray(), DateTime.Now.Hour);

            var week = new double[7];
            for (int i = 0; i < 7; i++) week[i] = (useClaude ? d.ClaudeWeek[i] : 0) + (useCodex ? d.CodexWeek[i] : 0);
            _week.Set(week, Enumerable.Range(0, 7).Select(i => DateTime.Today.AddDays(i - 6).ToString("ddd")).ToArray(), 6);

            _models.Children.Clear();
            var byModel = days.SelectMany(x => x.ByModel).GroupBy(x => x.Key).Select(g => (g.Key, g.Sum(v => v.Value))).OrderByDescending(x => x.Item2).ToList();
            if (byModel.Count == 0) _models.Children.Add(Kit.Small("No usage today yet."));
            long top = byModel.Count > 0 ? Math.Max(1, byModel[0].Item2) : 1;
            foreach (var (model, tokens) in byModel.Take(6))
            {
                var row = Kit.Cols(1, 0);
                row.Margin = new Thickness(0, 0, 0, 7);
                var name = new StackPanel();
                name.Children.Add(new TextBlock { Text = model, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                name.Children.Add(new ProgressLine { Value = tokens / (double)top, Brush = (Brush)FindResource("AccentBrush"), Height = 4, Margin = new Thickness(0, 4, 10, 0) });
                row.Children.Add(Kit.At(name, 0));
                row.Children.Add(Kit.At(new TextBlock { Text = AiUsageService.Tokens(tokens), FontSize = 12, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Top }, 1));
                _models.Children.Add(row);
            }

            _projects.Children.Clear();
            var proj = days.SelectMany(x => x.ByProject).GroupBy(x => x.Key).Select(g => (g.Key, g.Sum(v => v.Value))).OrderByDescending(x => x.Item2).FirstOrDefault();
            if (!string.IsNullOrEmpty(proj.Key)) _projects.Children.Add(Kit.Small($"Busiest project today: {proj.Key} ({AiUsageService.Tokens(proj.Item2)})"));
        }
    }

    // ===================================================================== Message
    public sealed class MessageView : UserControl, ITabView
    {
        static AppSettings S => SettingsStore.Current;
        readonly TextBox _text = new TextBox();
        readonly TextBlock _count = Kit.Small();

        public MessageView()
        {
            var root = Kit.Rows(new Grid(), 0, 0, 1);
            var input = Kit.Cols(1, 0);
            _text.Style = Ui.StyleRes("Field");
            _text.Tag = "Type a message to flash on the notch…";
            _text.MaxLength = 80;
            _text.TextChanged += (s, e) => _count.Text = $"{_text.Text.Length}/80";
            _text.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) Send(); };
            input.Children.Add(Kit.At(_text, 0));
            var flash = new Button { Style = Ui.StyleRes("Pill"), Content = "Flash", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(18, 7, 18, 7), Foreground = Brushes.Black };
            flash.SetResourceReference(BackgroundProperty, "AccentBrush");
            flash.Click += (s, e) => Send();
            input.Children.Add(Kit.At(flash, 1));
            root.Children.Add(Kit.At(Kit.Card(input), 0, 0));
            _count.HorizontalAlignment = HorizontalAlignment.Right;
            _count.Margin = new Thickness(0, 0, 18, 0);
            root.Children.Add(Kit.At(_count, 0, 1));

            var opts = Kit.Cols(1, 1, 1);
            opts.Children.Add(Kit.At(Kit.Card(Segs("msg-anim", new[] { ("Type", "Type-on"), ("Scroll", "Scroll"), ("Pulse", "Pulse") }, S.MsgAnim, v => S.MsgAnim = v), "Animation"), 0));
            var colors = new WrapPanel();
            foreach (var hex in new[] { "#FFFFFF", "#FF9F0A", "#FF453A", "#FF375F", "#BF5AF2", "#0A84FF", "#64D2FF", "#30D158", "#FFD60A" })
            {
                var ring = new System.Windows.Shapes.Ellipse { Width = 22, Height = 22, StrokeThickness = 2, Stroke = string.Equals(hex, S.MsgColor, StringComparison.OrdinalIgnoreCase) ? Brushes.White : Brushes.Transparent };
                var g = new Grid { Margin = new Thickness(2), Cursor = System.Windows.Input.Cursors.Hand, Background = Brushes.Transparent };
                g.Children.Add(ring);
                g.Children.Add(new System.Windows.Shapes.Ellipse { Width = 15, Height = 15, Fill = Ui.Brush(hex) });
                var h = hex;
                g.MouseLeftButtonUp += (s, e) =>
                {
                    S.MsgColor = h; SettingsStore.Save();
                    foreach (Grid o in colors.Children) ((System.Windows.Shapes.Ellipse)o.Children[0]).Stroke = Brushes.Transparent;
                    ring.Stroke = Ui.Res("TextBrush");
                };
                colors.Children.Add(g);
            }
            opts.Children.Add(Kit.At(Kit.Card(colors, "Colour"), 1));
            opts.Children.Add(Kit.At(Kit.Card(Segs("msg-dur", new[] { ("3", "3s"), ("5", "5s"), ("10", "10s"), ("30", "30s") }, S.MsgSeconds.ToString(), v => S.MsgSeconds = int.Parse(v)), "Duration"), 2));
            opts.VerticalAlignment = VerticalAlignment.Top;
            root.Children.Add(Kit.At(opts, 0, 2));
            Content = root;
        }

        static Border Segs(string group, (string Value, string Label)[] items, string current, Action<string> set)
        {
            var p = new WrapPanel();
            foreach (var (v, l) in items)
            {
                var rb = new RadioButton { Style = Ui.StyleRes("Seg"), Content = l, GroupName = group, IsChecked = v == current };
                var vv = v;
                rb.Checked += (s, e) => { set(vv); SettingsStore.Save(); };
                p.Children.Add(rb);
            }
            return new Border { Background = Ui.Res("Card2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(2), Child = p, HorizontalAlignment = HorizontalAlignment.Left };
        }

        void Send()
        {
            var t = _text.Text.Trim();
            if (t.Length == 0) return;
            NotchWindow.Instance?.Flash(t, Ui.Brush(S.MsgColor), S.MsgSeconds);
        }

        public void OnActivated() { }
        public void OnDeactivated() { }
    }
}
