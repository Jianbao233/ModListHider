using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Godot;

namespace ModListHider.Core
{
    /// <summary>
    /// Patch: ModManager.GetGameplayRelevantModNameList
    ///
    /// Two modes of operation:
    /// - VanillaMode ON:  Return null so the game thinks no mods are loaded at all.
    ///                    The MP handshake sends empty mod list -> vanilla players can join.
    /// - VanillaMode OFF: Remove individual HiddenModIds (per-mod eye icon toggle).
    /// </summary>
    [HarmonyPatch]
    internal static class ModListFilterPatch
    {
        private static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName("MegaCrit.Sts2.Core.Modding.ModManager")
                ?? AccessTools.TypeByName("ModManager");
            if (t == null) return null;

            return t.GetMethod("GetGameplayRelevantModNameList", BindingFlags.Public | BindingFlags.Static)
                ?? t.GetMethod("GetModNameList", BindingFlags.Public | BindingFlags.Static);
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Postfix(ref List<string> __result)
        {
            Config.ModListHiderConfig.Instance.ReloadFromDisk();
            var cfg = Config.ModListHiderConfig.Instance;

            // Priority 1: Vanilla Mode - pretend no mods exist at all
            if (cfg.VanillaMode)
            {
                if (__result != null && __result.Count > 0)
                {
                    GD.Print($"[ModListHider] VanillaMode ON: stripping all {__result.Count} mod(s) from MP list");
                    __result.Clear();
                }
                return;
            }

            // Priority 2: Per-mod hide (individual eye icons)
            if (cfg.HiddenModIds.Count == 0) return;

            int before = __result?.Count ?? 0;
            __result?.RemoveAll(id => cfg.ShouldStripFromMultiplayerList(id));
            int removed = before - (__result?.Count ?? 0);

            if (removed > 0)
                GD.Print($"[ModListHider] Filtered {removed} hidden mod(s), sent {__result?.Count ?? 0} to peer");
        }
    }
}
