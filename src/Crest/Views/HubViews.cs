using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Crest.Core;
using Crest.Services;

namespace Crest.Views
{
    // =====================================================================================
    //  Spotlight
    // =====================================================================================
    public sealed class SpotlightView : UserControl, ITabView
    {
        readonly TextBox _box = new TextBox();
        readonly StackPanel _list = new StackPanel();
        readonly TextBlock _hint = new TextBlock();
        List<SpotItem> _items = new List<SpotItem>();
        int _sel;

        public SpotlightView()
        {
            var root = Kit.Rows(new Grid(), 0, 1);
            var bar = new Border { Background = Ui.Res("CardBrush"), CornerRadius = new CornerRadius(16), Padding = new Thickness(14, 4, 14, 4), Margin = new Thickness(0, 0, 0, 8) };
            var dp = new DockPanel();
            var icon = new TextBlock { Text = Glyphs.Search, Style = Ui.StyleRes("Icon"), FontSize = 17, Foreground = Ui.Res("Text2Brush"), Margin = new Thickness(0, 0, 10, 0) };
            DockPanel.SetDock(icon, Dock.Left);
            dp.Children.Add(icon);
            _box.Style = Ui.StyleRes("Field");
            _box.Background = Brushes.Transparent;
            _box.FontSize = 20;
            _box.Padding = new Thickness(0, 8, 0, 8);
            _box.Tag = "Search apps, files, settings — or type 12*7, 10 km to mi";
            _box.TextChanged += (s, e) => Run();
            _box.PreviewKeyDown += OnKey;
            dp.Children.Add(_box);
            bar.Child = dp;
            root.Children.Add(bar);

            var scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _hint.Style = Ui.StyleRes("Caption");
            _hint.HorizontalAlignment = HorizontalAlignment.Center;
            _hint.VerticalAlignment = VerticalAlignment.Center;
            _hint.TextAlignment = TextAlignment.Center;
            _hint.Text = "↑ ↓ to move · Enter to open · Esc to close\nCtrl+Alt+Space opens this from anywhere";
            var body = new Grid();
            body.Children.Add(_hint);
            body.Children.Add(scroll);
            Grid.SetRow(body, 1);
            root.Children.Add(body);
            Content = root;
        }

        public void OnActivated()
        {
            SpotlightService.EnsureIndex();
            _box.SelectAll();
            Run();
        }

        public void OnDeactivated() { }

        public void FocusBox()
        {
            _box.Focus();
            Keyboard.Focus(_box);
            _box.SelectAll();
        }

        public void SetQuery(string q) { _box.Text = q; _box.CaretIndex = q.Length; }

        void Run()
        {
            _items = SpotlightService.Query(_box.Text);
            _sel = 0;
            Render();
        }

