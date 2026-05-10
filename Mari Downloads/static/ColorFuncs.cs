using System;
using System.Collections.Generic;
using System.Text;

namespace Mari_Downloads
{
    internal class ColorFuncs
{
        public static Color AdjustColor(Color c, int amount)
        {
            int r = Math.Clamp(c.R + amount, 0, 255);
            int g = Math.Clamp(c.G + amount, 0, 255);
            int b = Math.Clamp(c.B + amount, 0, 255);

            return Color.FromArgb(r, g, b);
        }

        public static Color GetSelectionColor(Color back)
        {
            int brightness = (back.R * 299 + back.G * 587 + back.B * 114) / 1000;

            if (brightness > 140)
                return AdjustColor(back, -40); // fondo claro → oscurecer
            else
                return AdjustColor(back, 40);  // fondo oscuro → aclarar
        }

        public static void ApplyRowColor(DataGridViewRow row, string status)
        {
            Color back = Properties.Settings.Default.MainBackColor;
            Color fore = Properties.Settings.Default.MainForeColor;

            switch (status)
            {
                case "Sleeping":
                    back = Properties.Settings.Default.ColorSleepingBack;
                    fore = Properties.Settings.Default.ColorSleepingFore;
                    break;

                case "Queued":
                    back = Properties.Settings.Default.ColorQueuedBack;
                    fore = Properties.Settings.Default.ColorQueuedFore;
                    break;

                case "Downloading":
                    back = Properties.Settings.Default.ColorDownloadingBack;
                    fore = Properties.Settings.Default.ColorDownloadingFore;
                    break;

                case "Done":
                    back = Properties.Settings.Default.ColorDoneBack;
                    fore = Properties.Settings.Default.ColorDoneFore;
                    break;

                case "Error":
                    back = Properties.Settings.Default.ColorErrorBack;
                    fore = Properties.Settings.Default.ColorErrorFore;
                    break;
            }

            row.DefaultCellStyle.BackColor = back;
            row.DefaultCellStyle.ForeColor = fore;

            row.DefaultCellStyle.SelectionBackColor = ColorFuncs.GetSelectionColor(back);
            row.DefaultCellStyle.SelectionForeColor = fore;
        }
    }
}
