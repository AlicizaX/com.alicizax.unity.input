#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using AlicizaX;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 提供 AlicizaX 输入功能的静态辅助入口。
/// </summary>
public static partial class UXInput
{
    /// <summary>
    /// 提供运行时交互式输入重绑定和绑定覆盖持久化的静态接口。
    /// </summary>
    public static class Rebind
    {
        private const string DefaultBindingFileName = "input_bindings.json";

        private static readonly List<RebindChange> PreparedRebinds = new List<RebindChange>(8);
        private static readonly Dictionary<DisplayKey, string> DisplayStrings = new Dictionary<DisplayKey, string>(16);

        private static InputActionAsset _actions;
        private static string _defaultBindingsJson = string.Empty;
        private static InputActionRebindingExtensions.RebindingOperation _operation;
        private static string _bindingFilePath;
        private static string _bindingFilePathOverride;
        private static int _bindingVersion;

        /// <summary>
        /// 获取是否已通过 Initialize 指定输入动作资源。
        /// </summary>
        public static bool IsInitialized => _actions != null;

        /// <summary>
        /// 获取当前是否有交互式重绑定操作正在监听输入。
        /// </summary>
        public static bool IsRebinding => _operation != null;

        /// <summary>
        /// 获取当前是否存在等待确认的绑定变更。
        /// </summary>
        public static bool HasPreparedRebinds => PreparedRebinds.Count > 0;

        /// <summary>
        /// 获取固定的绑定覆盖文件是否已存在于磁盘上。
        /// </summary>
        public static bool HasSavedBindings => File.Exists(BindingFilePath);

        /// <summary>
        /// 获取固定的绑定覆盖 JSON 路径。编辑器使用 Application.dataPath，运行时使用 Application.persistentDataPath。
        /// </summary>
        public static string BindingFilePath
        {
            get
            {
                if (!string.IsNullOrEmpty(_bindingFilePathOverride))
                {
                    return _bindingFilePathOverride;
                }

                return _bindingFilePath ??= Path.Combine(GetDefaultBindingDirectory(), DefaultBindingFileName);
            }
        }

        /// <summary>
        /// 测试用绑定文件路径覆盖。
        /// </summary>
        internal static string BindingFilePathOverride
        {
            get => _bindingFilePathOverride;
            set => _bindingFilePathOverride = value;
        }

        internal static InputActionAsset Actions => _actions;

        /// <summary>
        /// 绑定覆盖变化版本号，供 Glyph/Reader 的解析缓存失效。
        /// </summary>
        internal static int BindingVersion => _bindingVersion;

        /// <summary>
        /// 在交互式重绑定操作开始监听输入后触发。
        /// </summary>
        public static event Action OnRebindStarted;

        /// <summary>
        /// 在交互式重绑定操作完成或取消时触发。第一个参数为 true 表示已捕获绑定。
        /// </summary>
        public static event Action<bool, RebindChange> OnRebindEnded;

        /// <summary>
        /// 在捕获到的绑定已暂存并等待 ConfirmApply 时触发。
        /// </summary>
        public static event Action<RebindChange> OnRebindPrepared;

        /// <summary>
        /// 在暂存绑定与已有绑定冲突时触发。
        /// </summary>
        public static event Action<RebindChange, IReadOnlyList<RebindChange>> OnBindingConflict;

        /// <summary>
        /// 在绑定覆盖因应用、重置、加载或导入操作发生变化后触发。
        /// </summary>
        public static event Action OnBindingsChanged;

        /// <summary>
        /// 在待处理重绑定被确认或丢弃时触发。
        /// </summary>
        public static event Action<bool, IReadOnlyList<RebindChange>> OnApply;

        /// <summary>
        /// 在已保存的绑定覆盖从磁盘加载后触发。
        /// </summary>
        public static event Action OnBindingsLoaded;

        /// <summary>
        /// 在绑定覆盖写入固定 JSON 文件后触发。
        /// </summary>
        public static event Action OnBindingsSaved;

        internal static void Initialize(InputActionAsset actions)
        {
            if (_operation != null)
            {
                Cancel();
            }

            _actions = actions;
            _defaultBindingsJson = _actions != null ? _actions.SaveBindingOverridesAsJson() : string.Empty;
            _bindingFilePath = null;
            ClearPrepared();
            DisplayStrings.Clear();
            LoadBindingsInternal();
        }

