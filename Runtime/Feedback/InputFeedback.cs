#if INPUTSYSTEM_SUPPORT
using AlicizaX.Audio.Runtime;
using UnityEngine;
using AudioType = AlicizaX.Audio.Runtime.AudioType;

namespace AlicizaX.Input
{
    /// <summary>
    /// 交互反馈：按当前输入设备、是否为导航层程序化选中，把提示解析为主题里的音效并播放。
    /// 由 <see cref="UXInput"/> 创建，与输入层同生命周期；全局主题由业务设置 <see cref="Profile"/>（输入层重建后需重新设置）。
    /// </summary>
    public sealed class InputFeedback
    {
        private readonly InputWatch _watch;
        private readonly InputNavigation _navigation;

        internal InputFeedback(InputWatch watch, InputNavigation navigation)
        {
            _watch = watch;
            _navigation = navigation;
        }

        /// <summary>全局主题；null 表示静音。</summary>
        public InputFeedbackProfile Profile { get; set; }

        /// <summary>用全局主题播放。</summary>
        public void Play(InputFeedbackCue cue) => Play(cue, Profile);

        /// <summary>用指定主题播放；profile 为 null 时静音，不回退全局主题。</summary>
        public void Play(InputFeedbackCue cue, InputFeedbackProfile profile)
        {
            AudioClip clip = ResolveClip(cue, ToDevice(_watch.CurrentInputType), IsProgrammaticFocus(cue), profile);
            if (clip != null && AppServices.TryGet(out IAudioService audio))
                audio.Play(AudioType.UISound, clip);
        }

        /// <summary>FocusEnter 发生在导航层程序化选中期间（切换导航域、关窗恢复、补选中）。</summary>
        internal bool IsProgrammaticFocus(InputFeedbackCue cue) =>
            cue == InputFeedbackCue.FocusEnter && _navigation.IsApplyingSelection;

        internal static AudioClip ResolveClip(InputFeedbackCue cue, InputFeedbackDevice device, bool programmaticFocus,
            InputFeedbackProfile profile)
        {
            if (profile == null || !profile.TryResolve(cue, device, out InputFeedbackProfile.Rule rule))
                return null;

            return programmaticFocus && !rule.AllowProgrammaticFocus ? null : rule.Clip;
        }

        internal static InputFeedbackDevice ToDevice(InputWatch.InputType inputType) => inputType switch
        {
            InputWatch.InputType.Touch => InputFeedbackDevice.Touch,
            InputWatch.InputType.Gamepad => InputFeedbackDevice.Gamepad,
            _ => InputFeedbackDevice.KeyboardMouse,   // 含 Unknown：按键鼠处理
        };
    }
}
#endif