        void Render()
        {
            _list.Children.Clear();
            _hint.Visibility = _items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_items.Count == 0 && _box.Text.Length > 0 && SpotlightService.AppCount == 0) _hint.Text = "Indexing your apps…";
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                int idx = i;
                var row = new Grid { Margin = new Thickness(0, 0, 6, 3), Background = Brushes.Transparent, Cursor = Cursors.Hand };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var bg = new Border { CornerRadius = new CornerRadius(12), Background = i == _sel ? Ui.Res("Card2Brush") : Brushes.Transparent };
                Grid.SetColumnSpan(bg, 3);
                row.Children.Add(bg);
                ImageSource? img = it.Kind == "app" || it.Kind == "recent" ? ShellIcons.Get(it.Target, 32) : null;
                FrameworkElement ic = img != null
                    ? new Image { Source = img, Width = 26, Height = 26, Margin = new Thickness(8, 6, 6, 6) }
                    : new Border { Width = 26, Height = 26, CornerRadius = new CornerRadius(7), Background = Ui.Res("HoverBrush"), Margin = new Thickness(8, 6, 6, 6), Child = new TextBlock { Text = it.Kind == "uwp" ? Glyphs.Apps : it.Glyph, Style = Ui.StyleRes("Icon"), FontSize = 12 } };
                row.Children.Add(ic);
                var name = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 8, 0) };
                name.Children.Add(new TextBlock { Text = it.Name, FontSize = it.Kind == "math" ? 17 : 13.5, FontWeight = it.Kind == "math" ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis });
                Grid.SetColumn(name, 1);
                row.Children.Add(name);
                var sub = new TextBlock { Text = it.Sub, FontSize = 11, Foreground = Ui.Res("Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                Grid.SetColumn(sub, 2);
                row.Children.Add(sub);
                row.MouseEnter += (s, e) => { if (_sel != idx) { _sel = idx; Highlight(); } };
                row.MouseLeftButtonUp += (s, e) => Activate(_items[idx]);
                _list.Children.Add(row);
            }
        }

        void Highlight()
        {
            for (int i = 0; i < _list.Children.Count; i++)
                if (_list.Children[i] is Grid g && g.Children[0] is Border b) b.Background = i == _sel ? Ui.Res("Card2Brush") : Brushes.Transparent;
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Down) { _sel = Math.Min(_items.Count - 1, _sel + 1); Highlight(); e.Handled = true; }
            else if (e.Key == Key.Up) { _sel = Math.Max(0, _sel - 1); Highlight(); e.Handled = true; }
            else if (e.Key == Key.Enter && _items.Count > 0) { Activate(_items[Math.Clamp(_sel, 0, _items.Count - 1)]); e.Handled = true; }
        }

        void Activate(SpotItem it)
        {
            var notch = NotchWindow.Instance;
            switch (it.Kind)
            {
                case "math":
                    try { Clipboard.SetText(it.Target); } catch { }
                    notch?.ShowBanner(new Toast { Glyph = Glyphs.Copy, Title = "Copied", Value = it.Target });
                    return;
                case "unit":
                    notch?.SelectTab("Units");
                    return;
                case "assistant":
                    AssistantView.Pending = it.Target;
                    notch?.SelectTab("Assistant");
                    return;
            }
            SpotlightService.Launch(it);
            _box.Text = "";
            notch?.Collapse();
        }
    }

    // =====================================================================================
    //  Assistant
    // =====================================================================================
    public sealed class AssistantView : UserControl, ITabView
    {
        static AppSettings S => SettingsStore.Current;
        public static string? Pending;
        readonly StackPanel _chat = new StackPanel();
        readonly ScrollViewer _scroll;
        readonly TextBox _input = new TextBox();
        readonly Border _setup;
        readonly TextBlock _status = new TextBlock();
        readonly List<(string Role, string Text)> _history = new List<(string, string)>();
        CancellationTokenSource? _cts;

        public AssistantView()
        {
            var root = Kit.Rows(new Grid(), 0, 1, 0);
            var top = new DockPanel { Margin = new Thickness(2, 0, 2, 6) };
            var gear = new Button { Style = Ui.StyleRes("Pill"), Content = "Provider", Padding = new Thickness(10, 3, 10, 3), FontSize = 11.5 };
            var clear = new Button { Style = Ui.StyleRes("Pill"), Content = "New chat", Padding = new Thickness(10, 3, 10, 3), FontSize = 11.5, Margin = new Thickness(0, 0, 6, 0) };
            clear.Click += (s, e) => { _cts?.Cancel(); _history.Clear(); _chat.Children.Clear(); Welcome(); };
            DockPanel.SetDock(gear, Dock.Right);
            DockPanel.SetDock(clear, Dock.Right);
            top.Children.Add(gear);
            top.Children.Add(clear);
            _status.Style = Ui.StyleRes("Caption");
            _status.VerticalAlignment = VerticalAlignment.Center;
            top.Children.Add(_status);
            root.Children.Add(top);

            _scroll = new ScrollViewer { Content = _chat, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            _setup = BuildSetup();
            var mid = new Grid();
            mid.Children.Add(_scroll);
            mid.Children.Add(_setup);
            Grid.SetRow(mid, 1);
            root.Children.Add(mid);
            gear.Click += (s, e) => _setup.Visibility = _setup.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

            var bottom = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            var send = new Button { Style = Ui.StyleRes("Pill"), Content = "Send", Foreground = Brushes.Black, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8, 0, 0, 0) };
            send.SetResourceReference(BackgroundProperty, "AccentBrush");
            send.Click += (s, e) => Submit();
            DockPanel.SetDock(send, Dock.Right);
            bottom.Children.Add(send);
            _input.Style = Ui.StyleRes("Field");
            _input.Tag = "Ask something, or try /timer 10, /note …, /todo …, /search …, /volume 40, /flash …, /open app";
            _input.AcceptsReturn = false;
            _input.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Submit(); e.Handled = true; } };
            bottom.Children.Add(_input);
            Grid.SetRow(bottom, 2);
            root.Children.Add(bottom);
            Content = root;
            Welcome();
        }

        void Welcome()
        {
            if (_chat.Children.Count > 0) return;
            AddBubble("assistant", AssistantService.Configured
                ? "Hi! Ask me anything. Commands like /timer 10 or /note buy milk run locally without contacting any AI."
                : "Commands like /timer 10, /todo, /volume 40 work right away, offline.\nTo chat, pick a provider: Ollama (local, free) or your own OpenAI-compatible or Anthropic API key. Nothing is sent anywhere until you do.");
        }

        public void OnActivated()
        {
            UpdateStatus();
            if (Pending != null) { _input.Text = Pending; Pending = null; Submit(); }
        }

        public void OnDeactivated() { }

        void UpdateStatus() => _status.Text = S.AiProvider == "Off" ? "No provider — commands only" : $"{S.AiProvider} · {(string.IsNullOrWhiteSpace(S.AiModel) ? "no model set" : S.AiModel)}";

        Border AddBubble(string role, string text)
        {
            bool user = role == "user";
            var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, Foreground = user ? Brushes.Black : Ui.Res("TextBrush"), LineHeight = 19 };
            var b = new Border
            {
                Child = tb, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(user ? 80 : 0, 0, user ? 6 : 80, 6),
                HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left, Background = user ? null : Ui.Res("CardBrush"),
                Cursor = Cursors.Hand, ToolTip = "Click to copy"
            };
            if (user) b.SetResourceReference(Border.BackgroundProperty, "AccentBrush");
            b.MouseLeftButtonUp += (s, e) => { try { Clipboard.SetText(tb.Text); } catch { } NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Copy, Title = "Copied" }); };
            _chat.Children.Add(b);
            _scroll.ScrollToEnd();
            return b;
        }

        async void Submit()
        {
            var text = _input.Text.Trim();
            if (text.Length == 0) return;
            _input.Text = "";
            AddBubble("user", text);
            if (text.StartsWith("/")) { AddBubble("assistant", RunCommand(text)); return; }
            if (!AssistantService.Configured)
            {
                AddBubble("assistant", "No AI provider is set up yet. Click “Provider” to connect Ollama or your own API key.");
                _setup.Visibility = Visibility.Visible;
                return;
            }
            _history.Add(("user", text));
            var bubble = AddBubble("assistant", "…");
            var tb = (TextBlock)bubble.Child;
            var sb = new System.Text.StringBuilder();
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            try
            {
                await AssistantService.StreamAsync(_history.TakeLast(20).ToList(), d => Dispatcher.BeginInvoke(new Action(() => { sb.Append(d); tb.Text = sb.ToString(); _scroll.ScrollToEnd(); })), _cts.Token);
                await Dispatcher.InvokeAsync(() => { });
                _history.Add(("assistant", sb.ToString()));
                if (sb.Length == 0) tb.Text = "(no reply)";
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { tb.Text = "Couldn't get a reply: " + ex.Message; tb.Foreground = Ui.Res("RedBrush"); }
        }

        /// <summary>Local, deterministic commands — never sent to a model.</summary>
        static string RunCommand(string text)
        {
            var parts = text.Split(' ', 2);
            var arg = parts.Length > 1 ? parts[1].Trim() : "";
            var notch = NotchWindow.Instance;
            switch (parts[0].ToLowerInvariant())
            {
                case "/timer":
                    if (!int.TryParse(arg.Split(' ')[0].TrimEnd('m'), out var min) || min < 1) return "Usage: /timer 10  (minutes)";
                    TimerService.Instance.CdSet(min);
                    TimerService.Instance.CdToggle();
                    return $"✓ {min}-minute timer started.";
                case "/note":
                    if (arg.Length == 0) return "Usage: /note text";
                    NotesStore.Data.Text = (NotesStore.Data.Text.Length > 0 ? NotesStore.Data.Text + "\n" : "") + arg;
                    NotesStore.Save();
                    return "✓ Added to Notes.";
                case "/todo":
                    if (arg.Length == 0) return "Usage: /todo task";
                    NotesStore.Data.Todos.Insert(0, new TodoItem { Text = arg });
                    NotesStore.Save();
                    return "✓ Added to your to-do list.";
                case "/search":
                    if (arg.Length == 0) return "Usage: /search query";
                    Ui.Open("https://www.google.com/search?q=" + Uri.EscapeDataString(arg));
                    return "✓ Opened in your browser.";
                case "/volume":
                    if (!int.TryParse(arg.TrimEnd('%'), out var vol)) return "Usage: /volume 0-100";
                    AudioService.Instance.Volume = Math.Clamp(vol, 0, 100) / 100f;
                    return $"✓ Volume {Math.Clamp(vol, 0, 100)}%.";
                case "/flash":
                    if (arg.Length == 0) return "Usage: /flash message";
                    notch?.Flash(arg, Brushes.White, 5);
                    return "✓ Flashing on the notch.";
                case "/open":
                    var hit = SpotlightService.Query(arg).FirstOrDefault(i => i.Kind == "app" || i.Kind == "uwp" || i.Kind == "setting");
                    if (hit == null) return $"Couldn't find “{arg}”.";
                    SpotlightService.Launch(hit);
                    return $"✓ Opening {hit.Name}.";
                case "/alarm":
                    var t = arg.Split(':');
                    if (t.Length != 2 || !int.TryParse(t[0], out var h) || !int.TryParse(t[1], out var m) || h > 23 || m > 59) return "Usage: /alarm 07:30";
                    SettingsStore.Current.Alarms.Add(new AlarmItem { Hour = h, Minute = m, Label = "Alarm" });
                    SettingsStore.Save();
                    return $"✓ Alarm set for {h:00}:{m:00}.";
                default:
                    return "Commands: /timer 10 · /alarm 07:30 · /note text · /todo task · /search query · /volume 40 · /flash text · /open app";
            }
        }

        Border BuildSetup()
        {
            var p = new StackPanel();
            p.Children.Add(new TextBlock { Text = "AI provider", Style = Ui.StyleRes("H1"), Margin = new Thickness(0, 0, 0, 4) });
            p.Children.Add(new TextBlock { Text = "Opt-in. Ollama runs on your PC. API keys are encrypted for your Windows account (DPAPI) and only sent to the endpoint you choose. Crest itself collects nothing.", Style = Ui.StyleRes("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            var seg = new StackPanel { Orientation = Orientation.Horizontal };
            var endpoint = new TextBox { Style = Ui.StyleRes("Field"), Text = S.AiEndpoint, Margin = new Thickness(0, 8, 0, 0) };
            var model = new TextBox { Style = Ui.StyleRes("Field"), Text = S.AiModel, Tag = "Model name (e.g. the one you pulled in Ollama)", Margin = new Thickness(0, 8, 0, 0) };
            var key = new PasswordBox { Style = Ui.StyleRes("PasswordField"), Margin = new Thickness(0, 8, 0, 0) };
            var models = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            void apply(string prov)
            {
                endpoint.Tag = "Endpoint — default " + (AssistantService.DefaultEndpoint(prov) is var d && d.Length > 0 ? d : "n/a");
                key.Visibility = prov == "OpenAI" || prov == "Anthropic" ? Visibility.Visible : Visibility.Collapsed;
                endpoint.Visibility = model.Visibility = prov == "Off" ? Visibility.Collapsed : Visibility.Visible;
            }
            foreach (var prov in new[] { "Off", "Ollama", "OpenAI", "Anthropic" })
            {
                var rb = new RadioButton { Style = Ui.StyleRes("Seg"), Content = prov == "OpenAI" ? "OpenAI-compatible" : prov, GroupName = "ai-prov", IsChecked = S.AiProvider == prov };
                var pv = prov;
                rb.Checked += (s, e) => apply(pv);
                rb.Tag = prov;
                seg.Children.Add(rb);
            }
            p.Children.Add(new Border { Background = Ui.Res("Card2Brush"), CornerRadius = new CornerRadius(10), Padding = new Thickness(2), Child = seg, HorizontalAlignment = HorizontalAlignment.Left });
            p.Children.Add(endpoint);
            p.Children.Add(model);
            p.Children.Add(models);
            p.Children.Add(key);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 10, 0, 0) };
            var save = new Button { Style = Ui.StyleRes("Pill"), Content = "Save", Foreground = Brushes.Black, Padding = new Thickness(16, 6, 16, 6) };
            save.SetResourceReference(BackgroundProperty, "AccentBrush");
            var list = new Button { Style = Ui.StyleRes("Pill"), Content = "List Ollama models", Margin = new Thickness(8, 0, 0, 0) };
            var msg = new TextBlock { Style = Ui.StyleRes("Caption"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            row.Children.Add(save); row.Children.Add(list); row.Children.Add(msg);
            p.Children.Add(row);
            string Selected() => seg.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Tag as string ?? "Off";
            save.Click += (s, e) =>
            {
                S.AiProvider = Selected();
                S.AiEndpoint = endpoint.Text.Trim();
                S.AiModel = model.Text.Trim();
                if (key.Password.Length > 0) { AssistantService.SetKey(key.Password); key.Password = ""; }
                SettingsStore.Save();
                msg.Text = "Saved.";
                UpdateStatus();
            };
            list.Click += async (s, e) =>
            {
                S.AiProvider = "Ollama";
                S.AiEndpoint = endpoint.Text.Trim();
                models.Children.Clear();
                try
                {
                    foreach (var m in await AssistantService.OllamaModelsAsync())
                    {
                        var b = new Button { Style = Ui.StyleRes("Pill"), Content = m, Margin = new Thickness(0, 0, 6, 6), Padding = new Thickness(10, 3, 10, 3) };
                        var mm = m;
                        b.Click += (s2, e2) => model.Text = mm;
                        models.Children.Add(b);
                    }
                    msg.Text = models.Children.Count == 0 ? "Ollama has no models — run `ollama pull <model>`." : "Pick a model, then Save.";
                }
                catch { msg.Text = "Ollama isn't reachable at " + AssistantService.Endpoint; }
            };
            apply(S.AiProvider);
            return new Border { Style = Ui.StyleRes("Card"), Background = Ui.Res("Card2Brush"), Child = new ScrollViewer { Content = p, VerticalScrollBarVisibility = ScrollBarVisibility.Auto }, Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top };
        }
    }
}
