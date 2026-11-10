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
                    UnregisterHotkey();
                }

                _hotkeyAction = value;

                if (shouldRebind)
                {
                    RegisterHotkey();
                }
            }
        }

        public EHotkeyPressType HotkeyPressType => _hotkeyPressType;
        public UIHolderObjectBase HotkeyHolder => _holder;

        internal bool IsRegistered;
        internal InputAction RegisteredAction;
        internal UIHolderObjectBase RegisteredHolder;
        internal EHotkeyPressType RegisteredPressType;

        protected virtual void Reset()
        {
            AssignHolder();
        }

        protected virtual void OnEnable()
        {
            RegisterHotkey();
        }

        protected virtual void OnDisable()
        {
            UnregisterHotkey();
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            AssignHolder();
        }
#endif

        public abstract void TriggerHotkey();

        /// <summary>
        /// 注册到当前输入会话。输入层尚未创建（或已销毁）时保持未注册，由 Holder/Action 配置决定是否有效。
        /// </summary>
        private void RegisterHotkey()
        {
            UXInput.Hotkey?.RegisterHotkey(this, _holder, _hotkeyAction, _hotkeyPressType);
        }

        private void UnregisterHotkey()
        {
            UXInput.Hotkey?.UnregisterHotkey(this);
        }

        /// <summary>
        /// 编辑器期解析所属 Holder。Holder 位于同一预制体内，运行时直接使用序列化结果。
        /// </summary>
        private void AssignHolder()
        {
            _holder = GetComponentInParent<UIHolderObjectBase>(true);
        }
    }
}
#endif
