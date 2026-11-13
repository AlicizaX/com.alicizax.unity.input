#if INPUTSYSTEM_SUPPORT
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlicizaX.Input
{
    public enum InputFeedbackMode : byte
    {
        Global = 0,
        Custom = 1,
    }

    /// <summary>
    /// 把同一 GameObject 上的 UGUI 交互事件转成 <see cref="InputFeedbackCue"/> 并播放：
    /// 鼠标进入 → PointerEnter；被 EventSystem 选中（非指针引起）→ FocusEnter；左键点击 / Submit / 热键提交 → Press。
    /// 默认使用全局主题；Custom 模式使用本组件的主题，留空则静音（不回退全局）。必须与控件挂在同一个 GameObject 上。
    /// 没有对应 EventSystem 事件的交互（例如列表内的虚拟焦点移动）由宿主调用 <see cref="Play"/>。
    /// </summary>
    [DisallowMultipleComponent]
    [AddComponentMenu("UI/Input Feedback Observer")]
    public class InputFeedbackObserver : MonoBehaviour,
        IPointerEnterHandler, ISelectHandler, IPointerClickHandler, ISubmitHandler, IHotkeySubmitListener
    {
        [Tooltip("Global 使用全局主题；Custom 使用本组件的主题。")]
        [SerializeField] private InputFeedbackMode _mode = InputFeedbackMode.Global;

        [Tooltip("Custom 模式使用的主题；留空则静音（不回退全局主题）。")]
        [SerializeField] private InputFeedbackProfile _profile;

        private Selectable _selectable;

        private void Awake()
        {
            _selectable = GetComponent<Selectable>();
        }

#if UNITY_EDITOR
        // Global 模式不读本组件主题：清掉残留引用，避免 Inspector 看不见的隐藏状态和多余的资源依赖。
        private void OnValidate()
        {
            if (_mode == InputFeedbackMode.Global)
                _profile = null;
        }
#endif

        /// <summary>播放一个提示。输入层未初始化（编辑器、测试）是合法状态，此时不播放。子类可重写以自定义播放。</summary>
        public virtual void Play(InputFeedbackCue cue)
        {
            InputFeedback feedback = UXInput.Feedback;
            if (feedback == null)
                return;

            if (_mode == InputFeedbackMode.Custom)
                feedback.Play(cue, _profile);
            else
                feedback.Play(cue);
        }

        void IPointerEnterHandler.OnPointerEnter(PointerEventData eventData)
        {
            if (IsControlActive())
                Play(InputFeedbackCue.PointerEnter);
        }

        void ISelectHandler.OnSelect(BaseEventData eventData)
        {
            if (eventData is PointerEventData || !IsControlActive())
                return;

            Play(InputFeedbackCue.FocusEnter);
        }

        void IPointerClickHandler.OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button == PointerEventData.InputButton.Left && CanPress())
                Play(InputFeedbackCue.Press);
        }

        void ISubmitHandler.OnSubmit(BaseEventData eventData)
        {
            if (CanPress())
                Play(InputFeedbackCue.Press);
        }

        void IHotkeySubmitListener.OnHotkeySubmit(BaseEventData eventData)
        {
            if (CanPress())
                Play(InputFeedbackCue.Press);
        }

        private bool IsControlActive() => _selectable == null || _selectable.IsActive();

        private bool CanPress() => _selectable == null || (_selectable.IsActive() && _selectable.IsInteractable());
    }
}
#endif
