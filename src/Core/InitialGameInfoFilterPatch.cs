using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Godot;

namespace ModListHider.Core
{
    /// <summary>
    /// Backup patch: InitialGameInfoMessage.Basic
    ///
    /// VanillaMode ON: clear the mods field so the lobby handshake sends empty.
    /// VanillaMode OFF: per-mod filtering via ShouldStripFromMultiplayerList.
    /// </summary>
    [HarmonyPatch]
    internal static class InitialGameInfoFilterPatch
    {
        private static MethodBase TargetMethod()
        {
            var t = AccessTools.TypeByName(
                "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.InitialGameInfoMessage");
            if (t == null) return null;
            return t.GetMethod("Basic", BindingFlags.Public | BindingFlags.Static);
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        private static void Postfix(ref object __result)
        {
            if (__result == null) return;

            Config.ModListHiderConfig.Instance.ReloadFromDisk();
            var cfg = Config.ModListHiderConfig.Instance;

            var modsField = __result.GetType().GetField(
                "mods", BindingFlags.Public | BindingFlags.Instance);
            if (modsField == null) return;

            var list = modsField.GetValue(__result) as List<string>;
            if (list == null) return;

            // Priority 1: Vanilla Mode - send empty list
            if (cfg.VanillaMode)
            {
                if (list.Count > 0)
                {
                    GD.Print($"[ModListHider] VanillaMode ON: stripping all {list.Count} mod(s) from InitialGameInfo");
                    list.Clear();
                    modsField.SetValue(__result, list);
                }
                return;
            }

            // Priority 2: Per-mod hide
            if (cfg.HiddenModIds.Count == 0) return;

            int before = list.Count;
            list.RemoveAll(id => cfg.ShouldStripFromMultiplayerList(id));
            if (before != list.Count)
            {
                GD.Print($"[ModListHider] InitialGameInfo: filtered {before - list.Count} hidden mod(s)");
                modsField.SetValue(__result, list);
            }
        }
    }
}
