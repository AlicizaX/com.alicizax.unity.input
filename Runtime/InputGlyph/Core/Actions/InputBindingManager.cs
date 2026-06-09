#if INPUTSYSTEM_SUPPORT
using System;
using System.IO;
using System.Threading.Tasks;
using AlicizaX;
using Cysharp.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 输入绑定管理服务，负责加载、保存、重绑定和重置 Input System 绑定配置。
/// </summary>
public static class InputBindingManager
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

    private static InputActionAsset _actions;
    private static InputActionRebindingExtensions.RebindingOperation _rebindOperation;
    private static InputAction _rebindAction;
    private static int _rebindBindingIndex = -1;
    private static bool _isApplyPending;
    private static string _defaultBindingsJson = string.Empty;
    private static string _cachedSavePath;

    private static ActionMap[] _actionMaps = new ActionMap[InitialActionMapCapacity];
    private static int _actionMapCount;
    private static RebindContext[] _preparedRebinds = new RebindContext[InitialPreparedCapacity];
    private static int _preparedRebindCount;
    private static readonly InputGlyphGuidMap<ActionRecord> _actionLookupById = new InputGlyphGuidMap<ActionRecord>(64);

    /// <summary>
    /// 绑定变更确认或丢弃时触发。
    /// </summary>
    /// <remarks>第一个参数表示是否已确认应用；第二个参数为本次处理的重绑定上下文快照。</remarks>
    public static event Action<bool, RebindContext[]> OnApply;

    /// <summary>
    /// 新的重绑定结果进入待确认列表时触发。
    /// </summary>
    public static event Action<RebindContext> OnRebindPrepare;

    /// <summary>
    /// 交互式重绑定开始时触发。
    /// </summary>
    public static event Action OnRebindStart;

    /// <summary>
    /// 交互式重绑定结束或取消时触发。
    /// </summary>
    /// <remarks>第一个参数表示是否完成；第二个参数为本次重绑定上下文。</remarks>
    public static event Action<bool, RebindContext> OnRebindEnd;

    /// <summary>
    /// 新重绑定与已有绑定或待确认绑定冲突时触发。
    /// </summary>
    /// <remarks>第一个参数为新准备的绑定；第二个参数为发生冲突的绑定。</remarks>
    public static event Action<RebindContext, RebindContext> OnRebindConflict;

    /// <summary>
    /// 已保存的绑定配置发生变化时触发。
    /// </summary>
    public static event Action BindingsChanged;

    /// <summary>
    /// 已构建的动作映射缓存数组。
    /// </summary>
    /// <remarks>只读取前 <see cref="ActionMapCount"/> 个元素。</remarks>
    public static ActionMap[] ActionMaps
    {
        get
        {
            return _actionMaps;
        }
    }

    /// <summary>
    /// 当前有效的动作映射数量。
    /// </summary>
    public static int ActionMapCount
    {
        get
        {
            return _actionMapCount;
        }
    }

    /// <summary>
    /// 等待确认应用的重绑定上下文缓存数组。
    /// </summary>
    /// <remarks>只读取前 <see cref="PreparedRebindCount"/> 个元素。</remarks>
    public static RebindContext[] PreparedRebinds
    {
        get
        {
            return _preparedRebinds;
        }
    }

    /// <summary>
    /// 当前等待确认应用的重绑定数量。
    /// </summary>
    public static int PreparedRebindCount
    {
        get
        {
            return _preparedRebindCount;
        }
    }

    private static string SavePath
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

    internal static void Initialize(InputActionAsset actions)
    {
        if (IsMobilePlatform())
        {
            return;
        }

        if (actions == null)
        {
            Log.Error("InputBindingManager: InputActionAsset is required.");
            return;
        }

        if (_actions == actions && _actionMapCount > 0)
        {
            return;
        }

        CleanRebindOperation();
        _actions = actions;
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
#if UNITY_EDITOR
                Log.Info("Loaded overrides from {0}", SavePath);
#endif
            }
        }
    }

    internal static void Shutdown(InputActionAsset actions)
    {
        if (_actions != null && actions != null && _actions != actions)
        {
            return;
        }

        if (_rebindOperation != null)
        {
            _rebindOperation.Dispose();
            _rebindOperation = null;
        }

        ClearActionData();
        _actions = null;
        _defaultBindingsJson = string.Empty;
        _cachedSavePath = null;
        _isApplyPending = false;
        _rebindAction = null;
        _rebindBindingIndex = -1;
        OnApply = null;
        OnRebindPrepare = null;
        OnRebindStart = null;
        OnRebindEnd = null;
        OnRebindConflict = null;
        BindingsChanged = null;
    }

    private static bool EnsureInitialized()
    {
        if (_actions != null)
        {
            return true;
        }

        Log.Error("InputBindingManager: Initialize must be called by InputActionProvider before using bindings.");
        return false;
    }

    private static int FindBestBindingIndexForKeyboard(InputAction action, string compositePartName = null)
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

    /// <summary>
    /// 开始指定输入动作的键盘交互式重绑定。
    /// </summary>
    /// <param name="actionName">要重绑定的动作完整名称，格式通常为“动作映射名/动作名”。</param>
    /// <param name="compositePartName">组合绑定中的部分名称；重绑定普通绑定时传入 null。</param>
    public static void StartRebind(string actionName, string compositePartName = null)
    {
        if (!EnsureInitialized())
        {
            return;
        }

        InputAction action = InputActionResolver.Action(actionName);
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
#if UNITY_EDITOR
        Log.Info("[InputBindingManager] Rebind started");
#endif
    }

    /// <summary>
    /// 取消当前正在进行的交互式重绑定。
    /// </summary>
    public static void CancelRebind()
    {
        if (_rebindOperation != null)
        {
            _rebindOperation.Cancel();
        }
    }

    /// <summary>
    /// 确认应用所有待处理的重绑定并写入本地存档。
    /// </summary>
    /// <param name="clearConflicts">是否清除被新绑定冲突占用的旧绑定。</param>
    /// <returns>存在待处理重绑定并成功应用时返回 true；没有待处理重绑定时返回 false。</returns>
    public static async Task<bool> ConfirmApply(bool clearConflicts = true)
    {
        if (!EnsureInitialized())
        {
            return false;
        }

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
                bindingPath.SetEffectivePath(context.Action.bindings[context.BindingIndex].effectivePath);
            }
        }

        ClearPreparedRebinds();
        await WriteOverridesToDiskAsync();
        InvokeBindingsChanged();
        OnApply?.Invoke(true, appliedContexts);
        _isApplyPending = false;
