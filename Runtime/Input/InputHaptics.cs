#if INPUTSYSTEM_SUPPORT
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    /// <summary>
    /// 提供手柄震动反馈的控制接口。
    /// </summary>
    public sealed class InputHaptics
    {
        internal InputHaptics() { UpdateHandler = UpdateHaptics; }
        internal void Shutdown() { Reset(); }

        private float _intensity = 1f;
        private bool _enabled = true;
        private float _endTime;
        private bool _isPlaying;
        private HapticPattern _currentPattern;
        private float _patternStartTime;
        private bool _isUpdateRegistered;
        private readonly System.Action UpdateHandler;
        private Gamepad _gamepad;

        /// <summary>
        /// 获取或设置全局震动强度，取值范围为 0 到 1。
        /// </summary>
        public float Intensity
        {
            get => _intensity;
            set => _intensity = Mathf.Clamp01(value);
        }

        /// <summary>
        /// 获取或设置震动反馈是否启用。关闭时会立即停止当前震动。
        /// </summary>
        public bool Enabled
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
        public bool IsPlaying => _isPlaying;

        /// <summary>
        /// 播放一个内置震动预设。
        /// </summary>
        /// <param name="preset">要播放的震动预设。</param>
        public void Play(HapticPreset preset)
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
        public void Play(HapticPattern pattern)
        {
            if (!_enabled || pattern == null || !BindCurrentGamepad())
            {
                return;
            }

            _currentPattern = pattern;
            _patternStartTime = Time.unscaledTime;
            _endTime = _patternStartTime + Mathf.Max(0f, pattern.Duration);
            _isPlaying = true;
            RegisterUpdate();
        }

        /// <summary>
        /// 按指定左右马达强度和持续时间播放一次震动。
        /// </summary>
        /// <param name="leftMotor">左马达强度，取值范围为 0 到 1。</param>
        /// <param name="rightMotor">右马达强度，取值范围为 0 到 1。</param>
        /// <param name="duration">震动持续时间，单位为秒。</param>
        public void Play(float leftMotor, float rightMotor, float duration)
        {
            if (!_enabled)
            {
                return;
            }

            if (!BindCurrentGamepad())
            {
                return;
            }

            _currentPattern = null;
            _endTime = Time.unscaledTime + Mathf.Max(0f, duration);
            _isPlaying = true;

            _gamepad.SetMotorSpeeds(
                Mathf.Clamp01(leftMotor) * _intensity,
                Mathf.Clamp01(rightMotor) * _intensity);

            RegisterUpdate();
        }

        /// <summary>
        /// 停止当前震动反馈并取消后续更新。
        /// </summary>
        public void Stop()
        {
            _currentPattern = null;
            _endTime = 0f;
            _patternStartTime = 0f;
            _isPlaying = false;

            if (_gamepad != null && _gamepad.added)
                _gamepad.SetMotorSpeeds(0f, 0f);
            _gamepad = null;
            UnregisterUpdate();
        }

        /// <summary>由 InputSystem.onAfterUpdate 驱动，只在播放期间注册。</summary>
        private void UpdateHaptics()
        {
            if (!_enabled)
            {
                return;
            }

            Gamepad gamepad = _gamepad;
            if (gamepad == null || !gamepad.added)
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

            if (currentTime >= _endTime)
            {
                Stop();
            }
        }

        /// <summary>
        /// 停止震动并恢复默认启用状态和默认强度。
        /// </summary>
        public void Reset()
        {
            Stop();
            _intensity = 1f;
            _enabled = true;
        }

        private bool BindCurrentGamepad()
        {
            Gamepad current = Gamepad.current;
            if (!ReferenceEquals(_gamepad, current))
            {
                Stop();
                _gamepad = current;
            }
            return current != null;
        }

        private void RegisterUpdate()
        {
            if (_isUpdateRegistered)
            {
                return;
            }

            InputSystem.onAfterUpdate += UpdateHandler;
            _isUpdateRegistered = true;
        }

        private void UnregisterUpdate()
        {
            if (!_isUpdateRegistered)
            {
                return;
            }

            InputSystem.onAfterUpdate -= UpdateHandler;
            _isUpdateRegistered = false;
        }

        private HapticValues GetPresetValues(HapticPreset preset)
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
}
#endif
