using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Godot;

namespace ModListHider.Core
{
    /// <summary>
    /// Backup patch for lobby initial-game-info payload creation.
    /// Sanitizes mod-related members to survive field/method name changes after game updates.
    /// </summary>
    [HarmonyPatch]
    internal static class InitialGameInfoFilterPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            var t = AccessTools.TypeByName(
                "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.InitialGameInfoMessage");
            if (t == null)
                yield break;

            var flags = BindingFlags.Public | BindingFlags.Static;
            var seen = new HashSet<string>(StringComparer.Ordinal);

            string[] preferred = { "Basic", "Create", "Build" };
            foreach (var name in preferred)
            {
                var m = t.GetMethod(name, flags);
                if (m == null)
                    continue;
                if (m.ReturnType == typeof(void))
                    continue;
                if (seen.Add(m.Name))
                    yield return m;
            }

            // Fallback for future updates: static no-arg builders returning payload objects.
            foreach (var m in t.GetMethods(flags))
            {
                if (m.ReturnType == typeof(void))
                    continue;
                if (m.GetParameters().Length != 0)
                    continue;
                if (!seen.Add(m.Name))
                    continue;

                yield return m;
            }
        }

        private static bool Prepare()
        {
            return TargetMethods().Any();
        }

        private static void Postfix(ref object __result, MethodBase __originalMethod)
        {
            if (__result == null)
                return;

            Config.ModListHiderConfig.Instance.ReloadFromDisk();
            var cfg = Config.ModListHiderConfig.Instance;

            var removed = SanitizeObject(__result, cfg, 0, new HashSet<int>());
            if (removed > 0)
            {
                GD.Print($"[ModListHider] {__originalMethod.Name}: sanitized {removed} mod entr(y/ies) in payload");
            }
        }

        private static int SanitizeObject(
            object obj,
            Config.ModListHiderConfig cfg,
            int depth,
            HashSet<int> visited)
        {
            if (obj == null || depth > 2)
                return 0;

            var t = obj.GetType();
            if (t == typeof(string) || t.IsPrimitive || t.IsEnum)
                return 0;

            if (!t.IsValueType)
            {
                var id = RuntimeHelpers.GetHashCode(obj);
                if (!visited.Add(id))
                    return 0;
            }

            if (obj is List<string> directList)
                return FilterList(directList, cfg);

            var removed = 0;
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var field in t.GetFields(flags))
            {
                if (field.IsStatic)
                    continue;

                object value;
                try
                {
                    value = field.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                removed += SanitizeMember(
                    field.Name,
                    value,
                    v => field.SetValue(obj, v),
                    cfg,
                    depth,
                    visited);
            }

            foreach (var prop in t.GetProperties(flags))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0)
                    continue;

                object value;
                try
                {
                    value = prop.GetValue(obj);
                }
                catch
                {
                    continue;
                }

                Action<object> setter = null;
                if (prop.CanWrite)
                {
                    setter = v =>
                    {
                        try
                        {
                            prop.SetValue(obj, v);
                        }
                        catch
                        {
                            // Best-effort only.
                        }
                    };
                }

                removed += SanitizeMember(
                    prop.Name,
                    value,
                    setter,
                    cfg,
                    depth,
                    visited);
            }

            return removed;
        }

        private static int SanitizeMember(
            string memberName,
            object value,
            Action<object> setter,
            Config.ModListHiderConfig cfg,
            int depth,
            HashSet<int> visited)
        {
            if (value == null)
                return 0;

            var nameLooksMod = memberName.IndexOf("mod", StringComparison.OrdinalIgnoreCase) >= 0;

            if (nameLooksMod && value is List<string> list)
                return FilterList(list, cfg);

            if (nameLooksMod && value is string[] arr && setter != null)
            {
                var filtered = FilterArray(arr, cfg, out var removed);
                if (removed > 0)
                    setter(filtered);
                return removed;
            }

            if (depth < 2 && ShouldRecurse(value, nameLooksMod))
                return SanitizeObject(value, cfg, depth + 1, visited);

            return 0;
        }

        private static bool ShouldRecurse(object value, bool parentLooksMod)
        {
            if (value is string)
                return false;

            var t = value.GetType();
            if (t.IsPrimitive || t.IsEnum)
                return false;
            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(t))
                return false;

            if (parentLooksMod)
                return true;

            if (t.Name.IndexOf("mod", StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            var ns = t.Namespace ?? "";
            return ns.IndexOf("Multiplayer", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int FilterList(List<string> list, Config.ModListHiderConfig cfg)
        {
            if (list == null)
                return 0;

            if (cfg.VanillaMode)
            {
                var removedAll = list.Count;
                if (removedAll > 0)
                    list.Clear();
                return removedAll;
            }

            if (cfg.HiddenModIds.Count == 0)
                return 0;

            var before = list.Count;
            list.RemoveAll(id => cfg.ShouldStripFromMultiplayerList(id));
            return before - list.Count;
        }

        private static string[] FilterArray(string[] arr, Config.ModListHiderConfig cfg, out int removed)
        {
            removed = 0;
            if (arr == null || arr.Length == 0)
                return arr ?? Array.Empty<string>();

            if (cfg.VanillaMode)
            {
                removed = arr.Length;
                return Array.Empty<string>();
            }

            if (cfg.HiddenModIds.Count == 0)
                return arr;

            var list = new List<string>(arr);
            var before = list.Count;
            list.RemoveAll(id => cfg.ShouldStripFromMultiplayerList(id));
            removed = before - list.Count;
            return removed > 0 ? list.ToArray() : arr;
        }
    }
}
