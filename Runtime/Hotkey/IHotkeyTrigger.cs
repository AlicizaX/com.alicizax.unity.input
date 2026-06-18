#if INPUTSYSTEM_SUPPORT
using AlicizaX.UI.Runtime;
using UnityEngine.InputSystem;

namespace UnityEngine.UI
{
    public interface IHotkeyTrigger
    {
        InputActionReference HotkeyAction { get; }
        EHotkeyPressType HotkeyPressType { get; }
        EHotkeyActionOwnershipMode HotkeyActionOwnershipMode { get; }
        bool HotkeyConsumesInput { get; }
        UIHolderObjectBase HotkeyHolder { get; }
        void HotkeyActionTrigger();
    }
}

#endif
