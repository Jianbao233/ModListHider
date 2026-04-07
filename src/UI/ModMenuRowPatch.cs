using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

using System;
using System.Collections.Generic;
using Godot;

namespace ModListHider.UI
{
    /// <summary>
    /// Safe UI injection for mod row eye icons on Android.
    /// Uses periodic tree scanning instead of Harmony AddChild patches to avoid crashes.
    /// </summary>
    public partial class ModMenuRowIconInjector : Node
    {
        private const float ScanInterval = 0.3f;
        private const float MaxScanTime = 60f;
        private float _scanTimer;
        private float _totalScanTime;
        private readonly HashSet<ulong> _processedRows = new();

        public override void _Ready()
        {
            GD.Print("[ModListHider] ModMenuRowIconInjector started. Will scan for mod menu rows...");
            Config.ModListHiderConfig.Instance.Load();
        }

        public override void _Process(double delta)
        {
            _scanTimer += (float)delta;
            _totalScanTime += (float)delta;

            if (_scanTimer < ScanInterval) return;
            _scanTimer = 0f;

            if (_totalScanTime > MaxScanTime)
            {
                GD.Print("[ModListHider] ModMenuRowIconInjector: giving up, mod menu rows not found");
                QueueFree();
                return;
            }

            TryInjectIcons();
        }

        private void TryInjectIcons()
        {
            var screen = FindModdingScreen(GetTree()?.Root);
            if (screen == null) return;

            var rows = FindModMenuRows(screen);
            if (rows.Count == 0) return;

            GD.Print($"[ModListHider] Found {rows.Count} mod menu rows");

            int injected = 0;
            foreach (var row in rows)
            {
                if (_processedRows.Contains(row.GetInstanceId())) continue;
                _processedRows.Add(row.GetInstanceId());

                if (TryInjectIcon(row))
                    injected++;
            }

            if (injected > 0)
            {
                GD.Print($"[ModListHider] Injected icons into {injected} mod menu rows");
            }
        }

        private bool TryInjectIcon(Node row)
        {
            try
            {
                if (row.FindChild("HideIcon", true, false) != null)
                    return false;

                string? modId = GetModId(row);
                if (string.IsNullOrEmpty(modId))
                {
                    GD.Print($"[ModListHider] Could not get mod ID for row: {row.GetType().Name}");
                    return false;
                }

                var icon = new HideIconNode();
                icon.Name = "HideIcon";
                icon.ZIndex = 24;
                icon.ConfigureIcon(modId, Config.ModListHiderConfig.Instance.IsHidden(modId));

                // Position on the right side of the row
                icon.SetAnchorsPreset(Control.LayoutPreset.TopRight);
                icon.OffsetLeft = -140;
                icon.OffsetRight = -92;
                icon.OffsetTop = 12;
                icon.OffsetBottom = 60;

                row.AddChild(icon);
                GD.Print($"[ModListHider] Injected hide icon for mod: {modId}");
                return true;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] Failed to inject icon: {ex.Message}");
                return false;
            }
        }

        private string? GetModId(Node row)
        {
            try
            {
                // Try to get the title text and match against loaded mods
                var titleNode = row.FindChild("Title", true, false);
                if (titleNode == null) return null;

                // Get the text from the title node
                string? titleText = null;
                try
                {
                    var textProp = titleNode.GetType().GetProperty("Text");
                    if (textProp != null)
                        titleText = textProp.GetValue(titleNode) as string;
                }
                catch { }

                if (string.IsNullOrEmpty(titleText))
                    titleText = titleNode.GetType().Name; // Fallback

                GD.Print($"[ModListHider] Row title: {titleText}");
                return titleText;
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] GetModId failed: {ex.Message}");
                return null;
            }
        }

        private Node? FindModdingScreen(Node? root)
        {
            if (root == null) return null;

            var tn = root.GetType().Name;
            if (tn == "NModdingScreen" || tn.EndsWith(".NModdingScreen", StringComparison.Ordinal))
                return root;

            foreach (var child in root.GetChildren())
            {
                var found = FindModdingScreen(child);
                if (found != null) return found;
            }

            return null;
        }

        private List<Node> FindModMenuRows(Node parent)
        {
            var rows = new List<Node>();

            foreach (var child in parent.GetChildren())
            {
                // Check if this looks like a mod menu row
                if (LooksLikeModMenuRow(child))
                {
                    rows.Add(child);
                }

                // Recursively search in children
                rows.AddRange(FindModMenuRows(child));
            }

            return rows;
        }

        private bool LooksLikeModMenuRow(Node n)
        {
            // Mod menu rows typically have Title and Tickbox children
            if (n.FindChild("Tickbox", true, false) == null) return false;
            if (n.FindChild("Title", true, false) == null) return false;

            // Also check type name
            var tn = n.GetType().Name;
            return tn.Contains("ModMenuRow", StringComparison.OrdinalIgnoreCase)
                || tn.Contains("NModMenuRow", StringComparison.OrdinalIgnoreCase);
        }
    }
}
