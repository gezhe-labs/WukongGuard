# WukongGuard

Windows PC 版《黑神话：悟空》的游戏内防遗漏提醒插件。运行时 Mod 读取玩家位置与可用游戏状态，规则引擎在候选风险地点触发覆盖层提示；隐藏地区线索可在设置中开启。

当前版本为 **0.4.0-rc10 测试版**。已配置 7 条不可补救内容提醒与 6 条可选隐藏地区提示。规则目前主要依赖章节、地图和坐标，尚不能可靠识别每项内容在当前周目的完成状态；除第一章幽魂风险点的一次正向验证外，其余地点仍需实机校准。因此不要将其视为已验证的 1.0 防遗漏覆盖。

- [WukongGuard 插件、规则与安装说明](WukongGuard/README.md)
- [WukongStateProbe 运行时探针](WukongStateProbe/README.md)

## 安装测试版

从 GitHub Releases 下载 `WukongGuard-0.4.0-rc10-Setup.exe`，完全退出游戏后双击运行。安装器会尝试定位 Steam 游戏目录；找不到时可以手动选择 `BlackMythWukong` 文件夹。点击“安装 / 更新”后插件默认停用，单独启动游戏不会加载 WukongGuard。体验时先在 EXE 中点击“启用本次游戏”，保持窗口打开，再通过 Steam 启动游戏；游戏退出后自动停用。首次安装若缺少 B1CSharpLoader，安装器会从其作者的官方 Release 下载 v0.0.8，校验 SHA256 后安装，因此需要联网；已有 Loader 的配置会保留。覆盖层所需 .NET 运行环境已随 EXE 提供。

这仍是 **测试版**：候选地点尚未全部实机验收，规则也无法可靠判定每项内容在当前周目是否已完成。下载前请阅读[当前规则和限制](WukongGuard/README.md)。

0.4.0-rc10 的提醒默认不抢游戏焦点：按 `Ctrl+Shift+G` 或 Xbox 手柄长按 `Menu` 约 1 秒主动打开详情。手柄 Menu 可能同时打开游戏菜单，需逐游戏实测；按键与手柄入口可在托盘设置中调整。

开发者可运行 `WukongGuard.Installer/build.ps1`，把最新的 `WukongGuard/dist/WukongGuard-0.4.0-rc10-*.zip` 封装为单文件 EXE；ZIP 可由 `WukongGuard/release/build-package.ps1` 构建。

本仓库只跟踪可公开的源码、规则和文档。构建需要开发者安装游戏并取得 B1CSharpLoader 引用程序集；游戏 DLL、SDK、存档、实机日志和发布产物不纳入 Git。安装包应单独作为 GitHub Release 附件提供。
