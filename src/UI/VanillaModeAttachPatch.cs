using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Godot;

namespace ModListHider.UI
{
    /// <summary>
    /// VanillaMode toggle attach via Harmony patch on NModdingScreen._Ready.
    /// 替代旧的全局 VanillaModeToggleInjector 持续扫描方案：
    /// - screen 被构建 → 大眼睛自动出现在左上角
    /// - screen 被销毁 → 大眼睛随父节点回收
    /// </summary>
    [HarmonyPatch]
    internal static class VanillaModeAttachPatch
    {
        private const string TargetTypeName =
            "MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen.NModdingScreen";
        private const string ToggleNodeName = "VanillaModeToggle";

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName(TargetTypeName);
            if (t == null)
                yield break;

            var m = AccessTools.Method(t, "_Ready");
            if (m != null)
                yield return m;
        }

        private static bool Prepare() => TargetMethods().Any();

        private static void Postfix(object __instance)
        {
            try
            {
                if (__instance is not Node screen)
                    return;

                if (screen.FindChild(ToggleNodeName, true, false) != null)
                    return;

                Config.ModListHiderConfig.Instance.Load();
                var vanilla = Config.ModListHiderConfig.Instance.VanillaMode;

                var btn = new VanillaModeToggleNode { Name = ToggleNodeName };
                btn.Configure(vanilla);
                EnsureTogglePlacement(btn);
                screen.AddChild(btn);

                GD.Print($"[ModListHider] VanillaMode toggle attached. VanillaMode={vanilla}, parent={screen.GetType().Name}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] VanillaModeAttachPatch failed: {ex.Message}");
            }
        }

        private static void EnsureTogglePlacement(Control btn)
        {
            btn.ZIndex = 80;
            btn.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            btn.AnchorLeft = 0f;
            btn.AnchorRight = 0f;
            btn.AnchorTop = 0f;
            btn.AnchorBottom = 0f;
            btn.OffsetLeft = 18f;
            btn.OffsetTop = 18f;
            btn.OffsetRight = 66f;
            btn.OffsetBottom = 66f;
        }
    }
}