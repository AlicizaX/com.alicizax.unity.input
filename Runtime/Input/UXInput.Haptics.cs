#if INPUTSYSTEM_SUPPORT
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 提供 AlicizaX 输入功能的静态辅助入口。
/// </summary>
public static partial class UXInput
{
    /// <summary>
    /// 提供手柄震动反馈的静态控制接口。
    /// </summary>
    public static class Haptics
    {
        private static float _intensity = 1f;
        private static bool _enabled = true;
        private static float _endTime;
        private static HapticPattern _currentPattern;
        private static float _patternStartTime;
        private static bool _isUpdateRegistered;

        /// <summary>
        /// 获取或设置全局震动强度，取值范围为 0 到 1。
        /// </summary>
        public static float Intensity
        {
            get => _intensity;
            set => _intensity = Mathf.Clamp01(value);
        }

        /// <summary>
        /// 获取或设置震动反馈是否启用。关闭时会立即停止当前震动。
        /// </summary>
        public static bool Enabled
        {
            get => _enabled;
            set
            {
                _enabled = value;
                if (!value)
                {
                    Stop();
                }
            }
        }

        /// <summary>
        /// 获取当前是否正在播放震动反馈。
        /// </summary>
        public static bool IsPlaying => _endTime > 0f;

        /// <summary>
        /// 播放一个内置震动预设。
        /// </summary>
        /// <param name="preset">要播放的震动预设。</param>
        public static void Play(HapticPreset preset)
        {
            if (!_enabled)
            {
                return;
            }

            HapticValues values = GetPresetValues(preset);
            Play(values.LeftMotor, values.RightMotor, values.Duration);
        }

        /// <summary>
        /// 播放一个自定义震动曲线资源。
        /// </summary>
        /// <param name="pattern">要播放的震动曲线资源。</param>
        public static void Play(HapticPattern pattern)
        {
            if (!_enabled || pattern == null)
            {
                return;
            }

            _currentPattern = pattern;
            _patternStartTime = Time.unscaledTime;
            _endTime = _patternStartTime + Mathf.Max(0f, pattern.Duration);
            RegisterUpdate();
        }

        /// <summary>
        /// 按指定左右马达强度和持续时间播放一次震动。
        /// </summary>
        /// <param name="leftMotor">左马达强度，取值范围为 0 到 1。</param>
        /// <param name="rightMotor">右马达强度，取值范围为 0 到 1。</param>
        /// <param name="duration">震动持续时间，单位为秒。</param>
        public static void Play(float leftMotor, float rightMotor, float duration)
        {
            if (!_enabled)
            {
                return;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                return;
            }

            _currentPattern = null;
            _endTime = Time.unscaledTime + Mathf.Max(0f, duration);

            gamepad.SetMotorSpeeds(
                Mathf.Clamp01(leftMotor) * _intensity,
                Mathf.Clamp01(rightMotor) * _intensity);

            RegisterUpdate();
        }

        /// <summary>
        /// 停止当前震动反馈并取消后续更新。
        /// </summary>
        public static void Stop()
        {
            _currentPattern = null;
            _endTime = 0f;

            Gamepad gamepad = Gamepad.current;
            if (gamepad != null)
            {
                gamepad.SetMotorSpeeds(0f, 0f);
            }

            UnregisterUpdate();
        }

        /// <summary>
        /// 设置全局震动强度。
        /// </summary>
        /// <param name="intensity">震动强度，取值范围为 0 到 1。</param>
        public static void SetIntensity(float intensity)
        {
            Intensity = intensity;
        }

        /// <summary>
        /// 更新当前震动状态。通常由输入系统更新回调自动调用。
        /// </summary>
        public static void UpdateHaptics()
        {
            if (!_enabled)
            {
                return;
            }

            Gamepad gamepad = Gamepad.current;
            if (gamepad == null)
            {
                Stop();
                return;
            }

            float currentTime = Time.unscaledTime;
            if (_currentPattern != null)
            {
                if (currentTime >= _endTime)
                {
                    Stop();
                    return;
                }

                float duration = Mathf.Max(0.01f, _currentPattern.Duration);
                float normalizedTime = Mathf.Clamp01((currentTime - _patternStartTime) / duration);
                _currentPattern.Evaluate(normalizedTime, out float left, out float right);

                gamepad.SetMotorSpeeds(
                    Mathf.Clamp01(left) * _intensity,
                    Mathf.Clamp01(right) * _intensity);
                return;
            }

            if (_endTime > 0f && currentTime >= _endTime)
            {
                Stop();
            }
        }

        /// <summary>
        /// 停止震动并恢复默认启用状态和默认强度。
        /// </summary>
        public static void Reset()
        {
            Stop();
            _intensity = 1f;
            _enabled = true;
        }

        private static void RegisterUpdate()
        {
            if (_isUpdateRegistered)
            {
                return;
            }

            InputSystem.onAfterUpdate += UpdateHaptics;
            _isUpdateRegistered = true;
        }

        private static void UnregisterUpdate()
        {
            if (!_isUpdateRegistered)
            {
                return;
            }

            InputSystem.onAfterUpdate -= UpdateHaptics;
            _isUpdateRegistered = false;
        }

        private static HapticValues GetPresetValues(HapticPreset preset)
        {
            switch (preset)
            {
                case HapticPreset.Light:
                    return new HapticValues(0.2f, 0.2f, 0.1f);
                case HapticPreset.Medium:
                    return new HapticValues(0.5f, 0.5f, 0.15f);
                case HapticPreset.Heavy:
                    return new HapticValues(1f, 1f, 0.2f);
                case HapticPreset.Pulse:
                    return new HapticValues(0.8f, 0.3f, 0.1f);
                case HapticPreset.Success:
                    return new HapticValues(0.3f, 0.6f, 0.15f);
                case HapticPreset.Error:
                    return new HapticValues(0.8f, 0.2f, 0.25f);
                case HapticPreset.Selection:
                    return new HapticValues(0.1f, 0.3f, 0.05f);
                default:
                    return new HapticValues(0.5f, 0.5f, 0.15f);
            }
        }

        private struct HapticValues
        {
            internal readonly float LeftMotor;
            internal readonly float RightMotor;
            internal readonly float Duration;

            internal HapticValues(float leftMotor, float rightMotor, float duration)
            {
                LeftMotor = leftMotor;
                RightMotor = rightMotor;
                Duration = duration;
            }
        }
    }
}

/// <summary>
/// 表示内置震动反馈预设。
/// </summary>
public enum HapticPreset
{
    /// <summary>
    /// 轻微震动。
    /// </summary>
    Light,

    /// <summary>
    /// 中等震动。
    /// </summary>
    Medium,

    /// <summary>
    /// 强烈震动。
    /// </summary>
    Heavy,

    /// <summary>
    /// 短促脉冲震动。
    /// </summary>
    Pulse,

    /// <summary>
    /// 成功反馈震动。
    /// </summary>
    Success,

    /// <summary>
    /// 错误反馈震动。
    /// </summary>
    Error,

    /// <summary>
    /// 选择反馈震动。
    /// </summary>
    Selection
}

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
#endif
