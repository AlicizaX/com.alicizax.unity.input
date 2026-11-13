using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    public abstract class InputFeedbackCases
    {
        private InputFeedbackProfile _profile;
        private AudioClip _first;
        private AudioClip _second;

        [SetUp]
        public void SetUp()
        {
            Assert.That(UXInput.IsInitialized, Is.False);
            _profile = ScriptableObject.CreateInstance<InputFeedbackProfile>();
            _first = AudioClip.Create("feedback-first", 256, 1, 44100, false);
            _second = AudioClip.Create("feedback-second", 256, 1, 44100, false);
        }

        [TearDown]
        public void TearDown()
        {
            UXInput.Shutdown();
            Object.DestroyImmediate(_profile);
            Object.DestroyImmediate(_first);
            Object.DestroyImmediate(_second);
        }

        [Test]
        public void RulesMatchCueAndDevice()
        {
            SetRules(
                new InputFeedbackProfile.Rule { Cue = InputFeedbackCue.Press, Devices = InputFeedbackDevice.KeyboardMouse, Clip = _first },
                new InputFeedbackProfile.Rule { Cue = InputFeedbackCue.Press, Devices = InputFeedbackDevice.Gamepad, Clip = _second });

            Assert.That(Resolve(InputFeedbackCue.Press, InputFeedbackDevice.KeyboardMouse), Is.SameAs(_first));
            Assert.That(Resolve(InputFeedbackCue.Press, InputFeedbackDevice.Gamepad), Is.SameAs(_second));
            Assert.That(Resolve(InputFeedbackCue.Press, InputFeedbackDevice.Touch), Is.Null, "没有规则覆盖的设备静音");
            Assert.That(Resolve(InputFeedbackCue.PointerEnter, InputFeedbackDevice.KeyboardMouse), Is.Null, "没有规则覆盖的提示静音");
        }

        [Test]
        public void FirstMatchingRuleWinsAndOwnsProgrammaticFocusPolicy()
        {
            const InputFeedbackCue cue = InputFeedbackCue.FocusEnter;
            SetRules(
                new InputFeedbackProfile.Rule { Cue = cue, Devices = InputFeedbackDevice.Gamepad, Clip = _first },
                new InputFeedbackProfile.Rule { Cue = cue, Devices = InputFeedbackDevice.Gamepad, Clip = _second, AllowProgrammaticFocus = true });

            Assert.That(Resolve(cue, InputFeedbackDevice.Gamepad), Is.SameAs(_first));
            Assert.That(Resolve(cue, InputFeedbackDevice.Gamepad, true), Is.Null, "命中的第一条规则不允许程序化焦点，不得继续匹配下一条");

            SetRules(new InputFeedbackProfile.Rule { Cue = cue, Devices = InputFeedbackDevice.Gamepad, Clip = _first, AllowProgrammaticFocus = true });
            Assert.That(Resolve(cue, InputFeedbackDevice.Gamepad, true), Is.SameAs(_first));
        }

        [Test]
        public void NullProfileIsSilent()
        {
            Assert.That(InputFeedback.ResolveClip(InputFeedbackCue.Press, InputFeedbackDevice.All, false, null), Is.Null,
                "显式空主题静音，不回退全局主题");
        }

        [TestCase(InputWatch.InputType.Touch, InputFeedbackDevice.Touch)]
        [TestCase(InputWatch.InputType.KeyboardMouse, InputFeedbackDevice.KeyboardMouse)]
        [TestCase(InputWatch.InputType.Gamepad, InputFeedbackDevice.Gamepad)]
        [TestCase(InputWatch.InputType.Unknown, InputFeedbackDevice.KeyboardMouse)]
        public void InputTypeMapsToDeviceMask(InputWatch.InputType inputType, InputFeedbackDevice expected)
        {
            Assert.That(InputFeedback.ToDevice(inputType), Is.EqualTo(expected));
        }

        [Test]
        public void DefaultRulesHoverOnKeyboardMouseFocusOnGamepadPressEverywhere()
        {
            typeof(InputFeedbackProfile).GetMethod("Reset", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(_profile, null);

            Assert.That(_profile.TryResolve(InputFeedbackCue.PointerEnter, InputFeedbackDevice.KeyboardMouse, out _), Is.True);
            Assert.That(_profile.TryResolve(InputFeedbackCue.PointerEnter, InputFeedbackDevice.Gamepad, out _), Is.False);
            Assert.That(_profile.TryResolve(InputFeedbackCue.FocusEnter, InputFeedbackDevice.Gamepad, out _), Is.True);
            Assert.That(_profile.TryResolve(InputFeedbackCue.FocusEnter, InputFeedbackDevice.KeyboardMouse, out _), Is.False);
            Assert.That(_profile.TryResolve(InputFeedbackCue.Press, InputFeedbackDevice.Touch, out _), Is.True);
            Assert.That(_profile.TryResolve(InputFeedbackCue.Press, InputFeedbackDevice.KeyboardMouse, out _), Is.True);
            Assert.That(_profile.TryResolve(InputFeedbackCue.Press, InputFeedbackDevice.Gamepad, out _), Is.True);
        }

        [Test]
        public void FeedbackFollowsInputLifecycle()
        {
            UXInput.Initialize(null, null);
            InputFeedback first = UXInput.Feedback;
            Assert.That(first, Is.Not.Null);
            Assert.That(first.Profile, Is.Null, "未设置主题时静音");
            first.Profile = _profile;
            Assert.DoesNotThrow(() => first.Play(InputFeedbackCue.Press), "没有音频服务时播放是空操作");

            UXInput.Shutdown();
            Assert.That(UXInput.Feedback, Is.Null);

            UXInput.Initialize(null, null);
            Assert.That(UXInput.Feedback, Is.Not.SameAs(first));
            Assert.That(UXInput.Feedback.Profile, Is.Null, "输入层重建后主题需要重新设置");
        }

        private AudioClip Resolve(InputFeedbackCue cue, InputFeedbackDevice device, bool programmaticFocus = false)
            => InputFeedback.ResolveClip(cue, device, programmaticFocus, _profile);

        private void SetRules(params InputFeedbackProfile.Rule[] rules)
            => typeof(InputFeedbackProfile).GetField("_rules", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(_profile, rules);
    }
}
