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

        public static MiniPanel ArgsLast;
        public static MiniPanel FiltersLast;
        public static MiniPanel ConfigLast;

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

            _host.SuspendLayout();

            try
            {
                foreach (var p in _panels)
                    p.Visible = false;

                if (!_host.Controls.Contains(panel))
                    _host.Controls.Add(panel);

                panel.Visible = false;
                panel.Dock = DockStyle.Fill;

                panel.SuspendLayout();

                AppCustomization.ColorComponents(
                    panel,
                    Properties.Settings.Default.MainBackColor,
                    Properties.Settings.Default.MainForeColor);

                AppCustomization.FontChange(
                    panel,
                    Properties.Settings.Default.MainFont);

                panel.ResumeLayout(true);
                panel.PerformLayout();

                panel.BeginInvoke(new Action(() =>
                {
                    panel.Visible = true;
                    panel.BringToFront();
                    panel.Refresh();
                }));

                _current = panel;
                setLast(panel);
            }
            finally
            {
                _host.ResumeLayout(true);
            }
        }

        public static void PreloadAll()
        {
            if (_host == null)
                throw new Exception("Host not set");

            _host.SuspendLayout();

            try
            {
                foreach (var panel in _panels)
                {
                    if (!_host.Controls.Contains(panel))
                        _host.Controls.Add(panel);

                    panel.Dock = DockStyle.Fill;
                    panel.Visible = false;

                    // fuerza creación del handle
                    var h = panel.Handle;

                    AppCustomization.ColorComponents(
                        panel,
                        Properties.Settings.Default.MainBackColor,
                        Properties.Settings.Default.MainForeColor);

                    AppCustomization.FontChange(
                        panel,
                        Properties.Settings.Default.MainFont);

                    panel.PerformLayout();
                    panel.Refresh();
                }
            }
            finally
            {
                _host.ResumeLayout(true);
            }
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

        public static void setLast(MiniPanel panel)
        {
            string cat = panel.Category;
            if (cat == "Arg")
            {
                ArgsLast = panel;
            }
            else if (cat == "Config")
            {
                ConfigLast = panel;
            }
            else if(cat == "Filter")
            {
                FiltersLast = panel;
            }
        }
    }
}
