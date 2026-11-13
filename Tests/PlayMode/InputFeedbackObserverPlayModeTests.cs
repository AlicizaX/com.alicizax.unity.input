using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    public sealed class InputFeedbackObserverPlayModeTests
    {
        private sealed class RecordingObserver : InputFeedbackObserver
        {
            public readonly List<InputFeedbackCue> Cues = new List<InputFeedbackCue>();

            public override void Play(InputFeedbackCue cue) => Cues.Add(cue);
        }

        private GameObject _root;

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_root);
        }

        private RecordingObserver Create(bool withButton)
        {
            _root = new GameObject("feedback-observer", typeof(RectTransform));
            if (withButton) _root.AddComponent<Button>();
            return _root.AddComponent<RecordingObserver>();   // Awake 在 AddComponent 时同步执行，先加控件才能被缓存
        }

        private static PointerEventData Pointer(PointerEventData.InputButton button = PointerEventData.InputButton.Left) =>
            new PointerEventData(null) { button = button };

        [Test]
        public void HoverAndLeftClickEmitEnterAndPress()
        {
            RecordingObserver observer = Create(true);

            ExecuteEvents.Execute(_root, Pointer(), ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(_root, Pointer(PointerEventData.InputButton.Right), ExecuteEvents.pointerClickHandler);
            ExecuteEvents.Execute(_root, Pointer(), ExecuteEvents.pointerClickHandler);

            Assert.That(observer.Cues, Is.EqualTo(new[] { InputFeedbackCue.PointerEnter, InputFeedbackCue.Press }),
                "右键不触发 Press");
        }

        [Test]
        public void SubmitAndHotkeySubmitEmitPress()
        {
            RecordingObserver observer = Create(true);

            ExecuteEvents.Execute(_root, new BaseEventData(null), ExecuteEvents.submitHandler);
            ((IHotkeySubmitListener)observer).OnHotkeySubmit(new BaseEventData(null));

            Assert.That(observer.Cues, Is.EqualTo(new[] { InputFeedbackCue.Press, InputFeedbackCue.Press }));
        }

        [Test]
        public void NonPointerSelectEmitsFocusButPointerSelectDoesNot()
        {
            RecordingObserver observer = Create(true);

            ExecuteEvents.Execute(_root, Pointer(), ExecuteEvents.selectHandler);
            ExecuteEvents.Execute(_root, new BaseEventData(null), ExecuteEvents.selectHandler);

            Assert.That(observer.Cues, Is.EqualTo(new[] { InputFeedbackCue.FocusEnter }),
                "点击引起的选中不发 FocusEnter，悬停与点击已各自有提示");
        }

        [Test]
        public void NonInteractableControlKeepsHoverButSkipsPress()
        {
            RecordingObserver observer = Create(true);
            _root.GetComponent<Button>().interactable = false;

            ExecuteEvents.Execute(_root, Pointer(), ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(_root, Pointer(), ExecuteEvents.pointerClickHandler);
            ExecuteEvents.Execute(_root, new BaseEventData(null), ExecuteEvents.submitHandler);

            Assert.That(observer.Cues, Is.EqualTo(new[] { InputFeedbackCue.PointerEnter }));
        }

        [Test]
        public void DisabledControlEmitsNothing()
        {
            RecordingObserver observer = Create(true);
            _root.GetComponent<Button>().enabled = false;

            ExecuteEvents.Execute(_root, Pointer(), ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(_root, new BaseEventData(null), ExecuteEvents.selectHandler);
            ExecuteEvents.Execute(_root, Pointer(), ExecuteEvents.pointerClickHandler);
            ExecuteEvents.Execute(_root, new BaseEventData(null), ExecuteEvents.submitHandler);

            Assert.That(observer.Cues, Is.Empty, "控件被禁用时不发任何提示");
        }

        [Test]
        public void ObserverWithoutSelectableEmitsHoverAndPress()
        {
            RecordingObserver observer = Create(false);

            ExecuteEvents.Execute(_root, Pointer(), ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(_root, Pointer(), ExecuteEvents.pointerClickHandler);

            Assert.That(observer.Cues, Is.EqualTo(new[] { InputFeedbackCue.PointerEnter, InputFeedbackCue.Press }),
                "没有 Selectable 的物体（如列表项）按始终可用处理");
        }
    }
}
