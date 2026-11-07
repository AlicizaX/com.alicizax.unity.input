#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using AlicizaX;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 提供运行时交互式输入重绑定和绑定覆盖持久化的实例接口。
/// </summary>
public sealed class InputRebind
{
    private const string DefaultBindingFileName = "input_bindings.json";

    private readonly List<RebindChange> PreparedRebinds = new List<RebindChange>(8);
    private readonly Dictionary<DisplayKey, string> DisplayStrings = new Dictionary<DisplayKey, string>(16);

    private InputActionAsset _actions;
    private string _defaultBindingsJson;
    private InputActionRebindingExtensions.RebindingOperation _operation;
    private string _bindingFilePath;
    private int _bindingVersion;
    private InputAction _rebindAction;
    private int _rebindBindingIndex;
    private bool _restoreAction;
    private bool _prepareOnly;
    private string _selectedPath;
    private readonly Action<InputActionRebindingExtensions.RebindingOperation, string> _applyBinding;
    private readonly Action<InputActionRebindingExtensions.RebindingOperation> _completed;
    private readonly Action<InputActionRebindingExtensions.RebindingOperation> _canceled;

    /// <summary>
    /// 获取是否已通过 Initialize 指定输入动作资源。
    /// </summary>
    public bool IsInitialized => _actions != null;

    /// <summary>
    /// 获取当前是否有交互式重绑定操作正在监听输入。
    /// </summary>
    public bool IsRebinding => _operation != null;

    /// <summary>
    /// 获取当前是否存在等待确认的绑定变更。
    /// </summary>
    public bool HasPreparedRebinds => PreparedRebinds.Count > 0;

    /// <summary>
    /// 获取固定的绑定覆盖文件是否已存在于磁盘上。
    /// </summary>
    public bool HasSavedBindings => File.Exists(BindingFilePath);

    /// <summary>
    /// 获取固定的绑定覆盖 JSON 路径。编辑器使用 Application.dataPath，运行时使用 Application.persistentDataPath。
    /// </summary>
    public string BindingFilePath =>
        _bindingFilePath ??= Path.Combine(GetDefaultBindingDirectory(), DefaultBindingFileName);

    internal InputRebind(InputActionAsset actions, string bindingFilePath)
    {
        _actions = actions;
        _bindingFilePath = bindingFilePath;
        _applyBinding = HandleApplyBinding;
        _completed = HandleCompleted;
        _canceled = HandleCanceled;
    }

    internal InputActionAsset Actions => _actions;

    /// <summary>
    /// 绑定覆盖变化版本号，供 Glyph/Reader 的解析缓存失效。
    /// </summary>
    internal int BindingVersion => _bindingVersion;

    /// <summary>
    /// 在交互式重绑定操作开始监听输入后触发。
    /// </summary>
    public event Action OnRebindStarted;

    /// <summary>
    /// 在交互式重绑定操作完成或取消时触发。第一个参数为 true 表示已捕获绑定。
    /// </summary>
    public event Action<bool, RebindChange> OnRebindEnded;

    /// <summary>
    /// 在捕获到的绑定已暂存并等待 ConfirmApply 时触发。
    /// </summary>
    public event Action<RebindChange> OnRebindPrepared;

    /// <summary>
    /// 在暂存绑定与已有绑定冲突时触发。
    /// </summary>
    public event Action<RebindChange, IReadOnlyList<RebindChange>> OnBindingConflict;

    /// <summary>
    /// 在绑定覆盖因应用、重置、加载或导入操作发生变化后触发。
    /// </summary>
    public event Action OnBindingsChanged;

    /// <summary>
    /// 在待处理重绑定被确认或丢弃时触发。
    /// </summary>
    public event Action<bool, IReadOnlyList<RebindChange>> OnApply;

    /// <summary>
    /// 在已保存的绑定覆盖从磁盘加载后触发。
    /// </summary>
    public event Action OnBindingsLoaded;

