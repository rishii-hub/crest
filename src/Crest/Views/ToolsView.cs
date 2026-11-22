using System;
using System.IO;
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
    /// <summary>Voice memos, screen recording, your scripts with live progress, and LAN Drop from a phone.</summary>
    public sealed class ToolsView : UserControl, ITabView
    {
        static AppSettings S => SettingsStore.Current;
        readonly DispatcherTimer _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        readonly Button _rec = new Button(), _screen = new Button();
        readonly TextBlock _recTime = Kit.Big("00:00", 22), _recNote = Kit.Small(), _captureNote = Kit.Small(), _dropUrl = new TextBlock(), _dropPin = Kit.Big("", 24);
        readonly ProgressLine _level = new ProgressLine { Height = 4 };
        readonly StackPanel _memos = new StackPanel(), _autos = new StackPanel(), _dropOn = new StackPanel();
        readonly CheckBox _dropSwitch = new CheckBox();
        readonly Image _qr = new Image { Width = 112, Height = 112 };

        public ToolsView()
        {
            // Three columns: memo + screen recording stacked | automations | LAN Drop
            var grid = Kit.Cols(1, 1, 1);
            var left = new StackPanel();

            // ---- Voice memo
            var memo = new StackPanel();
            var recRow = new StackPanel { Orientation = Orientation.Horizontal };
            _rec.Style = Ui.StyleRes("PrimaryButton");
            _rec.Width = _rec.Height = 46;
            _rec.Background = Ui.Res("RedBrush");
            _rec.Foreground = Brushes.White;
            _rec.ToolTip = "Record / stop";
            _rec.Click += (s, e) =>
            {
                if (RecorderService.Recording) RecorderService.Stop();
                else if (RecorderService.Start() is string err) _recNote.Text = err;
                Refresh();
            };
            recRow.Children.Add(_rec);
            var rs = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = 120 };
            rs.Children.Add(_recTime);
            rs.Children.Add(_level);
            recRow.Children.Add(rs);
            memo.Children.Add(recRow);
            _recNote.Margin = new Thickness(0, 6, 0, 4);
            _recNote.TextWrapping = TextWrapping.Wrap;
            memo.Children.Add(_recNote);
            memo.Children.Add(_memos);
            left.Children.Add(Kit.Card(memo, "Voice memo"));

            // ---- Screen recording
            var scr = new StackPanel();
            _screen.Style = Ui.StyleRes("Pill");
            _screen.HorizontalAlignment = HorizontalAlignment.Left;
            _screen.Click += (s, e) => { RecorderService.ToggleGameBar(); Refresh(); };
            scr.Children.Add(_screen);
            _screen.ToolTip = "Xbox Game Bar (Win+Alt+R) records the app in front and saves to Videos\\Captures.";
            _captureNote.TextWrapping = TextWrapping.Wrap;
            _captureNote.Margin = new Thickness(0, 6, 0, 0);
            scr.Children.Add(_captureNote);
            var openCaps = new Button { Style = Ui.StyleRes("Pill"), Content = "Captures", ToolTip = "Open Videos\\Captures", Margin = new Thickness(6, 0, 0, 0) };
            openCaps.Click += (s, e) =>
            {
                var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "Captures");
                Directory.CreateDirectory(dir);
                Ui.Open(dir);
            };
            scr.Children.Remove(_screen);
            var scrRow = new StackPanel { Orientation = Orientation.Horizontal };
            scrRow.Children.Add(_screen);
            scrRow.Children.Add(openCaps);
            scr.Children.Insert(0, scrRow);
            var scrCard = Kit.Card(scr, "Screen recording");
            scrCard.Margin = new Thickness(scrCard.Margin.Left, 8, scrCard.Margin.Right, scrCard.Margin.Bottom);
            left.Children.Add(scrCard);
            grid.Children.Add(Kit.At(left, 0));

            // ---- Automations
            var auto = new DockPanel();
            var add = new Button { Style = Ui.StyleRes("Pill"), Content = "+ Add script…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
            add.Click += (s, e) => AddScript();
            DockPanel.SetDock(add, Dock.Bottom);
            auto.Children.Add(add);
            var help = new TextBlock { Text = "PowerShell, batch, AutoHotkey or any program. Print “progress: 40” or “[3/10]” to fill the bar.", Style = Ui.StyleRes("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };
            DockPanel.SetDock(help, Dock.Top);
            auto.Children.Add(help);
            auto.Children.Add(new ScrollViewer { Content = _autos, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            grid.Children.Add(Kit.At(Kit.Card(auto, "Automations"), 1));

            // ---- LAN Drop
            var drop = new StackPanel();
            var head = new DockPanel();
            _dropSwitch.Style = Ui.StyleRes("Switch");
            _dropSwitch.IsChecked = S.LanDrop;
            _dropSwitch.Click += (s, e) =>
            {
                S.LanDrop = _dropSwitch.IsChecked == true;
                SettingsStore.Save();
                if (S.LanDrop) { if (LanDropService.Start() is string err) { _dropUrl.Text = err; S.LanDrop = false; _dropSwitch.IsChecked = false; } }
                else LanDropService.Stop();
                Refresh();
            };
            DockPanel.SetDock(_dropSwitch, Dock.Right);
            head.Children.Add(_dropSwitch);
            head.Children.Add(new TextBlock { Text = "Get files from a phone's browser on this Wi-Fi. You accept each one.", Style = Ui.StyleRes("Caption"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 10, 0) });
            drop.Children.Add(head);
            var on = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
            _qr.Width = _qr.Height = 96;
            _qr.VerticalAlignment = VerticalAlignment.Top;
            DockPanel.SetDock(_qr, Dock.Right);
            on.Children.Add(_qr);
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(Kit.Small("PIN"));
            info.Children.Add(_dropPin);
            _dropUrl.FontSize = 10.5;
            _dropUrl.Foreground = Ui.Res("Text2Brush");
            _dropUrl.TextWrapping = TextWrapping.Wrap;
            _dropUrl.Margin = new Thickness(0, 4, 8, 4);
            info.Children.Add(_dropUrl);
            var folder = new Button { Style = Ui.StyleRes("Pill"), Content = "Received", ToolTip = "Open Downloads\\Crest Drop", HorizontalAlignment = HorizontalAlignment.Left };
            folder.Click += (s, e) => { Directory.CreateDirectory(LanDropService.Folder); Ui.Open(LanDropService.Folder); };
            info.Children.Add(folder);
            on.Children.Add(info);
            _dropOn.Children.Add(on);
            drop.Children.Add(_dropOn);
            grid.Children.Add(Kit.At(Kit.Card(drop, "LAN Drop"), 2));

            Content = grid;
            _timer.Tick += (s, e) => Tick();
            RecorderService.Changed += () => { if (IsVisible) Refresh(); };
            AutomationService.Changed += () => { if (IsVisible) Refresh(); };
            LanDropService.Changed += () => { if (IsVisible) Refresh(); };
        }

        public void OnActivated() { Refresh(); _timer.Start(); }
        public void OnDeactivated() => _timer.Stop();

        void Tick()
        {
            if (RecorderService.Recording)
            {
                var act = ActivityHub.Current().FirstOrDefault(a => a.Id == "memo");
                _recTime.Text = act?.Value ?? "";
                _level.Value = Math.Min(1, RecorderService.Level * 1.5);
            }
            _captureNote.Text = RecorderService.CaptureApps.Length > 0 ? "Capturing your screen right now: " + RecorderService.CaptureApps : "No app is capturing your screen right now.";
        }

        void Refresh()
        {
            bool rec = RecorderService.Recording;
            _rec.Content = new Border { Width = rec ? 16 : 18, Height = rec ? 16 : 18, CornerRadius = new CornerRadius(rec ? 4 : 9), Background = Brushes.White };
            if (!rec) { _recTime.Text = "00:00"; _level.Value = 0; }
            _level.Brush = Ui.Res("RedBrush");
            if (!rec && string.IsNullOrEmpty(_recNote.Text)) _recNote.Text = "Saved as WAV in Music\\Crest Memos.";

            _memos.Children.Clear();
            foreach (var f in RecorderService.Memos().Take(1))
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var acts = new StackPanel { Orientation = Orientation.Horizontal };
                var play = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Play, Width = 24, Height = 24, FontSize = 10, ToolTip = "Play" };
                var path = f.FullName;
                play.Click += (s, e) => Ui.Open(path);
                var reveal = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.OpenFolder, Width = 24, Height = 24, FontSize = 10, ToolTip = "Show in folder" };
                reveal.Click += (s, e) => Ui.Reveal(path);
                var del = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Delete, Width = 24, Height = 24, FontSize = 10, ToolTip = "Move to Recycle Bin" };
                del.Click += (s, e) =>
                {
                    try { Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin); } catch { }
                    Refresh();
                };
                acts.Children.Add(play); acts.Children.Add(reveal); acts.Children.Add(del);
                DockPanel.SetDock(acts, Dock.Right);
                row.Children.Add(acts);
                row.Children.Add(new TextBlock { Text = f.Name.Replace(".wav", ""), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
                _memos.Children.Add(row);
            }

            _screen.Content = RecorderService.GameBarRecording ? "■  Stop" : "●  Record";
            _screen.Foreground = RecorderService.GameBarRecording ? Ui.Res("RedBrush") : Ui.Res("TextBrush");

            _autos.Children.Clear();
            if (S.Automations.Count == 0) _autos.Children.Add(Kit.Small("No scripts yet."));
            foreach (var a in S.Automations.ToList())
            {
                var item = a;
                bool running = AutomationService.IsRunning(a.Id);
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var acts = new StackPanel { Orientation = Orientation.Horizontal };
                var run = new Button { Style = Ui.StyleRes("Pill"), Content = running ? "Stop" : "Run", Padding = new Thickness(10, 2, 10, 2), FontSize = 11.5 };
                run.Click += (s, e) =>
                {
                    if (AutomationService.IsRunning(item.Id)) AutomationService.Cancel(item.Id);
                    else if (AutomationService.Start(item) is string err) NotchWindow.Instance?.ShowBanner(new Toast { Glyph = Glyphs.Close, Title = err });
                    Refresh();
                };
                var rm = new Button { Style = Ui.StyleRes("IconButton"), Content = Glyphs.Close, Width = 22, Height = 22, FontSize = 9, ToolTip = "Remove" };
                rm.Click += (s, e) => { S.Automations.Remove(item); SettingsStore.Save(); Refresh(); };
                acts.Children.Add(run); acts.Children.Add(rm);
                DockPanel.SetDock(acts, Dock.Right);
                row.Children.Add(acts);
                row.Children.Add(new TextBlock { Text = a.Name, FontSize = 12.5, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = a.Path });
                _autos.Children.Add(row);
            }

            bool dropOn = LanDropService.Running;
            _dropSwitch.IsChecked = dropOn;
            _dropOn.Visibility = dropOn ? Visibility.Visible : Visibility.Collapsed;
            if (dropOn)
            {
                _dropPin.Text = LanDropService.Pin;
                var url = LanDropService.Url;
                _dropUrl.Text = url == null ? "No network connection." : new Uri(url).Authority;
                _dropUrl.ToolTip = url;
                _qr.ToolTip = url == null ? null : "Scan with your phone's camera\n" + url;
                _qr.Source = LanDropService.Qr();
            }
            Tick();
        }

        void AddScript()
        {
            var notch = NotchWindow.Instance;
            if (notch != null) notch.DragOut = true;
            try
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Add a script to run from the notch",
                    Filter = "Scripts and programs|*.ps1;*.bat;*.cmd;*.ahk;*.exe;*.py;*.vbs;*.lnk|All files|*.*"
                };
                if (dlg.ShowDialog() == true)
                {
                    S.Automations.Add(new AutomationItem { Name = Path.GetFileNameWithoutExtension(dlg.FileName), Path = dlg.FileName });
                    SettingsStore.Save();
                }
            }
            finally { if (notch != null) notch.DragOut = false; }
            Refresh();
        }
    }
}
