using System;
using System.Drawing;
using Crest.Core;
using Crest.Services;
using WF = System.Windows.Forms;

namespace Crest
{
    /// <summary>System-tray icon with a small menu (open, settings, hide, autostart, quit).</summary>
    public sealed class TrayIcon : IDisposable
    {
        readonly WF.NotifyIcon _icon;

        public TrayIcon(NotchWindow notch)
        {
            _icon = new WF.NotifyIcon { Text = "Crest — notch for Windows", Visible = true };
            try
            {
                var res = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/crest.ico"));
                _icon.Icon = res != null ? new Icon(res.Stream, WF.SystemInformation.SmallIconSize) : SystemIcons.Application;
            }
            catch { _icon.Icon = SystemIcons.Application; }

            var menu = new WF.ContextMenuStrip { ShowImageMargin = false };
            menu.Items.Add("Open Crest", null, (s, e) => AppWindow.ShowPage("Home"));
            menu.Items.Add("Settings", null, (s, e) => AppWindow.ShowPage("Appearance"));
            menu.Items.Add("Show the island", null, (s, e) => notch.Expand("Home"));
            var hide = new WF.ToolStripMenuItem("Hide notch");
            hide.Click += (s, e) => notch.SetUserHidden(!notch.UserHidden);
            menu.Items.Add(hide);
            var startup = new WF.ToolStripMenuItem("Start with Windows");
            startup.Click += (s, e) => Startup.Enabled = !Startup.Enabled;
            menu.Items.Add(startup);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Open data folder", null, (s, e) => { System.IO.Directory.CreateDirectory(Storage.Dir); Ui.Open(Storage.Dir); });
            menu.Items.Add("Crest on GitHub", null, (s, e) => Ui.Open("https://github.com/rishii-hub/crest"));
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add("Quit Crest", null, (s, e) => System.Windows.Application.Current.Shutdown());
            menu.Opening += (s, e) =>
            {
                hide.Text = notch.UserHidden ? "Show notch" : "Hide notch";
                startup.Checked = Startup.Enabled;
            };
            _icon.ContextMenuStrip = menu;
            _icon.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) notch.Toggle(); };
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }
    }
}
