# WukongStateProbe · P0

目标：先证明自定义 C# Mod 能加载，再手动抓取 World、Controller、Pawn 与位置快照。P0 的加载、对象读取和移动差分已在本机完成；土地庙、区域切换与 Boss/剧情事件的差分仍待实测。后续独立的 [WukongGuard MVP 候选](../WukongGuard/README.md) 承接状态映射、规则引擎与覆盖层。

## 已核对的技术依据与状态

- 原版 [B1CSharpLoader](https://github.com/czastack/B1CSharpLoader/blob/master/README.en.md) 文档规定 `ICSharpMod`、`Init/DeInit`、`Console.WriteLine`、Mod 安装目录和 `Develop`/`Console` 设置。其发布页把 [v0.0.8](https://github.com/czastack/B1CSharpLoader/releases/tag/v0.0.8) 标为 Latest。2025-10-16 另发了游戏 1.0.20.22023 对应的 GameDll 包；这是**引用程序集**，不等同于加载器新版本。
- 上游 [示例 Mod](https://github.com/czastack/B1CSharpLoader/blob/master/CSharpModExample/Program.cs) 使用 `Ctrl+Enter` 获取 Pawn；[MyUtils](https://github.com/czastack/B1CSharpLoader/blob/master/CSharpModExample/MyUtils.cs) 给出 `FGlobals.GWorld → GCHelper.FindRef → UWorld → GetFirstLocalPlayerController → GetControlledPawn` 路径。本项目的 RuntimeProbe 沿用这条路径，但在你的实际游戏版本上**待运行时验证**。
- [官方模板](https://github.com/BlackMythWukongMods/B1.Mod.Template) 与上游示例均以 `net472` 为目标，建议用 .NET 8 SDK 构建。模板需要本地 GameDll 引用；本项目的加载阶段只引用已安装加载器里的 `CSharpModBase.dll`。
- 上游仍有 [游戏更新改变 DLL 的兼容性问题](https://github.com/czastack/B1CSharpLoader/issues/17)。公开资料不足以确认它在你的当前游戏构建上工作。**P0 的第一项实验就是实机判定。**

## 文件结构

```text
WukongStateProbe/
  WukongStateProbe.csproj  构建配置；ProbeRuntime 开关
  Mod.cs                    阶段 A：最小入口
  TraceLog.cs               控制台与本地文件日志
  RuntimeProbe.cs           阶段 B：手动快照，默认不编译
  RuntimeMonitor.cs         1 秒一次的游戏线程差分采样
  NuGet.Config              官方 NuGet 源
  tools/read-game-console.ps1 读取游戏控制台缓冲区
  README.md                 本手册
```

## 环境准备

1. Windows PC 安装《黑神话：悟空》和 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。用 `dotnet --list-sdks` 确认有 SDK。SDK 用于构建；Mod 目标是 `net472`。项目通过 [Microsoft 的 .NET Framework 4.7.2 引用程序集 NuGet 包](https://www.nuget.org/packages/Microsoft.NETFramework.ReferenceAssemblies.net472) 编译，首次构建需要能访问 NuGet。
2. 从 [B1CSharpLoader Releases](https://github.com/czastack/B1CSharpLoader/releases) 下载 v0.0.8，按其压缩包结构解压到游戏的 `b1\Binaries\Win64`。最终应有：

   ```text
   <游戏根目录>\b1\Binaries\Win64\version.dll
   <游戏根目录>\b1\Binaries\Win64\CSharpLoader\CSharpModBase.dll
   <游戏根目录>\b1\Binaries\Win64\CSharpLoader\b1cs.ini
   ```

   若 `version.dll` 已被别的 Mod 使用，先按上游兼容说明处理冲突，不要直接覆盖。记录游戏版本、加载器压缩包名与来源。`EnableJit`/Harmony 此阶段不需要主动开启；若加载器包已启用 JIT，以包内说明为准。
3. 在 `CSharpLoader\b1cs.ini` 中设 `Console=1` 以查看 `Console.WriteLine` 输出。开发调试时可设 `Develop=1`，并按上游文档用 `Ctrl+F5` 重载。本机已实测热重载成功。输出也写入 `%LOCALAPPDATA%\WukongGuard\probe.log`。

## 阶段 A：只验证加载

**目标：** 启动游戏后看到且仅要求看到 `[WukongGuard] loaded`。

在 PowerShell 中，把路径改为你的实际安装位置：

```powershell
. .\WukongGuard\release\find-game.ps1
$gameBin = Join-Path (Find-WukongGameRoot '') 'b1\Binaries\Win64'
dotnet build .\WukongStateProbe\WukongStateProbe.csproj -c Release -p:GameBin="$gameBin"
$modDir = Join-Path $gameBin 'CSharpLoader\Mods\WukongStateProbe'
New-Item -ItemType Directory -Force -Path $modDir | Out-Null
Copy-Item .\WukongStateProbe\bin\Release\net472\WukongStateProbe.dll -Destination $modDir
```

最终 DLL 路径：`<游戏根目录>\b1\Binaries\Win64\CSharpLoader\Mods\WukongStateProbe\WukongStateProbe.dll`。保持 `ProbeRuntime=false`（默认值），然后从 Steam 正常启动游戏并进入存档。预期控制台输出：

```text
[WukongGuard] loaded
```

若启动失败或没有日志，**先停留在阶段 A**。保存游戏版本、`b1cs.ini`、加载器版本、完整控制台/加载器日志、异常堆栈、DLL 实际路径，并描述是否还有 `version.dll` 等其他代理 DLL。请勿把存档、账号凭据或整个游戏目录上传。

## 阶段 B：手动 Runtime Object 快照

**进入条件：** 阶段 A 已在本机确认可正常启动和加载。下载与游戏构建匹配的 GameDll 引用程序集（上游 Release 或从当前安装提取）。`GameDll` 目录至少应包含 `BtlSvr.Main.dll`、`b1.Native.dll`、`GSE.Core.dll`、`Protobuf.RunTime.dll`、`UnrealEngine.Runtime.dll`、`UnrealEngine.dll`。不要把这些游戏 DLL 提交到仓库。

```powershell
. .\WukongGuard\release\find-game.ps1
$gameBin = Join-Path (Find-WukongGameRoot '') 'b1\Binaries\Win64'
$gameDll = '<GameDll 引用程序集目录>'
dotnet build .\WukongStateProbe\WukongStateProbe.csproj -c Release -p:GameBin="$gameBin" -p:GameDll="$gameDll" -p:ProbeRuntime=true
$modDir = Join-Path $gameBin 'CSharpLoader\Mods\WukongStateProbe'
Copy-Item .\WukongStateProbe\bin\Release\net472\WukongStateProbe.dll -Destination $modDir
```

启动游戏并进入存档。在游戏窗口按 `Ctrl+Enter`；每次按键输出一条快照。预期（对象内容由实际运行决定）：

```text
[WukongGuard] loaded
[WukongGuard] probe ready; press Ctrl+Enter in game for a snapshot
[WukongGuard] world=... type=UnrealEngine.Engine.UWorld
[WukongGuard] controller=... type=b1.BGP_PlayerControllerB1
[WukongGuard] gameInstance=... type=b1.BGW_GameInstance_B1
[WukongGuard] chapter=... mapId=... areaId=... ngPlus=...
[WukongGuard] world=... controller=... pawn=... location=...
```

`Ctrl+F8` 调用 Unreal 的 `USystemLibrary.PrintString`，屏幕上应短暂显示 `[WukongGuard] UI probe`。`Ctrl+F9` 在存档中将任务 ID 和阶段写入本地日志，供 A/B 差分。显示接口在 Shipping 构建中是否真正可见仍待目视确认。

若只得到 `world=null`、`controller=null`、`roleData=null` 或 `pawn=null`，先在存档完全加载后重试。若出现 `probe error`，保留完整异常。若编译时报缺少类型/方法，记录完整 `CSxxxx` 错误以及 GameDll 和游戏版本；这通常表示引用程序集与当前运行时不一致或上游 API 已变化，**不要猜测替换类名**。如果按键没有反应，确认窗口焦点、`Develop` 配置及加载器按键机制，先用上游示例复现。

## 阶段 C：差分记录（不更改代码）

阶段 B 成功后，选择安全、可重复的动作，按 `Ctrl+Enter` 保存 A 快照，执行**一个**动作，再按键保存 B 快照。优先从玩家移动、土地庙交互、区域切换开始。每条记录附游戏版本、存档位置、动作、A/B 日志、可重复次数。`location` 变化只能证明位置读取；它不是剧情或永久遗漏 Flag。Boss/剧情节点只在可备份存档且阶段 B 稳定后试。

下一轮再根据实测结果调查 Chapter/Quest/Boss/Story 类或事件，并确定一个稳定进度字段。Harmony Hook 暂不启用，因为尚无已验证的目标函数。P0 完成前不能声称满足“稳定判断不可逆节点”。

## 验证状态

一次实机 P0 测试确认加载日志、`UWorld`、PlayerController、GameInstance、Pawn 与角色只读数据可读，角色移动后坐标发生变化。所用游戏版本与引用程序集版本不同；接口兼容性仍需在各安装版本上复测。Boss、剧情和支线的稳定进度字段尚待逐项校准。Shipping 构建中的 `PrintString` 未在画面上显示，因此产品侧采用独立覆盖层。
