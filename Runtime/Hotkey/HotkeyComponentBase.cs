#if INPUTSYSTEM_SUPPORT
using AlicizaX.UI.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    public abstract class HotkeyComponentBase : MonoBehaviour
    {
        [SerializeField] private UIHolderObjectBase _holder;
        [SerializeField] private InputActionReference _hotkeyAction;
        [SerializeField] private EHotkeyPressType _hotkeyPressType = EHotkeyPressType.Performed;

        /// <summary>注册令牌；只由 InputHotkey 的注册/注销路径写入。</summary>
        internal HotkeyRegistrationToken Registration;

        internal bool IsRegistered => Registration.IsRegistered;

        /// <summary>切换绑定；新配置注册失败时回滚字段并保留旧注册。</summary>
        public InputActionReference HotkeyAction
        {
            get => _hotkeyAction;
            set
            {
                if (ReferenceEquals(_hotkeyAction, value))
                    return;

                InputActionReference previous = _hotkeyAction;
                _hotkeyAction = value;

                if (Application.isPlaying && isActiveAndEnabled && !RegisterHotkey())
                    _hotkeyAction = previous;
            }
        }

        public EHotkeyPressType HotkeyPressType => _hotkeyPressType;

        public UIHolderObjectBase HotkeyHolder => _holder;

        protected virtual void Reset()
        {
            AssignHolder();
        }

        /// <summary>子类覆盖时必须调用 base，否则注册/注销会失配。</summary>
        protected virtual void OnEnable()
        {
            RegisterHotkey();
        }

        /// <summary>子类覆盖时必须调用 base，否则注册/注销会失配。</summary>
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
        /// 注册到当前输入会话。没有输入会话时返回 true 并保持未注册（输入层初始化不会补注册，组件需在其后启用）；
        /// 配置无效时返回 false，调用方据此回滚 HotkeyAction。
        /// </summary>
        internal bool RegisterHotkey()
        {
            InputHotkey hotkey = UXInput.Hotkey;
            return hotkey == null || hotkey.RegisterHotkey(this, _holder, _hotkeyAction, _hotkeyPressType);
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
