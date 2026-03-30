using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Godot;

namespace ModListHider.Config
{
    /// <summary>
    /// Persistent configuration for hidden mods.
    /// Stored as JSON at: %APPDATA%/SlayTheSpire2/ModListHider/hidden_mods.json
    /// </summary>
    internal sealed class ModListHiderConfig
    {
        private static readonly ModListHiderConfig _instance = new ModListHiderConfig();
        public static ModListHiderConfig Instance => _instance;

        /// <summary>Set of mod IDs that should be hidden from multiplayer.</summary>
        public HashSet<string> HiddenModIds { get; private set; } = new();

        /// <summary>
        /// Vanilla Mode: when true, pretends no mods are loaded at all.
        /// The multiplayer handshake sends null mod list, so vanilla players can join.
        /// Individual eye icons still work (per-mod toggle) when this is false.
        /// </summary>
        public bool VanillaMode { get; set; }

        /// <summary>Path to the hidden_mods.json file.</summary>
        private readonly string _configPath;

        private ModListHiderConfig()
        {
            string appData = OS.GetUserDataDir();
            string configDir = Path.Combine(appData ?? "", "ModListHider");
            _configPath = Path.Combine(configDir, "hidden_mods.json");
        }

        public bool IsHidden(string modId) => HiddenModIds.Contains(modId);

        /// <summary>
        /// 联机侧条目常为 <c>ManifestId-1.0.0</c>，而 UI 存的是 manifest 的 <c>id</c>（无版本）。
        /// 用精确匹配 +「隐藏 id + '-' + 版本号」匹配，避免 Contains 对不上。
        /// </summary>
        public bool ShouldStripFromMultiplayerList(string entry)
        {
            if (string.IsNullOrEmpty(entry))
                return false;
            if (HiddenModIds.Contains(entry))
                return true;

            foreach (var h in HiddenModIds)
            {
                if (string.IsNullOrEmpty(h))
                    continue;
                if (entry.Length <= h.Length + 1)
                    continue;
                if (!entry.StartsWith(h + "-", StringComparison.Ordinal))
                    continue;
                var suffix = entry.Substring(h.Length + 1);
                if (IsVersionSuffix(suffix))
                    return true;
            }

            return false;
        }

        /// <summary>Suffix after "ModId-" when it looks like a version token (e.g. 1.0.0).</summary>
        private static bool IsVersionSuffix(string s)
        {
            // Wire format observed: NecrobinderOstyAnimMod-1.0.0
            return Regex.IsMatch(s, @"^\d+(\.\d+)*$");
        }

        /// <summary>
        /// Reloads hidden set from disk without clearing existing entries.
        /// Used by hot-reload patches to pick up changes made during the session.
        /// </summary>
        public void ReloadFromDisk()
        {
            try
            {
                if (!File.Exists(_configPath))
                    return;

                string json = File.ReadAllText(_configPath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var fresh = new HashSet<string>();
                if (root.TryGetProperty("hidden_mods", out var mods) && mods.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in mods.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            fresh.Add(item.GetString()!);
                    }
                }

                // Merge: keep any entries added this session that aren't in the file yet
                HiddenModIds.IntersectWith(fresh);
                foreach (var id in fresh)
                    HiddenModIds.Add(id);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] ReloadFromDisk failed: {ex.Message}");
            }
        }

        public void SetHidden(string modId, bool hidden)
        {
            if (hidden)
            {
                if (HiddenModIds.Add(modId))
                    GD.Print($"[ModListHider] Hidden mod: {modId}");
            }
            else
            {
                if (HiddenModIds.Remove(modId))
                    GD.Print($"[ModListHider] Unhidden mod: {modId}");
            }
        }

        public void ToggleHidden(string modId)
        {
            if (HiddenModIds.Contains(modId))
                SetHidden(modId, false);
            else
                SetHidden(modId, true);
        }

        public void Load()
        {
            try
            {
                if (!File.Exists(_configPath))
                {
                    GD.Print($"[ModListHider] No config at {_configPath}, starting fresh.");
                    MergeNoClientCheatsConfig();
                    return;
                }

                string json = File.ReadAllText(_configPath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                HiddenModIds.Clear();
                if (root.TryGetProperty("hidden_mods", out var mods) && mods.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in mods.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            HiddenModIds.Add(item.GetString()!);
                    }
                }

                // Load VanillaMode flag
                VanillaMode = root.TryGetProperty("vanilla_mode", out var vm)
                    && vm.ValueKind == JsonValueKind.True;

                GD.Print($"[ModListHider] Config loaded. Hidden mods: {HiddenModIds.Count}, VanillaMode: {VanillaMode}");
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] Config.Load failed: {ex.Message}");
            }
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var obj = new ConfigData { 
                    hidden_mods = new List<string>(HiddenModIds),
                    vanilla_mode = VanillaMode
                };
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(obj, options);
                File.WriteAllText(_configPath, json);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] Config.Save failed: {ex.Message}");
            }
        }

        public void SetVanillaMode(bool on)
        {
            VanillaMode = on;
            Save();
            GD.Print($"[ModListHider] VanillaMode set to: {on}");
        }

        /// <summary>
        /// Reads HideFromModList from NoClientCheats (if installed) and imports mods.
        /// </summary>
        private void MergeNoClientCheatsConfig()
        {
            try
            {
                string appData = OS.GetUserDataDir();
                string nccPath = Path.Combine(appData ?? "", "NoClientCheats", "settings.json");
                if (!File.Exists(nccPath)) return;

                string json = File.ReadAllText(nccPath);
                var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("hide_from_mod_list", out var hidden)
                    && hidden.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in hidden.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                            HiddenModIds.Add(item.GetString()!);
                    }
                    if (HiddenModIds.Count > 0)
                    {
                        GD.Print($"[ModListHider] Merged {HiddenModIds.Count} mods from NoClientCheats.");
                        Save();
                    }
                }
            }
            catch (Exception ex)
            {
                GD.Print($"[ModListHider] Could not merge NCC config: {ex.Message}");
            }
        }

        private class ConfigData
        {
            [JsonPropertyName("hidden_mods")]
            public List<string> hidden_mods { get; set; } = new();

            [JsonPropertyName("vanilla_mode")]
            public bool vanilla_mode { get; set; }
        }
    }
}
