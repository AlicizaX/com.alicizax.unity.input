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

        internal bool IsRegistered;
        internal InputAction RegisteredAction;
        internal UIHolderObjectBase RegisteredHolder;
        internal EHotkeyPressType RegisteredPressType;

        protected virtual void Reset()
        {
            AutoAssignHolder(true);
        }

        protected virtual void OnEnable()
        {
            AutoAssignHolder(false);
            this.BindHotKey();
        }

        protected virtual void OnDisable()
        {
            this.UnBindHotKey();
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            AutoAssignHolder(true);
        }
#endif

        public abstract void HotkeyActionTrigger();

        /// <summary>
        /// 解析所属 Holder。运行时只在字段为空时解析一次，避免每次启用覆盖序列化结果。
        /// </summary>
        protected void AutoAssignHolder(bool force)
        {
            if (!force && _holder != null)
            {
                return;
            }

            _holder = GetComponentInParent<UIHolderObjectBase>(true);
        }
    }
}
#endif
