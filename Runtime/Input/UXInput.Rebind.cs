#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.IO;
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

        private static InputActionAsset _actions;
        private static string _defaultBindingsJson = string.Empty;
        private static InputActionRebindingExtensions.RebindingOperation _operation;
        private static readonly List<InputAction> _conflictingActions = new List<InputAction>(8);
        private static readonly List<RebindChange> _preparedRebinds = new List<RebindChange>(8);
        private static readonly List<RebindChange> _conflictClears = new List<RebindChange>(8);

        /// <summary>
        /// 获取是否已通过 <see cref="Initialize"/> 指定输入动作资源。
        /// </summary>
        public static bool IsInitialized => _actions != null;
        /// <summary>
        /// 获取当前是否有交互式重绑定操作正在监听输入。
        /// </summary>
        public static bool IsRebinding => _operation != null;
        /// <summary>
        /// 获取当前是否存在等待确认或丢弃的绑定变更。
        /// </summary>
        public static bool HasPreparedRebinds => _preparedRebinds.Count > 0 || _conflictClears.Count > 0;
        /// <summary>
        /// 获取固定的绑定覆盖文件是否已存在于磁盘上。
        /// </summary>
        public static bool HasSavedBindings
        {
            get
            {
                return File.Exists(BindingFilePath);
            }
        }
        /// <summary>
        /// 获取固定的绑定覆盖 JSON 路径。编辑器使用 <c>Application.dataPath</c>，运行时使用 <c>Application.persistentDataPath</c>。
        /// </summary>
        public static string BindingFilePath => Path.Combine(GetDefaultBindingDirectory(), DefaultBindingFileName);
        internal static InputActionAsset Actions => _actions;
        internal static IReadOnlyList<RebindChange> PreparedRebinds => _preparedRebinds;
        internal static IReadOnlyList<RebindChange> ConflictClears => _conflictClears;

        /// <summary>
        /// 在交互式重绑定操作开始监听输入后触发。
        /// </summary>
        public static event Action OnRebindStarted;
        /// <summary>
        /// 在交互式重绑定操作完成或取消时触发。
        /// </summary>
        /// <remarks>第一个参数为 true 表示已捕获绑定；第二个参数在可用时描述捕获到的变更。</remarks>
        public static event Action<bool, RebindChange> OnRebindEnded;
        /// <summary>
        /// 在捕获到的绑定已暂存并等待 <see cref="ConfirmApply"/> 时触发。
        /// </summary>
        public static event Action<RebindChange> OnRebindPrepared;
        /// <summary>
        /// 在暂存绑定与已有绑定或其他待处理绑定冲突时触发。
        /// </summary>
        /// <remarks>第一个参数是新的暂存绑定，第二个参数包含冲突绑定列表。</remarks>
        public static event Action<RebindChange, IReadOnlyList<RebindChange>> OnBindingConflict;
        /// <summary>
        /// 在绑定覆盖因应用、重置、加载或导入操作发生变化后触发。
        /// </summary>
        public static event Action OnBindingsChanged;
        /// <summary>
        /// 在待处理重绑定被确认或丢弃时触发。
        /// </summary>
        /// <remarks>第一个参数为 true 表示确认，为 false 表示丢弃；第二个参数是受影响的变更快照。</remarks>
        public static event Action<bool, IReadOnlyList<RebindChange>> OnApply;
        /// <summary>
        /// 在已保存的绑定覆盖从磁盘加载后触发。
        /// </summary>
        public static event Action OnBindingsLoaded;
        /// <summary>
        /// 在绑定覆盖写入固定 JSON 文件后触发。
        /// </summary>
        public static event Action OnBindingsSaved;

        /// <summary>
        /// 使用输入动作资源初始化重绑定服务，并从固定 JSON 文件加载已保存的绑定覆盖。
        /// </summary>
        /// <param name="actions">需要管理绑定覆盖的输入动作资源。</param>
        internal static void Initialize(InputActionAsset actions)
        {
            if (_operation != null)
            {
                CancelRebinding();
            }

            _actions = actions;
            _defaultBindingsJson = _actions != null ? _actions.SaveBindingOverridesAsJson() : string.Empty;

            LoadBindingsInternal();
        }

        /// <summary>
        /// 停止当前活动的重绑定操作，清理待处理变更，并释放当前输入动作资源。
        /// </summary>
        /// <param name="clearEvents">为 true 时清除注册到此服务的全部事件订阅。</param>
        internal static void Shutdown(bool clearEvents = false)
        {
            CancelRebinding();
            ClearPreparedInternal();
            _actions = null;
            _defaultBindingsJson = string.Empty;

            if (clearEvents)
            {
                OnRebindStarted = null;
                OnRebindEnded = null;
                OnRebindPrepared = null;
                OnBindingConflict = null;
                OnBindingsChanged = null;
                OnApply = null;
                OnBindingsLoaded = null;
                OnBindingsSaved = null;
            }
        }

        /// <summary>
        /// 为动作上最匹配的键盘鼠标绑定启动交互式重绑定。
        /// </summary>
        /// <param name="action">要重绑定的动作。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginRebind(InputAction action)
        {
            return BeginRebind(action, RebindTarget.KeyboardMouse);
        }

        /// <summary>
        /// 为引用的动作启动键盘鼠标交互式重绑定。
        /// </summary>
        /// <param name="actionReference">要重绑定的动作引用。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginRebind(InputActionReference actionReference)
        {
            return BeginRebind(actionReference != null ? actionReference.action : null);
        }

        /// <summary>
        /// 为通过名称或完整路径解析到的动作启动键盘鼠标交互式重绑定。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径，例如 <c>Gameplay/Jump</c>。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginRebind(string actionName)
        {
            return BeginRebind(ResolveAction(actionName));
        }
        /// <summary>
        /// 为选定设备目标上最匹配的绑定启动交互式重绑定。
        /// </summary>
        /// <param name="action">要重绑定的动作。</param>
        /// <param name="target">要重绑定的设备目标。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginRebind(InputAction action, RebindTarget target)
        {
            RebindOptions options = RebindOptions.ForTarget(target);
            return BeginRebind(action, options);
        }

        /// <summary>
        /// 为引用动作在选定设备目标上启动交互式重绑定。
        /// </summary>
        /// <param name="actionReference">要重绑定的动作引用。</param>
        /// <param name="target">要重绑定的设备目标。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginRebind(InputActionReference actionReference, RebindTarget target)
        {
            return BeginRebind(actionReference != null ? actionReference.action : null, target);
        }

        /// <summary>
        /// 为通过名称或完整路径解析到的动作在选定设备目标上启动交互式重绑定。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径，例如 <c>Gameplay/Jump</c>。</param>
        /// <param name="target">要重绑定的设备目标。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginRebind(string actionName, RebindTarget target)
        {
            return BeginRebind(ResolveAction(actionName), target);
        }

        /// <summary>
        /// 为组合绑定的指定部分启动键盘鼠标交互式重绑定。
        /// </summary>
        /// <param name="action">拥有组合绑定的动作。</param>
        /// <param name="compositePartName">组合绑定部分名称，例如 <c>up</c>、<c>down</c>、<c>left</c> 或 <c>right</c>。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginCompositePartRebind(InputAction action, string compositePartName)
        {
            return BeginCompositePartRebind(action, compositePartName, RebindTarget.KeyboardMouse);
        }

        /// <summary>
        /// 为引用动作的组合绑定指定部分启动键盘鼠标交互式重绑定。
        /// </summary>
        /// <param name="actionReference">拥有组合绑定的动作引用。</param>
        /// <param name="compositePartName">组合绑定部分名称，例如 <c>up</c>、<c>down</c>、<c>left</c> 或 <c>right</c>。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginCompositePartRebind(InputActionReference actionReference, string compositePartName)
        {
            return BeginCompositePartRebind(actionReference != null ? actionReference.action : null, compositePartName);
        }

        /// <summary>
        /// 为通过名称或完整路径解析到的动作的指定组合绑定部分启动键盘鼠标交互式重绑定。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径，例如 <c>Gameplay/Move</c>。</param>
        /// <param name="compositePartName">组合绑定部分名称，例如 <c>up</c>、<c>down</c>、<c>left</c> 或 <c>right</c>。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginCompositePartRebind(string actionName, string compositePartName)
        {
            return BeginCompositePartRebind(ResolveAction(actionName), compositePartName);
        }

        /// <summary>
        /// 为选定设备目标上的指定组合绑定部分启动交互式重绑定。
        /// </summary>
        /// <param name="action">拥有组合绑定的动作。</param>
        /// <param name="compositePartName">组合绑定部分名称，例如 <c>up</c>、<c>down</c>、<c>left</c> 或 <c>right</c>。</param>
        /// <param name="target">要重绑定的设备目标。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginCompositePartRebind(InputAction action, string compositePartName, RebindTarget target)
        {
            RebindOptions options = RebindOptions.ForTarget(target);
            options.CompositePartName = compositePartName;
            return BeginRebind(action, options);
        }

        /// <summary>
        /// 为引用动作在选定设备目标上的指定组合绑定部分启动交互式重绑定。
        /// </summary>
        /// <param name="actionReference">拥有组合绑定的动作引用。</param>
        /// <param name="compositePartName">组合绑定部分名称，例如 <c>up</c>、<c>down</c>、<c>left</c> 或 <c>right</c>。</param>
        /// <param name="target">要重绑定的设备目标。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginCompositePartRebind(
            InputActionReference actionReference,
            string compositePartName,
            RebindTarget target)
        {
            return BeginCompositePartRebind(
                actionReference != null ? actionReference.action : null,
                compositePartName,
                target);
        }

        /// <summary>
        /// 为通过名称或完整路径解析到的动作在选定设备目标上的指定组合绑定部分启动交互式重绑定。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径，例如 <c>Gameplay/Move</c>。</param>
        /// <param name="compositePartName">组合绑定部分名称，例如 <c>up</c>、<c>down</c>、<c>left</c> 或 <c>right</c>。</param>
        /// <param name="target">要重绑定的设备目标。</param>
        /// <returns>监听成功启动时返回 true；动作或目标绑定无效时返回 false。</returns>
        public static bool BeginCompositePartRebind(string actionName, string compositePartName, RebindTarget target)
        {
            return BeginCompositePartRebind(ResolveAction(actionName), compositePartName, target);
        }

        private static bool BeginRebind(InputAction action, RebindOptions options)
        {
            if (action == null)
            {
                Log.Error("[UXInput.Rebind] Action cannot be null.");
                return false;
            }

            options.EnsureDefaults();
            options.ApplyTargetDefaults();

            int bindingIndex = FindBestBindingIndex(action, options);
            return BeginRebindAtIndex(action, bindingIndex, options);
        }

        private static bool BeginRebindAtIndex(
            InputAction action,
            int bindingIndex,
            RebindOptions options)
        {
            if (action == null)
            {
                Log.Error("[UXInput.Rebind] Action cannot be null.");
                return false;
            }

            if (!IsBindingIndexValid(action, bindingIndex))
            {
                Log.Error("[UXInput.Rebind] Binding index is invalid.");
                return false;
            }

            EnsureActions(action);
            CancelRebinding();

            bool wasActionEnabled = action.enabled;
            if (wasActionEnabled)
            {
                action.Disable();
            }

            InputActionRebindingExtensions.RebindingOperation operation = action.PerformInteractiveRebinding(bindingIndex);

            if (!string.IsNullOrEmpty(options.CancelKey))
            {
                operation.WithCancelingThrough(options.CancelKey);
            }

            if (options.ExcludedControls != null)
            {
                for (int i = 0; i < options.ExcludedControls.Length; i++)
                {
                    if (!string.IsNullOrEmpty(options.ExcludedControls[i]))
                    {
                        operation.WithControlsExcluding(options.ExcludedControls[i]);
                    }
                }
            }

            if (!string.IsNullOrEmpty(options.RequiredControlPath))
            {
                operation.WithControlsHavingToMatchPath(options.RequiredControlPath);
            }

            if (!string.IsNullOrEmpty(options.BindingGroup))
            {
                operation.WithBindingGroup(options.BindingGroup);
            }

            if (options.WaitDelay > 0f)
            {
                operation.OnMatchWaitForAnother(options.WaitDelay);
            }

            string selectedPath = null;
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

                    RebindChange change = PrepareRebindAtIndex(action, bindingIndex, overridePath, options.PrepareOnly);
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
            _operation.Start();
            return true;
        }

        /// <summary>
        /// 取消当前正在运行的交互式重绑定操作。
        /// </summary>
        public static void CancelRebinding()
        {
            if (_operation == null)
            {
                return;
            }

            _operation.Cancel();
        }

        /// <summary>
        /// 应用暂存的绑定变更，可选清理冲突的旧绑定，并可选保存到磁盘。
        /// </summary>
        /// <param name="clearConflicts">为 true 时移除与暂存绑定冲突的覆盖。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        /// <returns>存在待应用变更时返回 true；否则返回 false。</returns>
        public static bool ConfirmApply(bool clearConflicts = true, bool save = true)
        {
            if (_actions == null || !HasPreparedRebinds)
            {
                return false;
            }

            RebindChange[] snapshot = BuildApplySnapshot();

            for (int i = 0; i < _preparedRebinds.Count; i++)
            {
                RebindChange change = _preparedRebinds[i];
                if (!change.IsValid)
                {
                    continue;
                }

                change.Action.ApplyBindingOverride(change.BindingIndex, change.OverridePath);
                RaiseBindingChanged(change.Action, change.BindingIndex);
            }

            if (clearConflicts)
            {
                for (int i = 0; i < _conflictClears.Count; i++)
                {
                    RebindChange change = _conflictClears[i];
                    if (!change.IsValid)
                    {
                        continue;
                    }

                    change.Action.RemoveBindingOverride(change.BindingIndex);
                    RaiseBindingChanged(change.Action, change.BindingIndex);
                }
            }

            ClearPreparedInternal();

            if (save)
            {
                SaveBindingsInternal();
            }

            OnApply?.Invoke(true, snapshot);
            return true;
        }

        /// <summary>
        /// 丢弃全部暂存绑定变更，不进行应用。
        /// </summary>
        public static void DiscardPrepared()
        {
            if (!HasPreparedRebinds)
            {
                return;
            }

            RebindChange[] snapshot = BuildApplySnapshot();
            ClearPreparedInternal();
            OnApply?.Invoke(false, snapshot);
        }

        private static RebindChange PrepareRebindAtIndex(InputAction action, int bindingIndex, string overridePath, bool prepareOnly = true)
        {
            if (action == null || !IsBindingIndexValid(action, bindingIndex) || string.IsNullOrEmpty(overridePath))
            {
                return RebindChange.Empty;
            }

            EnsureActions(action);

            List<RebindChange> conflicts = FindConflictingBindings(action, bindingIndex, overridePath);
            RebindChange change = new RebindChange(action, bindingIndex, overridePath);
            RemovePrepared(change.Action, change.BindingIndex);
            _preparedRebinds.Add(change);
            RebuildConflictClears();

            OnRebindPrepared?.Invoke(change);
            if (conflicts.Count > 0)
            {
                OnBindingConflict?.Invoke(change, conflicts);
            }

            if (!prepareOnly)
            {
                ConfirmApply(true, true);
            }

            return change;
        }

        /// <summary>
        /// 重置动作上选中的键盘鼠标绑定覆盖。
        /// </summary>
        /// <param name="action">要重置的动作。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetBinding(InputAction action, bool save = true)
        {
            ResetBinding(action, RebindTarget.KeyboardMouse, save);
        }

        /// <summary>
        /// 重置引用动作上选中的键盘鼠标绑定覆盖。
        /// </summary>
        /// <param name="actionReference">要重置的动作引用。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetBinding(InputActionReference actionReference, bool save = true)
        {
            ResetBinding(actionReference != null ? actionReference.action : null, save);
        }

        /// <summary>
        /// 重置通过名称或完整路径解析到的动作上选中的键盘鼠标绑定覆盖。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetBinding(string actionName, bool save = true)
        {
            ResetBinding(ResolveAction(actionName), save);
        }

        /// <summary>
        /// 重置动作和设备目标上选中的绑定覆盖。
        /// </summary>
        /// <param name="action">要重置的动作。</param>
        /// <param name="target">要重置的设备目标。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetBinding(InputAction action, RebindTarget target, bool save = true)
        {
            RebindOptions options = RebindOptions.ForTarget(target);
            int bindingIndex = FindBestBindingIndex(action, options);
            if (bindingIndex >= 0)
            {
                ResetBindingAtIndex(action, bindingIndex, save);
            }
        }

        /// <summary>
        /// 重置引用动作和设备目标上选中的绑定覆盖。
        /// </summary>
        /// <param name="actionReference">要重置的动作引用。</param>
        /// <param name="target">要重置的设备目标。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetBinding(InputActionReference actionReference, RebindTarget target, bool save = true)
        {
            ResetBinding(actionReference != null ? actionReference.action : null, target, save);
        }

        /// <summary>
        /// 重置通过名称或完整路径解析到的动作和设备目标上选中的绑定覆盖。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径。</param>
        /// <param name="target">要重置的设备目标。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetBinding(string actionName, RebindTarget target, bool save = true)
        {
            ResetBinding(ResolveAction(actionName), target, save);
        }

        private static void ResetBindingAtIndex(InputAction action, int bindingIndex, bool save = true)
        {
            if (action == null || !IsBindingIndexValid(action, bindingIndex))
            {
                return;
            }

            action.RemoveBindingOverride(bindingIndex);
            RemovePrepared(action, bindingIndex);
            RemoveConflictClear(action, bindingIndex);
            if (save)
            {
                SaveBindingsInternal();
            }
            RaiseBindingChanged(action, bindingIndex);
        }

        /// <summary>
        /// 重置指定组合绑定部分上选中的键盘鼠标绑定覆盖。
        /// </summary>
        /// <param name="action">拥有组合绑定的动作。</param>
        /// <param name="compositePartName">要重置的组合绑定部分名称。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetCompositePartBinding(
            InputAction action,
            string compositePartName,
            bool save = true)
        {
            ResetCompositePartBinding(action, compositePartName, RebindTarget.KeyboardMouse, save);
        }

        /// <summary>
        /// 重置引用动作的指定组合绑定部分上选中的键盘鼠标绑定覆盖。
        /// </summary>
        /// <param name="actionReference">拥有组合绑定的动作引用。</param>
        /// <param name="compositePartName">要重置的组合绑定部分名称。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetCompositePartBinding(
            InputActionReference actionReference,
            string compositePartName,
            bool save = true)
        {
            ResetCompositePartBinding(
                actionReference != null ? actionReference.action : null,
                compositePartName,
                save);
        }

        /// <summary>
        /// 重置指定组合绑定部分和设备目标上选中的绑定覆盖。
        /// </summary>
        /// <param name="action">拥有组合绑定的动作。</param>
        /// <param name="compositePartName">要重置的组合绑定部分名称。</param>
        /// <param name="target">要重置的设备目标。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetCompositePartBinding(
            InputAction action,
            string compositePartName,
            RebindTarget target,
            bool save = true)
        {
            RebindOptions options = RebindOptions.ForTarget(target);
            options.CompositePartName = compositePartName;
            int bindingIndex = FindBestBindingIndex(action, options);
            if (bindingIndex >= 0)
            {
                ResetBindingAtIndex(action, bindingIndex, save);
            }
        }

        /// <summary>
        /// 重置引用动作的指定组合绑定部分和设备目标上选中的绑定覆盖。
        /// </summary>
        /// <param name="actionReference">拥有组合绑定的动作引用。</param>
        /// <param name="compositePartName">要重置的组合绑定部分名称。</param>
        /// <param name="target">要重置的设备目标。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetCompositePartBinding(
            InputActionReference actionReference,
            string compositePartName,
            RebindTarget target,
            bool save = true)
        {
            ResetCompositePartBinding(
                actionReference != null ? actionReference.action : null,
                compositePartName,
                target,
                save);
        }

        /// <summary>
        /// 重置通过名称或完整路径解析到的动作的指定组合绑定部分和设备目标上选中的绑定覆盖。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径。</param>
        /// <param name="compositePartName">要重置的组合绑定部分名称。</param>
        /// <param name="target">要重置的设备目标。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetCompositePartBinding(
            string actionName,
            string compositePartName,
            RebindTarget target,
            bool save = true)
        {
            ResetCompositePartBinding(ResolveAction(actionName), compositePartName, target, save);
        }

        /// <summary>
        /// 重置通过名称或完整路径解析到的动作的指定组合绑定部分上选中的键盘鼠标绑定覆盖。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径。</param>
        /// <param name="compositePartName">要重置的组合绑定部分名称。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetCompositePartBinding(
            string actionName,
            string compositePartName,
            bool save = true)
        {
            ResetCompositePartBinding(ResolveAction(actionName), compositePartName, save);
        }

        /// <summary>
        /// 移除单个动作上的全部绑定覆盖。
        /// </summary>
        /// <param name="action">要移除绑定覆盖的动作。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetActionBindings(InputAction action, bool save = true)
        {
            if (action == null)
            {
                return;
            }

            action.RemoveAllBindingOverrides();
            RemovePrepared(action);
            RemoveConflictClear(action);
            if (save)
            {
                SaveBindingsInternal();
            }
            RaiseBindingChanged(action, -1);
        }

        /// <summary>
        /// 移除引用动作上的全部绑定覆盖。
        /// </summary>
        /// <param name="actionReference">要移除绑定覆盖的动作引用。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetActionBindings(InputActionReference actionReference, bool save = true)
        {
            ResetActionBindings(actionReference != null ? actionReference.action : null, save);
        }

        /// <summary>
        /// 移除通过名称或完整路径解析到的动作上的全部绑定覆盖。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径。</param>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
        public static void ResetActionBindings(string actionName, bool save = true)
        {
            ResetActionBindings(ResolveAction(actionName), save);
        }

        /// <summary>
        /// 移除当前输入动作资源上的全部绑定覆盖。
        /// </summary>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
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

            ClearPreparedInternal();
            if (save)
            {
                SaveBindingsInternal();
            }
            RaiseBindingChanged(null, -1);
        }

        /// <summary>
        /// 恢复 <see cref="Initialize"/> 时记录的绑定覆盖，并可选保存。
        /// </summary>
        /// <param name="save">为 true 时将最终绑定覆盖写入固定 JSON 文件。</param>
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

            ClearPreparedInternal();
            if (save)
            {
                SaveBindingsInternal();
            }
            RaiseBindingChanged(null, -1);
        }

        private static void SaveBindingsInternal()
        {
            if (_actions == null)
            {
                Log.Warning("[UXInput.Rebind] InputActionAsset is not set. Cannot save bindings.");
                return;
            }

            string json = _actions.SaveBindingOverridesAsJson();
            string path = BindingFilePath;
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllText(path, json);
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
            OnBindingsLoaded?.Invoke();
            RaiseBindingChanged(null, -1);
        }

        /// <summary>
        /// 将当前绑定覆盖导出为 Unity Input System JSON。
        /// </summary>
        /// <returns>绑定覆盖 JSON；未初始化输入动作资源时返回空字符串。</returns>
        public static string ExportBindingsJson()
        {
            return _actions != null ? _actions.SaveBindingOverridesAsJson() : string.Empty;
        }

        /// <summary>
        /// 将 Unity Input System 绑定覆盖 JSON 导入当前输入动作资源。
        /// </summary>
        /// <param name="json">要导入的绑定覆盖 JSON。</param>
        /// <param name="save">为 true 时将导入后的绑定覆盖写入固定 JSON 文件。</param>
        /// <returns>导入成功时返回 true；否则返回 false。</returns>
        public static bool ImportBindingsJson(string json, bool save = true)
        {
            if (_actions == null || string.IsNullOrEmpty(json))
            {
                return false;
            }

            _actions.LoadBindingOverridesFromJson(json);
            ClearPreparedInternal();
            if (save)
            {
                SaveBindingsInternal();
            }
            RaiseBindingChanged(null, -1);
            return true;
        }

        private static IReadOnlyList<InputAction> GetConflictingActionsAtIndex(InputAction action, int bindingIndex)
        {
            _conflictingActions.Clear();
            if (action == null || !IsBindingIndexValid(action, bindingIndex))
            {
                return _conflictingActions;
            }

            EnsureActions(action);
            string path = action.bindings[bindingIndex].effectivePath;
            if (string.IsNullOrEmpty(path))
            {
                return _conflictingActions;
            }

            AddConflictingActions(action, bindingIndex, path, _conflictingActions);
            return _conflictingActions;
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

            return InputActionProvider.TryResolveAction(actionName, out InputAction resolved)
                ? resolved
                : null;
        }

        /// <summary>
        /// 获取动作上选中的键盘鼠标绑定显示文本。
        /// </summary>
        /// <param name="action">要查询的动作。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(InputAction action)
        {
            return GetBindingDisplayString(action, RebindTarget.KeyboardMouse);
        }

        /// <summary>
        /// 获取动作和设备目标上选中的绑定显示文本。
        /// </summary>
        /// <param name="action">要查询的动作。</param>
        /// <param name="target">要查询的设备目标。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(InputAction action, RebindTarget target)
        {
            RebindOptions options = RebindOptions.ForTarget(target);
            int bindingIndex = FindBestBindingIndex(action, options);
            return bindingIndex >= 0 ? GetBindingDisplayStringAtIndex(action, bindingIndex) : string.Empty;
        }

        /// <summary>
        /// 获取引用动作上选中的键盘鼠标绑定显示文本。
        /// </summary>
        /// <param name="actionReference">要查询的动作引用。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(InputActionReference actionReference)
        {
            return GetBindingDisplayString(actionReference != null ? actionReference.action : null);
        }

        /// <summary>
        /// 获取引用动作和选定设备目标上的绑定显示文本。
        /// </summary>
        /// <param name="actionReference">要查询的动作引用。</param>
        /// <param name="target">要查询的设备目标。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(InputActionReference actionReference, RebindTarget target)
        {
            return GetBindingDisplayString(actionReference != null ? actionReference.action : null, target);
        }

        /// <summary>
        /// 获取通过名称或完整路径解析到的动作上选中的键盘鼠标绑定显示文本。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(string actionName)
        {
            return GetBindingDisplayString(ResolveAction(actionName));
        }

        /// <summary>
        /// 获取通过名称或完整路径解析到的动作和选定设备目标上的绑定显示文本。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径。</param>
        /// <param name="target">要查询的设备目标。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(string actionName, RebindTarget target)
        {
            return GetBindingDisplayString(ResolveAction(actionName), target);
        }

        /// <summary>
        /// 获取指定组合绑定部分上选中的键盘鼠标绑定显示文本。
        /// </summary>
        /// <param name="action">拥有组合绑定的动作。</param>
        /// <param name="compositePartName">要查询的组合绑定部分名称。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(InputAction action, string compositePartName)
        {
            return GetBindingDisplayString(action, compositePartName, RebindTarget.KeyboardMouse);
        }

        /// <summary>
        /// 获取引用动作的指定组合绑定部分上选中的键盘鼠标绑定显示文本。
        /// </summary>
        /// <param name="actionReference">拥有组合绑定的动作引用。</param>
        /// <param name="compositePartName">要查询的组合绑定部分名称。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(InputActionReference actionReference, string compositePartName)
        {
            return GetBindingDisplayString(actionReference != null ? actionReference.action : null, compositePartName);
        }

        /// <summary>
        /// 获取通过名称或完整路径解析到的动作的指定组合绑定部分上选中的键盘鼠标绑定显示文本。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径。</param>
        /// <param name="compositePartName">要查询的组合绑定部分名称。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(string actionName, string compositePartName)
        {
            return GetBindingDisplayString(ResolveAction(actionName), compositePartName);
        }

        /// <summary>
        /// 获取指定组合绑定部分在选定设备目标上的绑定显示文本。
        /// </summary>
        /// <param name="action">拥有组合绑定的动作。</param>
        /// <param name="compositePartName">要查询的组合绑定部分名称。</param>
        /// <param name="target">要查询的设备目标。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(InputAction action, string compositePartName, RebindTarget target)
        {
            RebindOptions options = RebindOptions.ForTarget(target);
            options.CompositePartName = compositePartName;
            int bindingIndex = FindBestBindingIndex(action, options);
            return bindingIndex >= 0 ? GetBindingDisplayStringAtIndex(action, bindingIndex) : string.Empty;
        }

        /// <summary>
        /// 获取引用动作的指定组合绑定部分在选定设备目标上的绑定显示文本。
        /// </summary>
        /// <param name="actionReference">拥有组合绑定的动作引用。</param>
        /// <param name="compositePartName">要查询的组合绑定部分名称。</param>
        /// <param name="target">要查询的设备目标。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(
            InputActionReference actionReference,
            string compositePartName,
            RebindTarget target)
        {
            return GetBindingDisplayString(
                actionReference != null ? actionReference.action : null,
                compositePartName,
                target);
        }

        /// <summary>
        /// 获取通过名称或完整路径解析到的动作的指定组合绑定部分在选定设备目标上的绑定显示文本。
        /// </summary>
        /// <param name="actionName">动作名称或完整动作路径。</param>
        /// <param name="compositePartName">要查询的组合绑定部分名称。</param>
        /// <param name="target">要查询的设备目标。</param>
        /// <returns>绑定显示文本；未找到绑定时返回空字符串。</returns>
        public static string GetBindingDisplayString(string actionName, string compositePartName, RebindTarget target)
        {
            return GetBindingDisplayString(ResolveAction(actionName), compositePartName, target);
        }

        private static string GetBindingDisplayStringAtIndex(InputAction action, int bindingIndex)
        {
            return action != null && IsBindingIndexValid(action, bindingIndex)
                ? action.GetBindingDisplayString(bindingIndex)
                : string.Empty;
        }

        private static void FinishRebinding(InputAction action, bool wasActionEnabled)
        {
            if (_operation != null)
            {
                _operation.Dispose();
                _operation = null;
            }

            if (wasActionEnabled && action != null)
            {
                action.Enable();
            }
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

        private static int FindBestBindingIndex(InputAction action, RebindOptions options)
        {
            if (action == null)
            {
                return -1;
            }

            int fallbackPart = -1;
            int fallbackNonComposite = -1;
            bool searchingForCompositePart = !string.IsNullOrEmpty(options.CompositePartName);

            for (int i = 0; i < action.bindings.Count; i++)
            {
                InputBinding binding = action.bindings[i];

                if (searchingForCompositePart)
                {
                    if (!binding.isPartOfComposite ||
                        !string.Equals(binding.name, options.CompositePartName, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                }
                else if (binding.isComposite)
                {
                    continue;
                }

                bool matchesTarget = MatchesTarget(binding, options);

                if (binding.isPartOfComposite)
                {
                    if (fallbackPart < 0)
                    {
                        fallbackPart = i;
                    }

                    if (matchesTarget)
                    {
                        return i;
                    }
                }
                else
                {
                    if (fallbackNonComposite < 0)
                    {
                        fallbackNonComposite = i;
                    }

                    if (matchesTarget)
                    {
                        return i;
                    }
                }
            }

            return fallbackNonComposite >= 0 ? fallbackNonComposite : fallbackPart;
        }

        private static bool MatchesTarget(InputBinding binding, RebindOptions options)
        {
            if (!string.IsNullOrEmpty(options.RequiredControlPath))
            {
                return PathMatches(binding.path, options.RequiredControlPath) ||
                       PathMatches(binding.effectivePath, options.RequiredControlPath);
            }

            if (!string.IsNullOrEmpty(options.BindingGroup))
            {
                return BindingGroupsContain(binding.groups, options.BindingGroup);
            }

            return true;
        }

        private static bool PathMatches(string bindingPath, string requiredControlPath)
        {
            return !string.IsNullOrEmpty(bindingPath) &&
                   !string.IsNullOrEmpty(requiredControlPath) &&
                   bindingPath.StartsWith(requiredControlPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool BindingGroupsContain(string groups, string group)
        {
            if (string.IsNullOrEmpty(groups) || string.IsNullOrEmpty(group))
            {
                return false;
            }

            string[] splitGroups = groups.Split(';');
            for (int i = 0; i < splitGroups.Length; i++)
            {
                if (string.Equals(splitGroups[i], group, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetDefaultBindingDirectory()
        {
#if UNITY_EDITOR
            return Application.dataPath;
#else
            return Application.persistentDataPath;
#endif
        }

        private static List<RebindChange> FindConflictingBindings(InputAction action, int bindingIndex, string path)
        {
            List<RebindChange> conflicts = new List<RebindChange>(4);
            if (_actions == null || string.IsNullOrEmpty(path))
            {
                return conflicts;
            }

            for (int mapIndex = 0; mapIndex < _actions.actionMaps.Count; mapIndex++)
            {
                InputActionMap map = _actions.actionMaps[mapIndex];
                for (int actionIndex = 0; actionIndex < map.actions.Count; actionIndex++)
                {
                    InputAction otherAction = map.actions[actionIndex];
                    for (int otherBindingIndex = 0; otherBindingIndex < otherAction.bindings.Count; otherBindingIndex++)
                    {
                        if (otherAction == action && otherBindingIndex == bindingIndex)
                        {
                            continue;
                        }

                        InputBinding binding = otherAction.bindings[otherBindingIndex];
                        if (!string.Equals(binding.effectivePath, path, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (TryGetPrepared(otherAction, otherBindingIndex, out RebindChange preparedConflict))
                        {
                            if (string.Equals(preparedConflict.OverridePath, path, StringComparison.Ordinal))
                            {
                                AddUnique(conflicts, preparedConflict);
                            }

                            continue;
                        }

                        conflicts.Add(new RebindChange(otherAction, otherBindingIndex, binding.effectivePath));
                    }
                }
            }

            for (int i = 0; i < _preparedRebinds.Count; i++)
            {
                RebindChange prepared = _preparedRebinds[i];
                if (prepared.Action == action && prepared.BindingIndex == bindingIndex)
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

        private static void AddConflictingActions(InputAction action, int bindingIndex, string path, List<InputAction> conflicts)
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
                    InputAction otherAction = map.actions[actionIndex];
                    if (otherAction == action)
                    {
                        continue;
                    }

                    for (int otherBindingIndex = 0; otherBindingIndex < otherAction.bindings.Count; otherBindingIndex++)
                    {
                        InputBinding binding = otherAction.bindings[otherBindingIndex];
                        if (string.Equals(binding.effectivePath, path, StringComparison.Ordinal))
                        {
                            conflicts.Add(otherAction);
                            break;
                        }
                    }
                }
            }
        }

        private static RebindChange[] BuildApplySnapshot()
        {
            RebindChange[] snapshot = new RebindChange[_preparedRebinds.Count + _conflictClears.Count];
            int index = 0;
            for (int i = 0; i < _preparedRebinds.Count; i++)
            {
                snapshot[index] = _preparedRebinds[i];
                index++;
            }

            for (int i = 0; i < _conflictClears.Count; i++)
            {
                snapshot[index] = _conflictClears[i];
                index++;
            }

            return snapshot;
        }

        private static void ClearPreparedInternal()
        {
            _preparedRebinds.Clear();
            _conflictClears.Clear();
        }

        private static void RemovePrepared(InputAction action)
        {
            for (int i = _preparedRebinds.Count - 1; i >= 0; i--)
            {
                if (_preparedRebinds[i].Action == action)
                {
                    _preparedRebinds.RemoveAt(i);
                }
            }
        }

        private static void RemovePrepared(InputAction action, int bindingIndex)
        {
            for (int i = _preparedRebinds.Count - 1; i >= 0; i--)
            {
                RebindChange change = _preparedRebinds[i];
                if (change.Action == action && change.BindingIndex == bindingIndex)
                {
                    _preparedRebinds.RemoveAt(i);
                }
            }
        }

        private static void RemoveConflictClear(InputAction action)
        {
            for (int i = _conflictClears.Count - 1; i >= 0; i--)
            {
                if (_conflictClears[i].Action == action)
                {
                    _conflictClears.RemoveAt(i);
                }
            }
        }

        private static void RemoveConflictClear(InputAction action, int bindingIndex)
        {
            for (int i = _conflictClears.Count - 1; i >= 0; i--)
            {
                RebindChange change = _conflictClears[i];
                if (change.Action == action && change.BindingIndex == bindingIndex)
                {
                    _conflictClears.RemoveAt(i);
                }
            }
        }

        private static void RebuildConflictClears()
        {
            _conflictClears.Clear();

            for (int i = 0; i < _preparedRebinds.Count; i++)
            {
                RebindChange change = _preparedRebinds[i];
                if (!change.IsValid)
                {
                    continue;
                }

                AddActualConflictsForPrepared(change);

                for (int previousIndex = 0; previousIndex < i; previousIndex++)
                {
                    RebindChange previous = _preparedRebinds[previousIndex];
                    if (previous.IsValid &&
                        string.Equals(previous.OverridePath, change.OverridePath, StringComparison.Ordinal))
                    {
                        AddUnique(_conflictClears, previous);
                    }
                }
            }
        }

        private static void AddActualConflictsForPrepared(RebindChange change)
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
                    InputAction otherAction = map.actions[actionIndex];
                    for (int bindingIndex = 0; bindingIndex < otherAction.bindings.Count; bindingIndex++)
                    {
                        if (otherAction == change.Action && bindingIndex == change.BindingIndex)
                        {
                            continue;
                        }

                        if (TryGetPrepared(otherAction, bindingIndex, out _))
                        {
                            continue;
                        }

                        InputBinding binding = otherAction.bindings[bindingIndex];
                        if (string.Equals(binding.effectivePath, change.OverridePath, StringComparison.Ordinal))
                        {
                            AddUnique(_conflictClears, new RebindChange(otherAction, bindingIndex, binding.effectivePath));
                        }
                    }
                }
            }
        }

        private static bool TryGetPrepared(InputAction action, int bindingIndex, out RebindChange change)
        {
            for (int i = 0; i < _preparedRebinds.Count; i++)
            {
                RebindChange prepared = _preparedRebinds[i];
                if (prepared.Action == action && prepared.BindingIndex == bindingIndex)
                {
                    change = prepared;
                    return true;
                }
            }

            change = RebindChange.Empty;
            return false;
        }

        private static void AddUnique(List<RebindChange> list, RebindChange change)
        {
            for (int i = 0; i < list.Count; i++)
            {
                RebindChange existing = list[i];
                if (existing.Action == change.Action && existing.BindingIndex == change.BindingIndex)
                {
                    return;
                }
            }

            list.Add(change);
        }

        private static void RaiseBindingChanged(InputAction action, int bindingIndex)
        {
            OnBindingsChanged?.Invoke();
        }
    }
}

internal struct RebindOptions
{
    internal RebindTarget Target;
    internal string CompositePartName;
    internal string CancelKey;
    internal string[] ExcludedControls;
    internal float WaitDelay;
    internal string BindingGroup;
    internal string RequiredControlPath;
    internal bool PrepareOnly;

    internal static RebindOptions Default
    {
        get
        {
            return new RebindOptions
            {
                Target = RebindTarget.KeyboardMouse,
                CompositePartName = null,
                CancelKey = "<Keyboard>/escape",
                ExcludedControls = new[]
                {
                    "<Mouse>/position",
                    "<Mouse>/delta",
                    "<Pointer>/position",
                    "<Pointer>/delta"
                },
                WaitDelay = 0.1f,
                BindingGroup = "KeyboardMouse",
                RequiredControlPath = "<Keyboard>",
                PrepareOnly = true
            };
        }
    }

    internal static RebindOptions ForTarget(RebindTarget target)
    {
        RebindOptions options = Default;
        options.Target = target;
        options.BindingGroup = null;
        options.RequiredControlPath = null;
        options.ApplyTargetDefaults();
        return options;
    }

    internal void EnsureDefaults()
    {
        if (string.IsNullOrEmpty(BindingGroup) &&
            string.IsNullOrEmpty(RequiredControlPath) &&
            string.IsNullOrEmpty(CancelKey) &&
            ExcludedControls == null &&
            WaitDelay <= 0f)
        {
            RebindOptions defaults = Default;
            Target = defaults.Target;
            CancelKey = defaults.CancelKey;
            ExcludedControls = defaults.ExcludedControls;
            WaitDelay = defaults.WaitDelay;
            BindingGroup = defaults.BindingGroup;
            RequiredControlPath = defaults.RequiredControlPath;
            PrepareOnly = defaults.PrepareOnly;
        }

        if (string.IsNullOrEmpty(CancelKey))
        {
            CancelKey = "<Keyboard>/escape";
        }

        if (ExcludedControls == null)
        {
            ExcludedControls = Default.ExcludedControls;
        }

        if (WaitDelay <= 0f)
        {
            WaitDelay = 0.1f;
        }
    }

    internal void ApplyTargetDefaults()
    {
        switch (Target)
        {
            case RebindTarget.KeyboardMouse:
                if (string.IsNullOrEmpty(BindingGroup))
                {
                    BindingGroup = "KeyboardMouse";
                }

                if (string.IsNullOrEmpty(RequiredControlPath))
                {
                    RequiredControlPath = "<Keyboard>";
                }
                break;
            case RebindTarget.Gamepad:
                if (string.IsNullOrEmpty(BindingGroup))
                {
                    BindingGroup = "Gamepad";
                }

                if (string.IsNullOrEmpty(RequiredControlPath))
                {
                    RequiredControlPath = "<Gamepad>";
                }
                break;
        }
    }
}

/// <summary>
/// 标识重绑定、重置和显示文本接口使用的设备绑定集。
/// </summary>
public enum RebindTarget
{
    /// <summary>
    /// 选择键盘鼠标绑定组。
    /// </summary>
    KeyboardMouse,

    /// <summary>
    /// 选择手柄绑定组。
    /// </summary>
    Gamepad
}

/// <summary>
/// 描述重绑定服务捕获到的暂存绑定覆盖。
/// </summary>
public struct RebindChange
{
    /// <summary>
    /// 表示空的或无效的绑定变更。
    /// </summary>
    public static readonly RebindChange Empty = new RebindChange(null, -1, null);

    /// <summary>
    /// 获取拥有此绑定变更的动作。
    /// </summary>
    public readonly InputAction Action;
    internal readonly int BindingIndex;

    /// <summary>
    /// 获取重绑定操作捕获到的覆盖控制路径。
    /// </summary>
    public readonly string OverridePath;

    /// <summary>
    /// 获取此变更是否包含有效动作、绑定索引和覆盖路径。
    /// </summary>
    public bool IsValid => Action != null && BindingIndex >= 0 && !string.IsNullOrEmpty(OverridePath);

    internal RebindChange(InputAction action, int bindingIndex, string overridePath)
    {
        Action = action;
        BindingIndex = bindingIndex;
        OverridePath = overridePath;
    }

    /// <summary>
    /// 返回描述动作和覆盖路径的诊断字符串。
    /// </summary>
    /// <returns>此绑定变更的诊断字符串。</returns>
    public override string ToString()
    {
        string actionName = Action != null ? Action.name : "<null>";
        return string.Concat(actionName, "[", BindingIndex, "] -> ", OverridePath);
    }
}
#endif
