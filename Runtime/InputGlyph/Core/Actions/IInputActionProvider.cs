#if INPUTSYSTEM_SUPPORT
using AlicizaX;
using UnityEngine.InputSystem;

public interface IInputActionProvider : IService
{
    InputActionAsset Actions { get; }
    bool TryGetAction(string actionName, out InputAction action);
    bool IsActionNameAmbiguous(string actionName);
}

#endif
