namespace AlicizaX.UI.UXNavigation
{
    /// <summary>
    /// 程序化恢复焦点时抑制 UI 选中反馈音（如 NavigationScope 默认选中）。
    /// </summary>
    public static class UXSelectionAudio
    {
        private static int _suppressCount;

        public static bool IsSuppressed => _suppressCount > 0;

        public static void BeginSuppress() => _suppressCount++;

        public static void EndSuppress()
        {
            if (_suppressCount > 0)
            {
                _suppressCount--;
            }
        }
    }
}