        internal static void Shutdown(bool clearEvents = false)
        {
            Cancel();
            ClearPrepared();
            DisplayStrings.Clear();
            _actions = null;
            _defaultBindingsJson = string.Empty;
            _bindingFilePath = null;

            if (!clearEvents)
            {
                return;
            }

            OnRebindStarted = null;
            OnRebindEnded = null;
            OnRebindPrepared = null;
            OnBindingConflict = null;
            OnBindingsChanged = null;
            OnApply = null;
            OnBindingsLoaded = null;
            OnBindingsSaved = null;
        }

        internal static void ResetForPlayMode()
        {
            Shutdown(true);
        }

        /// <summary>
        /// 为动作上最匹配的绑定启动交互式重绑定（键鼠目标）。
        /// </summary>
        public static bool Begin(InputAction action)
        {
            return Begin(action, RebindRequest.Default);
        }

        /// <summary>
        /// 为动作上最匹配的绑定启动交互式重绑定。
        /// </summary>
        public static bool Begin(InputAction action, in RebindRequest request)
        {
            if (action == null)
            {
                return false;
            }

            EnsureActions(action);
            if (_actions == null)
            {
                Log.Warning("[UXInput.Rebind] InputActionAsset is not available; call Rebind.Initialize first.");
                return false;
            }

            int bindingIndex = InputBindingResolver.SelectForRebind(
                action,
                request.CompositePartName,
                request.BindingGroup,
                request.RequiredControlPath);
            if (!IsBindingIndexValid(action, bindingIndex))
            {
                Log.Warning("[UXInput.Rebind] No bindable control was found on action '" + action.name + "'.");
                return false;
            }

            return BeginAtIndex(action, bindingIndex, request);
        }

        /// <summary>
        /// 为引用动作上最匹配的绑定启动交互式重绑定（键鼠目标）。
        /// </summary>
        public static bool Begin(InputActionReference actionReference)
        {
            return Begin(actionReference, RebindRequest.Default);
        }

        /// <summary>
        /// 为引用动作上最匹配的绑定启动交互式重绑定。
        /// </summary>
        public static bool Begin(InputActionReference actionReference, in RebindRequest request)
        {
            return Begin(actionReference != null ? actionReference.action : null, request);
        }

        /// <summary>
        /// 为通过名称或完整路径解析到的动作启动交互式重绑定（键鼠目标）。
        /// </summary>
        public static bool Begin(string actionName)
        {
            return Begin(actionName, RebindRequest.Default);
        }

        /// <summary>
        /// 为通过名称或完整路径解析到的动作启动交互式重绑定。
        /// </summary>
        public static bool Begin(string actionName, in RebindRequest request)
        {
            return Begin(ResolveAction(actionName), request);
        }

        /// <summary>
        /// 取消当前正在运行的交互式重绑定操作。
        /// </summary>
        public static void Cancel()
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
        public static bool ConfirmApply(bool clearConflicts = true, bool save = true)
        {
            if (_actions == null || PreparedRebinds.Count == 0)
            {
                return false;
            }

            RebindChange[] snapshot = OnApply != null ? BuildSnapshot() : null;
            List<RebindChange> clears = clearConflicts ? BuildConflictClears() : null;

            for (int i = 0; i < PreparedRebinds.Count; i++)
            {
                RebindChange change = PreparedRebinds[i];
                if (change.IsValid)
                {
                    change.Action.ApplyBindingOverride(change.BindingIndex, change.OverridePath);
                }
            }

            if (clears != null)
            {
                for (int i = 0; i < clears.Count; i++)
                {
                    RebindChange change = clears[i];
                    change.Action.RemoveBindingOverride(change.BindingIndex);
                }
            }

            ClearPrepared();

            if (save)
            {
                SaveBindingsInternal();
            }

            RaiseBindingsChanged();
            OnApply?.Invoke(true, snapshot);
            return true;
        }

        /// <summary>
        /// 丢弃全部暂存绑定变更，不进行应用。
        /// </summary>
        public static void DiscardPrepared()
        {
            if (PreparedRebinds.Count == 0)
            {
                return;
            }

            RebindChange[] snapshot = OnApply != null ? BuildSnapshot() : null;
            ClearPrepared();
            OnApply?.Invoke(false, snapshot);
        }

