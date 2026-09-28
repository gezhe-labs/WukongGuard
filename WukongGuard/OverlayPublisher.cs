using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using WukongGuard.Core;

namespace WukongGuard
{
    internal static class OverlayPublisher
    {
        private static int missingLogged;

        internal static void Show(MissableRule rule, Action<bool> completed = null)
        {
            string[] items = new string[6];
            items[0] = "SHOW";
            items[1] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rule.Id));
            for (int i = 0; i < 4; i++)
                items[i + 2] = Convert.ToBase64String(Encoding.UTF8.GetBytes(rule.Spoilers[i]));
            string line = string.Join("|", items);
            ThreadPool.QueueUserWorkItem(_ =>
            {
                bool delivered = Send(line);
                try { completed?.Invoke(delivered); }
                catch (Exception ex) { TraceLog.Write("[WukongGuard] delivery callback failed: " + ex); }
            });
        }

        internal static void ReportStatus(string status)
        {
            string line = "STATUS|" + Convert.ToBase64String(Encoding.UTF8.GetBytes(status));
            ThreadPool.QueueUserWorkItem(_ => Send(line));
        }

        private static bool Send(string line)
        {
            try
            {
                using (var pipe = new NamedPipeClientStream(".", "WukongGuardOverlay", PipeDirection.InOut))
                {
                    pipe.Connect(300);
                    // The game's Mono pipe stream does not implement ReadTimeout.
                    // The overlay replies with ACK and closes the connection.
                    using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 1024, true))
                    {
                        writer.WriteLine(line);
                        writer.Flush();
                    }
                    using (var reader = new StreamReader(pipe, Encoding.UTF8))
                        if (reader.ReadLine() == "ACK")
                        {
                            Interlocked.Exchange(ref missingLogged, 0);
                            return true;
                        }
                    throw new IOException("overlay did not acknowledge display");
                }
            }
            catch (Exception ex)
            {
                if (Interlocked.Exchange(ref missingLogged, 1) == 0)
                    TraceLog.Write("[WukongGuard] overlay unavailable: " + ex);
                return false;
            }
        }
    }
}
