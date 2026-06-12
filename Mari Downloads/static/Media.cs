using System;
using System.Collections.Generic;
using System.Text;

namespace Mari_Downloads
{
    internal class Media
{
        public static readonly Dictionary<string, string> media = new()
        {
            ["Icon"] = "icon.png",
            ["Folder"] = "folder.png",
            ["Config"] = "config.png",
            ["Clear"] = "clear.png"
        };

        public static Image Get(string key)
        {
            if (!media.TryGetValue(key, out string file))
                throw new KeyNotFoundException($"Media key '{key}' was not found.");

            string path = Path.Combine(
                Application.StartupPath,
                "media",
                file
            );

            using var temp = Image.FromFile(path);

            return new Bitmap(temp);
        }
        public static void Check()
        {
            string notFoundFiles = "";
            foreach (var file in media)
            {
                var path = Path.Combine(Application.StartupPath, "media", file.Value);
                if (!File.Exists(path))
                {
                    notFoundFiles += $"{file.Value}{Environment.NewLine}";
                }
            }

            if (!string.IsNullOrEmpty(notFoundFiles))
            {
                ScrollableMessageBox.Show(
                    $"Required files not found:{Environment.NewLine}{notFoundFiles}Ensure they exist in the 'media' directory.",
                    "File Missing",
                    MessageBoxButtons.OK
                );
                Environment.Exit(1);
            }
        }
    }
}
