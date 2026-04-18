using System;
using System.IO;
using System.Reflection;
using Godot;
using ModListHider.Core;

namespace ModListHider.UI
{
    /// <summary>
    /// Per-row hide icon that anchors to the folder button and stays within ModsBorder.
    /// </summary>
    public partial class HideIconNode : Control
    {
        private const float LayoutRefreshInterval = 0.20f;
        private const float MinIconSize = 24f;
        private const float MaxIconSize = 48f;

        private static bool _texturesLoaded;
        private static Texture2D? _openIconTex;
        private static Texture2D? _closedIconTex;

        private float _layoutTimer;

        public string ModId { get; set; } = "";
        public bool IsHiddenState { get; private set; }

        public override void _Ready()
        {
            MouseFilter = MouseFilterEnum.Stop;
            MouseDefaultCursorShape = CursorShape.PointingHand;
            FocusMode = FocusModeEnum.None;
            CustomMinimumSize = new Vector2(48, 48);
            SetProcess(true);
            EnsureTexturesLoaded();
            UpdateVisuals();
            Callable.From(RepositionAfterParentLayout).CallDeferred();
        }

        public override void _Process(double delta)
        {
            _layoutTimer += (float)delta;
            if (_layoutTimer < LayoutRefreshInterval)
                return;

            _layoutTimer = 0f;
            ApplyAnchorsAndOffsets();
        }

        public override void _Draw()
        {
            var sz = Size;
            if (sz.X < 2f || sz.Y < 2f)
                return;

            var tex = IsHiddenState ? _closedIconTex : _openIconTex;
            if (tex != null)
            {
                DrawTextureRect(tex, new Rect2(Vector2.Zero, sz), false);
                return;
            }

            // Fallback to vector eye if PNG was not packaged.
            var col = IsHiddenState
                ? new Color(0.62f, 0.62f, 0.62f, 1.0f)
                : Colors.White;

            if (IsHiddenState)
                DrawClosedEye(col, sz);
            else
                DrawOpenEye(col, sz);
        }

        public void ConfigureIcon(string modId, bool hidden)
        {
            ModId = modId;
            IsHiddenState = hidden;
            ApplyAnchorsAndOffsets();
            UpdateVisuals();
        }

        public void RefreshLayout()
        {
            ApplyAnchorsAndOffsets();
            QueueRedraw();
        }

