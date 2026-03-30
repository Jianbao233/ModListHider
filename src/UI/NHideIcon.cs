using System;
using Godot;

namespace ModListHider.UI
{
    /// <summary>
    /// Stub for NHideIcon. The actual implementation is HideIconNode.
    /// This provides compile-time compatibility only.
    /// </summary>
    public partial class NHideIcon : Godot.Node
    {
        /// <summary>The mod ID this icon controls.</summary>
        public string ModId { get; set; } = "";

        /// <summary>Whether this mod is currently hidden from multiplayer.</summary>
        public bool IsHidden { get; set; }

        /// <summary>The "open eye" texture resource path.</summary>
        public const string EyeOpenPath = IconResourcePaths.EyeOpenRes;

        /// <summary>The "closed eye" texture resource path.</summary>
        public const string EyeClosedPath = IconResourcePaths.EyeClosedRes;
    }
}