#if UNITY_EDITOR
        Log.Info("[InputBindingManager] Apply confirmed and saved.");
#endif

        return true;
    }

    /// <summary>
    /// 丢弃所有待确认的重绑定结果。
    /// </summary>
    public static void DiscardPrepared()
    {
        if (!_isApplyPending)
        {
            return;
        }

        RebindContext[] discardedContexts = BuildPreparedSnapshot();
        ClearPreparedRebinds();
        _isApplyPending = false;
        OnApply?.Invoke(false, discardedContexts);
#if UNITY_EDITOR
        Log.Info("[InputBindingManager] Prepared rebinds discarded.");
#endif
    }

    /// <summary>
    /// 将所有绑定恢复为默认配置并写入本地存档。
    /// </summary>
    /// <returns>表示异步重置和保存流程的任务。</returns>
    public static async Task ResetToDefaultAsync()
    {
        if (!EnsureInitialized())
        {
            return;
        }

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
#if UNITY_EDITOR
        Log.Info("Reset to default and saved.");
#endif
    }

    /// <summary>
    /// 根据动作名称和绑定索引获取绑定路径信息。
    /// </summary>
    /// <param name="actionName">动作完整名称，格式通常为“动作映射名/动作名”。</param>
    /// <param name="bindingIndex">要查询的绑定索引。</param>
    /// <returns>匹配到的绑定路径信息；未找到时返回 null。</returns>
    public static BindingPath GetBindingPath(string actionName, int bindingIndex = 0)
    {
        return InputActionResolver.TryGetAction(actionName, out InputAction action)
            ? GetBindingPath(action, bindingIndex)
            : null;
    }

    /// <summary>
    /// 根据输入动作和绑定索引获取绑定路径信息。
    /// </summary>
    /// <param name="action">要查询绑定路径的输入动作。</param>
    /// <param name="bindingIndex">要查询的绑定索引。</param>
    /// <returns>匹配到的绑定路径信息；未找到时返回 null。</returns>
    public static BindingPath GetBindingPath(InputAction action, int bindingIndex = 0)
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

    private static void BuildActionMap()
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
                RegisterActionRecord(actionMap.Actions[actionIndex]);
            }
        }
    }

    private static void ClearActionData()
    {
        Array.Clear(_actionMaps, 0, _actionMapCount);
        _actionMapCount = 0;
        _actionLookupById.Clear();
        ClearPreparedRebinds();
    }

    private static void RegisterActionRecord(ActionRecord action)
    {
        _actionLookupById.Set(action.Action.id, action);
    }

    private static void RefreshBindingPathsFromActions()
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
                        bindingPath.SetEffectivePath(actionRecord.Action.bindings[bindingIndex].effectivePath);
                    }
                }
            }
        }
    }

    private static void PerformInteractiveRebinding(InputAction action, int bindingIndex, string deviceMatchPath, bool excludeMouseMovementAndScroll)
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

    private static void HandleApplyBinding(InputActionRebindingExtensions.RebindingOperation operation, string path)
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

    private static void HandleRebindComplete(InputActionRebindingExtensions.RebindingOperation operation)
    {
#if UNITY_EDITOR
        Log.Info("[InputBindingManager] Rebind completed");
#endif

        _actions.Enable();
        OnRebindEnd?.Invoke(true, CreateCurrentRebindContext());
        CleanRebindOperation();
    }

    private static void HandleRebindCancel(InputActionRebindingExtensions.RebindingOperation operation)
    {
#if UNITY_EDITOR
        Log.Info("[InputBindingManager] Rebind cancelled");
#endif

        _actions.Enable();
        OnRebindEnd?.Invoke(false, CreateCurrentRebindContext());
        CleanRebindOperation();
    }

    private static RebindContext CreateCurrentRebindContext()
    {
        if (_rebindAction == null || _rebindBindingIndex < 0)
        {
            return null;
        }

        return new RebindContext(_rebindAction, _rebindBindingIndex, _rebindAction.bindings[_rebindBindingIndex].effectivePath);
    }

    private static void CleanRebindOperation()
    {
        if (_rebindOperation != null)
        {
            _rebindOperation.Dispose();
            _rebindOperation = null;
        }

        _rebindAction = null;
        _rebindBindingIndex = -1;
    }

    private static bool AnyPreparedRebind(string bindingPath, InputAction currentAction, int currentIndex, out RebindContext duplicate)
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

    private static bool AnyBindingPath(
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

    private static void PrepareRebind(RebindContext context)
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
#if UNITY_EDITOR
            Log.Info("Prepared rebind: {0} -> {1}", context, context.OverridePath);
#endif
        }
    }

    private static async Task WriteOverridesToDiskAsync()
    {
        string json = _actions.SaveBindingOverridesAsJson();
        EnsureSaveDirectoryExists();
        using (StreamWriter writer = new StreamWriter(SavePath, false))
        {
            await writer.WriteAsync(json);
        }

#if UNITY_EDITOR
        Log.Info("Overrides saved to {0}", SavePath);
#endif
    }

    private static void AddPreparedRebind(RebindContext context)
    {
        if (context == null)
        {
            return;
        }

        EnsurePreparedCapacity(_preparedRebindCount + 1);
        _preparedRebinds[_preparedRebindCount] = context;
        _preparedRebindCount++;
    }

    private static bool RemovePreparedRebind(RebindContext context)
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

    private static int IndexOfPreparedRebind(RebindContext context)
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

    private static RebindContext[] BuildPreparedSnapshot()
    {
        if (_preparedRebindCount == 0)
        {
            return Array.Empty<RebindContext>();
        }

        RebindContext[] snapshot = new RebindContext[_preparedRebindCount];
        Array.Copy(_preparedRebinds, snapshot, _preparedRebindCount);
        return snapshot;
    }

    private static void ClearPreparedRebinds()
    {
        Array.Clear(_preparedRebinds, 0, _preparedRebindCount);
        _preparedRebindCount = 0;
    }

    private static void EnsureActionMapCapacity(int capacity)
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

    private static void EnsurePreparedCapacity(int capacity)
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

    private static void EnsureSaveDirectoryExists()
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

    /// <summary>
    /// 输入动作映射的只读缓存数据。
    /// </summary>
    public sealed class ActionMap
    {
        /// <summary>
        /// 动作映射名称。
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// 该动作映射下的动作记录数组。
        /// </summary>
        public readonly ActionRecord[] Actions;

        /// <summary>
        /// 该动作映射下的动作数量。
        /// </summary>
        public readonly int ActionCount;

        internal ActionMap(InputActionMap map)
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

    /// <summary>
    /// 输入动作的只读缓存数据。
    /// </summary>
    public sealed class ActionRecord
    {
        /// <summary>
        /// 输入动作名称。
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// 对应的 Input System 输入动作实例。
        /// </summary>
        public readonly InputAction Action;

        /// <summary>
        /// 该动作下可显示或可重绑定的绑定记录数组。
        /// </summary>
        public readonly BindingRecord[] Bindings;

        /// <summary>
        /// 该动作下有效绑定记录数量。
        /// </summary>
        public readonly int BindingCount;

        /// <summary>
        /// 按原始绑定索引快速访问的绑定路径数组。
        /// </summary>
        public readonly BindingPath[] BindingPathByIndex;

        internal ActionRecord(InputAction action)
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

        /// <summary>
        /// 根据原始绑定索引获取绑定路径信息。
        /// </summary>
        /// <param name="bindingIndex">要查询的原始绑定索引。</param>
        /// <returns>匹配到的绑定路径信息；索引无效时返回 null。</returns>
        public BindingPath GetBindingPath(int bindingIndex)
        {
            if (bindingIndex < 0 || bindingIndex >= BindingPathByIndex.Length)
            {
                return null;
            }

            return BindingPathByIndex[bindingIndex];
        }
    }

    /// <summary>
    /// 输入动作绑定的只读缓存数据。
    /// </summary>
    public sealed class BindingRecord
    {
        /// <summary>
        /// 绑定显示名称或组合部分名称。
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// 绑定所属的输入动作名称。
        /// </summary>
        public readonly string ParentAction;

        /// <summary>
        /// 组合绑定部分名称；普通绑定通常与 <see cref="Name"/> 相同或为空。
        /// </summary>
        public readonly string CompositePart;

        /// <summary>
        /// 该绑定在 Input System 动作绑定列表中的原始索引。
        /// </summary>
        public readonly int BindingIndex;

        /// <summary>
        /// 该绑定对应的路径信息。
        /// </summary>
        public readonly BindingPath BindingPath;

        internal BindingRecord(
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

    /// <summary>
    /// 输入绑定路径信息，包含默认路径、覆盖路径和当前有效路径。
    /// </summary>
    public sealed class BindingPath
    {
        /// <summary>
        /// 绑定的默认路径。
        /// </summary>
        public readonly string BindingPathValue;

        /// <summary>
        /// 当前绑定覆盖路径；为空时表示使用默认路径。
        /// </summary>
        public string OverridePath { get; private set; }
        private event Action<string> _onEffectivePathChanged;

        internal BindingPath(string bindingPath, string overridePath)
        {
            BindingPathValue = bindingPath;
            OverridePath = overridePath;
        }

        /// <summary>
        /// 当前实际生效的绑定路径。
        /// </summary>
        public string EffectivePath
        {
            get
            {
                return !string.IsNullOrEmpty(OverridePath) ? OverridePath : BindingPathValue;
            }
        }

        internal void SetEffectivePath(string value)
        {
            OverridePath = InputGlyphStringUtility.EqualsOrdinal(value, BindingPathValue) ? string.Empty : value;
            _onEffectivePathChanged?.Invoke(EffectivePath);
        }

        /// <summary>
        /// 订阅有效绑定路径变化事件。
        /// </summary>
        /// <param name="callback">路径变化时调用的回调，参数为新的有效路径。</param>
        public void SubscribeToEffectivePathChanged(Action<string> callback)
        {
            _onEffectivePathChanged += callback;
        }

        /// <summary>
        /// 取消订阅有效绑定路径变化事件。
        /// </summary>
        /// <param name="callback">要移除的路径变化回调。</param>
        public void UnsubscribeFromEffectivePathChanged(Action<string> callback)
        {
            _onEffectivePathChanged -= callback;
        }

        /// <summary>
        /// 清空该绑定路径上的所有变化回调。
        /// </summary>
        public void Dispose()
        {
            _onEffectivePathChanged = null;
        }
    }

    /// <summary>
    /// 一次重绑定操作的上下文数据。
    /// </summary>
    public sealed class RebindContext : IEquatable<RebindContext>
    {
        /// <summary>
        /// 被重绑定的输入动作。
        /// </summary>
        public readonly InputAction Action;

        /// <summary>
        /// 被重绑定的原始绑定索引。
        /// </summary>
        public readonly int BindingIndex;

        /// <summary>
        /// 准备应用的覆盖路径。
        /// </summary>
        public readonly string OverridePath;

        internal RebindContext(InputAction action, int bindingIndex, string overridePath)
        {
            Action = action;
            BindingIndex = bindingIndex;
            OverridePath = overridePath;
        }

        /// <summary>
        /// 判断另一个重绑定上下文是否指向同一个动作绑定。
        /// </summary>
        /// <param name="other">要比较的另一个重绑定上下文。</param>
        /// <returns>两个上下文指向同一个动作 ID 和绑定索引时返回 true，否则返回 false。</returns>
        public bool Equals(RebindContext other)
        {
            if (Action == null || other == null || other.Action == null)
            {
                return false;
            }

            return Action.id.Equals(other.Action.id) && BindingIndex == other.BindingIndex;
        }

        /// <summary>
        /// 获取该重绑定上下文的可读标识。
        /// </summary>
        /// <returns>由动作映射名、动作名和绑定索引组成的可读字符串。</returns>
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
