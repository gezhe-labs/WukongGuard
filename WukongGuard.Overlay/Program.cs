using System.IO.Pipes;
using System.Text;

namespace WukongGuard.Overlay;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var singleInstance = new Mutex(true, "Local\\WukongGuardOverlay", out bool created);
        if (!created) return;
        ApplicationConfiguration.Initialize();
        using var form = new AlertForm(args.Contains("--exit-with-game", StringComparer.Ordinal));
        var server = new Thread(() => Serve(form)) { IsBackground = true, Name = "WukongGuard pipe" };
        server.Start();
        Application.Run(form);
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
