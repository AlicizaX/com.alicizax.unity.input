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

        protected override void Awake()
        {
            base.Awake();
            AutoAssignTarget();
            CacheTarget();
            CacheEventData();
        }

        protected override void OnEnable()
        {
            AutoAssignTarget();
            CacheTarget();
            base.OnEnable();
        }

        protected override void OnApplicationFocus(bool hasFocus)
        {
            base.OnApplicationFocus(hasFocus);

            if (hasFocus)
            {
                CacheEventData();
            }
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
            _submitHandler = null;
            _eventData = null;
            _eventSystem = null;
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            AutoAssignTarget();
            CacheTarget();

            if (_component != null && _submitHandler == null)
            {
                _component = null;
            }
        }
#endif

        public override void HotkeyActionTrigger()
        {
            if (!isActiveAndEnabled || _submitHandler == null)
            {
                return;
            }

            EventSystem currentEventSystem = EventSystem.current;
            if (!ReferenceEquals(_eventSystem, currentEventSystem))
            {
                CacheEventData(currentEventSystem);
            }

            if (_eventData == null || _eventSystem == null)
            {
                return;
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

        private void CacheTarget()
        {
            _submitHandler = _component as ISubmitHandler;
        }

        private void CacheEventData()
        {
            CacheEventData(EventSystem.current);
        }

        private void CacheEventData(EventSystem eventSystem)
        {
            _eventSystem = eventSystem;
            if (_eventSystem == null)
            {
                _eventData = null;
                return;
            }

            _eventData = new BaseEventData(_eventSystem);
        }
    }
}
#endif
