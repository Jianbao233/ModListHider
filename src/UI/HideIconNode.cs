using System;
using System.Reflection;
using Godot;

namespace ModListHider.UI
{
    /// <summary>
    /// 矢量绘制眼睛图标：不读磁盘 PNG，避免棋盘格贴图与每行重复解码/抠像造成的卡顿。
    /// </summary>
    public partial class HideIconNode : Control
    {
        public string ModId { get; set; } = "";

        public bool IsHiddenState { get; private set; }

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
            FocusMode = FocusModeEnum.None;
            CustomMinimumSize = new Vector2(48, 48);
            UpdateVisuals();
            Callable.From(RepositionAfterParentLayout).CallDeferred();
        }

        public override void _Draw()
        {
            var sz = Size;
            if (sz.X < 2f || sz.Y < 2f)
                return;

            var col = IsHiddenState
                ? new Color(0.55f, 0.55f, 0.55f, 1.0f)
                : Colors.White;

            if (IsHiddenState)
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

        public void ConfigureIcon(string modId, bool hidden)
        {
            ModId = modId;
            IsHiddenState = hidden;
            ApplyAnchorsAndOffsets();
            UpdateVisuals();
        }

        private void ApplyAnchorsAndOffsets()
        {
            AnchorLeft = 1.0f;
            AnchorRight = 1.0f;
            AnchorTop = 0.0f;
            AnchorBottom = 0.0f;
            OffsetLeft = -188;
            OffsetRight = -140;

            var row = GetParent() as Control;
            var parentH = row != null ? Mathf.Max(row.CustomMinimumSize.Y, row.Size.Y) : 0f;
            if (parentH < 1f)
                parentH = 72f;
            var y = Mathf.Max(0f, (parentH - 48f) * 0.5f);
            OffsetTop = y;
            OffsetBottom = y + 48f;
        }

        private void RepositionAfterParentLayout()
        {
            if (!IsInsideTree())
                return;
            ApplyAnchorsAndOffsets();
            QueueRedraw();
        }

        public void SetHidden(bool hidden)
        {
            IsHiddenState = hidden;
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
                SetHidden(!IsHiddenState);
                Config.ModListHiderConfig.Instance.ToggleHidden(ModId);
                Config.ModListHiderConfig.Instance.Save();
                PlayClickSound();
                GD.Print($"[ModListHider] Toggled '{ModId}' hidden={IsHiddenState}");
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
                    IsHiddenState ? "event:/sfx/ui/clicks/ui_checkbox_off"
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
