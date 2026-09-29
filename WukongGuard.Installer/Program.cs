using System.Drawing;
using System.Windows.Forms;

namespace WukongGuard.Installer;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--verify")
        {
            try
            {
                Installation.VerifyPayload();
                return 0;
            }
            catch (Exception ex)
            {
                Installation.Log(ex.ToString());
                return 1;
            }
        }
        if (args.Length == 3 && args[0] == "--install" && args[1] == "--game-root")
        {
            try
            {
                Installation.InstallAsync(args[2], _ => { }).GetAwaiter().GetResult();
                return 0;
            }
            catch (Exception ex)
            {
                Installation.Log(ex.ToString());
                return 1;
            }
        }
        if (args.Length == 3 && args[0] == "--disable" && args[1] == "--game-root")
        {
            try
            {
                SessionControl.Disable(args[2]);
                return 0;
            }
            catch (Exception ex)
            {
                Installation.Log(ex.ToString());
                return 1;
            }
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new InstallerForm());
        return 0;
    }
}

internal sealed class InstallerForm : Form
{
    private readonly TextBox gamePath;
    private readonly Button installButton;
    private readonly Button browseButton;
    private readonly Button sessionButton;
    private readonly Label status;
    private readonly System.Windows.Forms.Timer sessionTimer;
    private bool armedHere;
    private bool gameSeen;

    public InstallerForm()
    {
        Text = "WukongGuard 启动器";
        ClientSize = new Size(600, 293);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10);

        Controls.Add(new Label
        {
            Text = "WukongGuard 0.4.0-rc11",
            Location = new Point(22, 18), Size = new Size(550, 30),
            Font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold)
        });
        Controls.Add(new Label
        {
            Text = "先安装，再点“启用本次游戏”，然后从 Steam 启动游戏。",
            Location = new Point(22, 55), Size = new Size(550, 28)
        });
        gamePath = new TextBox
        {
            Location = new Point(22, 92), Size = new Size(460, 28),
            Text = Installation.TryFindGameRoot() ?? ""
        };
        Controls.Add(gamePath);
        browseButton = new Button
        {
            Text = "浏览…", Location = new Point(490, 91), Size = new Size(86, 30)
        };
        browseButton.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "选择 BlackMythWukong 游戏目录" };
            if (dialog.ShowDialog(this) == DialogResult.OK) gamePath.Text = dialog.SelectedPath;
        };
        Controls.Add(browseButton);
        Controls.Add(new Label
        {
            Text = "默认不加载插件。启动器须保持打开，游戏退出后会自动停用。首次安装 Loader 需联网。",
            Location = new Point(22, 129), Size = new Size(554, 43)
        });
        installButton = new Button
        {
            Text = "安装 / 更新", Location = new Point(311, 185), Size = new Size(124, 39)
        };
        installButton.Click += InstallClicked;
        Controls.Add(installButton);
        sessionButton = new Button
        {
            Text = "启用本次游戏", Location = new Point(444, 185), Size = new Size(132, 39)
        };
        sessionButton.Click += SessionClicked;
        Controls.Add(sessionButton);
        status = new Label
        {
            Text = "准备就绪", Location = new Point(22, 236), Size = new Size(554, 42)
        };
        Controls.Add(status);
        sessionTimer = new System.Windows.Forms.Timer { Interval = 1000 };
        sessionTimer.Tick += SessionTick;
        sessionTimer.Start();
        gamePath.TextChanged += (_, _) => RefreshSessionState();
        FormClosing += ClosingSession;
        RefreshSessionState();
    }

    private async void InstallClicked(object? sender, EventArgs e)
    {
        installButton.Enabled = false;
        sessionButton.Enabled = false;
        browseButton.Enabled = false;
        gamePath.Enabled = false;
        try
        {
            IProgress<string> progress = new Progress<string>(message => status.Text = message);
            await Installation.InstallAsync(gamePath.Text.Trim(), progress.Report);
            status.Text = "安装完成，插件默认停用。点击“启用本次游戏”，再从 Steam 启动。";
            MessageBox.Show(this, "安装完成。先点击“启用本次游戏”，保持启动器打开，再从 Steam 启动游戏。",
                "WukongGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            Installation.Log(ex.ToString());
            status.Text = "安装未完成；详情见本机 installer.log。";
            MessageBox.Show(this, ex.Message + "\n\n详细记录：%LOCALAPPDATA%\\WukongGuard\\installer.log",
                "WukongGuard 安装失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            installButton.Enabled = true;
            browseButton.Enabled = true;
            gamePath.Enabled = true;
            RefreshSessionState();
        }
    }

    private void SessionClicked(object? sender, EventArgs e)
    {
        try
        {
            var root = gamePath.Text.Trim();
            if (SessionControl.IsActive(root))
            {
                SessionControl.Disable(root);
                armedHere = false;
                gameSeen = false;
                status.Text = "已停用。直接从 Steam 启动游戏不会加载 WukongGuard。";
            }
            else
            {
                SessionControl.Enable(root);
                armedHere = true;
                gameSeen = false;
                status.Text = "已启用本次游戏。保持此窗口打开，现在从 Steam 启动游戏。";
            }
        }
        catch (Exception ex)
        {
            Installation.Log(ex.ToString());
            MessageBox.Show(this, ex.Message, "WukongGuard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        RefreshSessionState();
    }

    private void SessionTick(object? sender, EventArgs e)
    {
        if (!armedHere) return;
        try { SessionControl.Heartbeat(); }
        catch (Exception ex) { Installation.Log(ex.ToString()); }
        if (SessionControl.IsGameRunning)
        {
            gameSeen = true;
            status.Text = "游戏运行中，WukongGuard 本次会话已启用；请保持启动器打开。";
        }
        else if (gameSeen)
        {
            try
            {
                SessionControl.Disable(gamePath.Text.Trim());
                armedHere = false;
                gameSeen = false;
                status.Text = "游戏已退出，插件已自动停用。下次游戏前请重新启用。";
                RefreshSessionState();
            }
            catch (Exception ex) { Installation.Log(ex.ToString()); }
        }
    }

    private void ClosingSession(object? sender, FormClosingEventArgs e)
    {
        if (!armedHere) return;
        if (SessionControl.IsGameRunning)
        {
            MessageBox.Show(this, "游戏运行时请保持启动器打开。游戏退出后会自动停用。",
                "WukongGuard", MessageBoxButtons.OK, MessageBoxIcon.Information);
            e.Cancel = true;
            return;
        }
        try { SessionControl.Disable(gamePath.Text.Trim()); }
        catch (Exception ex) { Installation.Log(ex.ToString()); }
    }

    private void RefreshSessionState()
    {
        try
        {
            var root = gamePath.Text.Trim();
            var installed = SessionControl.IsInstalled(root);
            var active = SessionControl.IsActive(root);
            installButton.Enabled = !armedHere && !SessionControl.IsGameRunning;
            browseButton.Enabled = !armedHere;
            gamePath.Enabled = !armedHere;
            sessionButton.Enabled = installed && !SessionControl.IsGameRunning;
            sessionButton.Text = active ? "停用插件" : "启用本次游戏";
            if (!installed && !armedHere) status.Text = "尚未安装此版本；请先点击“安装 / 更新”。";
            else if (active && !armedHere) status.Text = "检测到上次留下的启用状态；请先点击“停用插件”。";
            else if (installed && !active && !armedHere && status.Text == "准备就绪")
                status.Text = "已安装，当前停用。点击“启用本次游戏”后再从 Steam 启动。";
        }
        catch (ArgumentException) { sessionButton.Enabled = false; }
    }
}
