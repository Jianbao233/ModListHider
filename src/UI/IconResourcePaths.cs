namespace ModListHider.UI
{
    /// <summary>
    /// 图标路径约定：磁盘文件相对「模组 DLL 所在目录」；res:// 相对 Godot 资源根（PCK）。
    /// </summary>
    public static class IconResourcePaths
    {
        public const string EyeOpenRelativeToModDir = "ModListHider_eye_open.png";
        public const string EyeClosedRelativeToModDir = "ModListHider_eye_closed.png";

        public const string EyeOpenRes = "res://ModListHider/assets/icons/eye_open.png";
        public const string EyeClosedRes = "res://ModListHider/assets/icons/eye_closed.png";
    }
}
