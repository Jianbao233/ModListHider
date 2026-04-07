using System;
using System.IO;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

using System;
using System.IO;
using Godot;

namespace ModListHider.UI
{
    /// <summary>
    /// Safe UI injection for Android.
    /// Instead of patching AddChild (which crashes on Android), this class
    /// periodically scans the scene tree for the modding screen and injects
    /// the Vanilla Mode toggle button when found.
    /// </summary>
    public partial class VanillaModeToggleInjector : Node
    {
        private const float ScanInterval = 0.5f;
        private const float MaxScanTime = 30f;
        private float _scanTimer;
        private float _totalScanTime;
        private bool _injected;

        public override void _Ready()
        {
            GD.Print("[ModListHider] VanillaModeToggleInjector started. Will scan for modding screen...");
        }

        public override void _Process(double delta)
        {
            if (_injected) return;

            _scanTimer += (float)delta;
            _totalScanTime += (float)delta;

            if (_scanTimer < ScanInterval) return;
            _scanTimer = 0f;

            // Give up after MaxScanTime seconds
            if (_totalScanTime > MaxScanTime)
            {
                GD.Print("[ModListHider] VanillaModeToggleInjector: giving up, modding screen not found");
                QueueFree();
                return;
            }

            TryInject();
        }

        private void TryInject()
        {
            var screen = FindModdingScreen(GetTree()?.Root);
            if (screen == null) return;

            GD.Print($"[ModListHider] Found modding screen: {screen.GetType().Name}");

            try
            {
                if (screen.FindChild("VanillaModeToggle", true, false) != null)
                {
                    _injected = true;
                    GD.Print("[ModListHider] VanillaModeToggle already injected, stopping scanner");
                    QueueFree();
                    return;
                }

                Config.ModListHiderConfig.Instance.Load();
                bool vanilla = Config.ModListHiderConfig.Instance.VanillaMode;

                var btn = new VanillaModeToggleNode();
                btn.Name = "VanillaModeToggle";
                btn.Configure(vanilla);

                // Position at top-left corner of the screen
                btn.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
                btn.OffsetLeft = 16;
                btn.OffsetTop = 16;
                btn.OffsetRight = 16 + 48;
                btn.OffsetBottom = 16 + 48;

                screen.AddChild(btn);

                _injected = true;
                GD.Print($"[ModListHider] VanillaMode toggle injected. VanillaMode={vanilla}");
                TryAppendDebug($"[ModListHider] VanillaMode toggle injected at {DateTime.Now}\n");

                // Stop scanning after injection
                QueueFree();
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] VanillaModeToggleInjector failed: {ex.Message}");
                TryAppendDebug($"Inject failed: {ex.Message}\n{ex.StackTrace}\n");
            }
        }

        private Node? FindModdingScreen(Node? root)
        {
            if (root == null) return null;

            // Check if this node looks like the modding screen
            if (LooksLikeModdingScreen(root))
                return root;

            // Search children recursively
            foreach (var child in root.GetChildren())
            {
                var found = FindModdingScreen(child);
                if (found != null) return found;
            }

            return null;
        }

        private bool LooksLikeModdingScreen(Node n)
        {
            // Check for known child names
            if (n.FindChild("InstalledModsTitle", true, false) != null)
                return true;
            if (n.FindChild("ModsBorder", true, false) != null)
                return true;

            // Check type name
            var tn = n.GetType().Name;
            if (tn == "NModdingScreen" || tn.EndsWith(".NModdingScreen", StringComparison.Ordinal))
                return true;

            return false;
        }

        private void TryAppendDebug(string line)
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
