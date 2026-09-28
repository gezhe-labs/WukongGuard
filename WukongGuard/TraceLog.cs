using System;
using System.IO;

namespace WukongGuard
{
    internal static class TraceLog
    {
        private const long MaxLogBytes = 2 * 1024 * 1024;
        private static readonly object Gate = new object();

        internal static void Write(string message)
        {
            Console.WriteLine(message);
            try
            {
                lock (Gate)
                {
                    string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WukongGuard");
                    Directory.CreateDirectory(directory);
                    string path = Path.Combine(directory, "guard.log");
                    if (File.Exists(path) && new FileInfo(path).Length >= MaxLogBytes)
                        File.WriteAllText(path, "[WukongGuard] log truncated at 2 MiB" + Environment.NewLine);
                    File.AppendAllText(path, DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
                }
            }
            catch (Exception ex) { Console.WriteLine("[WukongGuard] log file unavailable: " + ex.Message); }
        }
    }
}
