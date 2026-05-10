using System;
using System.Drawing;
using System.Windows.Forms;

namespace Mari_Downloads
{
    public static class AppCustomization
    {
        // ─── Traversal ──────────────────────────────────────────

        public static void TraverseAllControls(
            Control parent,
            Action<Control> action)
        {
            action(parent);

            foreach (Control child in parent.Controls)
                TraverseAllControls(child, action);
        }

        // ─── Colors ────────────────────────────────────────────

        public static void ColorComponents(
            Control parent,
            Color back,
            Color fore)
        {
            TraverseAllControls(parent, control =>
            {
                if (Equals(control.Tag, "NoAutoColor"))
                    return;

                bool insideSpecialPanel =
                    HasParentTag(control, "UpPanel") ||
                    HasParentTag(control, "DownPanel");

                bool isSpecialPanel =
                    Equals(control.Tag, "UpPanel") ||
                    Equals(control.Tag, "DownPanel");

                var currentBack = insideSpecialPanel || isSpecialPanel
                    ? ColorFuncs.AdjustColor(back, -10)
                    : back;

                switch (control)
                {
                    case DataGridView dgv:

                        ColorDataGridView(dgv, currentBack, fore);

                        break;

                    case Button btn:
                        control.BackColor = currentBack;
                        control.ForeColor = fore;
                        btn.FlatStyle = FlatStyle.Popup;
                        ApplyBorder(
                        control,
                        ColorFuncs.AdjustColor(back, -30));
                        break;

                    case ToolStrip ts:

                        currentBack = ColorFuncs.AdjustColor(back, -15);

                        ts.BackColor = currentBack;
                        ts.ForeColor = fore;

                        ts.GripStyle = ToolStripGripStyle.Hidden;
                        ts.Renderer = new FlatToolStripRenderer();

                        ApplyBorder(
                            ts,
                            ColorFuncs.AdjustColor(back, -30));

                        break;

                    default:

                        control.BackColor = currentBack;
                        control.ForeColor = fore;

                        break;
                }

                // Solo el panel principal recibe borde
                if (isSpecialPanel)
                {
                    control.Padding = new Padding(3);
                    ApplyBorder(
                        control,
                        ColorFuncs.AdjustColor(back, -30));
                }
            });
        }

        private static bool HasParentTag(
            Control control,
            string tag)
        {
            Control parent = control.Parent;

            while (parent != null)
            {
                if (Equals(parent.Tag, tag))
                    return true;

                parent = parent.Parent;
            }

            return false;
        }

        private static void ApplyBorder(
        Control control,
        Color borderColor)
        {
            control.Paint -= DrawBorder;
            control.Paint += DrawBorder;

            void DrawBorder(object sender, PaintEventArgs e)
            {
                using var pen = new Pen(borderColor);

                var rect = new Rectangle(
                    1,
                    1,
                    control.Width - 3,
                    control.Height - 3);

                e.Graphics.DrawRectangle(pen, rect);
            }

            control.Invalidate();
        }

        private static void ColorDataGridView(
            DataGridView dgv,
            Color back,
            Color fore)
        {
            dgv.BackgroundColor = back;

            dgv.DefaultCellStyle.BackColor = back;
            dgv.DefaultCellStyle.ForeColor = fore;

            dgv.ColumnHeadersDefaultCellStyle.BackColor = back;
            dgv.ColumnHeadersDefaultCellStyle.ForeColor = fore;

            dgv.BorderStyle = BorderStyle.FixedSingle;
            dgv.GridColor = ColorFuncs.AdjustColor(back, -30);
        }

        // ─── AutoSize ──────────────────────────────────────────

        public static void EnsureAutoSize(Control parent)
        {
            TraverseAllControls(parent, control =>
            {
                if (control is DataGridView)
                    return;

                try
                {
                    control.AutoSize = true;
                }
                catch
                {
                }
            });
        }

        // ─── Font ──────────────────────────────────────────────

        public static void FontChange(Control form, Font font)
        {
            if (font == null)
                return;

            form.SuspendLayout();

            try
            {
                form.Font = font;
            }
            finally
            {
                form.ResumeLayout(true);
                form.Refresh();
            }
        }

        //Renderers
        public class FlatToolStripRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderToolStripBorder(
                ToolStripRenderEventArgs e)
            {
                // No dibujar borde
            }

            protected override void OnRenderGrip(
                ToolStripGripRenderEventArgs e)
            {
                // No dibujar grip
            }
        }
    }
}