using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Mari_Downloads
{
    internal class Packages
{
        public static string GetLatestPipVersion(string package)
        {
            var info = new ProcessStartInfo()
            {
                FileName = "py",
                Arguments = $"-m pip index versions {package}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true
            };

            using (var process = new Process())
            {
                process.StartInfo = info;
                process.Start();

                string output = process.StandardOutput.ReadToEnd();

                var match = Regex.Match(output, @"LATEST:\s+(\d+\.\d+\.\d+)");
                if (match.Success)
                    return match.Groups[1].Value;
            }

            return null;
        }
        public static bool InstallPackage(string package)
        {
            try
            {
                var info = new ProcessStartInfo()
                {
                    FileName = "py",
                    Arguments = $"-m pip install {package}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = new Process())
                {
                    process.StartInfo = info;
                    process.Start();

                    process.WaitForExit();

                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
        public static bool UpdatePackage(string package)
        {
            try
            {
                var info = new ProcessStartInfo()
                {
                    FileName = "py",
                    Arguments = $"-m pip install --upgrade {package}",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };

                using (var process = new Process())
                {
                    process.StartInfo = info;
                    process.Start();

                    process.WaitForExit();

                    return process.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
