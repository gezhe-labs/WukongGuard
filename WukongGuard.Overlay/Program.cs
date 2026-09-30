using System.IO.Pipes;
using System.Text;

namespace WukongGuard.Overlay;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 3 && args[0] == "--smoke-input" && args[1] == "--output")
        {
            ApplicationConfiguration.Initialize();
            return InputSmoke.Run(args[2]);
        }
        using var singleInstance = new Mutex(true, "Local\\WukongGuardOverlay", out bool created);
        if (!created) return 0;
        ApplicationConfiguration.Initialize();
        using var form = new AlertForm(args.Contains("--exit-with-game", StringComparer.Ordinal),
            args.Contains("--managed-session", StringComparer.Ordinal));
        var server = new Thread(() => Serve(form)) { IsBackground = true, Name = "WukongGuard pipe" };
        server.Start();
        Application.Run(form);
        return 0;
    }

    private static void Serve(AlertForm form)
    {
        while (!form.IsDisposed)
        {
            try
            {
                using var pipe = new NamedPipeServerStream("WukongGuardOverlay", PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.None);
                pipe.WaitForConnection();
                using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
                string? line = reader.ReadLine();
                if (line == null) continue;
                string[] parts = line.Split('|');
                if (form.IsDisposed || !form.IsHandleCreated) continue;
                if (parts.Length == 2 && parts[0] == "STATUS")
                {
                    string status = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                    if (status.Length > 100) continue;
                    form.Invoke(new Action(() => form.SetStatus(status)));
                }
                else if (line == "CLEAR_HISTORY")
                {
                    form.Invoke(new Action(form.ClearHistory));
                }
                else if (parts.Length == 6 && parts[0] == "SHOW")
                {
                    string[] levels = new string[4];
                    for (int i = 0; i < 4; i++)
                        levels[i] = Encoding.UTF8.GetString(Convert.FromBase64String(parts[i + 2]));
                    string id = Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                    form.Invoke(new Action(() => form.ShowAlert(id, levels)));
                }
                else continue;
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false));
                writer.WriteLine("ACK");
                writer.Flush();
            }
            catch (Exception) { Thread.Sleep(500); }
        }
    }
}
