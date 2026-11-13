#if INPUTSYSTEM_SUPPORT
using System;

namespace AlicizaX.Input
{
    /// <summary>反馈规则适用的输入设备掩码。数值已序列化进主题资源，不可调整。</summary>
    [Flags]
    public enum InputFeedbackDevice : byte
    {
        None = 0,
        Touch = 1 << 0,
        KeyboardMouse = 1 << 1,
        Gamepad = 1 << 2,
        All = Touch | KeyboardMouse | Gamepad,
    }
}
#endif
