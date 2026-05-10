using Microsoft.Toolkit.Uwp.Notifications;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mari_Downloads
{
    public static class Notifications
    {
        public static void Show(
        string title,
        string desc,
        Type.NotifType type,
        ToastDuration duration = ToastDuration.Short,
        Dictionary<string, string>? args = null)
        {
            if (!Properties.Settings.Default.ShowNotifs)
                return;

            bool show = type switch
            {
                Notifications.Type.NotifType.Export => Properties.Settings.Default.ExportNotifications,
                Notifications.Type.NotifType.Misc => Properties.Settings.Default.MiscNotifications,
                Notifications.Type.NotifType.MinorError => Properties.Settings.Default.MinorErrorNotifications,
                Notifications.Type.NotifType.Dependencies => Properties.Settings.Default.DependencyNotifications,
                _ => Properties.Settings.Default.ShowNotifs,
            };

            if (!show)
                return;

            try
            {
                var builder = new ToastContentBuilder()
                    .AddText(title)
                    .AddText(desc);

                if (args != null)
                {
                    foreach (var kv in args)
                        builder.AddArgument(kv.Key, kv.Value);
                }

                builder.AddAppLogoOverride(
                    new Uri(Path.Combine(Application.StartupPath, "media/icon.png")),
                    ToastGenericAppLogoCrop.Default);

                builder.SetToastDuration(duration);
                builder.Show(toast => toast.Tag = "Mari");
            }
            catch (Exception ex)
            {
                ScrollableMessageBox.Show(
                    $"Error showing notification:{Environment.NewLine}{ex.Message}",
                    "Notification Error",
                    MessageBoxButtons.OK);
            }
        }
        public class Type
        {
            public enum NotifType
            {
                Misc,
                Export,
                Dependencies,
                MinorError
            }
            public NotifType Current { get; private set; } = NotifType.Misc;

            public bool Is(NotifType type) => Current == type;

            public string GetDisplay()
            {
                return Current switch
                {
                    NotifType.Export => "Export",
                    NotifType.Misc => "Misc",
                    NotifType.Dependencies => "Dependencies",
                    NotifType.MinorError => "Minor Error",
                    _ => "Unknown"
                };
            }
        }
    }
}