        /// <summary>
        /// 移除动作上的全部绑定覆盖。
        /// </summary>
        public static void Reset(InputAction action, bool save = true)
        {
            if (action == null)
            {
                return;
            }

            EnsureActions(action);
            action.RemoveAllBindingOverrides();
            RemovePrepared(action);
            if (save)
            {
                SaveBindingsInternal();
            }

            RaiseBindingsChanged();
        }

        /// <summary>
        /// 重置动作上最匹配的绑定覆盖。
        /// </summary>
        public static void Reset(InputAction action, in RebindRequest request, bool save = true)
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
        public static void Reset(InputActionReference actionReference, bool save = true)
        {
            Reset(actionReference != null ? actionReference.action : null, save);
        }

        /// <summary>
        /// 重置引用动作上最匹配的绑定覆盖。
        /// </summary>
        public static void Reset(InputActionReference actionReference, in RebindRequest request, bool save = true)
        {
            Reset(actionReference != null ? actionReference.action : null, request, save);
        }

        /// <summary>
        /// 移除通过名称或完整路径解析到的动作上的全部绑定覆盖。
        /// </summary>
        public static void Reset(string actionName, bool save = true)
        {
            Reset(ResolveAction(actionName), save);
        }

        /// <summary>
        /// 重置通过名称或完整路径解析到的动作上最匹配的绑定覆盖。
        /// </summary>
        public static void Reset(string actionName, in RebindRequest request, bool save = true)
        {
            Reset(ResolveAction(actionName), request, save);
        }

        /// <summary>
        /// 移除当前输入动作资源上的全部绑定覆盖。
        /// </summary>
        public static void ResetAllBindings(bool save = true)
        {
            if (_actions == null)
            {
                return;
            }

            for (int i = 0; i < _actions.actionMaps.Count; i++)
            {
                _actions.actionMaps[i].RemoveAllBindingOverrides();
            }

            ClearPrepared();
            if (save)
            {
                SaveBindingsInternal();
            }

            RaiseBindingsChanged();
        }

        /// <summary>
        /// 恢复 Initialize 时记录的绑定覆盖，并可选保存。
        /// </summary>
        public static void ResetToDefault(bool save = true)
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

            ClearPrepared();
            if (save)
            {
                SaveBindingsInternal();
            }

            RaiseBindingsChanged();
        }

        /// <summary>
        /// 将当前绑定覆盖导出为 Unity Input System JSON。
        /// </summary>
        public static string ExportBindingsJson()
        {
            return _actions != null ? _actions.SaveBindingOverridesAsJson() : string.Empty;
        }

        /// <summary>
        /// 将 Unity Input System 绑定覆盖 JSON 导入当前输入动作资源。
        /// </summary>
        public static bool ImportBindingsJson(string json, bool save = true)
        {
            if (_actions == null || string.IsNullOrEmpty(json))
            {
                return false;
            }

            _actions.LoadBindingOverridesFromJson(json);
            ClearPrepared();
            if (save)
            {
                SaveBindingsInternal();
            }

            RaiseBindingsChanged();
            return true;
        }

        /// <summary>
        /// 获取动作上最匹配绑定的显示文本。
        /// </summary>
        public static string GetDisplayString(InputAction action)
        {
            return GetDisplayString(action, RebindRequest.Default);
        }

        /// <summary>
        /// 获取动作上最匹配绑定的显示文本。
        /// </summary>
        public static string GetDisplayString(InputAction action, in RebindRequest request)
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
        public static string GetDisplayString(InputActionReference actionReference)
        {
            return GetDisplayString(actionReference != null ? actionReference.action : null);
        }

        /// <summary>
        /// 获取引用动作上最匹配绑定的显示文本。
        /// </summary>
        public static string GetDisplayString(InputActionReference actionReference, in RebindRequest request)
        {
            return GetDisplayString(actionReference != null ? actionReference.action : null, request);
        }

        /// <summary>
        /// 获取通过名称或完整路径解析到的动作上最匹配绑定的显示文本。
        /// </summary>
        public static string GetDisplayString(string actionName)
        {
            return GetDisplayString(ResolveAction(actionName));
        }

        /// <summary>
        /// 获取通过名称或完整路径解析到的动作上最匹配绑定的显示文本。
        /// </summary>
        public static string GetDisplayString(string actionName, in RebindRequest request)
        {
            return GetDisplayString(ResolveAction(actionName), request);
        }

