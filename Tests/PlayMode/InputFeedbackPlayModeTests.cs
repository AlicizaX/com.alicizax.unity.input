using System.Reflection;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    public sealed class InputFeedbackPlayModeTests : InputFeedbackCases
    {
        private GameObject _eventRoot;
        private GameObject _scopeRoot;
        private EventSystem _previousEventSystem;

        [TearDown]
        public void TearDownScene()
        {
            InputTestViews.UnbindAll();
            Object.DestroyImmediate(_scopeRoot);
            Object.DestroyImmediate(_eventRoot);
            if (_previousEventSystem != null) EventSystem.current = _previousEventSystem;
        }

        [Test]
        public void NavigationAppliedFocusIsProgrammaticButUserSelectionIsNot()
        {
            _previousEventSystem = EventSystem.current;
            _eventRoot = new GameObject("feedback-events", typeof(EventSystem));
            EventSystem events = _eventRoot.GetComponent<EventSystem>();
            EventSystem.current = events;
            UXInput.Initialize(null, null);

            _scopeRoot = new GameObject("feedback-scope", typeof(RectTransform), typeof(Canvas));
            _scopeRoot.SetActive(false);
            _scopeRoot.layer = UIComponent.UIShowLayer;
            var scope = _scopeRoot.AddComponent<NavigationScope>();
            typeof(NavigationScope).GetField("_holder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(scope, InputTestViews.Bind(_scopeRoot).GetHolder());
            _scopeRoot.SetActive(true);

            var buttonObject = new GameObject("feedback-button", typeof(RectTransform), typeof(Button));
            buttonObject.transform.SetParent(_scopeRoot.transform, false);
            bool? focus = null;
            bool? press = null;
            var trigger = buttonObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            entry.callback.AddListener(_ =>
            {
                focus = UXInput.Feedback.IsProgrammaticFocus(InputFeedbackCue.FocusEnter);
                press = UXInput.Feedback.IsProgrammaticFocus(InputFeedbackCue.Press);
            });
            trigger.triggers.Add(entry);

            scope.RegisterSelectable(buttonObject.GetComponent<Button>());
            UXInput.Navigation.SetRequireSelection(true, true);      // 导航层补选中
            Assert.That(focus, Is.True, "导航层设置选中期间的 FocusEnter 是程序化的");
            Assert.That(press, Is.False, "只有 FocusEnter 区分程序化");
            Assert.That(UXInput.Feedback.IsProgrammaticFocus(InputFeedbackCue.FocusEnter), Is.False,
                "只在导航层同步设置选中的回调内为真");

            events.SetSelectedGameObject(null);
            events.SetSelectedGameObject(buttonObject);              // 业务 / 用户选中
            Assert.That(focus, Is.False);
        }
    }
}
