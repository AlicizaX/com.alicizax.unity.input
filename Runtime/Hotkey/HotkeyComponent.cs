#if INPUTSYSTEM_SUPPORT
using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.Serialization;

namespace AlicizaX.Input
{
    /// <summary>
    /// 单个热键组件；同一 GameObject 可挂多个，各自持有提交目标和 action。
    /// 提交目标和旁路监听者在编辑器期解析并序列化；运行时直接使用序列化结果，不再扫描组件。
    /// </summary>
    public sealed class HotkeyComponent : HotkeyComponentBase
    {
        // 编辑器期由 ResolveSubmitTargets 写回的已选提交目标。
        [FormerlySerializedAs("_component")]
        [SerializeField] private Component _submitTarget;

        [SerializeField] private Component[] _listenerComponents = Array.Empty<Component>();

        private BaseEventData _eventData;
        private EventSystem _eventSystem;

#if UNITY_EDITOR
        protected override void Reset()
        {
            base.Reset();
            ResolveSubmitTargets();
        }

        protected override void OnValidate()
        {
            base.OnValidate();
            ResolveSubmitTargets();
        }

        /// <summary>
        /// 编辑器期解析并写回 _submitTarget / _listenerComponents。
        /// 运行时不得调用：目标必须来自预制体序列化。
        /// </summary>
        private void ResolveSubmitTargets()
        {
            if (_submitTarget != null && _submitTarget is not ISubmitHandler)
                _submitTarget = null;

            Component[] components = GetComponents<Component>();
            Component fallback = null;
            int listenerCount = 0;

            for (int i = 0; i < components.Length; i++)
            {
                Component component = components[i];
                bool isListener = component is IHotkeySubmitListener;
                if (isListener)
                    listenerCount++;

                if (component is not ISubmitHandler)
                    continue;

                if (isListener)
                {
                    if (fallback == null)
                        fallback = component;
                    continue;
                }

                if (_submitTarget == null)
                    _submitTarget = component;
            }

            if (_submitTarget == null)
                _submitTarget = fallback;

            _listenerComponents = listenerCount == 0
                ? Array.Empty<Component>()
                : new Component[listenerCount];

            int index = 0;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is IHotkeySubmitListener)
                    _listenerComponents[index++] = components[i];
            }
        }
#endif

        public override void TriggerHotkey()
        {
            Component[] listeners = _listenerComponents;
            if (_submitTarget == null && (listeners == null || listeners.Length == 0))
                return;

            EventSystem currentEventSystem = EventSystem.current;
            if (currentEventSystem == null)
                return;

            if (!ReferenceEquals(_eventSystem, currentEventSystem))
            {
                _eventSystem = currentEventSystem;
                _eventData = new BaseEventData(currentEventSystem);
            }
            else
            {
                // 复用的事件数据必须清掉上次的 used 标记，否则任一 handler 调用 Use() 后之后所有提交都被视为已处理。
                _eventData.Reset();
            }

            if (_submitTarget is Behaviour submitBehaviour
                && IsActive(submitBehaviour)
                && _submitTarget is ISubmitHandler submitHandler)
            {
                submitHandler.OnSubmit(_eventData);
            }

            if (listeners == null)
                return;

            for (int i = 0; i < listeners.Length; i++)
            {
                Component component = listeners[i];
                if (ReferenceEquals(component, _submitTarget)
                    || component is not Behaviour listenerBehaviour
                    || !IsActive(listenerBehaviour)
                    || component is not IHotkeySubmitListener listener)
                {
                    continue;
                }

                listener.OnHotkeySubmit(_eventData);
            }
        }

        private static bool IsActive(Behaviour behaviour)
        {
            // 接口引用比较不会走 UnityEngine.Object 的 fake-null；必须显式判空后再读原生属性。
            return behaviour != null && behaviour.isActiveAndEnabled;
        }
    }
}
#endif
