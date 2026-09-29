using System.Collections.Generic;
using CSharpModBase;
using CSharpModBase.Input;
using WukongGuard.Core;

namespace WukongGuard
{
    public sealed class Mod : ICSharpMod
    {
        private static IReadOnlyList<MissableRule> previewRules;
        private static int previewIndex;

        public string Name => "WukongGuard";
        public string Version => "0.4.0-rc9";

        public void Init()
        {
            TraceLog.Write("[WukongGuard] loaded");
            bool developmentMode = RuleLoader.LoadDevelopmentMode();
            previewRules = developmentMode ? RuleLoader.LoadPreviewRules() : new List<MissableRule>();
            previewIndex = 0;
            if (developmentMode)
            {
                Utils.RegisterKeyBind(ModifierKeys.Control, Key.F6, GuardMonitor.ToggleExperience);
                Utils.RegisterKeyBind(ModifierKeys.Control, Key.F7, ShowNextPreview);
                Utils.RegisterKeyBind(ModifierKeys.Control, Key.F8, ShowOverlayDemo);
                Utils.RegisterKeyBind(ModifierKeys.Control, Key.F9, GuardMonitor.Snapshot);
                Utils.RegisterKeyBind(ModifierKeys.Control, Key.F10, GuardMonitor.DumpQuests);
            }
            OverlayLauncher.EnsureRunning();
            GuardMonitor.Start(previewRules, developmentMode);
        }

        public void DeInit()
        {
            GuardMonitor.Stop();
            previewRules = null;
            TraceLog.Write("[WukongGuard] unloaded");
        }

        private static void ShowNextPreview()
        {
            if (previewRules == null || previewRules.Count == 0)
            {
                TraceLog.Write("[WukongGuard] no preview rules available");
                return;
            }
            var rule = previewRules[previewIndex];
            previewIndex = (previewIndex + 1) % previewRules.Count;
            OverlayPublisher.Show(new MissableRule
            {
                Id = "preview_" + rule.Id,
                Spoilers = rule.Spoilers
            });
            TraceLog.Write("[WukongGuard] manual preview " + rule.Id);
        }

        private static void ShowOverlayDemo()
        {
            OverlayPublisher.Show(new MissableRule
            {
                Id = "overlay_demo",
                Spoilers = new[] {
                    "这是 WukongGuard 显示测试，不代表附近有遗漏点。",
                    "显示测试：游戏状态来自运行时只读接口。",
                    "显示测试：规则需经实际剧情节点校准后启用。",
                    "显示测试：按关闭可返回游戏。"
                }
            });
            TraceLog.Write("[WukongGuard] overlay demo sent");
        }
    }
}
