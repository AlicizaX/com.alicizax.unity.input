#if INPUTSYSTEM_SUPPORT
using AlicizaX;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[AddComponentMenu("Input/Input Service")]
[DefaultExecutionOrder(-30000)]
public sealed class InputService : MonoServiceBehaviour
{

    [SerializeField] private InputActionAsset actions;
    [SerializeField] private InputGlyphDatabase glyphDatabase;

    /// <summary>
    /// 由代码创建输入层。已存在实例时直接返回，保证全局只有一份。
    /// </summary>
    public static void Initialize(InputActionAsset actions, InputGlyphDatabase glyphDatabase)
    {
        if (AppServices.TryGet(out InputService _))
            return;


        var root = new GameObject(nameof(InputService));
        var service = root.AddComponent<InputService>();
        service.actions = actions;
        service.glyphDatabase = glyphDatabase;
        root.SetActive(true);
    }

    protected override void Awake()
    {
        base.Awake();
        if (actions == null)
        {
            Debug.LogError("InputService requires an InputActionAsset. Input stays disabled.", this);
            return;
        }

        UXInput.Initialize(actions, glyphDatabase);
        UXHotkeySystem.Initialize(actions);
        DontDestroyOnLoad(gameObject);
    }

    protected override void OnDestroy()
    {
        UXHotkeySystem.Shutdown();
        UXInput.Shutdown();
        base.OnDestroy();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetForPlayMode()
    {
        UXHotkeySystem.Shutdown();
        UXInput.Shutdown();
    }
}
#endif
