using System;
using System.IO;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace ModListHider.UI
{
    /// <summary>
    /// Injects a global Vanilla Mode toggle button into the modding screen header.
    ///
    /// Vanilla Mode ON  = pretend no mods at all (欺骗服务端联机检测)
    /// Vanilla Mode OFF = per-mod eye icons work as before
    ///
    /// Detects the NModdingScreen via AddChild(..., deepChild), then schedules
    /// button injection on the first frame so parent layout is ready.
    /// </summary>
    [HarmonyPatch]
    internal static class VanillaModeTogglePatch
    {
        /// <summary>Set once on first hit so we never inject twice.</summary>
        private static bool _injected;

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Node), "AddChild",
                new[] { typeof(Node), typeof(bool), typeof(Node.InternalMode) });
        }

        private static bool Prepare() => TargetMethod() != null;

        [HarmonyPostfix]
        private static void Postfix(Node __instance, Node node)
        {
            if (_injected) return;

            try
            {
                // The page itself gets added, or a child deep in the page tree.
                if (node != null && LooksLikeModdingScreen(node))
                    ScheduleInject(node);
                else if (__instance != null && LooksLikeModdingScreen(__instance))
                    ScheduleInject(__instance);
            }
            catch (Exception ex)
            {
                TryAppendDebug($"VanillaModeToggle Postfix: {ex.Message}\n");
            }
        }

        /// <summary>
        /// Approximate detection: NModdingScreen is a Control with child "InstalledModsTitle"
        /// (the "Installed Mods" label at the top of the page).
        /// </summary>
        private static bool LooksLikeModdingScreen(Node n)
        {
            if (n.FindChild("InstalledModsTitle", true, false) != null)
                return true;
            // Fallback: look for the mods border that wraps the list
            if (n.FindChild("ModsBorder", true, false) != null)
                return true;
            // Also match by type name
            var tn = n.GetType().Name;
            return tn == "NModdingScreen" || tn.EndsWith(".NModdingScreen", StringComparison.Ordinal);
        }

        private static void ScheduleInject(Node screenNode)
        {
            _injected = true;

            void InjectOnce()
            {
                try
                {
                    if (!GodotObject.IsInstanceValid(screenNode))
                        return;
                    if (screenNode.FindChild("VanillaModeToggle", true, false) != null)
                        return;

                    Config.ModListHiderConfig.Instance.Load();
                    bool vanilla = Config.ModListHiderConfig.Instance.VanillaMode;

                    // Inject directly into screenNode for maximum control
                    var btn = new VanillaModeToggleNode();
                    btn.Name = "VanillaModeToggle";
                    btn.Configure(vanilla);

                    // Place at (0,0) of screenNode so user can see exact position
                    btn.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
                    btn.OffsetLeft = 0;
                    btn.OffsetTop = 0;
                    btn.OffsetRight = 48;
                    btn.OffsetBottom = 48;

                    screenNode.AddChildSafely(btn);
                    TryAppendDebug($"[ModListHider] VanillaMode toggle injected. State={vanilla}, parent={screenNode.GetType().Name}\n");
                    GD.Print($"[ModListHider] VanillaMode toggle injected. VanillaMode={vanilla}, parent={screenNode.GetType().Name}");
                }
                catch (Exception ex)
                {
                    TryAppendDebug($"VanillaModeToggle InjectOnce: {ex.Message}\n{ex.StackTrace}\n");
                }
            }

            InjectOnce();
            Callable.From(InjectOnce).CallDeferred();

            // Backup: re-check in 2s in case page wasn't fully laid out yet
            var tree = screenNode.GetTree();
            if (tree == null) return;
            var timer = tree.CreateTimer(2.0f);
            timer.Timeout += () =>
            {
                InjectOnce();
            };
        }

        /// <summary>
        /// Find the ModsBorder (scroll container with the mod list) inside the screen.
        /// Injecting into the border puts the button at the top-left of the list area,
        /// not affected by the description panel in the top-right.
        /// </summary>
        private static Node? FindModsBorder(Node root)
        {
            // Try named children first
            var border = root.GetNodeOrNull<Node>("%ModsBorder");
            if (border != null) return border;

            // Search recursively for ModsBorder
            return FindChildByName(root, "ModsBorder");
        }

        private static Node? FindChildByName(Node parent, string name)
        {
            foreach (var child in parent.GetChildren())
            {
                if (child.Name == name)
                    return child;
                var found = FindChildByName(child, name);
                if (found != null)
                    return found;
            }
            return null;
        }

        private static void TryAppendDebug(string line)
        {
            try
            {
                var path = Path.Combine(OS.GetUserDataDir() ?? "", "ModListHider_vanilla_debug.txt");
                File.AppendAllText(path, line);
            }
            catch { /* ignore */ }
        }
    }
}
