#if INPUTSYSTEM_SUPPORT
using System.Collections.Generic;
using AlicizaX;
using AlicizaX.UI.UXNavigation;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.InputSystem;

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

    public static bool TryResolveAction(string actionName, out InputAction action)
    {
        if (!string.IsNullOrWhiteSpace(actionName) && ActionLookup.TryGetValue(actionName, out action))
        {
            return action != null;
        }

        action = null;
        return false;
    }

    public static InputAction ResolveAction(string actionName)
    {
        if (TryResolveAction(actionName, out InputAction action))
        {
            return action;
        }

        Log.Error("[InputActionProvider] Could not find action '{0}'. Use full action path 'MapName/ActionName'.", actionName);
        return null;
    }

    public static InputActionMap FindActionMap(string mapName, bool throwIfNotFound = false)
    {
        return _actions.FindActionMap(mapName, throwIfNotFound);
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
                string fullName = ZString.Concat(map.name, "/", action.name);
                ActionLookup[fullName] = action;
            }
        }
    }
}
#endif
