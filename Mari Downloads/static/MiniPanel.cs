using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mari_Downloads
{
    public class MiniPanel : Panel
    {
        private readonly Panel _contentPanel;
        private readonly FlowLayoutPanel _downPanel;
        private readonly FlowLayoutPanel _upPanel;
        private readonly FlowLayoutPanel _rowsContainer;

        public Panel ContentPanel => _contentPanel;

        public bool IsVisible => Visible;

        public MiniPanel(bool bottom = false, bool up = false)
        {
            Dock = DockStyle.Fill;
            Visible = false;

            MiniPanelManager.Register(this);

            Padding = new Padding(10);
            MinimumSize = new Size(100, 100);

            _contentPanel = MakeScrollPanel(DockStyle.Fill);

            _downPanel = MakeFlowPanel(
                DockStyle.Bottom,
                FlowDirection.RightToLeft,
                60,
                "DownPanel");

            _upPanel = MakeFlowPanel(
                DockStyle.Top,
                FlowDirection.LeftToRight,
                60,
                "UpPanel");

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

        // ─── Helpers ─────────────────────────────────────────────

        private static Panel MakeScrollPanel(DockStyle dock)
        {
            return new Panel
            {
                Dock = dock,
                AutoScroll = true
            };
        }

        private static FlowLayoutPanel MakeFlowPanel(
        DockStyle dock,
        FlowDirection direction,
        int height,
        string tag = "Flow")
        {
            return new FlowLayoutPanel
            {
                Dock = dock,
                FlowDirection = direction,
                Height = height,
                AutoScroll = true,
                WrapContents = false,
                Tag = tag
            };
        }

        private static FlowLayoutPanel MakeRow(params Control[] controls)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 5)
            };

            row.Controls.AddRange(controls);

            return row;
        }

        private static Control MakeCenteredRow(
    FlowDirection direction,
    params Control[] controls)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = direction,
                WrapContents = false,
                Margin = new Padding(0)
            };

            row.Controls.AddRange(controls);

            var container = new Panel
            {
                Height = row.PreferredSize.Height + 10,
                Width = row.PreferredSize.Width + 20,
                Margin = new Padding(0),
                Padding = new Padding(0)
            };

            void CenterRow()
            {
                row.Location = new Point(
                    Math.Max(0, (container.Width - row.Width) / 2),
                    Math.Max(0, (container.Height - row.Height) / 2));
            }

            container.Controls.Add(row);

            row.SizeChanged += (_, __) => CenterRow();

            container.Resize += (_, __) => CenterRow();

            CenterRow();

            return container;
        }

        // ─── Public API ─────────────────────────────────────────

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

        public void AddDownControls(params Control[] controls)
        {
            _downPanel.Controls.Add(
            MakeCenteredRow(
                FlowDirection.LeftToRight,
                controls));
                }

        public void AddUpControls(params Control[] controls)
        {
            _upPanel.Controls.Add(
            MakeCenteredRow(
                FlowDirection.LeftToRight,
                controls));
                }

        public void AddRow(params Control[] controls)
        {
            _rowsContainer.Controls.Add(MakeRow(controls));
        }
    }
}