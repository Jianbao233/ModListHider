// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
//  ModListHider Stubs — 编译用类型占位符
//  提供 StS2 游戏专有类型的 stub，用于编译。
// ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

using System;
using Godot;

// ── 游戏专有命名空间 ───────────────────────────────────────────────

namespace MegaCrit.Sts2.Core.Modding
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class ModInitializerAttribute : Attribute
    {
        public ModInitializerAttribute(string methodName) { }
    }

    public static class ModManager
    {
        public static event Action? OnModDetected;
        public static event Action? Initialize;
        public static object? AllMods;
    }

    public class Mod { public object? manifest; public bool wasLoaded; }
}

namespace MegaCrit.Sts2.Core.Logging
{
    public static class Log
    {
        public static void Info(string msg) { GD.Print($"[ModListHider] {msg}"); }
        public static void Warn(string msg) { GD.Print($"[ModListHider] WARN: {msg}"); }
        public static void Error(string msg) { GD.PrintErr($"[ModListHider] ERROR: {msg}"); }
    }
}

namespace MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen
{
    using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

    // partial 必须（Godot C# 要求）
    public partial class NModMenuRow : Godot.Control
    {
        public object? Mod;
        protected virtual void OnRelease() { }
        public void SetSelected(bool v) { }
        public static NModMenuRow Create(object? screen, object? mod) => null!;
    }

    // partial 必须（Godot C# 要求）
    public partial class NModdingScreen : Godot.Control
    {
        public void OnRowSelected(NModMenuRow row) { }
        public void OnModEnabledOrDisabled() { }
        public static NModdingScreen Create() => null!;
    }

    // partial 必须（Godot C# 要求）
    public partial class NClickableControl : Godot.Control
    {
        protected virtual void OnRelease() { }
        protected virtual void OnFocus() { }
        protected virtual void OnUnfocus() { }
        protected virtual void OnPress() { }
        protected virtual void OnDisable() { }
        protected virtual void OnEnable() { }
    }

    public partial class NModInfoContainer : Godot.Control { }
}

namespace MegaCrit.Sts2.Core.Nodes.GodotExtensions
{
    public static class Extensions_
    {
        public static T AddChildSafely<T>(this Godot.Node parent, T child) where T : Godot.Node
        {
            parent.AddChild(child);
            return child;
        }
        public static void QueueFreeSafely(this Godot.Node? node) { node?.QueueFree(); }
    }
}

namespace MegaCrit.Sts2.Core.Nodes.CommonUi
{
    public partial class NTickbox : Godot.Control
    {
        public bool IsTicked;
        protected virtual void OnTick() { }
        protected virtual void OnUntick() { }
        protected void ConnectSignals() { }
        public static class SignalName_
        {
            public static StringName Toggled => "Toggled";
        }
    }
}

namespace MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby
{
    public static class InitialGameInfoMessage
    {
        public static object Basic() => new object();
    }
}
