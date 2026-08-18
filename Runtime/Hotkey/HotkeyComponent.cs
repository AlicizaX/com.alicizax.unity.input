#if INPUTSYSTEM_SUPPORT
using UnityEngine.EventSystems;

namespace UnityEngine.UI
{
    [DisallowMultipleComponent]
    public sealed class HotkeyComponent : HotkeyComponentBase
    {
        [SerializeField] private Component _component;

        private ISubmitHandler _submitHandler;
        private BaseEventData _eventData;
        private EventSystem _eventSystem;

        protected override void Reset()
        {
            base.Reset();
            AutoAssignTarget();
        }

        private void Awake()
        {
            AutoAssignTarget();
            _submitHandler = _component as ISubmitHandler;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            AutoAssignTarget();
            _submitHandler = _component as ISubmitHandler;
            if (_component != null && _submitHandler == null)
            {
                _component = null;
            }
        }
#endif

        public override void HotkeyActionTrigger()
        {
            if (_submitHandler == null)
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

            _submitHandler.OnSubmit(_eventData);
        }

        private void AutoAssignTarget()
        {
            if (_component != null)
            {
                return;
            }

            if (TryGetComponent(typeof(ISubmitHandler), out Component submitHandler))
            {
                _component = submitHandler;
            }
        }
    }
}
#endif
