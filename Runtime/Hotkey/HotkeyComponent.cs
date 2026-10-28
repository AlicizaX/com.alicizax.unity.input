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

        /// <summary>
        /// 解析 ISubmitHandler 目标。组件的接口查询不依赖 GetComponent 对接口的支持：
        /// 先使用显式引用，否则扫描一次组件数组（只在 Awake/编辑期发生）。
        /// </summary>
        private void ResolveSubmitTarget()
        {
            if (_component != null)
            {
                _submitHandler = _component as ISubmitHandler;
                if (_submitHandler != null)
                {
                    return;
                }

                _component = null;
            }

            Component[] components = GetComponents<Component>();
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is ISubmitHandler submitHandler)
                {
                    _component = components[i];
                    _submitHandler = submitHandler;
                    return;
                }
            }

            _submitHandler = null;
        }
    }
}
#endif
