# 后悔药 0.5.0-rc1 · 开发者 ZIP

面向玩家请交付单文件 `后悔药-0.5.0-rc1.exe`。它会在首次打开时自动寻找游戏、准备依赖并待命；用户随后自行从 Steam 启动《黑神话：悟空》。ZIP 中的脚本保留给开发者排查和离线安装。

## 开发者脚本

游戏退出后，解压 ZIP 并运行 `./release/install.ps1`。需要 B1CSharpLoader v0.0.8。安装后运行 `./release/start-session.ps1`，再从 Steam 启动游戏；游戏退出后脚本自动停用。卸载使用 `./release/uninstall.ps1`。配置和诊断日志在 `%LOCALAPPDATA%\WukongGuard`。

## 提醒行为和限制

默认启用 7 条不可补救内容候选地点提醒；6 条可回访隐藏地区提示需从 EXE 的“设置”页开启。普通提醒不抢焦点，10 秒后收起；`Ctrl+Shift+G` 或手柄长按 Menu 打开详情。已提醒地点在离开后再次进入可重新触发。

当前只有第一章幽魂风险点完成一次真实正向验收。其余位置和时机仍需实测，全部正式规则暂时不能可靠判断每项内容在本周目是否已完成。详情见仓库 `WukongGuard/README.md`。

首次准备会备份已有 `b1cs.ini` 并设置 `Console=0`、`EnableJit=0`。这会影响共用同一 B1CSharpLoader 的其他 Mod；旧配置保存在同目录的 `.before-regretpill-*.bak` 文件中。
