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
                    + $"HarmonyPatches={patchCount}");

                // Start Vanilla Mode injector
                try
                {
                    GD.Print("[ModListHider] Creating VanillaModeToggleInjector...");
                    var injector = new UI.VanillaModeToggleInjector();
                    Callable.From(() =>
                    {
                        try
                        {
                            var sceneTree = Engine.GetMainLoop() as SceneTree;
                            if (sceneTree != null)
                            {
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
                    var injector = new UI.ModMenuRowIconInjector();
                    Callable.From(() =>
                    {
                        try
                        {
                            var sceneTree = Engine.GetMainLoop() as SceneTree;
                            if (sceneTree != null)
                            {
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
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] ModuleInit.Initialize failed: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }
}
