using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using ModListHider.Core;

namespace ModListHider.UI
{
    /// <summary>
    /// Persistent injector for per-mod eye icons.
    /// Keeps running so icons recover automatically after screen rebuilds.
    /// </summary>
    public partial class ModMenuRowIconInjector : Node
    {
        private sealed record ModIdentity(string StableId, string DisplayKey);

        private const float ScanInterval = 0.25f;
        private float _scanTimer;
        private int _debugRowDumpBudget = 12;

        public override void _Ready()
        {
            GD.Print("[ModListHider] ModMenuRowIconInjector started (persistent mode).");
            Config.ModListHiderConfig.Instance.Load();
        }

        public override void _Process(double delta)
        {
            _scanTimer += (float)delta;
            if (_scanTimer < ScanInterval) return;
            _scanTimer = 0f;

            TryInjectIcons();
        }

        private void TryInjectIcons()
        {
            var screen = FindModdingScreen(GetTree()?.Root);
            if (screen == null) return;

            var rows = FindModMenuRows(screen);
            if (rows.Count == 0) return;

            CleanupOrphanIcons(screen, rows);

            int injected = 0;
            int refreshed = 0;
            foreach (var row in rows)
            {
                if (!IsRowLikelyVisible(row, screen))
                    continue;

                if (FindDirectChildByName(row, "HideIcon") is HideIconNode existing)
                {
                    existing.RefreshLayout();
                    refreshed++;
                    continue;
                }

                if (TryInjectIcon(row))
                {
                    injected++;
                }
            }

            if (injected > 0 || refreshed > 0)
            {
                GD.Print($"[ModListHider] Row icons refreshed. injected={injected}, realigned={refreshed}, rows={rows.Count}");
            }
        }

        private bool TryInjectIcon(Node row)
        {
            try
            {
                if (row is not Control rowControl || rowControl.Size.X < 60f || rowControl.Size.Y < 18f)
                    return false;

                var identity = ResolveModIdentity(row);
                if (identity == null)
                {
                    GD.Print($"[ModListHider] Could not resolve mod identity for row: {row.GetType().Name}");
                    return false;
                }

                var cfg = Config.ModListHiderConfig.Instance;
                bool hidden = cfg.IsAnyHidden(identity.StableId, identity.DisplayKey);
                if (cfg.MigrateLegacyHiddenKey(identity.DisplayKey, identity.StableId))
                    cfg.Save();

                var icon = new HideIconNode();
                icon.Name = "HideIcon";
                icon.ZIndex = 40;
                icon.ConfigureIcon(identity.StableId, hidden);

                row.AddChild(icon);
                icon.RefreshLayout();
                GD.Print($"[ModListHider] Injected hide icon for mod: {identity.StableId} (title: {identity.DisplayKey})");

                if (DebugLog.Enabled)
                {
                    DumpRowLayout(row);
                }
                return true;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] Failed to inject icon: {ex.Message}");
                return false;
            }
        }

        private static bool IsRowLikelyVisible(Node row, Node screen)
        {
            if (row is not Control rowControl)
                return true;
            if (!rowControl.Visible || !rowControl.IsVisibleInTree())
                return false;

            var border = screen.FindChild("ModsBorder", true, false) as Control;
            if (border == null || border.Size.X <= 1f || border.Size.Y <= 1f)
                return true;

            if (rowControl.Size.X < 40f || rowControl.Size.Y < 12f)
                return false;

            var rowRect = new Rect2(rowControl.GlobalPosition, rowControl.Size).Grow(2f);
            var borderRect = new Rect2(border.GlobalPosition, border.Size).Grow(8f);
            if (!rowRect.Intersects(borderRect))
                return false;

            var titleNode = FindDirectChildByName(row, "Title") as Control;
            if (titleNode != null)
            {
                var titleRect = new Rect2(titleNode.GlobalPosition, titleNode.Size);
                if (!titleRect.Intersects(borderRect))
                    return false;
            }

            return true;
        }

