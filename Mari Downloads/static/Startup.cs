using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mari_Downloads
{
     public class Startup
{
        public static void SetStartup(bool enable)
        {
            const string appName = "Mari";

            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run",
                    true);

            if (key == null)
                return;

            if (enable)
            {
                string exePath = Application.ExecutablePath;

                // Comillas por si la ruta tiene espacios
                key.SetValue(appName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(appName, false);
            }
        }
        public static bool IsStartupEnabled()
        {
            const string appName = "Mari";

            using RegistryKey? key =
                Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run");

            return key?.GetValue(appName) != null;
        }
    }
}
