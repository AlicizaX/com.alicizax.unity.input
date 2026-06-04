#if INPUTSYSTEM_SUPPORT
using System;
using System.IO;
using System.Threading.Tasks;
using AlicizaX;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class InputBindingManager : MonoServiceBehaviour<AppScope>
{
    private const string NullBinding = "__NULL__";
    private const string KeyboardDevice = "<Keyboard>";
    private const string MouseDelta = "<Mouse>/delta";
    private const string MouseScroll = "<Mouse>/scroll";
    private const string MouseScrollX = "<Mouse>/scroll/x";
    private const string MouseScrollY = "<Mouse>/scroll/y";
    private const string KeyboardEscape = "<Keyboard>/escape";
    private const string FileName = "input_bindings.json";
    private const int InitialActionMapCapacity = 8;
    private const int InitialPreparedCapacity = 16;

    public bool debugMode;

    private InputActionAsset _actions;
    private InputActionRebindingExtensions.RebindingOperation _rebindOperation;
    private InputAction _rebindAction;
    private int _rebindBindingIndex = -1;
    private bool _isApplyPending;
    private string _defaultBindingsJson = string.Empty;
    private string _cachedSavePath;

    private ActionMap[] _actionMaps = new ActionMap[InitialActionMapCapacity];
    private int _actionMapCount;
    private RebindContext[] _preparedRebinds = new RebindContext[InitialPreparedCapacity];
    private int _preparedRebindCount;
    private readonly InputGlyphStringMap<ActionRecord> _actionLookup = new InputGlyphStringMap<ActionRecord>(64);
    private readonly InputGlyphGuidMap<ActionRecord> _actionLookupById = new InputGlyphGuidMap<ActionRecord>(64);
    private readonly InputGlyphStringMap<bool> _ambiguousActionNames = new InputGlyphStringMap<bool>(16);

    public event Action<bool, RebindContext[]> OnApply;
    public event Action<RebindContext> OnRebindPrepare;
    public event Action OnRebindStart;
    public event Action<bool, RebindContext> OnRebindEnd;
    public event Action<RebindContext, RebindContext> OnRebindConflict;
    public static event Action BindingsChanged;

    public ActionMap[] ActionMaps
    {
        get
        {
            return _actionMaps;
        }
    }

    public int ActionMapCount
    {
        get
        {
            return _actionMapCount;
        }
    }

    public RebindContext[] PreparedRebinds
    {
        get
        {
            return _preparedRebinds;
        }
    }

    public int PreparedRebindCount
    {
        get
        {
            return _preparedRebindCount;
        }
    }

    private string SavePath
    {
        get
        {
            if (!string.IsNullOrEmpty(_cachedSavePath))
            {
                return _cachedSavePath;
            }

#if UNITY_EDITOR
            string folder = Application.dataPath;
#else
            string folder = Application.persistentDataPath;
#endif
            _cachedSavePath = Path.Combine(folder, FileName);
            return _cachedSavePath;
        }
    }

    protected override void Awake()
    {
        base.Awake();
    }

    protected override void OnInitialize()
    {
        if (IsMobilePlatform())
        {
            return;
        }

        if (!AppServices.TryGet(out IInputActionProvider provider) || provider.Actions == null)
        {
            Log.Error("InputBindingManager: IInputActionProvider with InputActionAsset is required.");
            return;
        }

        _actions = provider.Actions;
        BuildActionMap();
        _defaultBindingsJson = _actions.SaveBindingOverridesAsJson();

        if (File.Exists(SavePath))
        {
            string json = File.ReadAllText(SavePath);
            if (!string.IsNullOrEmpty(json))
            {
                _actions.LoadBindingOverridesFromJson(json);
                RefreshBindingPathsFromActions();
                InvokeBindingsChanged();
                if (debugMode)
                {
                    Log.Info("Loaded overrides from {0}", SavePath);
                }
            }
        }

        _actions.Enable();
    }

    protected override void OnDestroyService()
    {
        if (_rebindOperation != null)
        {
            _rebindOperation.Dispose();
            _rebindOperation = null;
        }

        OnApply = null;
        OnRebindPrepare = null;
        OnRebindStart = null;
        OnRebindEnd = null;
        OnRebindConflict = null;
        BindingsChanged = null;
    }

    public static InputAction Action(string actionName)
    {
        return InputActionResolver.Action(actionName);
    }

    public bool TryGetAction(string actionName, out InputAction action)
    {
        action = null;
        if (string.IsNullOrWhiteSpace(actionName))
        {
            return false;
        }

        if (_actionLookup.TryGetValue(actionName, out ActionRecord record))
        {
            action = record.Action;
            return action != null;
        }

        return false;
    }

    public bool IsActionNameAmbiguous(string actionName)
    {
        return !string.IsNullOrWhiteSpace(actionName) && _ambiguousActionNames.ContainsKey(actionName);
    }

    public int FindBestBindingIndexForKeyboard(InputAction action, string compositePartName = null)
    {
        if (action == null)
        {
            return -1;
        }

        int fallbackPart = -1;
        int fallbackNonComposite = -1;
        bool searchingForCompositePart = !string.IsNullOrEmpty(compositePartName);

        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (searchingForCompositePart)
            {
                if (!binding.isPartOfComposite)
                {
                    continue;
                }

                if (!InputGlyphStringUtility.EqualsIgnoreCase(binding.name, compositePartName))
                {
                    continue;
                }
            }

            bool isKeyboardBinding =
                (!string.IsNullOrEmpty(binding.path) && InputGlyphStringUtility.StartsWithIgnoreCase(binding.path, KeyboardDevice))
                || (!string.IsNullOrEmpty(binding.effectivePath) && InputGlyphStringUtility.StartsWithIgnoreCase(binding.effectivePath, KeyboardDevice));

            if (binding.isPartOfComposite)
            {
                if (fallbackPart == -1)
                {
                    fallbackPart = i;
                }

                if (isKeyboardBinding)
                {
                    return i;
                }
            }
            else
            {
                if (fallbackNonComposite == -1)
                {
                    fallbackNonComposite = i;
                }

                if (isKeyboardBinding)
                {
                    return i;
                }
            }
        }

        return fallbackNonComposite >= 0 ? fallbackNonComposite : fallbackPart;
    }

    public void StartRebind(string actionName, string compositePartName = null)
    {
        InputAction action = Action(actionName);
        if (action == null)
        {
            return;
        }

        int bindingIndex = FindBestBindingIndexForKeyboard(action, compositePartName);
        if (bindingIndex < 0)
        {
            Log.Error(
                "[InputBindingManager] No suitable binding found for action '{0}' (part={1})",
                actionName,
                string.IsNullOrEmpty(compositePartName) ? "<null>" : compositePartName);
            return;
        }

        _actions.Disable();
        PerformInteractiveRebinding(action, bindingIndex, KeyboardDevice, true);
        OnRebindStart?.Invoke();
        if (debugMode)
        {
            Log.Info("[InputBindingManager] Rebind started");
        }
    }

    public void CancelRebind()
    {
        if (_rebindOperation != null)
        {
            _rebindOperation.Cancel();
        }
    }

    public async Task<bool> ConfirmApply(bool clearConflicts = true)
    {
        if (!_isApplyPending)
        {
            return false;
        }

        RebindContext[] appliedContexts = BuildPreparedSnapshot();
        for (int i = 0; i < _preparedRebindCount; i++)
        {
            RebindContext context = _preparedRebinds[i];
            if (InputGlyphStringUtility.EqualsOrdinal(context.OverridePath, NullBinding) && !clearConflicts)
            {
                continue;
            }

            if (!string.IsNullOrEmpty(context.OverridePath))
            {
                if (InputGlyphStringUtility.EqualsOrdinal(context.OverridePath, NullBinding))
                {
                    context.Action.RemoveBindingOverride(context.BindingIndex);
                }
                else
                {
                    context.Action.ApplyBindingOverride(context.BindingIndex, context.OverridePath);
                }
            }

            BindingPath bindingPath = GetBindingPath(context.Action, context.BindingIndex);
            if (bindingPath != null)
            {
                bindingPath.EffectivePath = context.Action.bindings[context.BindingIndex].effectivePath;
            }
        }

        ClearPreparedRebinds();
        await WriteOverridesToDiskAsync();
        InvokeBindingsChanged();
        OnApply?.Invoke(true, appliedContexts);
        _isApplyPending = false;
        if (debugMode)
        {
            Log.Info("[InputBindingManager] Apply confirmed and saved.");
        }

        return true;
    }

    public void DiscardPrepared()
    {
        if (!_isApplyPending)
        {
            return;
        }

        RebindContext[] discardedContexts = BuildPreparedSnapshot();
        ClearPreparedRebinds();
        _isApplyPending = false;
        OnApply?.Invoke(false, discardedContexts);
        if (debugMode)
        {
            Log.Info("[InputBindingManager] Prepared rebinds discarded.");
        }
    }

    public async Task ResetToDefaultAsync()
    {
        if (!string.IsNullOrEmpty(_defaultBindingsJson))
        {
            _actions.LoadBindingOverridesFromJson(_defaultBindingsJson);
        }
        else
        {
            for (int mapIndex = 0; mapIndex < _actionMapCount; mapIndex++)
            {
                ActionMap map = _actionMaps[mapIndex];
                for (int actionIndex = 0; actionIndex < map.ActionCount; actionIndex++)
                {
                    ActionRecord actionRecord = map.Actions[actionIndex];
                    for (int bindingIndex = 0; bindingIndex < actionRecord.Action.bindings.Count; bindingIndex++)
                    {
                        actionRecord.Action.RemoveBindingOverride(bindingIndex);
                    }
                }
            }
        }

        RefreshBindingPathsFromActions();
        await WriteOverridesToDiskAsync();
        InvokeBindingsChanged();
        if (debugMode)
        {
            Log.Info("Reset to default and saved.");
        }
    }

    public BindingPath GetBindingPath(string actionName, int bindingIndex = 0)
    {
        if (TryGetActionRecord(actionName, out ActionRecord record))
        {
            return record.GetBindingPath(bindingIndex);
        }

        return null;
    }

    public BindingPath GetBindingPath(InputAction action, int bindingIndex = 0)
    {
        if (action == null)
        {
            return null;
        }

        if (_actionLookupById.TryGetValue(action.id, out ActionRecord record) && record.Action == action)
        {
            return record.GetBindingPath(bindingIndex);
        }

        return null;
    }

    private void BuildActionMap()
    {
        ClearActionData();
        int mapCount = _actions != null ? _actions.actionMaps.Count : 0;
        EnsureActionMapCapacity(mapCount);

        for (int mapIndex = 0; mapIndex < mapCount; mapIndex++)
        {
            InputActionMap sourceMap = _actions.actionMaps[mapIndex];
            ActionMap actionMap = new ActionMap(sourceMap);
            _actionMaps[_actionMapCount] = actionMap;
            _actionMapCount++;

            for (int actionIndex = 0; actionIndex < actionMap.ActionCount; actionIndex++)
            {
                RegisterActionLookup(sourceMap.name, actionMap.Actions[actionIndex].Name, actionMap.Actions[actionIndex]);
            }
        }
    }

    private void ClearActionData()
    {
        Array.Clear(_actionMaps, 0, _actionMapCount);
        _actionMapCount = 0;
        _actionLookup.Clear();
        _actionLookupById.Clear();
        _ambiguousActionNames.Clear();
        ClearPreparedRebinds();
    }

    private void RegisterActionLookup(string mapName, string actionName, ActionRecord action)
    {
        _actionLookupById.Set(action.Action.id, action);
        _actionLookup.Set(ZString.Concat(mapName, "/", actionName), action);

        if (_ambiguousActionNames.ContainsKey(actionName))
        {
            return;
        }

        if (_actionLookup.TryGetValue(actionName, out ActionRecord existing))
        {
            if (existing.Action != action.Action)
            {
                _actionLookup.Remove(actionName);
                _ambiguousActionNames.Set(actionName, true);
                Log.Warning("[InputBindingManager] Duplicate action name '{0}' detected. Use 'MapName/{0}' to resolve it.", actionName);
            }

            return;
        }

        _actionLookup.Set(actionName, action);
    }

    private void RefreshBindingPathsFromActions()
    {
        for (int mapIndex = 0; mapIndex < _actionMapCount; mapIndex++)
        {
            ActionMap map = _actionMaps[mapIndex];
            for (int actionIndex = 0; actionIndex < map.ActionCount; actionIndex++)
            {
                ActionRecord actionRecord = map.Actions[actionIndex];
                for (int bindingIndex = 0; bindingIndex < actionRecord.BindingPathByIndex.Length; bindingIndex++)
                {
                    BindingPath bindingPath = actionRecord.BindingPathByIndex[bindingIndex];
                    if (bindingPath != null)
                    {
                        bindingPath.EffectivePath = actionRecord.Action.bindings[bindingIndex].effectivePath;
                    }
                }
            }
        }
    }

    private void PerformInteractiveRebinding(InputAction action, int bindingIndex, string deviceMatchPath, bool excludeMouseMovementAndScroll)
    {
        _rebindAction = action;
        _rebindBindingIndex = bindingIndex;
        InputActionRebindingExtensions.RebindingOperation operation = action.PerformInteractiveRebinding(bindingIndex);

        if (!string.IsNullOrEmpty(deviceMatchPath))
        {
            operation = operation.WithControlsHavingToMatchPath(deviceMatchPath);
        }

        if (excludeMouseMovementAndScroll)
        {
            operation = operation.WithControlsExcluding(MouseDelta)
                .WithControlsExcluding(MouseScroll)
                .WithControlsExcluding(MouseScrollX)
                .WithControlsExcluding(MouseScrollY);
        }

        _rebindOperation = operation
            .OnApplyBinding(HandleApplyBinding)
            .OnComplete(HandleRebindComplete)
            .OnCancel(HandleRebindCancel)
            .WithCancelingThrough(KeyboardEscape)
            .Start();
    }

    private void HandleApplyBinding(InputActionRebindingExtensions.RebindingOperation operation, string path)
    {
        RebindContext preparedContext = new RebindContext(_rebindAction, _rebindBindingIndex, path);
        if (AnyPreparedRebind(path, _rebindAction, _rebindBindingIndex, out RebindContext existing))
        {
            PrepareRebind(preparedContext);
            PrepareRebind(new RebindContext(existing.Action, existing.BindingIndex, NullBinding));
            OnRebindConflict?.Invoke(preparedContext, existing);
            return;
        }

        if (AnyBindingPath(path, _rebindAction, _rebindBindingIndex, out InputAction duplicateAction, out int duplicateBindingIndex))
        {
            RebindContext conflictingContext = new RebindContext(
                duplicateAction,
                duplicateBindingIndex,
                duplicateAction.bindings[duplicateBindingIndex].path);
            PrepareRebind(preparedContext);
            PrepareRebind(new RebindContext(duplicateAction, duplicateBindingIndex, NullBinding));
            OnRebindConflict?.Invoke(preparedContext, conflictingContext);
            return;
        }

        PrepareRebind(preparedContext);
    }

    private void HandleRebindComplete(InputActionRebindingExtensions.RebindingOperation operation)
    {
        if (debugMode)
        {
            Log.Info("[InputBindingManager] Rebind completed");
        }

        _actions.Enable();
        OnRebindEnd?.Invoke(true, CreateCurrentRebindContext());
        CleanRebindOperation();
    }

    private void HandleRebindCancel(InputActionRebindingExtensions.RebindingOperation operation)
    {
        if (debugMode)
        {
            Log.Info("[InputBindingManager] Rebind cancelled");
        }

        _actions.Enable();
        OnRebindEnd?.Invoke(false, CreateCurrentRebindContext());
        CleanRebindOperation();
    }

    private RebindContext CreateCurrentRebindContext()
    {
        if (_rebindAction == null || _rebindBindingIndex < 0)
        {
            return null;
        }

        return new RebindContext(_rebindAction, _rebindBindingIndex, _rebindAction.bindings[_rebindBindingIndex].effectivePath);
    }

    private void CleanRebindOperation()
    {
        if (_rebindOperation != null)
        {
            _rebindOperation.Dispose();
            _rebindOperation = null;
        }

        _rebindAction = null;
        _rebindBindingIndex = -1;
    }

    private bool AnyPreparedRebind(string bindingPath, InputAction currentAction, int currentIndex, out RebindContext duplicate)
    {
        for (int i = 0; i < _preparedRebindCount; i++)
        {
            RebindContext context = _preparedRebinds[i];
            if (context != null
                && InputGlyphStringUtility.EqualsOrdinal(context.OverridePath, bindingPath)
                && (context.Action != currentAction || context.BindingIndex != currentIndex))
            {
                duplicate = context;
                return true;
            }
        }

        duplicate = null;
        return false;
    }

    private bool AnyBindingPath(
        string bindingPath,
        InputAction currentAction,
        int currentIndex,
        out InputAction duplicateAction,
        out int duplicateBindingIndex)
    {
        duplicateAction = null;
        duplicateBindingIndex = -1;
        for (int mapIndex = 0; mapIndex < _actionMapCount; mapIndex++)
        {
            ActionMap map = _actionMaps[mapIndex];
            for (int actionIndex = 0; actionIndex < map.ActionCount; actionIndex++)
            {
                ActionRecord actionRecord = map.Actions[actionIndex];
                bool isSameAction = actionRecord.Action == currentAction;
                for (int bindingIndex = 0; bindingIndex < actionRecord.BindingCount; bindingIndex++)
                {
                    BindingRecord binding = actionRecord.Bindings[bindingIndex];
                    if (isSameAction && binding.BindingIndex == currentIndex)
                    {
                        continue;
                    }

                    if (InputGlyphStringUtility.EqualsOrdinal(binding.BindingPath.EffectivePath, bindingPath))
                    {
                        duplicateAction = actionRecord.Action;
                        duplicateBindingIndex = binding.BindingIndex;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private void PrepareRebind(RebindContext context)
    {
        if (context == null || context.Action == null)
        {
            return;
        }

        RemovePreparedRebind(context);

        BindingPath bindingPath = GetBindingPath(context.Action, context.BindingIndex);
        if (bindingPath == null)
        {
            return;
        }

        string overridePath = context.OverridePath;
        if (string.IsNullOrEmpty(overridePath))
        {
            overridePath = bindingPath.BindingPathValue;
            context = new RebindContext(context.Action, context.BindingIndex, overridePath);
        }

        if (!InputGlyphStringUtility.EqualsOrdinal(bindingPath.EffectivePath, overridePath))
        {
            AddPreparedRebind(context);
            _isApplyPending = true;
            OnRebindPrepare?.Invoke(context);
            if (debugMode)
            {
                Log.Info("Prepared rebind: {0} -> {1}", context, context.OverridePath);
            }
        }
    }

    private async Task WriteOverridesToDiskAsync()
    {
        string json = _actions.SaveBindingOverridesAsJson();
        EnsureSaveDirectoryExists();
        using (StreamWriter writer = new StreamWriter(SavePath, false))
        {
            await writer.WriteAsync(json);
        }

        if (debugMode)
        {
            Log.Info("Overrides saved to {0}", SavePath);
        }
    }

    private bool TryGetActionRecord(string actionName, out ActionRecord result)
    {
        return _actionLookup.TryGetValue(actionName, out result);
    }

    private void AddPreparedRebind(RebindContext context)
    {
        if (context == null)
        {
            return;
        }

        EnsurePreparedCapacity(_preparedRebindCount + 1);
        _preparedRebinds[_preparedRebindCount] = context;
        _preparedRebindCount++;
    }

    private bool RemovePreparedRebind(RebindContext context)
    {
        if (context == null)
        {
            return false;
        }

        int index = IndexOfPreparedRebind(context);
        if (index < 0)
        {
            return false;
        }

        _preparedRebindCount--;
        if (index < _preparedRebindCount)
        {
            _preparedRebinds[index] = _preparedRebinds[_preparedRebindCount];
        }

        _preparedRebinds[_preparedRebindCount] = default;
        return true;
    }

    private int IndexOfPreparedRebind(RebindContext context)
    {
        for (int i = 0; i < _preparedRebindCount; i++)
        {
            RebindContext currentContext = _preparedRebinds[i];
            if (currentContext != null && currentContext.Equals(context))
            {
                return i;
            }
        }

        return -1;
    }

    private RebindContext[] BuildPreparedSnapshot()
    {
        if (_preparedRebindCount == 0)
        {
            return Array.Empty<RebindContext>();
        }

        RebindContext[] snapshot = new RebindContext[_preparedRebindCount];
        Array.Copy(_preparedRebinds, snapshot, _preparedRebindCount);
        return snapshot;
    }

    private void ClearPreparedRebinds()
    {
        Array.Clear(_preparedRebinds, 0, _preparedRebindCount);
        _preparedRebindCount = 0;
    }

    private void EnsureActionMapCapacity(int capacity)
    {
        if (_actionMaps.Length >= capacity)
        {
            return;
        }

        int newCapacity = _actionMaps.Length;
        while (newCapacity < capacity)
        {
            newCapacity <<= 1;
        }

        Array.Resize(ref _actionMaps, newCapacity);
    }

    private void EnsurePreparedCapacity(int capacity)
    {
        if (_preparedRebinds.Length >= capacity)
        {
            return;
        }

        int newCapacity = _preparedRebinds.Length;
        while (newCapacity < capacity)
        {
            newCapacity <<= 1;
        }

        Array.Resize(ref _preparedRebinds, newCapacity);
    }

    private void EnsureSaveDirectoryExists()
    {
        string directory = Path.GetDirectoryName(SavePath);
        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

    private static void InvokeBindingsChanged()
    {
        InputGlyphService.ClearBindingCache();
        InputActionReader.ClearBindingCaches();
        BindingsChanged?.Invoke();
    }

    private static bool IsMobilePlatform()
    {
#if UNITY_ANDROID || UNITY_IOS
        return true;
#else
        return Application.isMobilePlatform;
#endif
    }

    public sealed class ActionMap
    {
        public readonly string Name;
        public readonly ActionRecord[] Actions;
        public readonly int ActionCount;

        public ActionMap(InputActionMap map)
        {
            Name = map.name;
            ActionCount = map.actions.Count;
            Actions = new ActionRecord[ActionCount];
            for (int i = 0; i < ActionCount; i++)
            {
                Actions[i] = new ActionRecord(map.actions[i]);
            }
        }
    }

    public sealed class ActionRecord
    {
        public readonly string Name;
        public readonly InputAction Action;
        public readonly BindingRecord[] Bindings;
        public readonly int BindingCount;
        public readonly BindingPath[] BindingPathByIndex;

        public ActionRecord(InputAction action)
        {
            Action = action;
            Name = action.name;
            BindingPathByIndex = new BindingPath[action.bindings.Count];
            Bindings = new BindingRecord[action.bindings.Count];
            int bindingCount = 0;

            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];
                if (binding.isComposite)
                {
                    continue;
                }

                BindingPath bindingPath = new BindingPath(binding.path, binding.overridePath);
                BindingPathByIndex[i] = bindingPath;
                Bindings[bindingCount] = new BindingRecord(
                    binding.name,
                    action.name,
                    binding.name,
                    i,
                    bindingPath);
                bindingCount++;
            }

            BindingCount = bindingCount;
        }

        public BindingPath GetBindingPath(int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= BindingPathByIndex.Length)
            {
                return null;
            }

            return BindingPathByIndex[bindingIndex];
        }
    }

    public sealed class BindingRecord
    {
        public readonly string Name;
        public readonly string ParentAction;
        public readonly string CompositePart;
        public readonly int BindingIndex;
        public readonly BindingPath BindingPath;

        public BindingRecord(
            string name,
            string parentAction,
            string compositePart,
            int bindingIndex,
            BindingPath bindingPath)
        {
            Name = name;
            ParentAction = parentAction;
            CompositePart = compositePart;
            BindingIndex = bindingIndex;
            BindingPath = bindingPath;
        }
    }

    public sealed class BindingPath
    {
        public readonly string BindingPathValue;
        public string OverridePath;
        private event Action<string> _onEffectivePathChanged;

        public BindingPath(string bindingPath, string overridePath)
        {
            BindingPathValue = bindingPath;
            OverridePath = overridePath;
        }

        public string EffectivePath
        {
            get
            {
                return !string.IsNullOrEmpty(OverridePath) ? OverridePath : BindingPathValue;
            }
            set
            {
                OverridePath = InputGlyphStringUtility.EqualsOrdinal(value, BindingPathValue) ? string.Empty : value;
                _onEffectivePathChanged?.Invoke(EffectivePath);
            }
        }

        public void SubscribeToEffectivePathChanged(Action<string> callback)
        {
            _onEffectivePathChanged += callback;
        }

        public void UnsubscribeFromEffectivePathChanged(Action<string> callback)
        {
            _onEffectivePathChanged -= callback;
        }

        public void Dispose()
        {
            _onEffectivePathChanged = null;
        }
    }

    public sealed class RebindContext : IEquatable<RebindContext>
    {
        public readonly InputAction Action;
        public readonly int BindingIndex;
        public readonly string OverridePath;

        public RebindContext(InputAction action, int bindingIndex, string overridePath)
        {
            Action = action;
            BindingIndex = bindingIndex;
            OverridePath = overridePath;
        }

        public bool Equals(RebindContext other)
        {
            if (Action == null || other == null || other.Action == null)
            {
                return false;
            }

            return Action.id.Equals(other.Action.id) && BindingIndex == other.BindingIndex;
        }

        public override string ToString()
        {
            if (Action == null)
            {
                return "<null>";
            }

            string mapName = Action.actionMap != null ? Action.actionMap.name : "<no-map>";
            return ZString.Concat(mapName, "/", Action.name, ":", BindingIndex.ToString());
        }
    }
}
#endif
