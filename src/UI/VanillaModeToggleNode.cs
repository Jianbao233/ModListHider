using System;
using System.Reflection;
using Godot;

namespace ModListHider.UI
{
    /// <summary>
    /// A global Vanilla Mode toggle button injected into the modding screen header.
    ///
    /// Vanilla Mode ON  (closed eye): Pretend no mods loaded at all.
    ///                                  Game sends null mod list to MP peers.
    ///                                  -> Vanilla players can join, you see your skins locally.
    /// Vanilla Mode OFF (open eye)  : Per-mod eye icons control which mods are hidden.
    ///                                  Existing behavior unchanged.
    ///
    /// The button is a simple 48x48 vector-drawn eye matching the style of HideIconNode.
    /// It appears at the top-left corner of the modding screen.
    /// Cyan = Vanilla OFF (normal), Red = Vanilla ON (hide all mods from MP).
    /// </summary>
    public partial class VanillaModeToggleNode : Control
    {
        /// <summary>Current Vanilla Mode state (true = ON = all mods hidden from MP).</summary>
        public bool IsVanillaOn { get; private set; }

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.None;
            CustomMinimumSize = new Vector2(48, 48);
            UpdateTooltip();
            UpdateVisuals();
        }

        private void UpdateTooltip()
        {
            // Game TranslationServer does not load mod JSON; use locale-based copy.
            TooltipText = IsVanillaOn ? VanillaTooltipOn() : VanillaTooltipOff();
        }

        private static bool PreferChinese()
        {
            var loc = TranslationServer.GetLocale();
            if (!string.IsNullOrEmpty(loc) && loc.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                return true;
            var os = OS.GetLocale();
            return !string.IsNullOrEmpty(os) && os.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        }

        private static string VanillaTooltipOn()
        {
            if (PreferChinese())
            {
                return "原版模式：已开启\n"
                    + "联机握手时不会向对方报告任何 Mod，对方会把你当作原版客户端。\n"
                    + "本地 Mod（皮肤、界面等）仍会照常生效。\n"
                    + "点击图标关闭原版模式；关闭后由各 Mod 行右侧小眼睛决定是否在对方列表中隐藏。";
            }
            return "Vanilla mode: ON\n"
                + "Multiplayer handshake sends no mods; peers see you as unmodded.\n"
                + "Mods still run locally (skins, UI, etc.).\n"
                + "Click to turn OFF; then each mod’s eye icon controls visibility in others’ mod lists.";
        }

        private static string VanillaTooltipOff()
        {
            if (PreferChinese())
            {
                return "原版模式：已关闭\n"
                    + "联机时是否向对方显示某个 Mod，由各 Mod 行右侧小眼睛控制（睁开=显示，闭上=隐藏）。\n"
                    + "若要与未装 Mod 的玩家联机或加入其房间，请点击图标开启原版模式。";
            }
            return "Vanilla mode: OFF\n"
                + "Each mod’s eye icon (right of the row) controls whether that mod appears in others’ lists (open = visible, closed = hidden).\n"
                + "Turn ON vanilla mode (this icon) to join unmodded players or rooms.";
        }

        public override void _Draw()
        {
            var sz = Size;
            if (sz.X < 2f || sz.Y < 2f)
                return;

            // Cyan = Vanilla OFF (normal), Red = Vanilla ON (active/hiding)
            var col = IsVanillaOn
                ? new Color(1.0f, 0.3f, 0.3f, 1.0f)   // Red = Vanilla ON (active)
                : new Color(0.3f, 0.85f, 1.0f, 1.0f); // Cyan = Vanilla OFF (normal)

            if (IsVanillaOn)
                DrawClosedEye(col, sz);
            else
                DrawOpenEye(col, sz);
        }

        private void DrawOpenEye(Color col, Vector2 sz)
        {
            float cx = sz.X * 0.5f;
            float cy = sz.Y * 0.5f;
            float rx = sz.X * 0.36f;
            float ry = sz.Y * 0.20f;
            DrawEllipseOutline(new Vector2(cx, cy), rx, ry, col, 2.2f);
            float pr = Mathf.Min(sz.X, sz.Y) * 0.09f;
            DrawCircle(new Vector2(cx + rx * 0.2f, cy), pr, col);
        }

        private void DrawClosedEye(Color col, Vector2 sz)
        {
            float cx = sz.X * 0.5f;
            float cy = sz.Y * 0.5f;
            float r = sz.X * 0.34f;
            DrawArc(new Vector2(cx, cy), r, 0.2f * Mathf.Pi, 0.8f * Mathf.Pi, 14, col, 2.2f, true);
        }

        private void DrawEllipseOutline(Vector2 center, float rx, float ry, Color col, float width)
        {
            const int seg = 22;
            var pts = new Vector2[seg + 1];
            for (int i = 0; i <= seg; i++)
            {
                float t = i / (float)seg * Mathf.Tau;
                pts[i] = center + new Vector2(Mathf.Cos(t) * rx, Mathf.Sin(t) * ry);
            }
            DrawPolyline(pts, col, width, true);
        }

        /// <summary>
        /// Configure the button state and apply layout anchors.
        /// </summary>
        /// <param name="vanillaMode">Current Vanilla Mode setting.</param>
        public void Configure(bool vanillaMode)
        {
            IsVanillaOn = vanillaMode;
            UpdateTooltip();
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            QueueRedraw();
        }

        public override void _GuiInput(InputEvent @event)
        {
            if (@event is InputEventMouseButton mb
                && mb.Pressed
                && mb.ButtonIndex == MouseButton.Left)
            {
                IsVanillaOn = !IsVanillaOn;
                Config.ModListHiderConfig.Instance.SetVanillaMode(IsVanillaOn);
                UpdateTooltip();
                UpdateVisuals();
                PlayClickSound();
                GD.Print($"[ModListHider] VanillaMode toggled: {IsVanillaOn}");
                AcceptEvent();
            }
        }

        private void PlayClickSound()
        {
            try
            {
                var sfxType = Type.GetType("MegaCrit.Sts2.Core.Commands.SfxCmd");
                var playMethod = sfxType?.GetMethod("Play",
                    BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(float) }, null);
                playMethod?.Invoke(null, new object[]
                {
                    IsVanillaOn ? "event:/sfx/ui/clicks/ui_checkbox_off"
                        : "event:/sfx/ui/clicks/ui_checkbox_on",
                    1f
                });
            }
            catch
            {
                // Audio is optional
            }
        }
    }
}
