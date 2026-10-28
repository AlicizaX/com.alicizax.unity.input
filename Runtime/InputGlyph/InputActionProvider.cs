#if INPUTSYSTEM_SUPPORT
using System.Collections.Generic;
using AlicizaX;
using AlicizaX.UI.UXNavigation;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

[AddComponentMenu("Input/Input Action Provider")]
[DefaultExecutionOrder(-10000)]
public sealed class InputActionProvider : MonoBehaviour
{
    private static readonly Dictionary<string, InputAction> ActionLookup =
        new Dictionary<string, InputAction>(64, System.StringComparer.Ordinal);

    private static InputActionProvider _owner;
    private static InputActionAsset _actions;

    [Tooltip("InputActionAsset to read and enable at runtime.")] [SerializeField]
    private InputActionAsset actions;

    [SerializeField] private InputGlyphDatabase glyphDatabase;

    private void Awake()
    {
        Initialize(this, actions, glyphDatabase);
    }

    private void OnDestroy()
    {
        Shutdown(this);
    }

    /// <summary>
    /// 当前 Provider 使用的输入动作资源。
    /// </summary>
    internal static InputActionAsset Actions => _actions;

    internal static void ResetForPlayMode()
    {
        ActionLookup.Clear();
        _actions = null;
        _owner = null;
    }

    /// <summary>
    /// 尝试按完整路径解析动作；未初始化或不存在时返回 false。
    /// </summary>
    public static bool TryResolveAction(string actionName, out InputAction action)
    {
        if (!string.IsNullOrWhiteSpace(actionName) && ActionLookup.TryGetValue(actionName, out action))
        {
            return action != null;
        }

        action = null;
        return false;
    }

    /// <summary>
    /// 按完整路径解析动作；不存在时返回 null，不产生运行时日志（路径合法性由编辑器生成器保证）。
    /// </summary>
    public static InputAction ResolveAction(string actionName)
    {
        return TryResolveAction(actionName, out InputAction action) ? action : null;
    }

    /// <summary>
    /// 按名称查找动作表；输入资源未初始化时返回 null。
    /// </summary>
    public static InputActionMap FindActionMap(string mapName, bool throwIfNotFound = false)
    {
        return _actions != null ? _actions.FindActionMap(mapName, throwIfNotFound) : null;
    }

    private static void Initialize(InputActionProvider owner, InputActionAsset actions, InputGlyphDatabase glyphDatabase)
    {
        if (_owner != null && _owner != owner)
        {
            Shutdown(_owner);
        }

        _owner = owner;
        UXInput.Watch.Initialize();
        UXInput.Glyph.SetDatabase(glyphDatabase);
        InitializeActions(actions);
        UXInput.Rebind.Initialize(actions);

        if (actions != null)
        {
            actions.Enable();
        }

        UXNavigationSystem.Initialize();
    }

    private static void Shutdown(InputActionProvider owner)
    {
        if (_owner != owner)
        {
            return;
        }

        UXNavigationSystem.Shutdown();
        UXHotkeySystem.Shutdown();
        UXInput.Rebind.Shutdown();
        ClearActions(true);
        UXInput.Glyph.SetDatabase(null);
        UXInput.Watch.Shutdown();
        _owner = null;
    }

    private static void InitializeActions(InputActionAsset actions)
    {
        ClearActions(false);
        _actions = actions;
        if (_actions == null)
        {
            return;
        }

        BuildActionLookup();
    }

    private static void ClearActions(bool disableActions)
    {
        if (disableActions && _actions != null)
        {
            _actions.Disable();
        }

        ActionLookup.Clear();
        _actions = null;
    }

    private static void BuildActionLookup()
    {
        ActionLookup.Clear();
        if (_actions == null)
        {
            return;
        }

        for (int mapIndex = 0; mapIndex < _actions.actionMaps.Count; mapIndex++)
        {
            InputActionMap map = _actions.actionMaps[mapIndex];
            for (int actionIndex = 0; actionIndex < map.actions.Count; actionIndex++)
            {
                InputAction action = map.actions[actionIndex];
                string fullName = map.name + "/" + action.name;
                ActionLookup[fullName] = action;
            }
        }
    }
}
#endif
