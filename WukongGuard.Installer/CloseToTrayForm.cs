namespace WukongGuard.Installer;

internal sealed class CloseToTrayForm : Form
{
    private readonly Label heading = UiStyle.Label("收起到托盘？", 18, true);
    private readonly Label explanation = UiStyle.Label(
        "关闭窗口后，后悔药会收起到托盘。已启动的保护会继续运行；如需退出，请使用托盘菜单。", 10);
    private readonly CheckBox doNotRemind = new() { Text = "不再提醒", ForeColor = UiStyle.Text,
        BackColor = UiStyle.Canvas, AutoSize = false };
    private readonly Label feedback = UiStyle.Label("", 9, false, UiStyle.Error);
    private readonly RoundedButton hide = UiStyle.Button("收起到托盘", true), cancel = UiStyle.Button("取消");

    internal CheckBox DoNotRemind => doNotRemind;
    internal RoundedButton HideAction => hide;
    internal RoundedButton CancelAction => cancel;

    internal CloseToTrayForm()
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        Text = "后悔药";
        ClientSize = new Size((int)(520 * UiStyle.PreviewScale), (int)(292 * UiStyle.PreviewScale));
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = UiStyle.Canvas;
        ForeColor = UiStyle.Text;
        Font = new Font("Microsoft YaHei UI", 10f * UiStyle.PreviewScale);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        DoubleBuffered = true;
        explanation.AutoEllipsis = false;
        Controls.AddRange(new Control[] { heading, explanation, doNotRemind, feedback, hide, cancel });
        cancel.DialogResult = DialogResult.Cancel;
        AcceptButton = hide; CancelButton = cancel;
        hide.Click += (_, _) =>
        {
            try
            {
                // A cancelled dialog must not save the checkbox choice.
                if (doNotRemind.Checked) UserPreferences.SaveSkipCloseToTrayPrompt(true);
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Installation.Log(ex.ToString());
                feedback.Text = "无法保存选择，请取消勾选后重试。";
            }
        };
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        if (heading == null || hide == null) return;
        int P(int n) => UiStyle.Pixels(this, n);
        int width = ClientSize.Width - P(48);
        heading.SetBounds(P(24), P(24), width, P(38));
        explanation.SetBounds(P(24), P(82), width, P(76));
        doNotRemind.SetBounds(P(24), P(167), width, P(28));
        feedback.SetBounds(P(24), P(201), width, P(25));
        hide.SetBounds(ClientSize.Width - P(244), ClientSize.Height - P(62), P(130), P(40));
        cancel.SetBounds(ClientSize.Width - P(102), ClientSize.Height - P(62), P(78), P(40));
    }
}