    /// <summary>
    /// 在绑定覆盖写入固定 JSON 文件后触发。
    /// </summary>
    public event Action OnBindingsSaved;

    internal void Initialize()
    {
        _defaultBindingsJson = _actions != null ? _actions.SaveBindingOverridesAsJson() : null;
        LoadBindings();
    }

    internal void Shutdown()
    {
        OnRebindStarted = null;
        OnRebindEnded = null;
        OnRebindPrepared = null;
        OnBindingConflict = null;
        OnBindingsChanged = null;
        OnApply = null;
        OnBindingsLoaded = null;
        OnBindingsSaved = null;
        FinishRebinding(false);
        if (_actions != null && _defaultBindingsJson != null)
            _actions.LoadBindingOverridesFromJson(_defaultBindingsJson);
        PreparedRebinds.Clear();
        DisplayStrings.Clear();
        _actions = null;
        _defaultBindingsJson = null;
        _bindingFilePath = null;
        _bindingVersion = 0;
    }

    /// <summary>
    /// 为动作上最匹配的绑定启动交互式重绑定（键鼠目标）。
    /// </summary>
    public bool Begin(InputAction action)
    {
        return Begin(action, RebindRequest.Default);
    }

    /// <summary>
    /// 为动作上最匹配的绑定启动交互式重绑定。
    /// </summary>
    public bool Begin(InputAction action, in RebindRequest request)
    {
        if (action == null)
        {
            return false;
        }

        if (_actions == null)
        {
            Log.Warning("[UXInput.Rebind] InputActionAsset is not available; initialize UXInput with an InputActionAsset first.");
            return false;
        }

        int bindingIndex = InputBindingResolver.SelectForRebind(
            action,
            request.CompositePartName,
            request.BindingGroup,
            request.RequiredControlPath);
        if (bindingIndex < 0)
        {
            Log.Warning("[UXInput.Rebind] No bindable control was found on action '" + action.name + "'.");
            return false;
        }

        return BeginAtIndex(action, bindingIndex, request);
    }

    /// <summary>
    /// 为引用动作上最匹配的绑定启动交互式重绑定（键鼠目标）。
    /// </summary>
    public bool Begin(InputActionReference actionReference)
    {
        return Begin(actionReference, RebindRequest.Default);
    }

    /// <summary>
    /// 为引用动作上最匹配的绑定启动交互式重绑定。
    /// </summary>
    public bool Begin(InputActionReference actionReference, in RebindRequest request)
    {
        return Begin(actionReference != null ? actionReference.action : null, request);
    }

    /// <summary>
    /// 为通过名称或完整路径解析到的动作启动交互式重绑定（键鼠目标）。
    /// </summary>
    public bool Begin(string actionName)
    {
        return Begin(actionName, RebindRequest.Default);
    }

    /// <summary>
    /// 为通过名称或完整路径解析到的动作启动交互式重绑定。
    /// </summary>
    public bool Begin(string actionName, in RebindRequest request)
    {
        return Begin(ResolveAction(actionName), request);
    }

    /// <summary>
    /// 取消当前正在运行的交互式重绑定操作。
    /// </summary>
    public void Cancel()
    {
        InputActionRebindingExtensions.RebindingOperation operation = _operation;
        if (operation == null)
        {
            return;
        }

        operation.Cancel();
    }

    /// <summary>
    /// 应用暂存的绑定变更，可选清理冲突的旧绑定，并可选保存到磁盘。
    /// </summary>
    public bool ConfirmApply(bool clearConflicts = true, bool save = true)
    {
        if (_actions == null || PreparedRebinds.Count == 0)
        {
            return false;
        }

        RebindChange[] snapshot = OnApply != null ? PreparedRebinds.ToArray() : null;
        if (clearConflicts) ClearBindingConflicts();

        for (int i = 0; i < PreparedRebinds.Count; i++)
        {
            RebindChange change = PreparedRebinds[i];
            change.Action.ApplyBindingOverride(change.BindingIndex, change.OverridePath);
        }

        PreparedRebinds.Clear();

        RaiseBindingsChanged();
        if (save)
        {
            SaveBindings();
        }
        OnApply?.Invoke(true, snapshot);
        return true;
    }

