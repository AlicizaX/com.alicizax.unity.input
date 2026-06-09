#if INPUTSYSTEM_SUPPORT
using AlicizaX;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.InputSystem;

[AddComponentMenu("Input/Input Action Provider")]
public sealed class InputActionProvider : MonoBehaviour
{
    [Tooltip("InputActionAsset to read and enable at runtime.")] [SerializeField]
    private InputActionAsset actions;

    public InputActionAsset Actions
    {
        get { return actions; }
    }

    private void Awake()
    {
        InputActionResolver.Actions = actions;
        InputActionResolver.Initialize();
    }

    private void OnDestroy()
    {
        InputActionResolver.Reset();
    }
}
#endif
