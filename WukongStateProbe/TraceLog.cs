using System;
using System.IO;

namespace WukongStateProbe
{
    internal static class TraceLog
    {
        internal static void Write(string message)
        {
            Console.WriteLine(message);
            try
            {
                string directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WukongGuard");
                Directory.CreateDirectory(directory);
                File.AppendAllText(
                    Path.Combine(directory, "probe.log"),
                    DateTime.UtcNow.ToString("O") + " " + message + Environment.NewLine);
            }
            catch (Exception ex)
            {
                Console.WriteLine("[WukongGuard] file log unavailable: " + ex.Message);
            }
        }
    }
}