    /// <summary>
    /// 丢弃全部暂存绑定变更，不进行应用。
    /// </summary>
    public void DiscardPrepared()
    {
        if (PreparedRebinds.Count == 0)
        {
            return;
        }

        RebindChange[] snapshot = OnApply != null ? PreparedRebinds.ToArray() : null;
        PreparedRebinds.Clear();
        OnApply?.Invoke(false, snapshot);
    }

    /// <summary>
    /// 移除动作上的全部绑定覆盖。
    /// </summary>
    public void Reset(InputAction action, bool save = true)
    {
        if (action == null)
        {
            return;
        }

        action.RemoveAllBindingOverrides();
        RemovePrepared(action);
        RaiseBindingsChanged();
        if (save)
        {
            SaveBindings();
        }
    }

    /// <summary>
    /// 重置动作上最匹配的绑定覆盖。
    /// </summary>
    public void Reset(InputAction action, in RebindRequest request, bool save = true)
    {
        if (action == null)
        {
            return;
        }

        int bindingIndex = InputBindingResolver.SelectForRebind(
            action,
            request.CompositePartName,
            request.BindingGroup,
            request.RequiredControlPath);
        ResetAtIndex(action, bindingIndex, save);
    }

    /// <summary>
    /// 移除引用动作上的全部绑定覆盖。
    /// </summary>
    public void Reset(InputActionReference actionReference, bool save = true)
    {
        Reset(actionReference != null ? actionReference.action : null, save);
    }

    /// <summary>
    /// 重置引用动作上最匹配的绑定覆盖。
    /// </summary>
    public void Reset(InputActionReference actionReference, in RebindRequest request, bool save = true)
    {
        Reset(actionReference != null ? actionReference.action : null, request, save);
    }

    /// <summary>
    /// 移除通过名称或完整路径解析到的动作上的全部绑定覆盖。
    /// </summary>
    public void Reset(string actionName, bool save = true)
    {
        Reset(ResolveAction(actionName), save);
    }

    /// <summary>
    /// 重置通过名称或完整路径解析到的动作上最匹配的绑定覆盖。
    /// </summary>
    public void Reset(string actionName, in RebindRequest request, bool save = true)
    {
        Reset(ResolveAction(actionName), request, save);
    }

    /// <summary>
    /// 移除当前输入动作资源上的全部绑定覆盖。
    /// </summary>
    public void ResetAllBindings(bool save = true)
    {
        if (_actions == null)
        {
            return;
        }

        for (int i = 0; i < _actions.actionMaps.Count; i++)
        {
            _actions.actionMaps[i].RemoveAllBindingOverrides();
        }

        PreparedRebinds.Clear();
        RaiseBindingsChanged();
        if (save)
        {
            SaveBindings();
        }
    }

    /// <summary>
    /// 恢复 Initialize 时记录的绑定覆盖，并可选保存。
    /// </summary>
    public void ResetToDefault(bool save = true)
    {
        if (_actions == null)
        {
            return;
        }

        if (!string.IsNullOrEmpty(_defaultBindingsJson))
        {
            _actions.LoadBindingOverridesFromJson(_defaultBindingsJson);
        }
        else
        {
            for (int i = 0; i < _actions.actionMaps.Count; i++)
            {
                _actions.actionMaps[i].RemoveAllBindingOverrides();
            }
        }

        PreparedRebinds.Clear();
        RaiseBindingsChanged();
        if (save)
        {
            SaveBindings();
        }
    }

    /// <summary>
    /// 将当前绑定覆盖导出为 Unity Input System JSON。
    /// </summary>
    public string ExportBindingsJson()
    {
        return _actions != null ? _actions.SaveBindingOverridesAsJson() : string.Empty;
    }

