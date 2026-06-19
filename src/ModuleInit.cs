using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;

namespace ModListHider
{
    /// <summary>
    /// Module initializer - guaranteed to run when this assembly is loaded.
    /// 重构后职责精简：
    /// 1. 加载配置
    /// 2. 通过 [HarmonyPatch] 自动应用所有 Patch（包括 RowIconAttachPatch / VanillaModeAttachPatch）
    /// 3. 创建 DebugHotkeyWatcher（仅用于 Ctrl+Shift+F8 调试开关，开销极低）
    ///
    /// 不再创建任何全局持续扫描的 injector：
    /// 眼睛 UI 通过 Harmony patch 在 NModMenuRow._Ready / NModdingScreen._Ready 时直接 AddChild，
    /// 生命周期完全跟随对应行/屏幕节点。
    /// </summary>
    public static class ModuleInit
    {
        private const string DebugHotkeyNodeName = "ModListHider_DebugHotkeyWatcher";

        [ModuleInitializer]
        public static void Initialize()
        {
            try
            {
                GD.Print("[ModListHider] ModuleInit.Initialize() called!");

                Config.ModListHiderConfig.Instance.Load();

                var patchCount = typeof(ModuleInit).Assembly
                    .GetTypes()
                    .Where(t => Attribute.IsDefined(t, typeof(HarmonyLib.HarmonyPatch)))
                    .Count();

                GD.Print($"[ModListHider] VanillaMode={Config.ModListHiderConfig.Instance.VanillaMode}, "
                    + $"HiddenMods={Config.ModListHiderConfig.Instance.HiddenModIds.Count}, "
                    + $"DebugMode={Config.ModListHiderConfig.Instance.DebugMode}, "
                    + $"HarmonyPatches={patchCount}");

                AttachDebugHotkeyWatcherDeferred();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] ModuleInit.Initialize failed: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void AttachDebugHotkeyWatcherDeferred()
        {
            Callable.From(() =>
            {
                try
                {
                    var sceneTree = Engine.GetMainLoop() as SceneTree;
                    if (sceneTree == null) return;

                    if (sceneTree.Root.FindChild(DebugHotkeyNodeName, true, false) != null)
                        return;

                    var watcher = new UI.DebugHotkeyWatcher
                    {
                        Name = DebugHotkeyNodeName
                    };
                    sceneTree.Root.AddChild(watcher);
                    GD.Print("[ModListHider] DebugHotkeyWatcher added to tree");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[ModListHider] Failed to add DebugHotkeyWatcher: {ex.Message}");
                }
            }).CallDeferred();
        }
    }
}