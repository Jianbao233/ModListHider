using Godot;

namespace ModListHider.Core
{
    internal static class DebugLog
    {
        public static bool Enabled => Config.ModListHiderConfig.Instance.DebugMode;

        public static void Info(string message)
        {
            if (!Enabled) return;
            GD.Print($"[ModListHider][DEBUG] {message}");
        }

        public static void Error(string message)
        {
            if (!Enabled) return;
            GD.PrintErr($"[ModListHider][DEBUG] {message}");
        }
    }
}