    /// <summary>
    /// 将 Unity Input System 绑定覆盖 JSON 导入当前输入动作资源。
    /// </summary>
    public bool ImportBindingsJson(string json, bool save = true)
    {
        if (_actions == null || string.IsNullOrEmpty(json))
        {
            return false;
        }

        _actions.LoadBindingOverridesFromJson(json);
        PreparedRebinds.Clear();
        RaiseBindingsChanged();
        if (save)
        {
            SaveBindings();
        }
        return true;
    }

    /// <summary>
    /// 获取动作上最匹配绑定的显示文本。
    /// </summary>
    public string GetDisplayString(InputAction action)
    {
        return GetDisplayString(action, RebindRequest.Default);
    }

    /// <summary>
    /// 获取动作上最匹配绑定的显示文本。
    /// </summary>
    public string GetDisplayString(InputAction action, in RebindRequest request)
    {
        if (action == null)
        {
            return string.Empty;
        }

        int bindingIndex = InputBindingResolver.SelectForRebind(
            action,
            request.CompositePartName,
            request.BindingGroup,
            request.RequiredControlPath);
        return GetBindingDisplayString(action, bindingIndex);
    }

    /// <summary>
    /// 获取引用动作上最匹配绑定的显示文本。
    /// </summary>
    public string GetDisplayString(InputActionReference actionReference)
    {
        return GetDisplayString(actionReference != null ? actionReference.action : null);
    }

    /// <summary>
    /// 获取引用动作上最匹配绑定的显示文本。
    /// </summary>
    public string GetDisplayString(InputActionReference actionReference, in RebindRequest request)
    {
        return GetDisplayString(actionReference != null ? actionReference.action : null, request);
    }

    /// <summary>
    /// 获取通过名称或完整路径解析到的动作上最匹配绑定的显示文本。
    /// </summary>
    public string GetDisplayString(string actionName)
    {
        return GetDisplayString(ResolveAction(actionName));
    }

    /// <summary>
    /// 获取通过名称或完整路径解析到的动作上最匹配绑定的显示文本。
    /// </summary>
    public string GetDisplayString(string actionName, in RebindRequest request)
    {
        return GetDisplayString(ResolveAction(actionName), request);
    }

    /// <summary>
    /// 暂存一次绑定覆盖（供测试与自定义输入流程使用）。
    /// </summary>
    internal RebindChange PrepareRebind(
        InputAction action,
        int bindingIndex,
        string overridePath,
        bool prepareOnly = true)
    {
        if (!IsBindingIndexValid(action, bindingIndex) || string.IsNullOrEmpty(overridePath))
        {
            return RebindChange.Empty;
        }
        var change = new RebindChange(action, bindingIndex, overridePath);
        RemovePrepared(action, bindingIndex);
        PreparedRebinds.Add(change);

        List<RebindChange> conflicts = OnBindingConflict != null
            ? CollectConflicts(action, bindingIndex, overridePath)
            : null;

        OnRebindPrepared?.Invoke(change);
        if (conflicts != null && conflicts.Count > 0)
        {
            OnBindingConflict?.Invoke(change, conflicts);
        }

        if (!prepareOnly)
        {
            ConfirmApply(true, true);
        }

        return change;
    }

