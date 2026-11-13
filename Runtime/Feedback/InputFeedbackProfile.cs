#if INPUTSYSTEM_SUPPORT
using UnityEngine;

namespace AlicizaX.Input
{
    /// <summary>
    /// 交互反馈主题：按“提示类型 + 当前设备”取第一条匹配规则的音效。
    /// </summary>
    [CreateAssetMenu(menuName = "AlicizaX/Input/Feedback Profile", fileName = "InputFeedbackProfile")]
    public sealed class InputFeedbackProfile : ScriptableObject
    {
        [System.Serializable]
        public struct Rule
        {
            public InputFeedbackCue Cue;
            public InputFeedbackDevice Devices;

            /// <summary>导航层程序化选中（切换导航域、关窗恢复）时是否仍播放；只对 FocusEnter 有意义。</summary>
            public bool AllowProgrammaticFocus;

            public AudioClip Clip;
        }

        [SerializeField] private Rule[] _rules;

        internal bool TryResolve(InputFeedbackCue cue, InputFeedbackDevice device, out Rule rule)
        {
            if (_rules != null)
            {
                for (int i = 0; i < _rules.Length; i++)
                {
                    rule = _rules[i];
                    if (rule.Cue == cue && (rule.Devices & device) != 0)
                        return true;
                }
            }

            rule = default;
            return false;
        }

#if UNITY_EDITOR
        private void Reset()
        {
            _rules = new[]
            {
                new Rule { Cue = InputFeedbackCue.PointerEnter, Devices = InputFeedbackDevice.KeyboardMouse },
                new Rule { Cue = InputFeedbackCue.FocusEnter, Devices = InputFeedbackDevice.Gamepad },
                new Rule { Cue = InputFeedbackCue.Press, Devices = InputFeedbackDevice.All },
            };
        }
#endif
    }
}
#endif
