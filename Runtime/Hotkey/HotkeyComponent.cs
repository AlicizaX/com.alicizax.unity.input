#if INPUTSYSTEM_SUPPORT
using AlicizaX.UI.Runtime;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace UnityEngine.UI
{
    [DisallowMultipleComponent]
    public sealed class HotkeyComponent : MonoBehaviour, IHotkeyTrigger
    {
        [SerializeField] private Component _component;
        [SerializeField] private UIHolderObjectBase _holder;
        [SerializeField] private InputActionReference _hotkeyAction;
        [SerializeField] private EHotkeyPressType _hotkeyPressType = EHotkeyPressType.Performed;

        private ISubmitHandler _submitHandler;
        private BaseEventData _eventData;
        private EventSystem _eventSystem;

        public InputActionReference HotkeyAction
        {
            get => _hotkeyAction;
            set => _hotkeyAction = value;
        }

        public EHotkeyPressType HotkeyPressType => _hotkeyPressType;
        public UIHolderObjectBase HotkeyHolder => _holder;

        private void Reset()
        {
            AutoAssignTarget();
            AutoAssignHolder();
        }

        private void Awake()
        {
            AutoAssignTarget();
            AutoAssignHolder();
            CacheTarget();
            CacheEventData();
        }

        private void OnEnable()
        {
            AutoAssignTarget();
            AutoAssignHolder();
            CacheTarget();
            ((IHotkeyTrigger)this).BindHotKey();
        }

        private void OnDisable()
        {
            ((IHotkeyTrigger)this).UnBindHotKey();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (hasFocus)
            {
                CacheEventData();
            }
        }

        private void OnDestroy()
        {
            ((IHotkeyTrigger)this).UnBindHotKey();
            _submitHandler = null;
            _eventData = null;
            _eventSystem = null;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            AutoAssignTarget();
            AutoAssignHolder();
            CacheTarget();

            if (_component != null && _submitHandler == null)
            {
                _component = null;
            }
        }
#endif

        public void HotkeyActionTrigger()
        {
            if (!isActiveAndEnabled || _submitHandler == null)
            {
                return;
            }

            if (_eventData == null)
            {
                return;
            }

            if (!ReferenceEquals(_eventSystem, EventSystem.current))
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

        private void AutoAssignHolder()
        {
            if (_holder != null && _holder.IsValid())
            {
                return;
            }

            _holder = GetComponentInParent<UIHolderObjectBase>(true);
        }

        private void CacheTarget()
        {
            _submitHandler = _component as ISubmitHandler;
        }

        private void CacheEventData()
        {
            _eventSystem = EventSystem.current;
            _eventData = new BaseEventData(_eventSystem);
        }
    }
}
#endif
