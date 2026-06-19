using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Godot;

namespace ModListHider.UI
{
    /// <summary>
    /// Per-row HideIcon attach via Harmony patch on NModMenuRow._Ready.
    /// 替代旧的全局 ModMenuRowIconInjector 持续扫描方案：
    /// - 行被创建 → 图标自动出现
    /// - 行被销毁 → 图标随父节点回收
    /// - 不再持续扫描 sceneTree
    /// </summary>
    [HarmonyPatch]
    internal static class RowIconAttachPatch
    {
        private const string TargetTypeName =
            "MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen.NModMenuRow";
        private const string IconChildName = "HideIcon";

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
                if (__instance is not Node row)
                    return;

                if (FindDirectChildByName(row, IconChildName) != null)
                    return;

                var (stableId, displayKey) = ResolveModIdentity(row);
                if (string.IsNullOrEmpty(stableId))
                    return;

                var cfg = Config.ModListHiderConfig.Instance;
                var hidden = cfg.IsAnyHidden(stableId, displayKey);

                if (cfg.MigrateLegacyHiddenKey(displayKey, stableId))
                    cfg.Save();

                var icon = new HideIconNode
                {
                    Name = IconChildName,
                    ZIndex = 40
                };
                icon.ConfigureIcon(stableId, hidden);
                row.AddChild(icon);

                if (Core.DebugLog.Enabled)
                    Core.DebugLog.Info($"RowIconAttachPatch: injected for stableId={stableId} title={displayKey}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] RowIconAttachPatch failed: {ex.Message}");
            }
        }

        private static (string stableId, string displayKey) ResolveModIdentity(Node row)
        {
            var title = ReadTitleText(row) ?? string.Empty;
            var stableId = ReadStableModId(row) ?? string.Empty;

            if (string.IsNullOrWhiteSpace(stableId))
                stableId = title;
            if (string.IsNullOrWhiteSpace(title))
                title = stableId;

            return (stableId.Trim(), title.Trim());
        }

        private static string? ReadTitleText(Node row)
        {
            var titleNode = FindDirectChildByName(row, "Title")
                ?? row.FindChild("Title", true, false);
            if (titleNode == null)
                return null;

            var v = GetMemberValue(titleNode, "Text", "text");
            return ToNonEmpty(v);
        }

        private static string? ReadStableModId(Node row)
        {
            var modObj = GetMemberValue(row, "Mod", "mod", "_mod");
            if (modObj == null)
                return null;

            var manifest = GetMemberValue(modObj, "manifest", "Manifest");
            if (manifest != null)
            {
                var mid = GetMemberValue(manifest, "id", "Id", "ModId", "manifestId", "ManifestId");
                var asString = ToNonEmpty(mid);
                if (asString != null)
                    return asString;
            }

            var direct = GetMemberValue(modObj, "id", "Id", "ModId", "manifestId", "ManifestId");
            return ToNonEmpty(direct);
        }

        private static string? ToNonEmpty(object? v)
        {
            if (v is string s && !string.IsNullOrWhiteSpace(s))
                return s;
            var asString = v?.ToString();
            return string.IsNullOrWhiteSpace(asString) ? null : asString;
        }

        private static object? GetMemberValue(object target, params string[] names)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic
                | BindingFlags.Instance | BindingFlags.IgnoreCase;
            var t = target.GetType();
            foreach (var name in names)
            {
                try
                {
                    var prop = t.GetProperty(name, flags);
                    if (prop != null && prop.GetIndexParameters().Length == 0)
                        return prop.GetValue(target);

                    var field = t.GetField(name, flags);
                    if (field != null)
                        return field.GetValue(target);
                }
                catch
                {
                }
            }

            return null;
        }

        private static Node? FindDirectChildByName(Node parent, string name)
        {
            foreach (var child in parent.GetChildren())
            {
                if (string.Equals(child.Name.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    return child;
            }

            return null;
        }
    }
}