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
    private readonly Label status;

    public InstallerForm()
    {
        Text = "WukongGuard 安装程序";
        ClientSize = new Size(600, 246);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Microsoft YaHei UI", 10);

        Controls.Add(new Label
        {
            Text = "WukongGuard 0.4.0-rc9",
            Location = new Point(22, 18), Size = new Size(550, 30),
            Font = new Font("Microsoft YaHei UI", 15, FontStyle.Bold)
        });
        Controls.Add(new Label
        {
            Text = "选择《黑神话：悟空》的安装目录。请先完全退出游戏。",
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
            Text = "如未安装 B1CSharpLoader，程序将从作者的 GitHub Release 下载并校验。首次安装需联网。",
            Location = new Point(22, 129), Size = new Size(554, 43)
        });
        installButton = new Button
        {
            Text = "安装", Location = new Point(458, 185), Size = new Size(118, 39)
        };
        installButton.Click += InstallClicked;
        Controls.Add(installButton);
        status = new Label
        {
            Text = "准备就绪", Location = new Point(22, 190), Size = new Size(425, 34)
        };
        Controls.Add(status);
    }

    private async void InstallClicked(object? sender, EventArgs e)
    {
        installButton.Enabled = false;
        browseButton.Enabled = false;
        gamePath.Enabled = false;
        try
        {
            IProgress<string> progress = new Progress<string>(message => status.Text = message);
            await Installation.InstallAsync(gamePath.Text.Trim(), progress.Report);
            status.Text = "安装完成。请通过 Steam 启动游戏。";
            MessageBox.Show(this, "安装完成。通过 Steam 启动《黑神话：悟空》即可使用 WukongGuard。",
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
        }
    }
}
