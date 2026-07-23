#if INPUTSYSTEM_SUPPORT
using AlicizaX.UI.Runtime;
using UnityEngine.InputSystem;

namespace UnityEngine.UI
{
    public abstract class HotkeyComponentBase : MonoBehaviour
    {
        [SerializeField] private UIHolderObjectBase _holder;
        [SerializeField] private InputActionReference _hotkeyAction;
        [SerializeField] private EHotkeyPressType _hotkeyPressType = EHotkeyPressType.Performed;
        [SerializeField] private bool _hotkeyConsumesInput = true;

        public InputActionReference HotkeyAction
        {
            get => _hotkeyAction;
            set
            {
                if (ReferenceEquals(_hotkeyAction, value))
                {
                    return;
                }

                bool shouldRebind = Application.isPlaying && isActiveAndEnabled;
                if (shouldRebind)
                {
                    this.UnBindHotKey();
                }

                _hotkeyAction = value;

                if (shouldRebind)
                {
                    this.BindHotKey();
                }
            }
        }

        public EHotkeyPressType HotkeyPressType => _hotkeyPressType;
        public UIHolderObjectBase HotkeyHolder => _holder;
        public bool HotkeyConsumesInput => _hotkeyConsumesInput;

        protected virtual void Reset()
        {
            AutoAssignHolder();
        }

        protected virtual void Awake()
        {
            AutoAssignHolder();
        }

        protected virtual void OnEnable()
        {
            AutoAssignHolder();
            this.BindHotKey();
        }

        protected virtual void OnDisable()
        {
            this.UnBindHotKey();
        }

        protected virtual void OnDestroy()
        {
            this.UnBindHotKey();
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            AutoAssignHolder();

            if (Application.isPlaying && isActiveAndEnabled)
            {
                this.UnBindHotKey();
                this.BindHotKey();
            }
        }
#endif

        public abstract void HotkeyActionTrigger();

        protected void AutoAssignHolder()
        {
            if (_holder != null && _holder.IsValid())
            {
                return;
            }

            _holder = GetComponentInParent<UIHolderObjectBase>(true);
        }
    }
}
#endif