        /// <summary>
        /// 暂存一次绑定覆盖（供测试与自定义输入流程使用）。
        /// </summary>
        internal static RebindChange PrepareRebind(
            InputAction action,
            int bindingIndex,
            string overridePath,
            bool prepareOnly = true)
        {
            if (action == null || !IsBindingIndexValid(action, bindingIndex) || string.IsNullOrEmpty(overridePath))
            {
                return RebindChange.Empty;
            }

            EnsureActions(action);

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

        private static bool BeginAtIndex(InputAction action, int bindingIndex, in RebindRequest request)
        {
            Cancel();

            bool wasActionEnabled = action.enabled;
            if (wasActionEnabled)
            {
                action.Disable();
            }

            InputActionRebindingExtensions.RebindingOperation operation = action.PerformInteractiveRebinding(bindingIndex);

            if (!string.IsNullOrEmpty(request.CancelKey))
            {
                operation.WithCancelingThrough(request.CancelKey);
            }

            if (request.ExcludedControls != null)
            {
                for (int i = 0; i < request.ExcludedControls.Length; i++)
                {
                    if (!string.IsNullOrEmpty(request.ExcludedControls[i]))
                    {
                        operation.WithControlsExcluding(request.ExcludedControls[i]);
                    }
                }
            }

            if (!string.IsNullOrEmpty(request.RequiredControlPath))
            {
                operation.WithControlsHavingToMatchPath(request.RequiredControlPath);
            }

            if (!string.IsNullOrEmpty(request.BindingGroup))
            {
                operation.WithBindingGroup(request.BindingGroup);
            }

            // 始终显式设置等待时间：0 用于测试/即时确认，同时覆盖 PerformInteractiveRebinding 的默认 0.05s。
            operation.OnMatchWaitForAnother(request.WaitDelay > 0f ? request.WaitDelay : 0f);

            string selectedPath = null;
            RebindRequest capturedRequest = request;
            operation
                .OnApplyBinding((_, path) =>
                {
                    selectedPath = path;
                })
                .OnComplete(op =>
                {
                    string overridePath = string.IsNullOrEmpty(selectedPath)
                        ? action.bindings[bindingIndex].overridePath
                        : selectedPath;

                    if (string.IsNullOrEmpty(overridePath))
                    {
                        overridePath = action.bindings[bindingIndex].effectivePath;
                    }

                    RebindChange change = PrepareRebind(action, bindingIndex, overridePath, capturedRequest.PrepareOnly);
                    OnRebindEnded?.Invoke(true, change);
                    FinishRebinding(action, wasActionEnabled);
                })
                .OnCancel(_ =>
                {
                    OnRebindEnded?.Invoke(false, RebindChange.Empty);
                    FinishRebinding(action, wasActionEnabled);
                });

            _operation = operation;

            OnRebindStarted?.Invoke();
            try
            {
                _operation.Start();
            }
            catch
            {
                FinishRebinding(action, wasActionEnabled);
                throw;
            }

            return true;
        }

        private static void FinishRebinding(InputAction action, bool wasActionEnabled)
        {
            InputActionRebindingExtensions.RebindingOperation operation = _operation;
            _operation = null;
            if (operation != null)
            {
                operation.Dispose();
            }

            if (wasActionEnabled && action != null && !action.enabled)
            {
                action.Enable();
            }
        }

        private static void ResetAtIndex(InputAction action, int bindingIndex, bool save)
        {
            if (!IsBindingIndexValid(action, bindingIndex))
            {
                return;
            }

            EnsureActions(action);
            action.RemoveBindingOverride(bindingIndex);
            RemovePrepared(action, bindingIndex);
            if (save)
            {
                SaveBindingsInternal();
            }

            RaiseBindingsChanged();
        }

        private static void SaveBindingsInternal()
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

        private static void LoadBindingsInternal()
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

        internal static string GetBindingDisplayString(InputAction action, int bindingIndex)
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

        private static void EnsureActions(InputAction action)
        {
            if (_actions == null && action != null && action.actionMap != null)
            {
                _actions = action.actionMap.asset;
            }
        }

        private static bool IsBindingIndexValid(InputAction action, int bindingIndex)
        {
            return action != null && bindingIndex >= 0 && bindingIndex < action.bindings.Count;
        }

        private static InputAction ResolveAction(string actionName)
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

            return InputActionProvider.TryResolveAction(actionName, out InputAction resolved) ? resolved : null;
        }

