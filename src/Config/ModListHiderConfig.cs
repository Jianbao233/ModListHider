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
        private static readonly StringComparer KeyComparer = StringComparer.OrdinalIgnoreCase;
        private static readonly Regex VersionedModEntryRegex = new(
            @"^(?<id>.+)-(?<ver>\d+(?:\.\d+)*(?:[-+][0-9A-Za-z.-]+)?)$",
            RegexOptions.Compiled);

        private static readonly ModListHiderConfig _instance = new ModListHiderConfig();
        public static ModListHiderConfig Instance => _instance;

        /// <summary>Set of mod IDs that should be hidden from multiplayer.</summary>
        public HashSet<string> HiddenModIds { get; private set; } = new(KeyComparer);

        /// <summary>
        /// Vanilla Mode: when true, pretends no mods are loaded at all.
        /// The multiplayer handshake sends null mod list, so vanilla players can join.
        /// Individual eye icons still work (per-mod toggle) when this is false.
        /// </summary>
        public bool VanillaMode { get; set; }

        /// <summary>Verbose diagnostics switch for UI injection/layout issues.</summary>
        public bool DebugMode { get; set; }

        /// <summary>Path to the hidden_mods.json file.</summary>
        private readonly string _configPath;

        private ModListHiderConfig()
        {
            string appData = OS.GetUserDataDir();
            string configDir = Path.Combine(appData ?? "", "ModListHider");
            _configPath = Path.Combine(configDir, "hidden_mods.json");
        }

        public bool IsHidden(string modId)
        {
            var key = NormalizeKey(modId);
            return !string.IsNullOrEmpty(key) && HiddenModIds.Contains(key);
        }

        public bool IsAnyHidden(params string?[] candidates)
        {
            foreach (var c in candidates)
            {
                if (IsHidden(c ?? string.Empty))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Legacy migration: replace a display-name key with stable manifest id key.
        /// Returns true if HiddenModIds changed.
        /// </summary>
        public bool MigrateLegacyHiddenKey(string legacyKey, string stableKey)
        {
            var oldKey = NormalizeKey(legacyKey);
            var newKey = NormalizeKey(stableKey);
            if (string.IsNullOrEmpty(oldKey) || string.IsNullOrEmpty(newKey))
                return false;

            if (KeyComparer.Equals(oldKey, newKey))
                return false;

            if (!HiddenModIds.Remove(oldKey))
                return false;

            HiddenModIds.Add(newKey);
            GD.Print($"[ModListHider] Migrated hidden key '{oldKey}' -> '{newKey}'");
            return true;
        }

        /// <summary>
        /// 联机侧条目常为 <c>ManifestId-1.0.0</c>，而 UI 存的是 manifest 的 <c>id</c>（无版本）。
        /// 用精确匹配 +「隐藏 id + '-' + 版本号」匹配，避免 Contains 对不上。
        /// </summary>
        public bool ShouldStripFromMultiplayerList(string entry)
        {
            var normalizedEntry = NormalizeKey(entry);
            if (string.IsNullOrEmpty(normalizedEntry))
                return false;
            if (HiddenModIds.Contains(normalizedEntry))
                return true;

            var baseId = TryExtractBaseModId(normalizedEntry);
            if (!string.IsNullOrEmpty(baseId) && HiddenModIds.Contains(baseId))
                return true;

            foreach (var h in HiddenModIds)
            {
                if (string.IsNullOrEmpty(h) || normalizedEntry.Length <= h.Length + 1)
                    continue;
                if (!normalizedEntry.StartsWith(h + "-", StringComparison.OrdinalIgnoreCase))
                    continue;
                var suffix = normalizedEntry.Substring(h.Length + 1);
                if (LooksLikeVersionSuffix(suffix))
                    return true;
            }

            return false;
        }

        private static string NormalizeKey(string? raw)
        {
            return raw?.Trim() ?? string.Empty;
        }

        private static string TryExtractBaseModId(string entry)
        {
            var match = VersionedModEntryRegex.Match(entry);
            if (!match.Success)
                return entry;
            return NormalizeKey(match.Groups["id"].Value);
        }

        /// <summary>Suffix after "ModId-" when it looks like a version token (e.g. 1.0.0-beta.1).</summary>
        private static bool LooksLikeVersionSuffix(string s)
        {
            return Regex.IsMatch(s, @"^\d+(\.\d+)*(?:[-+][0-9A-Za-z.-]+)?$");
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

                var fresh = new HashSet<string>(KeyComparer);
                if (root.TryGetProperty("hidden_mods", out var mods) && mods.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in mods.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.String)
                        {
                            var key = NormalizeKey(item.GetString());
                            if (!string.IsNullOrEmpty(key))
                                fresh.Add(key);
                        }
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
            var key = NormalizeKey(modId);
            if (string.IsNullOrEmpty(key))
                return;

            if (hidden)
            {
                if (HiddenModIds.Add(key))
                    GD.Print($"[ModListHider] Hidden mod: {key}");
            }
            else
            {
                if (HiddenModIds.Remove(key))
                    GD.Print($"[ModListHider] Unhidden mod: {key}");
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
                        {
                            var key = NormalizeKey(item.GetString());
                            if (!string.IsNullOrEmpty(key))
                                HiddenModIds.Add(key);
                        }
                    }
                }

                // Load VanillaMode flag
                VanillaMode = root.TryGetProperty("vanilla_mode", out var vm)
                    && vm.ValueKind == JsonValueKind.True;

                DebugMode = root.TryGetProperty("debug_mode", out var dm)
                    && dm.ValueKind == JsonValueKind.True;

                GD.Print($"[ModListHider] Config loaded. Hidden mods: {HiddenModIds.Count}, VanillaMode: {VanillaMode}, DebugMode: {DebugMode}");
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
                    vanilla_mode = VanillaMode,
                    debug_mode = DebugMode
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

        public void SetDebugMode(bool on)
        {
            DebugMode = on;
            Save();
            GD.Print($"[ModListHider] DebugMode set to: {on}");
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
                        {
                            var key = NormalizeKey(item.GetString());
                            if (!string.IsNullOrEmpty(key))
                                HiddenModIds.Add(key);
                        }
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

            [JsonPropertyName("debug_mode")]
            public bool debug_mode { get; set; }
        }
    }
}
