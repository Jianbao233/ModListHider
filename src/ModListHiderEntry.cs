using System;
using System.Linq;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace ModListHider
{
    /// <summary>
    /// Mod entry: loads config and prints diagnostic info about loaded patches.
    /// Uses [ModInitializer] so it runs after all mods are loaded.
    /// Vanilla Mode patches (ModListFilterPatch, InitialGameInfoFilterPatch, VanillaModeTogglePatch)
    /// are applied via the game's own Harmony.PatchAll on this assembly.
    /// </summary>
    [ModInitializer("ModLoaded")]
    public static class ModListHiderEntry
    {
        public static void ModLoaded()
        {
            try
            {
                Config.ModListHiderConfig.Instance.Load();

                // Log patch availability for diagnostics
                var rowType = AccessTools.TypeByName(
                    "MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen.NModMenuRow");
                var mmType = AccessTools.TypeByName(
                    "MegaCrit.Sts2.Core.Modding.ModManager")
                    ?? AccessTools.TypeByName("ModManager");
                var iiType = AccessTools.TypeByName(
                    "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.InitialGameInfoMessage");

                var patchCount = typeof(ModListHiderEntry).Assembly
                    .GetTypes()
                    .Where(t => t.GetCustomAttribute<HarmonyPatch>() != null)
                    .Count();

                GD.Print($"[ModListHider] Loaded. VanillaMode={Config.ModListHiderConfig.Instance.VanillaMode}, "
                    + $"HiddenMods={Config.ModListHiderConfig.Instance.HiddenModIds.Count}, "
                    + $"HarmonyPatches={patchCount}");
                GD.Print($"[ModListHider] Key types: NModMenuRow={(rowType != null)}, "
                    + $"ModManager={(mmType != null)}, InitialGameInfoMessage={(iiType != null)}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] ModLoaded failed: {ex.Message}");
            }
        }
    }
}
