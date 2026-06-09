#if INPUTSYSTEM_SUPPORT
using AlicizaX;
using Cysharp.Text;
using UnityEngine.InputSystem;

public static class InputActionResolver
{
    private static readonly InputGlyphStringMap<InputAction> _actionLookup = new InputGlyphStringMap<InputAction>(64);
    public static InputActionAsset Actions;

    public static void Initialize()
    {
        if (Actions == null) return;
        BuildActionLookup();
        InputBindingManager.Initialize(Actions);
        Actions?.Enable();
    }

    public static void Reset()
    {
        if (Actions == null) return;
        InputBindingManager.Shutdown(Actions);
        _actionLookup.Clear();
        Actions = null;
    }

    public static bool TryGetAction(string actionName, out InputAction action)
    {
        if (!string.IsNullOrWhiteSpace(actionName) && _actionLookup.TryGetValue(actionName, out action))
        {
            return action != null;
        }

        action = null;
        return false;
    }

    private static void BuildActionLookup()
    {
        _actionLookup.Clear();

        if (Actions == null)
        {
            Log.Error("[InputActionProvider] InputActionAsset not assigned.");
            return;
        }

        for (int mapIndex = 0; mapIndex < Actions.actionMaps.Count; mapIndex++)
        {
            InputActionMap map = Actions.actionMaps[mapIndex];
            for (int actionIndex = 0; actionIndex < map.actions.Count; actionIndex++)
            {
                InputAction action = map.actions[actionIndex];
                RegisterActionLookup(map.name, action.name, action);
            }
        }
    }

    private static void RegisterActionLookup(string mapName, string actionName, InputAction action)
    {
        string fullName = ZString.Concat(mapName, "/", actionName);
        _actionLookup.Set(fullName, action);
    }

    public static InputAction Action(string actionName)
    {
        if (TryGetAction(actionName, out InputAction action))
        {
            return action;
        }

        Log.Error("[InputActionResolver] Could not find action '{0}'. Use full action path 'MapName/ActionName'.", actionName);
        return null;
    }
}

#endif
