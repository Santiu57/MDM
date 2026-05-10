using System;
using System.Collections.Generic;
using System.Text;
using static Mari_Downloads.Main;

namespace Mari_Downloads
{
    //Panels Manager
    public static class MiniPanelManager
    {
        private static readonly List<MiniPanel> _panels = new();

        private static MiniPanel _current;

        public static Control _host;

        public static MiniPanel Current => _current;


        public static void SetHost(Control host)
        {
            _host = host;
        }


        public static void Register(MiniPanel panel)
        {
            if (!_panels.Contains(panel))
                _panels.Add(panel);
        }


        public static void Show(MiniPanel panel)
        {
            if (_host == null)
                throw new Exception("MiniPanelManager host not set");

            if (_current == panel)
                return;

            foreach (var p in _panels)
                p.Visible = false;


            if (!_host.Controls.Contains(panel))
                _host.Controls.Add(panel);


            panel.Dock = DockStyle.Fill;
            panel.Visible = true;
            panel.BringToFront();

            _current = panel;
            AppCustomization.ColorComponents(panel, Properties.Settings.Default.MainBackColor, Properties.Settings.Default.MainForeColor);
            AppCustomization.FontChange(panel, Properties.Settings.Default.MainFont);
        }


        public static void HideCurrent()
        {
            if (_current != null)
            {
                _current.Visible = false;
                _current = null;
            }
        }


        public static bool IsShowing(MiniPanel panel)
        {
            return _current == panel;
        }
    }
}
