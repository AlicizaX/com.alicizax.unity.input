#if INPUTSYSTEM_SUPPORT
namespace AlicizaX.Input
{
    /// <summary>交互反馈提示类型。只表达“发生了什么”，不含播放策略。</summary>
    public enum InputFeedbackCue : byte
    {
        PointerEnter = 0,
        Press = 1,
        FocusEnter = 2,
    }
}
#endif
