using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace ModListHider.UI
{
    /// <summary>
    /// 在模组列表行上注入眼睛图标。
    /// 日志显示 PatchAll 成功但从未生成 row_debug：说明补丁 <see cref="Node._Ready"/> 在 Godot 4 + 生成代码下
    /// 可能根本不会在子类行控件上触发。改为在 <see cref="Node.AddChild"/> 上检测「模组行」场景结构（Title + Tickbox），再调度注入。
    /// </summary>
    [HarmonyPatch]
    internal static class ModMenuRowAddChildPatch
    {
        private static bool _configLoaded;
        private static bool _loggedFirstHit;

        /// <summary>同一行会多次 AddChild 子节点，避免每波都挂 20 个 Timer。</summary>
        private static readonly HashSet<ulong> _injectTimersRegistered = new();

        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(Node), "AddChild",
                new[] { typeof(Node), typeof(bool), typeof(Node.InternalMode) });
        }

        private static bool Prepare()
        {
            return TargetMethod() != null;
        }

        [HarmonyPostfix]
        private static void Postfix(Node __instance, Node node)
        {
            try
            {
                // 可能先加行根节点（子树已完整），也可能逐个子节点加入；两者都试。
                Node? row = null;
                if (node != null && LooksLikeModMenuRowScene(node))
                    row = node;
                else if (__instance != null && LooksLikeModMenuRowScene(__instance))
                    row = __instance;

                if (row == null)
                    return;

                if (!_loggedFirstHit)
                {
                    _loggedFirstHit = true;
                    TryAppendDebugFile(
                        $"[ModListHider] AddChild hit mod row. rowType={row.GetType().FullName}\n");
                }

                EnsureConfigLoaded();
                ScheduleInjectAttempts(row);
            }
            catch (Exception ex)
            {
                TryAppendDebugFile($"AddChild Postfix: {ex.Message}\n{ex.StackTrace}\n");
            }
        }

        /// <summary>与 modding_screen_row.tscn 一致：根下含 Title 与 Tickbox。</summary>
        private static bool LooksLikeModMenuRowScene(Node n)
        {
            if (n.FindChild("Tickbox", true, false) == null) return false;
            if (n.FindChild("Title", true, false) == null) return false;
            return true;
        }

        private static void EnsureConfigLoaded()
        {
            if (_configLoaded) return;
            _configLoaded = true;
            try
            {
                Config.ModListHiderConfig.Instance.Load();
            }
            catch
            {
                /* ignore */
            }
        }

        private static void ScheduleInjectAttempts(Node rowNode)
        {
            void InjectOnce()
            {
                try
                {
                    if (!GodotObject.IsInstanceValid(rowNode))
                        return;
                    if (rowNode.FindChild("HideIcon", true, false) != null)
                        return;

                    string? modId = GetModId(rowNode);
                    if (string.IsNullOrEmpty(modId))
                        return;

                    var icon = new HideIconNode();
                    icon.Name = "HideIcon";
                    icon.ZIndex = 24;
                    icon.ConfigureIcon(modId, Config.ModListHiderConfig.Instance.IsHidden(modId));
                    rowNode.AddChildSafely(icon);
                }
                catch (Exception ex)
                {
                    TryAppendDebugFile($"InjectOnce: {ex.Message}\n");
                }
            }

            InjectOnce();
            Callable.From(InjectOnce).CallDeferred();

            var tree = rowNode.GetTree();
            if (tree == null)
                return;

            // 每行只挂一组延时器；但上面 InjectOnce 每次 AddChild 仍会执行。
            if (!_injectTimersRegistered.Add(rowNode.GetInstanceId()))
                return;

            foreach (var delay in new[] { 0.08f, 0.22f, 0.5f })
            {
                var timer = tree.CreateTimer(delay);
                timer.Timeout += InjectOnce;
            }

            var finalTimer = tree.CreateTimer(0.85f);
            finalTimer.Timeout += () =>
            {
                if (!GodotObject.IsInstanceValid(rowNode))
                    return;
                if (rowNode.FindChild("HideIcon", true, false) != null)
                    return;
                DumpRowReflectionOnce(rowNode);
            };
        }

        private static void DumpRowReflectionOnce(Node rowNode)
        {
            try
            {
                var path = Path.Combine(OS.GetUserDataDir() ?? "", "ModListHider_row_debug.txt");
                if (File.Exists(path)) return;

                var sb = new StringBuilder();
                var t = rowNode.GetType();
                sb.AppendLine($"Type: {t.FullName}");
                foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    try
                    {
                        var v = p.GetValue(rowNode);
                        sb.AppendLine($"  prop {p.Name} = {v?.GetType().Name ?? "null"}");
                    }
                    catch
                    {
                        sb.AppendLine($"  prop {p.Name} = <get err>");
                    }
                }
                foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    try
                    {
                        var v = f.GetValue(rowNode);
                        sb.AppendLine($"  field {f.Name} = {v?.GetType().Name ?? "null"}");
                    }
                    catch
                    {
                        sb.AppendLine($"  field {f.Name} = <get err>");
                    }
                }
                File.WriteAllText(path, sb.ToString());
            }
            catch
            {
                /* ignore */
            }
        }

        private static void TryAppendDebugFile(string line)
        {
            try
            {
                var path = Path.Combine(OS.GetUserDataDir() ?? "", "ModListHider_debug.txt");
                File.AppendAllText(path, line);
            }
            catch
            {
                /* ignore */
            }
        }

        private static string? GetModId(object rowInstance)
        {
            try
            {
                object? mod = ExtractModReferenceFromRow(rowInstance);
                if (mod != null)
                {
                    var id = TryGetIdFromMegacritModObject(mod);
                    if (!string.IsNullOrEmpty(id))
                        return id;
                    id = TryGetIdFromModLikeObject(mod);
                    if (!string.IsNullOrEmpty(id))
                        return id;
                    id = ResolveModIdViaModManager(mod);
                    if (!string.IsNullOrEmpty(id))
                        return id;
                }

                if (rowInstance is Node rowNode)
                    return TryMatchModIdFromTitleAndLoadedMods(rowNode);

                return null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 游戏 <see cref="NModMenuRow"/> 的 Mod 为属性；Godot 生成类上 Harmony AccessTools.Property 有时取不到，沿继承链 GetProperty。
        /// </summary>
        private static object? ExtractModReferenceFromRow(object rowInstance)
        {
            var rowType = rowInstance.GetType();
            for (var t = rowType; t != null && t != typeof(object); t = t.BaseType)
            {
                const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var prop = t.GetProperty("Mod", inst);
                if (prop != null && prop.CanRead)
                {
                    try
                    {
                        var v = prop.GetValue(rowInstance);
                        if (v != null)
                            return v;
                    }
                    catch
                    {
                        /* ignore */
                    }
                }

                foreach (var fn in new[] { "<Mod>k__BackingField", "_mod" })
                {
                    var bf = t.GetField(fn, inst);
                    if (bf == null)
                        continue;
                    try
                    {
                        var v = bf.GetValue(rowInstance);
                        if (v != null)
                            return v;
                    }
                    catch
                    {
                        /* ignore */
                    }
                }
            }

            foreach (var f in rowType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (!f.Name.Contains("Mod", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    var v = f.GetValue(rowInstance);
                    if (v == null)
                        continue;
                    var vt = v.GetType();
                    if (vt.Name == "Mod" || (vt.FullName?.EndsWith(".Modding.Mod", StringComparison.Ordinal) ?? false))
                        return v;
                }
                catch
                {
                    /* ignore */
                }
            }

            return null;
        }

        /// <summary>
        /// 与反编译一致：<see cref="MegaCrit.Sts2.Core.Modding.Mod"/> 的 manifest、path 为字段；<see cref="MegaCrit.Sts2.Core.Modding.ModManifest"/> 的 id 为字段。
        /// </summary>
        private static string? TryGetIdFromMegacritModObject(object? mod)
        {
            if (mod == null)
                return null;
            var mt = mod.GetType();
            var full = mt.FullName ?? "";
            if (mt.Name != "Mod" && !full.EndsWith(".Modding.Mod", StringComparison.Ordinal))
                return null;

            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            var manifest = mt.GetField("manifest", inst)?.GetValue(mod);
            if (manifest != null)
            {
                var mft = manifest.GetType();
                foreach (var idFieldName in new[] { "id", "Id" })
                {
                    var mf = mft.GetField(idFieldName, inst);
                    if (mf != null)
                    {
                        var id = CoerceToIdString(mf.GetValue(manifest));
                        if (!string.IsNullOrEmpty(id))
                            return id;
                    }

                    var mp = mft.GetProperty(idFieldName, inst);
                    if (mp != null)
                    {
                        var id = CoerceToIdString(mp.GetValue(manifest));
                        if (!string.IsNullOrEmpty(id))
                            return id;
                    }
                }
            }

            var pathObj = mt.GetField("path", inst)?.GetValue(mod);
            var pathStr = pathObj as string ?? CoerceToIdString(pathObj);
            if (!string.IsNullOrEmpty(pathStr))
            {
                var leaf = Path.GetFileName(pathStr.TrimEnd('/', '\\'));
                if (IsPlausibleModIdToken(leaf))
                    return leaf;
            }

            return null;
        }

        private static string? ResolveModIdViaModManager(object rowMod)
        {
            try
            {
                var mgrType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Modding.ModManager")
                    ?? AccessTools.TypeByName("ModManager");
                if (mgrType == null)
                    return null;

                foreach (var propName in new[] { "LoadedMods", "AllMods" })
                {
                    var prop = mgrType.GetProperty(propName, BindingFlags.Public | BindingFlags.Static);
                    var coll = prop?.GetValue(null) as IEnumerable;
                    if (coll == null)
                        continue;
                    foreach (var item in coll)
                    {
                        if (item == null)
                            continue;
                        if (ReferenceEquals(item, rowMod))
                            return TryGetIdFromMegacritModObject(item) ?? TryGetIdFromModLikeObject(item);
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            return null;
        }

        private static string? TryMatchModIdFromTitleAndLoadedMods(Node row)
        {
            try
            {
                var titleNode = row.FindChild("Title", true, false);
                if (titleNode == null)
                    return null;

                var plain = GetTitlePlainText(titleNode);
                if (string.IsNullOrWhiteSpace(plain))
                    return null;
                var firstLine = plain.Split('\n')[0].Trim();
                if (string.IsNullOrEmpty(firstLine))
                    return null;

                var mgrType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Modding.ModManager")
                    ?? AccessTools.TypeByName("ModManager");
                if (mgrType == null)
                    return null;

                foreach (var propName in new[] { "LoadedMods", "AllMods" })
                {
                    var prop = mgrType.GetProperty(propName, BindingFlags.Public | BindingFlags.Static);
                    var coll = prop?.GetValue(null) as IEnumerable;
                    if (coll == null)
                        continue;
                    foreach (var item in coll)
                    {
                        if (item == null)
                            continue;
                        var disp = GetMegacritModDisplayName(item);
                        if (!string.IsNullOrEmpty(disp) &&
                            string.Equals(disp.Trim(), firstLine, StringComparison.OrdinalIgnoreCase))
                            return TryGetIdFromMegacritModObject(item) ?? TryGetIdFromModLikeObject(item);
                    }
                }
            }
            catch
            {
                /* ignore */
            }

            return null;
        }

        private static string? GetMegacritModDisplayName(object mod)
        {
            try
            {
                var mt = mod.GetType();
                const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var manifest = mt.GetField("manifest", inst)?.GetValue(mod);
                if (manifest == null)
                    return null;
                var mft = manifest.GetType();
                var nameObj = mft.GetField("name", inst)?.GetValue(manifest);
                return nameObj as string ?? CoerceToIdString(nameObj);
            }
            catch
            {
                return null;
            }
        }

        private static string GetTitlePlainText(Node titleNode)
        {
            try
            {
                var m = titleNode.GetType().GetMethod("GetParsedText", Type.EmptyTypes);
                if (m != null)
                {
                    var r = m.Invoke(titleNode, null) as string;
                    if (!string.IsNullOrEmpty(r))
                        return r;
                }
            }
            catch
            {
                /* ignore */
            }

            try
            {
                var p = titleNode.GetType().GetProperty("Text",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return p?.GetValue(titleNode) as string ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static bool IsPlausibleModIdToken(string? s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;
            s = s.Trim();
            if (s.Length < 2 || s.Length > 96)
                return false;
            var allDigit = true;
            foreach (var c in s)
            {
                if (!char.IsDigit(c))
                {
                    allDigit = false;
                    break;
                }
            }

            if (allDigit)
                return false;
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.')
                    continue;
                return false;
            }

            return true;
        }

        /// <summary>
        /// 游戏内 Mod / ModManifest 上 id 常为 Godot StringName，不能 as string；属性名也可能是 PckName 等。
        /// </summary>
        private static string? TryGetIdFromModLikeObject(object? mod)
        {
            if (mod == null) return null;
            var modType = mod.GetType();

            foreach (var key in new[] { "id", "Id", "ModId", "PckName", "pck_name", "FolderName", "folder_name" })
            {
                var id = ReadStringMember(mod, modType, key);
                if (!string.IsNullOrEmpty(id)) return id;
            }

            var manifest = TryGetManifestObject(mod, modType);
            if (manifest != null)
            {
                var mType = manifest.GetType();
                foreach (var key in new[] { "id", "Id", "ModId", "PckName", "pck_name", "FolderName", "folder_name" })
                {
                    var id = ReadStringMember(manifest, mType, key);
                    if (!string.IsNullOrEmpty(id)) return id;
                }

                var scanned = ScanIdLikeStringMembers(manifest, mType);
                if (!string.IsNullOrEmpty(scanned)) return scanned;
            }

            return ScanIdLikeStringMembers(mod, modType);
        }

        private static object? TryGetManifestObject(object mod, Type modType)
        {
            foreach (var name in new[] { "manifest", "Manifest", "ModManifest", "modManifest", "_manifest" })
            {
                var p = FindPropertyIgnoreCase(modType, name);
                if (p != null)
                {
                    try
                    {
                        var v = p.GetValue(mod);
                        if (v != null) return v;
                    }
                    catch
                    {
                        /* ignore */
                    }
                }

                var f = FindFieldIgnoreCase(modType, name);
                if (f != null)
                {
                    try
                    {
                        var v = f.GetValue(mod);
                        if (v != null) return v;
                    }
                    catch
                    {
                        /* ignore */
                    }
                }
            }

            foreach (var p in modType.GetProperties(BindingFlags.Instance | BindingFlags.Public
                     | BindingFlags.NonPublic))
            {
                if (!p.Name.Contains("Manifest", StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    var v = p.GetValue(mod);
                    if (v != null) return v;
                }
                catch
                {
                    /* ignore */
                }
            }

            return null;
        }

        private static string? ReadStringMember(object target, Type t, string memberName)
        {
            var p = FindPropertyIgnoreCase(t, memberName);
            if (p != null)
            {
                try
                {
                    return CoerceToIdString(p.GetValue(target));
                }
                catch
                {
                    /* ignore */
                }
            }

            var f = FindFieldIgnoreCase(t, memberName);
            if (f != null)
            {
                try
                {
                    return CoerceToIdString(f.GetValue(target));
                }
                catch
                {
                    /* ignore */
                }
            }

            return null;
        }

        private static string? ScanIdLikeStringMembers(object target, Type t)
        {
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public
                     | BindingFlags.NonPublic))
            {
                if (!p.CanRead || p.GetIndexParameters().Length != 0) continue;
                var n = p.Name;
                if (!NameLooksLikeModIdKey(n)) continue;
                try
                {
                    var s = CoerceToIdString(p.GetValue(target));
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                catch
                {
                    /* ignore */
                }
            }

            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public
                     | BindingFlags.NonPublic))
            {
                if (!NameLooksLikeModIdKey(f.Name)) continue;
                try
                {
                    var s = CoerceToIdString(f.GetValue(target));
                    if (!string.IsNullOrEmpty(s)) return s;
                }
                catch
                {
                    /* ignore */
                }
            }

            return null;
        }

        private static bool NameLooksLikeModIdKey(string name)
        {
            return name.Contains("id", StringComparison.OrdinalIgnoreCase)
                || name.Contains("pck", StringComparison.OrdinalIgnoreCase)
                || name.Contains("folder", StringComparison.OrdinalIgnoreCase);
        }

        private static PropertyInfo? FindPropertyIgnoreCase(Type t, string name)
        {
            foreach (var p in t.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        private static FieldInfo? FindFieldIgnoreCase(Type t, string name)
        {
            foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return f;
            }
            return null;
        }

        private static string? CoerceToIdString(object? val)
        {
            if (val == null) return null;
            if (val is string s) return string.IsNullOrWhiteSpace(s) ? null : s;
            // GodotSharp: StringName
            if (val.GetType().Name == "StringName")
            {
                var t = val.ToString();
                return string.IsNullOrWhiteSpace(t) ? null : t;
            }
            return null;
        }
    }
}
