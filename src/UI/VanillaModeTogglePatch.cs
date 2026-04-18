using System;
using System.IO;
using Godot;

namespace ModListHider.UI
{
    /// <summary>
    /// Persistent VanillaMode button injector.
    /// Keeps scanning to handle screen rebuilds after game UI flow changes.
    /// </summary>
    public partial class VanillaModeToggleInjector : Node
    {
        private const float ScanInterval = 0.35f;
        private const string ToggleNodeName = "VanillaModeToggle";
        private float _scanTimer;
        private ulong _lastScreenId;

        public override void _Ready()
        {
            GD.Print("[ModListHider] VanillaModeToggleInjector started (persistent mode).");
        }

        public override void _Process(double delta)
        {
            _scanTimer += (float)delta;
            if (_scanTimer < ScanInterval) return;
            _scanTimer = 0f;
            TryInject();
        }

        private void TryInject()
        {
            var screen = FindModdingScreen(GetTree()?.Root);
            if (screen == null)
            {
                _lastScreenId = 0;
                return;
            }

            try
            {
                var currentScreenId = screen.GetInstanceId();
                if (_lastScreenId != currentScreenId)
                {
                    _lastScreenId = currentScreenId;
                    GD.Print($"[ModListHider] Tracking modding screen: {screen.GetType().Name} ({currentScreenId})");
                }

                if (screen.FindChild(ToggleNodeName, true, false) is VanillaModeToggleNode existing)
                {
                    EnsureTogglePlacement(existing);
                    return;
                }

                Config.ModListHiderConfig.Instance.Load();
                bool vanilla = Config.ModListHiderConfig.Instance.VanillaMode;

                var btn = new VanillaModeToggleNode();
                btn.Name = ToggleNodeName;
                btn.Configure(vanilla);
                EnsureTogglePlacement(btn);

                screen.AddChild(btn);
                GD.Print($"[ModListHider] VanillaMode toggle injected. VanillaMode={vanilla}, parent={screen.GetType().Name}");
                TryAppendDebug($"[ModListHider] VanillaMode toggle injected at {DateTime.Now}\n");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] VanillaModeToggleInjector failed: {ex.Message}");
                TryAppendDebug($"Inject failed: {ex.Message}\n{ex.StackTrace}\n");
            }
        }

        private static void EnsureTogglePlacement(Control btn)
        {
            btn.ZIndex = 80;
            btn.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            btn.AnchorLeft = 0f;
            btn.AnchorRight = 0f;
            btn.AnchorTop = 0f;
            btn.AnchorBottom = 0f;
            btn.OffsetLeft = 18f;
            btn.OffsetTop = 18f;
            btn.OffsetRight = 66f;
            btn.OffsetBottom = 66f;
        }

        private Node? FindModdingScreen(Node? root)
        {
            if (root == null) return null;

            // Prefer strict type match first to avoid injecting into lookalike nodes.
            var byType = FindModdingScreenByType(root);
            if (byType != null)
                return byType;

            // Fallback for future game updates if type name changes.
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