    private bool BeginAtIndex(InputAction action, int bindingIndex, in RebindRequest request)
    {
        Cancel();
        // A cancellation listener may already have started its next binding.
        if (_operation != null) return false;

        _rebindAction = action;
        _rebindBindingIndex = bindingIndex;
        _restoreAction = action.enabled;
        _prepareOnly = request.PrepareOnly;
        if (_restoreAction)
        {
            action.Disable();
        }

        try
        {
            _operation = action.PerformInteractiveRebinding(bindingIndex);
            if (!string.IsNullOrEmpty(request.CancelKey))
            {
                _operation.WithCancelingThrough(request.CancelKey);
            }

            if (request.ExcludedControls != null)
            {
                for (int i = 0; i < request.ExcludedControls.Length; i++)
                {
                    if (!string.IsNullOrEmpty(request.ExcludedControls[i]))
                        _operation.WithControlsExcluding(request.ExcludedControls[i]);
                }
            }

            if (!string.IsNullOrEmpty(request.RequiredControlPath))
                _operation.WithControlsHavingToMatchPath(request.RequiredControlPath);
            if (!string.IsNullOrEmpty(request.BindingGroup))
                _operation.WithBindingGroup(request.BindingGroup);

            _operation.OnMatchWaitForAnother(Mathf.Max(0f, request.WaitDelay))
                .OnApplyBinding(_applyBinding)
                .OnComplete(_completed)
                .OnCancel(_canceled)
                .Start();
        }
        catch
        {
            FinishRebinding(true);
            throw;
        }

        OnRebindStarted?.Invoke();
        return true;
    }

    private void HandleApplyBinding(InputActionRebindingExtensions.RebindingOperation operation, string path)
    {
        _selectedPath = path;
    }

    private void HandleCompleted(InputActionRebindingExtensions.RebindingOperation operation)
    {
        var change = new RebindChange(_rebindAction, _rebindBindingIndex, _selectedPath);
        bool prepareOnly = _prepareOnly;
        FinishRebinding(true);
        PrepareRebind(change.Action, change.BindingIndex, change.OverridePath, prepareOnly);
        OnRebindEnded?.Invoke(true, change);
    }

    private void HandleCanceled(InputActionRebindingExtensions.RebindingOperation operation)
    {
        FinishRebinding(true);
        OnRebindEnded?.Invoke(false, RebindChange.Empty);
    }

    private void FinishRebinding(bool restoreAction)
    {
        InputActionRebindingExtensions.RebindingOperation operation = _operation;
        InputAction action = _rebindAction;
        bool enableAction = restoreAction && _restoreAction;
        _operation = null;
        _rebindAction = null;
        _rebindBindingIndex = 0;
        _restoreAction = false;
        _prepareOnly = false;
        _selectedPath = null;
        operation?.Dispose();

        if (enableAction)
        {
            action.Enable();
        }
    }

    private void ResetAtIndex(InputAction action, int bindingIndex, bool save)
    {
        if (!IsBindingIndexValid(action, bindingIndex))
        {
            return;
        }

        action.RemoveBindingOverride(bindingIndex);
        RemovePrepared(action, bindingIndex);
        RaiseBindingsChanged();
        if (save)
        {
            SaveBindings();
        }
    }

