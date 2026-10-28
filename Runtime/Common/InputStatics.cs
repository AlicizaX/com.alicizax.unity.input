#if INPUTSYSTEM_SUPPORT
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 关闭 Domain Reload 进入 Play Mode 时重置输入包的静态状态，避免上一轮运行的残留。
/// </summary>
internal static class InputStatics
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        UXInput.Reader.ResetForPlayMode();
        UXInput.Rebind.ResetForPlayMode();
        UXInput.Glyph.ResetForPlayMode();
        UXInput.Watch.ResetForPlayMode();
        InputActionProvider.ResetForPlayMode();
        UXHotkeySystem.ResetForPlayMode();
    }
}
#endif
