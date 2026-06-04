#if INPUTSYSTEM_SUPPORT
using AlicizaX;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.InputSystem;

[AddComponentMenu("Input/Input Action Provider")]
public sealed class InputActionProvider : MonoBehaviour
{
    [Tooltip("InputActionAsset to read and enable at runtime.")]
    [SerializeField] private InputActionAsset actions;

    private InputActionProviderService service;

    public InputActionAsset Actions
    {
        get
        {
            return actions;
        }
    }

    private void Awake()
    {
        if (AppServices.TryGet(out IInputActionProvider _))
        {
            Log.Warning("[InputActionProvider] Another IInputActionProvider is already registered.");
            enabled = false;
            return;
        }

        service = new InputActionProviderService(actions);
        AppServices.App.Register<IInputActionProvider>(service);
    }

    private void OnDestroy()
    {
        if (!AppServices.HasWorld || service == null)
        {
            return;
        }

        if (AppServices.App.TryGet(out IInputActionProvider current) && ReferenceEquals(current, service))
        {
            AppServices.App.Unregister<IInputActionProvider>();
        }

        service = null;
    }

    private sealed class InputActionProviderService : ServiceBase, IInputActionProvider
    {
        private readonly InputGlyphStringMap<InputAction> _actionLookup = new InputGlyphStringMap<InputAction>(64);
        private readonly InputGlyphStringMap<bool> _ambiguousActionNames = new InputGlyphStringMap<bool>(16);
        private InputActionAsset actions;

        public InputActionProviderService(InputActionAsset actions)
        {
            this.actions = actions;
        }

        public InputActionAsset Actions
        {
            get
            {
                return actions;
            }
        }

        public void SetActions(InputActionAsset inputActions)
        {
            if (actions == inputActions && IsInitialized)
            {
                return;
            }

            actions = inputActions;
            if (IsInitialized)
            {
                BuildActionLookup();
                actions?.Enable();
            }
        }

        protected override void OnInitialize()
        {
            BuildActionLookup();
            actions?.Enable();
        }

        protected override void OnDestroyService()
        {
            _actionLookup.Clear();
            _ambiguousActionNames.Clear();
        }

        public bool TryGetAction(string actionName, out InputAction action)
        {
            if (!string.IsNullOrWhiteSpace(actionName) && _actionLookup.TryGetValue(actionName, out action))
            {
                return action != null;
            }

            action = null;
            return false;
        }

        public bool IsActionNameAmbiguous(string actionName)
        {
            return !string.IsNullOrWhiteSpace(actionName) && _ambiguousActionNames.ContainsKey(actionName);
        }

        private void BuildActionLookup()
        {
            _actionLookup.Clear();
            _ambiguousActionNames.Clear();

            if (actions == null)
            {
                Log.Error("[InputActionProvider] InputActionAsset not assigned.");
                return;
            }

            for (int mapIndex = 0; mapIndex < actions.actionMaps.Count; mapIndex++)
            {
                InputActionMap map = actions.actionMaps[mapIndex];
                for (int actionIndex = 0; actionIndex < map.actions.Count; actionIndex++)
                {
                    InputAction action = map.actions[actionIndex];
                    RegisterActionLookup(map.name, action.name, action);
                }
            }
        }

        private void RegisterActionLookup(string mapName, string actionName, InputAction action)
        {
            string fullName = ZString.Concat(mapName, "/", actionName);
            _actionLookup.Set(fullName, action);

            if (_ambiguousActionNames.ContainsKey(actionName))
            {
                return;
            }

            if (_actionLookup.TryGetValue(actionName, out InputAction existing))
            {
                if (existing != action)
                {
                    _actionLookup.Remove(actionName);
                    _ambiguousActionNames.Set(actionName, true);
                    Log.Warning("[InputActionProvider] Duplicate action name '{0}' detected. Use 'MapName/{0}' to resolve it.", actionName);
                }

                return;
            }

            _actionLookup.Set(actionName, action);
        }
    }
}
#endif
