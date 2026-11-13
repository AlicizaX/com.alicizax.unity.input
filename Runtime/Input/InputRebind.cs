#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    /// <summary>
    /// 提供运行时交互式输入重绑定和绑定覆盖持久化的实例接口。
    /// 动作资产由 <see cref="InputReader"/> 唯一持有；绑定变化通过 <see cref="InputBindingVersion"/> 通知各解析缓存。
    /// </summary>
    public sealed class InputRebind
    {
        private const string DefaultBindingFileName = "input_bindings.json";
        private const string CorruptFileSuffix = ".corrupt";
        private const string TempFileSuffix = ".tmp";

        private readonly List<RebindChange> PreparedRebinds = new List<RebindChange>(8);
        private readonly InputReader _reader;
        private readonly InputBindingVersion _bindingVersion;
        private readonly InputBindingDisplayCache _displayCache;

        private string _defaultBindingsJson;
        private InputActionRebindingExtensions.RebindingOperation _operation;
        private string _bindingFilePath;
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
        public bool IsInitialized => Asset != null;

        private InputActionAsset Asset => _reader.Actions;

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
        /// 获取固定的绑定覆盖 JSON 路径。编辑器使用工程 Library 目录（不进版本库、不触发导入），运行时使用 Application.persistentDataPath。
        /// </summary>
        public string BindingFilePath =>
            _bindingFilePath ??= Path.Combine(GetDefaultBindingDirectory(), DefaultBindingFileName);

        internal InputRebind(
            InputReader reader,
            InputBindingVersion bindingVersion,
            InputBindingDisplayCache displayCache,
            string bindingFilePath)
        {
            _reader = reader;
            _bindingVersion = bindingVersion;
            _displayCache = displayCache;
            _bindingFilePath = bindingFilePath;
            _applyBinding = HandleApplyBinding;
            _completed = HandleCompleted;
            _canceled = HandleCanceled;
        }

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
            _defaultBindingsJson = Asset != null ? Asset.SaveBindingOverridesAsJson() : null;
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
            if (Asset != null && _defaultBindingsJson != null)
                Asset.LoadBindingOverridesFromJson(_defaultBindingsJson);
            PreparedRebinds.Clear();
            _defaultBindingsJson = null;
            _bindingFilePath = null;
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

            if (Asset == null)
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
        /// 为通过完整 "Map/Action" 路径解析到的动作启动交互式重绑定（键鼠目标）。
        /// </summary>
        public bool Begin(string actionName)
        {
            return Begin(actionName, RebindRequest.Default);
        }

        /// <summary>
        /// 为通过完整 "Map/Action" 路径解析到的动作启动交互式重绑定。
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
            if (Asset == null || PreparedRebinds.Count == 0)
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
        /// 移除通过完整 "Map/Action" 路径解析到的动作上的全部绑定覆盖。
        /// </summary>
        public void Reset(string actionName, bool save = true)
        {
            Reset(ResolveAction(actionName), save);
        }

        /// <summary>
        /// 重置通过完整 "Map/Action" 路径解析到的动作上最匹配的绑定覆盖。
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
            if (Asset == null)
            {
                return;
            }

            for (int i = 0; i < Asset.actionMaps.Count; i++)
            {
                Asset.actionMaps[i].RemoveAllBindingOverrides();
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
            if (Asset == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(_defaultBindingsJson))
            {
                Asset.LoadBindingOverridesFromJson(_defaultBindingsJson);
            }
            else
            {
                for (int i = 0; i < Asset.actionMaps.Count; i++)
                {
                    Asset.actionMaps[i].RemoveAllBindingOverrides();
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
            return Asset != null ? Asset.SaveBindingOverridesAsJson() : string.Empty;
        }

        /// <summary>
        /// 将 Unity Input System 绑定覆盖 JSON 导入当前输入动作资源。
        /// </summary>
        public bool ImportBindingsJson(string json, bool save = true)
        {
            if (Asset == null || string.IsNullOrEmpty(json))
            {
                return false;
            }

            Asset.LoadBindingOverridesFromJson(json);
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
            return _displayCache.Get(action, bindingIndex);
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
        /// 获取通过完整 "Map/Action" 路径解析到的动作上最匹配绑定的显示文本。
        /// </summary>
        public string GetDisplayString(string actionName)
        {
            return GetDisplayString(ResolveAction(actionName));
        }

        /// <summary>
        /// 获取通过完整 "Map/Action" 路径解析到的动作上最匹配绑定的显示文本。
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

        /// <summary>
        /// 先写临时文件再替换，写入中途崩溃不会留下半个文件。IO 失败只记录错误，内存中的绑定保持已应用状态。
        /// </summary>
        private void SaveBindings()
        {
            if (Asset == null)
            {
                return;
            }

            string path = BindingFilePath;
            string tempPath = path + TempFileSuffix;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(tempPath, Asset.SaveBindingOverridesAsJson());
                if (File.Exists(path))
                {
                    File.Delete(path);
                }

                File.Move(tempPath, path);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Log.Error("[UXInput.Rebind] Failed to save binding overrides to '" + path + "': " + exception.Message);
                return;
            }

            OnBindingsSaved?.Invoke();
        }

        /// <summary>
        /// 存档损坏时恢复入口绑定、把文件改名为 .corrupt 留作排查，然后继续启动；输入层不因玩家存档而整体失效。
        /// </summary>
        private void LoadBindings()
        {
            if (Asset == null)
            {
                return;
            }

            string path = BindingFilePath;
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json))
                {
                    return;
                }

                Asset.LoadBindingOverridesFromJson(json);
            }
            catch (Exception exception)
            {
                RestoreEntryBindings();
                QuarantineCorruptFile(path);
                Log.Warning("[UXInput.Rebind] Ignored unreadable binding file '" + path + "': " + exception.Message);
                return;
            }

            RaiseBindingsChanged();
            OnBindingsLoaded?.Invoke();
        }

        private void RestoreEntryBindings()
        {
            for (int i = 0; i < Asset.actionMaps.Count; i++)
            {
                Asset.actionMaps[i].RemoveAllBindingOverrides();
            }

            if (!string.IsNullOrEmpty(_defaultBindingsJson))
            {
                Asset.LoadBindingOverridesFromJson(_defaultBindingsJson);
            }
        }

        private static void QuarantineCorruptFile(string path)
        {
            try
            {
                string corruptPath = path + CorruptFileSuffix;
                if (File.Exists(corruptPath))
                {
                    File.Delete(corruptPath);
                }

                File.Move(path, corruptPath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Log.Warning("[UXInput.Rebind] Failed to move corrupt binding file aside: " + exception.Message);
            }
        }

        private bool IsBindingIndexValid(InputAction action, int bindingIndex)
        {
            return action != null && bindingIndex >= 0 && bindingIndex < action.bindings.Count;
        }

        // 与 Reader 同一条解析规则：完整 "Map/Action" 路径。
        private InputAction ResolveAction(string actionPath) => _reader.ResolveAction(actionPath);

        private List<RebindChange> CollectConflicts(InputAction action, int bindingIndex, string path)
        {
            List<RebindChange> conflicts = null;
            foreach (InputActionMap map in Asset.actionMaps)
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
            foreach (InputActionMap map in Asset.actionMaps)
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
            _bindingVersion.Bump();
            OnBindingsChanged?.Invoke();
        }

        private static string GetDefaultBindingDirectory()
        {
#if UNITY_EDITOR
            return Path.Combine(Path.GetDirectoryName(Application.dataPath) ?? string.Empty, "Library", "AlicizaXInput");
#else
            return Application.persistentDataPath;
#endif
        }
    }
}
#endif
