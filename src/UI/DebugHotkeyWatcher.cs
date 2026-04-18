using Godot;

namespace ModListHider.UI
{
    /// <summary>
    /// Runtime debug switch.
    /// Ctrl+Shift+F8 toggles debug_mode in config and persists it.
    /// </summary>
    public partial class DebugHotkeyWatcher : Node
    {
        public override void _UnhandledInput(InputEvent @event)
        {
            if (@event is not InputEventKey key) return;
            if (!key.Pressed || key.Echo) return;
            if (key.Keycode != Key.F8) return;
            if (!key.CtrlPressed || !key.ShiftPressed) return;

            var cfg = Config.ModListHiderConfig.Instance;
            cfg.SetDebugMode(!cfg.DebugMode);
            GD.Print($"[ModListHider] Debug hotkey toggled. DebugMode={cfg.DebugMode}");
            GetViewport().SetInputAsHandled();
        }
    }
}
