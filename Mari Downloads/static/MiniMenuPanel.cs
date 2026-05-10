using System;
using System.Collections.Generic;
using System.Text;

namespace Mari_Downloads
{
    //Context Menu for some buttons
    public class MiniMenuPanel
    {
        private readonly FlowLayoutPanel Menu;
        private readonly Control Anchor;
        private readonly FlowLayoutPanel _rowsContainer;

        public MiniMenuPanel(Control anchor)
        {
            Anchor = anchor;
            Menu = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = true,
                BorderStyle = BorderStyle.FixedSingle,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Visible = false
            };

            _rowsContainer = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true
            };

            Menu.Controls.Add(_rowsContainer);

            Anchor.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    Menu.Visible = !Menu.Visible;
                    UpdateMenuPosition();
                }
            };

            Anchor.ParentChanged += Anchor_ParentChanged;
            Anchor.LocationChanged += (s, e) => UpdateMenuPosition();
            Anchor.SizeChanged += (s, e) => UpdateMenuPosition();
            Menu.SizeChanged += (s, e) => UpdateMenuPosition();
        }

        private void Anchor_ParentChanged(object sender, EventArgs e)
        {
            Control host = MiniPanelManager._host ?? Anchor.FindForm();

            // Menu se agrega al host principal
            if (!host.Controls.Contains(Menu))
                host.Controls.Add(Menu);

            UpdateMenuPosition();

            Menu.BringToFront();
        }

        private void UpdateMenuPosition()
        {
            Menu.BringToFront();
            if (Anchor.Parent == null || Menu.Parent == null) return;
            Point screen = Anchor.PointToScreen(Point.Empty);
            Point client = Menu.Parent.PointToClient(screen);
            Menu.Location = new Point((client.X + (Anchor.Size.Width / 2)) - (Menu.Size.Width / 2), (client.Y - 3) - Menu.Size.Height);
        }
        public void AddRow(Control[] controls)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 10)
            };

            foreach (Control control in controls)
                row.Controls.Add(control);

            _rowsContainer.Controls.Add(row);
        }
    }
}
