using System;
using System.Collections.Generic;
using System.Text;

namespace Mari_Downloads
{
    public class MiniPanel : Panel
    {
        private readonly Panel _contentPanel;
        private readonly FlowLayoutPanel _downPanel;
        private readonly FlowLayoutPanel _upPanel;
        private readonly FlowLayoutPanel _rowsContainer;

        public Panel ContentPanel => _contentPanel;

        public bool IsVisible => this.Visible;

        public MiniPanel(bool bottom = false, bool up = false)
        {
            this.Dock = DockStyle.Fill;
            this.Visible = false;

            MiniPanelManager.Register(this);

            this.Padding = new Padding(10);
            this.MinimumSize = new Size(100, 100);

            _contentPanel = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true
            };

            _downPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                FlowDirection = FlowDirection.RightToLeft,
                Height = 45,
            };

            _upPanel = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.LeftToRight,
                Height = 60,
                AutoScroll = true,
            };

            _rowsContainer = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = true
            };

            _contentPanel.Controls.Add(_rowsContainer);

            Controls.Add(_contentPanel);

            if (bottom)
                Controls.Add(_downPanel);
            if (up)
                Controls.Add(_upPanel);
        }

        public void AddControl(Control control, DockStyle dock = DockStyle.Top)
        {
            control.Dock = dock;
            _contentPanel.Controls.Add(control);
            _contentPanel.Controls.SetChildIndex(control, 0);
        }

        public void SetMainControl(Control control)
        {
            _contentPanel.Controls.Clear();
            control.Dock = DockStyle.Fill;
            _contentPanel.Controls.Add(control);
        }

        public void AddDownControls(Control[] controls)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            foreach (Control control in controls)
            {
                row.Controls.Add(control);
            }
            _downPanel.Controls.Add(row);
        }
        public void AddUpControls(Control[] controls)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false
            };
            foreach (Control control in controls)
            {
                row.Controls.Add(control);
            }
            _upPanel.Controls.Add(row);
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
