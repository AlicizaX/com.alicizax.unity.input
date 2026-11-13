#if INPUTSYSTEM_SUPPORT
using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    public abstract class InputGlyphBehaviourBase : MonoBehaviour
    {
        public enum ActionSourceMode
        {
            ActionReference,
            HotkeyTrigger
        }

        [Serializable]
        public sealed class DeviceProfileEvent
        {
            public string profileId;
            public UnityEvent onMatched;
            public UnityEvent onNotMatched;
        }

        [Header("Source")]
        [SerializeField] private ActionSourceMode actionSourceMode = ActionSourceMode.ActionReference;
        [SerializeField] private InputActionReference actionReference;
        [SerializeField] private HotkeyComponentBase hotkeyTrigger;
        [SerializeField] private string compositePartName;

        [Header("Platform Events")]
        [SerializeField] private DeviceProfileEvent[] profileEvents = Array.Empty<DeviceProfileEvent>();

        private InputAction _runtimeAction;
        private string _runtimeCompositePartName;
        private bool _useRuntimeAction;
        private string _lastInvokedProfileId;

        // 订阅时的实例：退订必须对同一实例进行，UXInput 关闭后全局属性为 null。
        private InputWatch _watch;
        private InputRebind _rebind;
        private InputGlyph _glyph;

        /// <summary>当前订阅的图标服务；未启用或输入层未初始化时为 null。</summary>
        protected InputGlyph Glyph => _glyph;
        protected string CurrentProfileId => _watch != null ? _watch.CurrentProfileId : null;
        protected string CompositePartName => _useRuntimeAction ? _runtimeCompositePartName : compositePartName;

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            AssignHotkeyTriggerIfMissing();
            AssignTargetIfMissing();
        }
#endif

        protected virtual void OnEnable()
        {
            AttachToInput();
        }

        /// <summary>
        /// 订阅当前输入会话并刷新。输入层未初始化时什么都不做（初始化不会补接入，组件需在其后启用）；
        /// 已订阅旧会话（会话重启）时先退订旧实例。
        /// </summary>
        internal void AttachToInput()
        {
            if (!UXInput.IsInitialized || ReferenceEquals(_watch, UXInput.Watch))
            {
                return;
            }

            DetachFromInput();
            AssignHotkeyTriggerIfMissing();
            AssignTargetIfMissing();
            _watch = UXInput.Watch;
            _rebind = UXInput.Rebind;
            _glyph = UXInput.Glyph;
            _watch.OnInputProfileChanged += HandleInputProfileChanged;
            _rebind.OnBindingsChanged += HandleBindingsChanged;
            RefreshGlyph();
            InvokeProfileEvents(true);
        }

        protected virtual void OnDisable()
        {
            DetachFromInput();
        }

        private void DetachFromInput()
        {
            if (_watch == null)
            {
                return;
            }

            _watch.OnInputProfileChanged -= HandleInputProfileChanged;
            _rebind.OnBindingsChanged -= HandleBindingsChanged;
            _watch = null;
            _rebind = null;
            _glyph = null;
        }

        /// <summary>
        /// 运行时动态切换用于图标解析的 InputAction，并立即刷新显示。
        /// </summary>
        /// <param name="action">目标 Action；传 null 清空显示。</param>
        /// <param name="compositePartName">
        /// Composite 子部分名，例如 Move 的 "Up"/"Down"/"Left"/"Right"。
        /// 普通绑定传 null 或空字符串。
        /// </param>
        public void SetAction(InputAction action, string compositePartName = null)
        {
            _useRuntimeAction = true;
            _runtimeAction = action;
            _runtimeCompositePartName = compositePartName;
            RefreshIfActive();
        }

        /// <summary>
        /// 清除运行时 Action 覆盖，恢复为 Inspector 配置的数据源。
        /// </summary>
        public void ClearRuntimeAction()
        {
            if (!_useRuntimeAction)
            {
                return;
            }

            _useRuntimeAction = false;
            _runtimeAction = null;
            _runtimeCompositePartName = null;
            RefreshIfActive();
        }

        /// <summary>
        /// 运行时切换数据源后刷新：已接入当前会话直接刷新；未接入（编辑器下 OnEnable 不执行、会话重启）先接入，接入时会刷新。
        /// </summary>
        private void RefreshIfActive()
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (_watch != null && ReferenceEquals(_watch, UXInput.Watch))
            {
                RefreshGlyph();
            }
            else
            {
                AttachToInput();
            }
        }

        private void HandleInputProfileChanged(InputWatch.InputProfile profile)
        {
            InvokeProfileEvents(false);
            RefreshGlyph();
        }

        private void HandleBindingsChanged()
        {
            RefreshGlyph();
        }

        protected InputAction ResolveAction()
        {
            if (_useRuntimeAction)
            {
                return _runtimeAction;
            }

            switch (actionSourceMode)
            {
                case ActionSourceMode.ActionReference:
                    return actionReference != null ? actionReference.action : null;
                case ActionSourceMode.HotkeyTrigger:
                    return hotkeyTrigger != null && hotkeyTrigger.HotkeyAction != null
                        ? hotkeyTrigger.HotkeyAction.action
                        : null;
                default:
                    return null;
            }
        }

        protected virtual void AssignTargetIfMissing()
        {
        }

        private void AssignHotkeyTriggerIfMissing()
        {
            if (actionSourceMode != ActionSourceMode.HotkeyTrigger || hotkeyTrigger != null)
            {
                return;
            }

            hotkeyTrigger = GetComponent<HotkeyComponentBase>();
        }

        private void InvokeProfileEvents(bool force)
        {
            string currentProfileId = CurrentProfileId;
            if (!force && string.Equals(_lastInvokedProfileId, currentProfileId, StringComparison.Ordinal))
            {
                return;
            }

            _lastInvokedProfileId = currentProfileId;
            for (int i = 0; i < profileEvents.Length; i++)
            {
                DeviceProfileEvent profileEvent = profileEvents[i];
                if (string.Equals(profileEvent.profileId, currentProfileId, StringComparison.OrdinalIgnoreCase))
                {
                    profileEvent.onMatched?.Invoke();
                }
                else
                {
                    profileEvent.onNotMatched?.Invoke();
                }
            }
        }

        protected abstract void RefreshGlyph();
    }
}
#endif