        public void SetHidden(bool hidden)
        {
            IsHiddenState = hidden;
            UpdateVisuals();
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

        private static void EnsureTexturesLoaded()
        {
            if (_texturesLoaded)
                return;

            _texturesLoaded = true;
            _openIconTex = TryLoadTexture(
                IconResourcePaths.EyeOpenSmallRes,
                IconResourcePaths.EyeOpenSmallRelativeToModDir);
            _closedIconTex = TryLoadTexture(
                IconResourcePaths.EyeClosedSmallRes,
                IconResourcePaths.EyeClosedSmallRelativeToModDir);
        }

        private static Texture2D? TryLoadTexture(string resPath, string relativeToModDir)
        {
            try
            {
                var fromRes = GD.Load<Texture2D>(resPath);
                if (fromRes != null)
                    return fromRes;

                var dllPath = Assembly.GetExecutingAssembly().Location;
                var dllDir = Path.GetDirectoryName(dllPath);
                if (string.IsNullOrEmpty(dllDir))
                    return null;

                var iconPath = Path.Combine(dllDir, relativeToModDir);
                if (!File.Exists(iconPath))
                    return null;

                var img = Image.LoadFromFile(iconPath);
                if (img == null || img.IsEmpty())
                    return null;

                return ImageTexture.CreateFromImage(img);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[ModListHider] Failed to load icon '{resPath}': {ex.Message}");
                return null;
            }
        }

        private void ApplyAnchorsAndOffsets()
        {
            AnchorLeft = 0f;
            AnchorRight = 0f;
            AnchorTop = 0f;
            AnchorBottom = 0f;

            var row = GetParent() as Control;
            if (row == null || !row.Visible || !row.IsVisibleInTree())
            {
                Visible = false;
                return;
            }

            var rowW = Mathf.Max(row.Size.X, row.CustomMinimumSize.X);
            var rowH = Mathf.Max(row.Size.Y, row.CustomMinimumSize.Y);
            if (rowW < 80f || rowH < 18f)
            {
                Visible = false;
                return;
            }

            var tickbox = FindDirectControlChildByName(row, "Tickbox");
            var folder = FindFolderAnchorControl(row, tickbox);
            if (tickbox == null && folder == null)
            {
                Visible = false;
                return;
            }

            var iconSize = Mathf.Clamp(rowH * 0.46f * 2f, MinIconSize, MaxIconSize);
            float x;
            float y;

            if (folder != null && folder.Size.X > 1f && folder.Size.Y > 1f)
            {
                var folderSize = Mathf.Min(folder.Size.X, folder.Size.Y);
                if (folderSize > 8f)
                    iconSize = Mathf.Clamp(folderSize * 0.72f * 2f, MinIconSize, MaxIconSize);

                var gap = Mathf.Clamp(iconSize * 0.12f, 3f, 10f);
                x = folder.Position.X - iconSize - gap;
                y = folder.Position.Y + (folder.Size.Y - iconSize) * 0.5f;

                // Never cross into folder area.
                var folderMaxRight = folder.Position.X - 2f;
                if (x + iconSize > folderMaxRight)
                    x = folderMaxRight - iconSize;
            }
            else
            {
                var tickPos = tickbox!.Position;
                var tickSize = tickbox.Size;
                x = tickPos.X - iconSize - 4f;
                y = tickPos.Y + (tickSize.Y - iconSize) * 0.5f;
            }

            if (tickbox != null && tickbox.Size.X > 1f && tickbox.Size.Y > 1f)
            {
                var tickMaxRight = tickbox.Position.X - 2f;
                if (x + iconSize > tickMaxRight)
                    x = tickMaxRight - iconSize;
            }

            x = Mathf.Clamp(x, 2f, Mathf.Max(2f, rowW - iconSize - 2f));
            y = Mathf.Clamp(y, 0f, Mathf.Max(0f, rowH - iconSize));

            OffsetLeft = x;
            OffsetTop = y;
            OffsetRight = x + iconSize;
            OffsetBottom = y + iconSize;

            var inBorder = IsInsideModsBorder(row, x, y, iconSize);
            Visible = inBorder;

            if (DebugLog.Enabled && IsInsideTree())
            {
                DebugLog.Info(
                    $"HideIcon layout mod={ModId} row={row.Name} rowSize={row.Size} icon=({OffsetLeft},{OffsetTop},{iconSize}) inBorder={inBorder} tick={tickbox?.Name} folder={folder?.Name}");
            }
        }

        private static bool IsInsideModsBorder(Control row, float x, float y, float iconSize)
        {
            try
            {
                var root = row.GetTree()?.Root;
                if (root == null)
                    return true;

                var border = root.FindChild("ModsBorder", true, false) as Control;
                if (border == null || border.Size.X < 12f || border.Size.Y < 12f)
                    return true;

                var borderRect = new Rect2(border.GlobalPosition, border.Size).Grow(-1f);
                var iconRect = new Rect2(
                    row.GlobalPosition + new Vector2(x, y),
                    new Vector2(iconSize, iconSize));

                return borderRect.Encloses(iconRect);
            }
            catch
            {
                return true;
            }
        }

        private static Control? FindDirectControlChildByName(Node parent, string name)
        {
            foreach (var child in parent.GetChildren())
            {
                if (child is Control c &&
                    string.Equals(c.Name.ToString(), name, StringComparison.OrdinalIgnoreCase))
                {
                    return c;
                }
            }

            return null;
        }

        private static Control? FindFolderAnchorControl(Node parent, Control? tickbox)
        {
            Control? best = null;
            var tickLeft = tickbox?.Position.X ?? float.PositiveInfinity;
            foreach (var child in parent.GetChildren())
            {
                if (child is not Control c)
                    continue;

                var name = c.Name.ToString();
                if (name.IndexOf("folder", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;
                if (c.Size.X < 12f || c.Size.Y < 12f)
                    continue;
                if (c.Size.X > 96f || c.Size.Y > 96f)
                    continue;
                if (tickbox != null && c.Position.X >= tickLeft)
                    continue;

                if (best == null || c.Position.X > best.Position.X)
                    best = c;
            }

            if (best != null)
                return best;

            if (tickbox == null)
                return null;

            // Fallback: nearest square-ish control immediately left of Tickbox.
            Control? geoBest = null;
            foreach (var child in parent.GetChildren())
            {
                if (child is not Control c)
                    continue;
                if (ReferenceEquals(c, tickbox))
                    continue;
                if (c.Size.X < 12f || c.Size.Y < 12f)
                    continue;
                if (c.Size.X > 96f || c.Size.Y > 96f)
                    continue;

                var right = c.Position.X + c.Size.X;
                if (right > tickLeft)
                    continue;

                if (geoBest == null || right > geoBest.Position.X + geoBest.Size.X)
                    geoBest = c;
            }

            return geoBest;
        }

        private void RepositionAfterParentLayout()
        {
            if (!IsInsideTree())
                return;

            ApplyAnchorsAndOffsets();
            QueueRedraw();
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

        private void UpdateVisuals()
        {
            QueueRedraw();
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
                // Audio is optional.
            }
        }
    }
}
