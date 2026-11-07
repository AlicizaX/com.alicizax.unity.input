#if INPUTSYSTEM_SUPPORT
using System;
using UnityEngine.EventSystems;

namespace UnityEngine.UI
{
    [DisallowMultipleComponent]
    public sealed class HotkeyComponent : HotkeyComponentBase
    {
        [SerializeField] private Component _component;

        private ISubmitHandler _submitHandler;
        private IHotkeySubmitListener[] _submitListeners = Array.Empty<IHotkeySubmitListener>();
        private BaseEventData _eventData;
        private EventSystem _eventSystem;

        protected override void Reset()
        {
            base.Reset();
            ResolveSubmitTarget();
        }

        private void Awake()
        {
            ResolveSubmitTarget();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            ResolveSubmitTarget();
        }
#endif

        public override void TriggerHotkey()
        {
            if (_submitHandler == null && _submitListeners.Length == 0)
            {
                return;
            }

            EventSystem currentEventSystem = EventSystem.current;
            if (currentEventSystem == null)
            {
                return;
            }

            if (!ReferenceEquals(_eventSystem, currentEventSystem))
            {
                _eventSystem = currentEventSystem;
                _eventData = new BaseEventData(currentEventSystem);
            }

            if (_submitHandler != null && IsActive(_submitHandler))
            {
                _submitHandler.OnSubmit(_eventData);
            }

            for (int i = 0; i < _submitListeners.Length; i++)
            {
                IHotkeySubmitListener listener = _submitListeners[i];
                if (ReferenceEquals(listener, _submitHandler) || !IsActive(listener))
                {
                    continue;
                }

                listener.OnHotkeySubmit(_eventData);
            }
        }

        private static bool IsActive(object handler)
        {
            return handler is not Behaviour behaviour || behaviour.isActiveAndEnabled;
        }

        /// <summary>
        /// 解析业务提交目标（优先非监听者），并缓存同 GameObject 的热键旁路监听者。
        /// </summary>
        private void ResolveSubmitTarget()
        {
            if (_component != null && _component is not ISubmitHandler)
            {
                _component = null;
            }

            Component[] components = GetComponents<Component>();

            if (_component == null)
            {
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] is ISubmitHandler && components[i] is not IHotkeySubmitListener)
                    {
                        _component = components[i];
                        break;
                    }
                }
            }

            if (_component == null)
            {
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] is ISubmitHandler)
                    {
                        _component = components[i];
                        break;
                    }
                }
            }

            _submitHandler = _component as ISubmitHandler;

            int count = 0;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is IHotkeySubmitListener)
                {
                    count++;
                }
            }

            _submitListeners = count == 0
                ? Array.Empty<IHotkeySubmitListener>()
                : new IHotkeySubmitListener[count];

            int index = 0;
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is IHotkeySubmitListener listener)
                {
                    _submitListeners[index++] = listener;
                }
            }
        }
    }
}
#endif
