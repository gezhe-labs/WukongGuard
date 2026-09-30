namespace WukongGuard.Installer;

internal enum LauncherStage { Idle, Preparing, Ready, Connecting, Running, RuntimeIssue, Failed, GameEnded, GameAlreadyRunning }

internal sealed record StagePresentation(string Heading, string Button, string Guidance, bool Enabled, bool Primary, Color Color)
{
    internal static StagePresentation For(LauncherStage stage) => stage switch
    {
        LauncherStage.Preparing => new("正在准备保护", "正在准备…",
            "正在检查游戏目录并准备组件；首次使用可能需要系统授权。", false, false, UiStyle.Accent),
        LauncherStage.Ready => new("已就绪，等待游戏启动", "取消待命",
            "已就绪。现在从 Steam 启动游戏，后悔药会自动连接。", true, false, UiStyle.Accent),
        LauncherStage.Connecting => new("正在连接游戏", "连接中…",
            "已检测到游戏，正在确认实时读取状态。", false, false, UiStyle.Accent),
        LauncherStage.Running => new("实时读取正常", "停止保护",
            "保护运行中；符合触发条件时才会显示提醒。", true, false, UiStyle.Success),
        LauncherStage.RuntimeIssue => new("实时读取需检查", "查看诊断",
            "游戏已运行，但尚未确认实时读取；提醒可能无法触发。", true, false, UiStyle.Error),
        LauncherStage.Failed => new("启动未完成", "重试启动",
            "请检查游戏目录或打开诊断，修复后重试。", true, true, UiStyle.Error),
        LauncherStage.GameEnded => new("本次游戏已结束", "重新启动",
            "本次保护已结束；再次进入游戏前请重新启动。", true, true, UiStyle.Muted),
        LauncherStage.GameAlreadyRunning => new("游戏已经运行", "等待游戏退出",
            "请先退出游戏，再点击启动保护；保护组件需要在游戏启动前加载。", false, false, UiStyle.Error),
        _ => new("保护尚未启动", "启动保护", "先点这里，再由你从 Steam 启动游戏。", true, true, UiStyle.Muted)
    };
}
