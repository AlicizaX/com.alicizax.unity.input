#if INPUTSYSTEM_SUPPORT
using UnityEngine;

namespace AlicizaX.Input
{
    /// <summary>
    /// 描述通过左右马达曲线驱动的自定义震动模式。
    /// </summary>
    [CreateAssetMenu(fileName = "NewHapticPattern", menuName = "AlicizaX/Input/Haptic Pattern")]
    public sealed class HapticPattern : ScriptableObject
    {
        /// <summary>
        /// 左马达强度曲线，横轴为归一化时间，纵轴为强度。
        /// </summary>
        public AnimationCurve LeftMotorCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 0f);

        /// <summary>
        /// 右马达强度曲线，横轴为归一化时间，纵轴为强度。
        /// </summary>
        public AnimationCurve RightMotorCurve = AnimationCurve.Linear(0f, 0.5f, 1f, 0f);

        /// <summary>
        /// 震动模式持续时间，单位为秒。
        /// </summary>
        [Range(0.01f, 5f)] public float Duration = 0.2f;

        /// <summary>
        /// 根据归一化时间采样左右马达强度。
        /// </summary>
        /// <param name="normalizedTime">归一化时间，取值范围为 0 到 1。</param>
        /// <param name="left">输出左马达强度。</param>
        /// <param name="right">输出右马达强度。</param>
        public void Evaluate(float normalizedTime, out float left, out float right)
        {
            left = LeftMotorCurve != null ? LeftMotorCurve.Evaluate(normalizedTime) : 0f;
            right = RightMotorCurve != null ? RightMotorCurve.Evaluate(normalizedTime) : 0f;
        }
    }
}
#endif
