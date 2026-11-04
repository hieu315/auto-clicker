using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace SwiftMacroPro
{
    internal static class HeadlessBrowserRunner
    {
        public static string ProfileDirectory
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SwiftMacroPro", "ChromeProfile"); }
        }

        public static string Run(MacroProfile profile, List<MacroStep> steps, int loopCount, double speed, int jitter, CancellationToken token)
        {
            string worker = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "browser", "headless.js");
            if (!File.Exists(worker)) throw new FileNotFoundException("Thiếu browser/headless.js", worker);
            Directory.CreateDirectory(ProfileDirectory);
            string configPath = Path.Combine(Path.GetTempPath(), "swiftmacro-" + Guid.NewGuid().ToString("N") + ".json");
            var config = new
            {
                url = profile.BrowserUrl,
                originX = profile.BrowserOriginX,
                originY = profile.BrowserOriginY,
                width = profile.BrowserViewportWidth,
                height = profile.BrowserViewportHeight,
                loopCount = loopCount,
                speed = speed,
                jitter = jitter,
                steps = steps,
                profileDir = ProfileDirectory
            };
            JavaScriptSerializer serializer = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            File.WriteAllText(configPath, serializer.Serialize(config), new UTF8Encoding(false));
            try
            {
                using (Process process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = "node.exe",
                        Arguments = "\"" + worker + "\" \"" + configPath + "\"",
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    if (!process.Start()) throw new InvalidOperationException("Không khởi động được Node.js.");
                    bool stopSent = false;
                    while (!process.WaitForExit(100))
                    {
                        if (token.IsCancellationRequested && !stopSent)
                        {
                            stopSent = true;
                            try { process.StandardInput.WriteLine("STOP"); process.StandardInput.Flush(); } catch { }
                        }
                        if (stopSent && !process.WaitForExit(2000))
                        {
                            using (Process cleanup = Process.Start(new ProcessStartInfo
                            {
                                FileName = "taskkill.exe",
                                Arguments = "/PID " + process.Id + " /T /F",
                                UseShellExecute = false,
                                CreateNoWindow = true,
                                WindowStyle = ProcessWindowStyle.Hidden
                            }))
                            {
                                if (cleanup != null) cleanup.WaitForExit(3000);
                            }
                            if (!process.HasExited) process.Kill();
                            process.WaitForExit();
                        }
                    }
                    string output = process.StandardOutput.ReadToEnd().Trim();
                    string error = process.StandardError.ReadToEnd().Trim();
                    if (token.IsCancellationRequested) return "Đã dừng Chrome ẩn.";
                    if (process.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrEmpty(error) ? "Chrome ẩn kết thúc lỗi." : error);
                    return string.IsNullOrEmpty(output) ? "Chrome ẩn đã hoàn thành." : output;
                }
            }
            finally
            {
                try { File.Delete(configPath); } catch { }
            }
        }
    }
}
