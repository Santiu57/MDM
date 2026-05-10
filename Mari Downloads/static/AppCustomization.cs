using System;
using System.Collections.Generic;
using System.Text;

namespace Mari_Downloads
{
    public static class AppCustomization
    {
        public static void TraverseAllControls(Control parent, Action<Control> action)
        {
            action(parent);

            foreach (Control control in parent.Controls)
            {
                TraverseAllControls(control, action);
            }
        }
        public static void ColorComponents(Control parent, Color back, Color fore)
        {
            TraverseAllControls(parent, control =>
            {
                if (Equals(control.Tag, "NoAutoColor"))
                    return;

                switch (control)
                {
                    case DataGridView dgv:

                        dgv.BackgroundColor = back;

                        dgv.DefaultCellStyle.BackColor = back;
                        dgv.DefaultCellStyle.ForeColor = fore;

                        dgv.ColumnHeadersDefaultCellStyle.BackColor = back;
                        dgv.ColumnHeadersDefaultCellStyle.ForeColor = fore;

                        break;

                    case Panel:

                        break;

                    default:

                        control.BackColor = back;
                        control.ForeColor = fore;

                        break;
                }
            });
        }
        public static void EnsureAutoSize(Control parent)
        {
            TraverseAllControls(parent, control =>
            {
                try
                {
                    if (control is DataGridView) return;
                    var prop = control.GetType().GetProperty("AutoSize");
                    if (prop != null && prop.CanWrite)
                    {
                        // Only set if currently false to avoid overwriting explicit true values
                        var current = (bool)prop.GetValue(control);
                        if (!current)
                            prop.SetValue(control, true);
                    }
                }
                catch { }
            });
        }
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
        public static void ForceToolStripRefresh(Control parent)
        {
            foreach (Control control in parent.Controls)
            {
                if (control is ToolStrip ts)
                {
                    ts.SuspendLayout();
                    ts.Font = parent.Font;

                    foreach (ToolStripItem item in ts.Items)
                    {
                        item.Font = parent.Font;
                        item.AutoSize = true;
                    }

                    ts.PerformLayout();
                    ts.ResumeLayout();
                }

                if (control.HasChildren)
                    ForceToolStripRefresh(control);
            }
        }
    }
}