        private ModIdentity? ResolveModIdentity(Node row)
        {
            try
            {
                string? title = ReadTitleText(row);
                string? stableId = ReadStableModId(row);

                if (string.IsNullOrWhiteSpace(stableId))
                    stableId = title;
                if (string.IsNullOrWhiteSpace(title))
                    title = stableId;

                if (string.IsNullOrWhiteSpace(stableId) || string.IsNullOrWhiteSpace(title))
                    return null;

                stableId = stableId.Trim();
                title = title.Trim();
                return new ModIdentity(stableId, title);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] ResolveModIdentity failed: {ex.Message}");
                return null;
            }
        }

        private static string? ReadTitleText(Node row)
        {
            var titleNode = FindDirectChildByName(row, "Title") ?? row.FindChild("Title", true, false);
            if (titleNode == null) return null;

            var value = GetMemberValue(titleNode, "Text", "text");
            if (value is string text && !string.IsNullOrWhiteSpace(text))
                return text;

            var asString = value?.ToString();
            return string.IsNullOrWhiteSpace(asString) ? null : asString;
        }

        private static string? ReadStableModId(Node row)
        {
            var modObj = GetMemberValue(row, "Mod", "mod", "_mod");
            if (modObj == null) return null;

            var manifest = GetMemberValue(modObj, "manifest", "Manifest");
            if (manifest != null)
            {
                var manifestId = GetMemberString(
                    manifest,
                    "id", "Id", "manifestId", "ManifestId", "modId", "ModId");
                if (!string.IsNullOrWhiteSpace(manifestId))
                    return manifestId;
            }

            return GetMemberString(modObj, "id", "Id", "manifestId", "ManifestId", "modId", "ModId");
        }

        private static string? GetMemberString(object target, params string[] names)
        {
            var value = GetMemberValue(target, names);
            if (value is string s && !string.IsNullOrWhiteSpace(s))
                return s;

            var asString = value?.ToString();
            return string.IsNullOrWhiteSpace(asString) ? null : asString;
        }

        private static object? GetMemberValue(object target, params string[] names)
        {
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.IgnoreCase;
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
                    // Keep probing possible member names.
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

        private Node? FindModdingScreen(Node? root)
        {
            if (root == null) return null;

            var byType = FindModdingScreenByType(root);
            if (byType != null)
                return byType;

            return FindModdingScreenBySignature(root);
        }

        private Node? FindModdingScreenByType(Node root)
        {
            var tn = root.GetType().Name;
            if (tn == "NModdingScreen" || tn.EndsWith(".NModdingScreen", StringComparison.Ordinal))
                return root;

            foreach (var child in root.GetChildren())
            {
                var found = FindModdingScreenByType(child);
                if (found != null) return found;
            }

            return null;
        }

        private Node? FindModdingScreenBySignature(Node root)
        {
            if (LooksLikeModdingScreen(root))
                return root;

            foreach (var child in root.GetChildren())
            {
                var found = FindModdingScreenBySignature(child);
                if (found != null) return found;
            }

            return null;
        }

        private static bool LooksLikeModdingScreen(Node n)
        {
            return n.FindChild("InstalledModsTitle", true, false) != null
                && n.FindChild("ModsBorder", true, false) != null;
        }

        private List<Node> FindModMenuRows(Node parent)
        {
            var rows = new List<Node>();
            foreach (var child in parent.GetChildren())
            {
                if (LooksLikeModMenuRow(child))
                {
                    rows.Add(child);
                    // Real row found: avoid recursive descent to reduce false positives.
                    continue;
                }

                rows.AddRange(FindModMenuRows(child));
            }

            return rows;
        }

        private static bool LooksLikeModMenuRow(Node node)
        {
            if (node is not Control row) return false;
            if (row.Size.X < 40f || row.Size.Y < 12f) return false;

            var titleNode = FindDirectChildByName(node, "Title");
            var tickboxNode = FindDirectChildByName(node, "Tickbox");
            if (titleNode == null || tickboxNode == null) return false;

            // Filter out visual templates: real rows usually carry mod data.
            var hasModPayload = GetMemberValue(node, "Mod", "mod", "_mod") != null;
            if (!hasModPayload) return false;

            var tn = node.GetType().Name;
            return tn.Contains("ModMenuRow", StringComparison.OrdinalIgnoreCase)
                || tn.Contains("NModMenuRow", StringComparison.OrdinalIgnoreCase);
        }

        private void CleanupOrphanIcons(Node screen, List<Node> rows)
        {
            var rowIds = new HashSet<ulong>();
            foreach (var row in rows)
                rowIds.Add(row.GetInstanceId());

            int removed = 0;
            foreach (var icon in CollectHideIcons(screen))
            {
                var parent = icon.GetParent();
                if (parent == null || !rowIds.Contains(parent.GetInstanceId()))
                {
                    icon.QueueFree();
                    removed++;
                    continue;
                }

                if (!IsRowLikelyVisible(parent, screen))
                {
                    icon.QueueFree();
                    removed++;
                }
            }

            if (removed > 0)
            {
                GD.Print($"[ModListHider] Removed {removed} orphan/outside HideIcon node(s).");
            }
        }

        private static List<HideIconNode> CollectHideIcons(Node root)
        {
            var list = new List<HideIconNode>();
            CollectHideIconsRecursive(root, list);
            return list;
        }

        private static void CollectHideIconsRecursive(Node node, List<HideIconNode> output)
        {
            if (node is HideIconNode icon)
                output.Add(icon);

            foreach (var child in node.GetChildren())
                CollectHideIconsRecursive(child, output);
        }

        private void DumpRowLayout(Node row)
        {
            if (!DebugLog.Enabled || _debugRowDumpBudget <= 0) return;
            _debugRowDumpBudget--;

            if (row is not Control rc) return;
            var title = FindDirectChildByName(row, "Title") as Control;
            var tick = FindDirectChildByName(row, "Tickbox") as Control;
            var folder = FindControlByNameContains(row, "folder");

            DebugLog.Info(
                $"row={row.GetType().Name} id={row.GetInstanceId()} " +
                $"rowPos={rc.Position} rowSize={rc.Size} " +
                $"titlePos={title?.Position} titleSize={title?.Size} " +
                $"tickPos={tick?.Position} tickSize={tick?.Size} " +
                $"folder={folder?.Name} folderPos={folder?.Position} folderSize={folder?.Size}");
        }

        private static Control? FindControlByNameContains(Node root, string tokenLower)
        {
            foreach (var child in root.GetChildren())
            {
                if (child is Control c &&
                    c.Name.ToString().IndexOf(tokenLower, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return c;
                }
            }

            return null;
        }
    }
}
