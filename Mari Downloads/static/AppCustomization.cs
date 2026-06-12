using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Mari_Downloads
{
    public static class AppCustomization
    {
        // Guarda el handler activo por control para poder desuscribirlo antes de poner uno nuevo
        private static readonly Dictionary<Control, PaintEventHandler> _borderHandlers = new();

        // ─── Traversal ──────────────────────────────────────────

        public static void TraverseAllControls(Control parent, Action<Control> action)
        {
            action(parent);
            foreach (Control child in parent.Controls)
                TraverseAllControls(child, action);
        }

        // ─── Colors ────────────────────────────────────────────

        public static void ColorComponents(Control parent, Color back, Color fore)
        {
            parent.SuspendLayout();

            try
            {
                TraverseAllControls(parent, control =>
            {

                if (Equals(control.Tag, "NoAutoColor"))
                    return;

                bool isSpecialPanel =
                    Equals(control.Tag, "UpPanel") ||
                    Equals(control.Tag, "DownPanel");

                bool insideSpecialPanel =
                    HasParentTag(control, "UpPanel") ||
                    HasParentTag(control, "DownPanel");

                Color currentBack = insideSpecialPanel || isSpecialPanel
                    ? ColorFuncs.AdjustColor(back, -10)
                    : back;

                switch (control)
                {
                    case DataGridView dgv:
                        ColorDataGridView(dgv, currentBack, fore);
                        break;

                    case Button btn:
                        btn.BackColor = currentBack;
                        btn.ForeColor = fore;
                        btn.FlatStyle = FlatStyle.Popup;
                        ApplyBorder(btn, ColorFuncs.AdjustColor(back, -30));
                        break;

                    case ToolStrip ts:
                        Color tsBack = ColorFuncs.AdjustColor(back, -15);
                        ts.BackColor = tsBack;
                        ts.ForeColor = fore;
                        ts.GripStyle = ToolStripGripStyle.Hidden;
                        ts.Renderer = new FlatToolStripRenderer();
                        ApplyBorder(ts, ColorFuncs.AdjustColor(back, -30));
                        break;

                    default:
                        control.BackColor = currentBack;
                        control.ForeColor = fore;
                        break;
                }

                if (isSpecialPanel)
                {
                    control.Padding = new Padding(3);
                    ApplyBorder(control, ColorFuncs.AdjustColor(back, -30));
                }
            });
            }
            finally
            {
                parent.ResumeLayout(true);
                parent.PerformLayout();
                parent.Refresh();
            }
        
        }

        private static bool HasParentTag(Control control, string tag)
        {
            Control parent = control.Parent;
            while (parent != null)
            {
                if (Equals(parent.Tag, tag)) return true;
                parent = parent.Parent;
            }
            return false;
        }

        private static void ApplyBorder(Control control, Color borderColor)
        {
            // Desuscribir handler anterior si existe, evita acumulación de bordes
            if (_borderHandlers.TryGetValue(control, out var oldHandler))
            {
                control.Paint -= oldHandler;
                control.Resize -= control_Resize; // limpia resize anterior también
            }

            PaintEventHandler newHandler = (sender, e) =>
            {
                var c = (Control)sender;
                using var pen = new Pen(borderColor);
                e.Graphics.DrawRectangle(pen, new Rectangle(1, 1, c.Width - 3, c.Height - 3));
            };

            _borderHandlers[control] = newHandler;
            control.Paint += newHandler;

            // Invalidar en resize para limpiar artefactos visuales
            control.Resize += control_Resize;

            control.Invalidate();
        }

        private static void control_Resize(object sender, EventArgs e)
        {
            ((Control)sender).Invalidate();
        }

        private static void ColorDataGridView(DataGridView dgv, Color back, Color fore)
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
                if (control is DataGridView ||
                    control is Panel ||
                    control is FlowLayoutPanel)
                    return;

                try
                {
                    control.AutoSize = true;
                }
                catch { }
            });
        }

        // ─── Font ──────────────────────────────────────────────

        public static void FontChange(Control form, Font font)
        {
            if (font == null) return;

            form.SuspendLayout();
            try { form.Font = font; }
            finally
            {
                form.ResumeLayout(true);
                form.Refresh();
            }
        }

        // ─── Renderers ─────────────────────────────────────────

        public class FlatToolStripRenderer : ToolStripProfessionalRenderer
        {
            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
            protected override void OnRenderGrip(ToolStripGripRenderEventArgs e) { }
        }
    }
}