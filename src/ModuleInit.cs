using System;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;

namespace ModListHider
{
    /// <summary>
    /// Module initializer - guaranteed to run when this assembly is loaded.
    /// Works on .NET 8+ with AOT module initializers enabled.
    /// </summary>
    public static class ModuleInit
    {
        private const string VanillaInjectorNodeName = "ModListHider_VanillaInjector";
        private const string RowInjectorNodeName = "ModListHider_RowInjector";
        private const string DebugHotkeyNodeName = "ModListHider_DebugHotkeyWatcher";

        [ModuleInitializer]
        public static void Initialize()
        {
            try
            {
                // Use GD.Print as early as possible
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

                // Start Vanilla Mode injector
                try
                {
                    GD.Print("[ModListHider] Creating VanillaModeToggleInjector...");
                    Callable.From(() =>
                    {
                        try
                        {
                            var sceneTree = Engine.GetMainLoop() as SceneTree;
                            if (sceneTree != null)
                            {
                                if (sceneTree.Root.FindChild(VanillaInjectorNodeName, true, false) != null)
                                {
                                    GD.Print("[ModListHider] VanillaModeToggleInjector already exists, skipping.");
                                    return;
                                }

                                var injector = new UI.VanillaModeToggleInjector
                                {
                                    Name = VanillaInjectorNodeName
                                };
                                sceneTree.Root.AddChild(injector);
                                GD.Print("[ModListHider] VanillaModeToggleInjector added to tree");
                            }
                        }
                        catch (Exception ex)
                        {
                            GD.PrintErr($"[ModListHider] Failed to add VM injector: {ex.Message}");
                        }
                    }).CallDeferred();
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[ModListHider] StartVanillaModeInjector failed: {ex.Message}");
                }

                // Start Mod Row Icon injector
                try
                {
                    GD.Print("[ModListHider] Creating ModMenuRowIconInjector...");
                    Callable.From(() =>
                    {
                        try
                        {
                            var sceneTree = Engine.GetMainLoop() as SceneTree;
                            if (sceneTree != null)
                            {
                                if (sceneTree.Root.FindChild(RowInjectorNodeName, true, false) != null)
                                {
                                    GD.Print("[ModListHider] ModMenuRowIconInjector already exists, skipping.");
                                    return;
                                }

                                var injector = new UI.ModMenuRowIconInjector
                                {
                                    Name = RowInjectorNodeName
                                };
                                sceneTree.Root.AddChild(injector);
                                GD.Print("[ModListHider] ModMenuRowIconInjector added to tree");
                            }
                        }
                        catch (Exception ex)
                        {
                            GD.PrintErr($"[ModListHider] Failed to add row icon injector: {ex.Message}");
                        }
                    }).CallDeferred();
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[ModListHider] StartModMenuRowIconInjector failed: {ex.Message}");
                }

                // Debug hotkey watcher (Ctrl+Shift+F8)
                try
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
                        catch (Exception ex2)
                        {
                            GD.PrintErr($"[ModListHider] Failed to add DebugHotkeyWatcher: {ex2.Message}");
                        }
                    }).CallDeferred();
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[ModListHider] StartDebugHotkeyWatcher failed: {ex.Message}");
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] ModuleInit.Initialize failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