        private static RebindChange[] BuildSnapshot()
        {
            var snapshot = new RebindChange[PreparedRebinds.Count];
            for (int i = 0; i < PreparedRebinds.Count; i++)
            {
                snapshot[i] = PreparedRebinds[i];
            }

            return snapshot;
        }

        private static List<RebindChange> CollectConflicts(InputAction action, int bindingIndex, string path)
        {
            var conflicts = new List<RebindChange>(4);
            ForEachBinding((otherAction, otherBindingIndex, binding) =>
            {
                if (ReferenceEquals(otherAction, action) && otherBindingIndex == bindingIndex)
                {
                    return;
                }

                if (string.Equals(binding.effectivePath, path, StringComparison.Ordinal))
                {
                    AddUnique(conflicts, new RebindChange(otherAction, otherBindingIndex, binding.effectivePath));
                }
            });

            for (int i = 0; i < PreparedRebinds.Count; i++)
            {
                RebindChange prepared = PreparedRebinds[i];
                if (ReferenceEquals(prepared.Action, action) && prepared.BindingIndex == bindingIndex)
                {
                    continue;
                }

                if (string.Equals(prepared.OverridePath, path, StringComparison.Ordinal))
                {
                    AddUnique(conflicts, prepared);
                }
            }

            return conflicts;
        }

        private static List<RebindChange> BuildConflictClears()
        {
            var clears = new List<RebindChange>(8);
            for (int i = 0; i < PreparedRebinds.Count; i++)
            {
                RebindChange change = PreparedRebinds[i];
                if (!change.IsValid)
                {
                    continue;
                }

                RebindChange prepared = change;
                ForEachBinding((action, bindingIndex, binding) =>
                {
                    if (ReferenceEquals(action, prepared.Action) && bindingIndex == prepared.BindingIndex)
                    {
                        return;
                    }

                    if (IsPrepared(action, bindingIndex))
                    {
                        return;
                    }

                    if (string.Equals(binding.effectivePath, prepared.OverridePath, StringComparison.Ordinal))
                    {
                        AddUnique(clears, new RebindChange(action, bindingIndex, binding.effectivePath));
                    }
                });
            }

            return clears;
        }

        private static void ForEachBinding(Action<InputAction, int, InputBinding> visitor)
        {
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
                    for (int bindingIndex = 0; bindingIndex < action.bindings.Count; bindingIndex++)
                    {
                        visitor(action, bindingIndex, action.bindings[bindingIndex]);
                    }
                }
            }
        }

        private static bool IsPrepared(InputAction action, int bindingIndex)
        {
            for (int i = 0; i < PreparedRebinds.Count; i++)
            {
                RebindChange prepared = PreparedRebinds[i];
                if (ReferenceEquals(prepared.Action, action) && prepared.BindingIndex == bindingIndex)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AddUnique(List<RebindChange> changes, RebindChange change)
        {
            for (int i = 0; i < changes.Count; i++)
            {
                RebindChange existing = changes[i];
                if (ReferenceEquals(existing.Action, change.Action) && existing.BindingIndex == change.BindingIndex)
                {
                    return;
                }
            }

            changes.Add(change);
        }

        private static void ClearPrepared()
        {
            PreparedRebinds.Clear();
        }

        private static void RemovePrepared(InputAction action)
        {
            for (int i = PreparedRebinds.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(PreparedRebinds[i].Action, action))
                {
                    PreparedRebinds.RemoveAt(i);
                }
            }
        }

        private static void RemovePrepared(InputAction action, int bindingIndex)
        {
            for (int i = PreparedRebinds.Count - 1; i >= 0; i--)
            {
                RebindChange change = PreparedRebinds[i];
                if (ReferenceEquals(change.Action, action) && change.BindingIndex == bindingIndex)
                {
                    PreparedRebinds.RemoveAt(i);
                }
            }
        }

        private static void RaiseBindingsChanged()
        {
            _bindingVersion++;
            DisplayStrings.Clear();
            OnBindingsChanged?.Invoke();
        }

        private static string GetDefaultBindingDirectory()
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
}
#endif