    private void SaveBindings()
    {
        if (_actions == null)
        {
            return;
        }

        string path = BindingFilePath;
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, _actions.SaveBindingOverridesAsJson());
        OnBindingsSaved?.Invoke();
    }

    private void LoadBindings()
    {
        if (_actions == null)
        {
            return;
        }

        string path = BindingFilePath;
        if (!File.Exists(path))
        {
            return;
        }

        string json = File.ReadAllText(path);
        if (string.IsNullOrEmpty(json))
        {
            return;
        }

        _actions.LoadBindingOverridesFromJson(json);
        RaiseBindingsChanged();
        OnBindingsLoaded?.Invoke();
    }

    internal string GetBindingDisplayString(InputAction action, int bindingIndex)
    {
        if (!IsBindingIndexValid(action, bindingIndex))
        {
            return string.Empty;
        }

        var key = new DisplayKey(action, bindingIndex);
        if (DisplayStrings.TryGetValue(key, out string cached))
        {
            return cached;
        }

        string value = action.GetBindingDisplayString(bindingIndex) ?? string.Empty;
        DisplayStrings[key] = value;
        return value;
    }

    private bool IsBindingIndexValid(InputAction action, int bindingIndex)
    {
        return action != null && bindingIndex >= 0 && bindingIndex < action.bindings.Count;
    }

    private InputAction ResolveAction(string actionName)
    {
        if (string.IsNullOrWhiteSpace(actionName))
        {
            return null;
        }

        if (_actions != null)
        {
            InputAction action = _actions.FindAction(actionName, false);
            if (action != null)
            {
                return action;
            }
        }

        return null;
    }

    private List<RebindChange> CollectConflicts(InputAction action, int bindingIndex, string path)
    {
        List<RebindChange> conflicts = null;
        foreach (InputActionMap map in _actions.actionMaps)
        {
            foreach (InputAction otherAction in map.actions)
            {
                for (int i = 0; i < otherAction.bindings.Count; i++)
                {
                    if (ReferenceEquals(otherAction, action) && i == bindingIndex) continue;
                    int preparedIndex = FindPreparedIndex(otherAction, i);
                    string otherPath = preparedIndex >= 0
                        ? PreparedRebinds[preparedIndex].OverridePath
                        : otherAction.bindings[i].effectivePath;
                    if (string.Equals(otherPath, path, StringComparison.Ordinal))
                    {
                        conflicts ??= new List<RebindChange>(4);
                        conflicts.Add(new RebindChange(otherAction, i, otherPath));
                    }
                }
            }
        }

        return conflicts;
    }

    private void ClearBindingConflicts()
    {
        foreach (InputActionMap map in _actions.actionMaps)
        {
            foreach (InputAction action in map.actions)
            {
                for (int bindingIndex = 0; bindingIndex < action.bindings.Count; bindingIndex++)
                {
                    if (FindPreparedIndex(action, bindingIndex) >= 0) continue;
                    string path = action.bindings[bindingIndex].effectivePath;
                    for (int i = 0; i < PreparedRebinds.Count; i++)
                    {
                        if (!string.Equals(path, PreparedRebinds[i].OverridePath, StringComparison.Ordinal)) continue;
                        action.ApplyBindingOverride(bindingIndex, string.Empty);
                        break;
                    }
                }
            }
        }
    }

    private int FindPreparedIndex(InputAction action, int bindingIndex)
    {
        for (int i = 0; i < PreparedRebinds.Count; i++)
        {
            RebindChange prepared = PreparedRebinds[i];
            if (ReferenceEquals(prepared.Action, action) && prepared.BindingIndex == bindingIndex)
            {
                return i;
            }
        }

        return -1;
    }

    private void RemovePrepared(InputAction action)
    {
        for (int i = PreparedRebinds.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(PreparedRebinds[i].Action, action))
            {
                PreparedRebinds.RemoveAt(i);
            }
        }
    }

    private void RemovePrepared(InputAction action, int bindingIndex)
    {
        int index = FindPreparedIndex(action, bindingIndex);
        if (index >= 0) PreparedRebinds.RemoveAt(index);
    }

    private void RaiseBindingsChanged()
    {
        _bindingVersion++;
        DisplayStrings.Clear();
        OnBindingsChanged?.Invoke();
    }

    private string GetDefaultBindingDirectory()
    {
#if UNITY_EDITOR
        return Application.dataPath;
#else
        return Application.persistentDataPath;
#endif
    }

    private readonly struct DisplayKey : IEquatable<DisplayKey>
    {
        private readonly InputAction _action;
        private readonly int _bindingIndex;
        private readonly int _hash;

        internal DisplayKey(InputAction action, int bindingIndex)
        {
            _action = action;
            _bindingIndex = bindingIndex;
            unchecked
            {
                _hash = (RuntimeHelpers.GetHashCode(action) * 397) ^ bindingIndex;
            }
        }

        public bool Equals(DisplayKey other)
        {
            return ReferenceEquals(_action, other._action) && _bindingIndex == other._bindingIndex;
        }

        public override bool Equals(object obj)
        {
            return obj is DisplayKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _hash;
        }
    }
}
#endif
