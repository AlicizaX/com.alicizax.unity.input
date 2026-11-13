using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    public sealed class NavigationInputPlayModeTests : InputTestFixture
    {
        private sealed class TestHolder : UIHolderObjectBase { }
        private readonly List<GameObject> _roots = new();
        private readonly List<InputActionReference> _references = new();
        private EventSystem _previousEventSystem;
        private EventSystem _eventSystem;
        private InputActionAsset _actions;
        private Gamepad _gamepad;
        private Mouse _mouse;
        private Keyboard _keyboard;

        public override void Setup()
        {
            Assert.That(UXInput.Navigation, Is.Null);
            base.Setup();
            InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            _gamepad = InputSystem.AddDevice<Gamepad>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _previousEventSystem = EventSystem.current;

            var root = new GameObject("navigation-input-events");
            _roots.Add(root);
            root.SetActive(false);
            _eventSystem = root.AddComponent<EventSystem>();
            var module = root.AddComponent<InputSystemUIInputModule>();
            _actions = ScriptableObject.CreateInstance<InputActionAsset>();
            var map = _actions.AddActionMap("UI");
            var move = map.AddAction("Navigate", InputActionType.PassThrough, "<Gamepad>/dpad");
            var submit = map.AddAction("Submit", InputActionType.Button, "<Gamepad>/buttonSouth");
            submit.AddBinding("<Keyboard>/enter");
            var point = map.AddAction("Point", InputActionType.PassThrough, "<Mouse>/position");
            var click = map.AddAction("Click", InputActionType.PassThrough, "<Mouse>/leftButton");
            module.actionsAsset = _actions;
            module.move = Reference(move);
            module.submit = Reference(submit);
            module.point = Reference(point);
            module.leftClick = Reference(click);
            module.moveRepeatDelay = 100;
            root.SetActive(true);
            EventSystem.current = _eventSystem;
            UXInput.Initialize(_actions, null);
        }

        public override void TearDown()
        {
            try
            {
                UXInput.Shutdown();
                InputTestViews.UnbindAll();
                for (int i = _roots.Count - 1; i >= 0; i--)
                    if (_roots[i] != null) Object.DestroyImmediate(_roots[i]);
                _roots.Clear();
                foreach (var reference in _references) Object.DestroyImmediate(reference);
                _references.Clear();
                if (_actions != null) Object.DestroyImmediate(_actions);
                if (_previousEventSystem != null) EventSystem.current = _previousEventSystem;
                Assert.That(UXInput.Navigation, Is.Null);
            }
            finally { base.TearDown(); }
        }

        private InputActionReference Reference(InputAction action)
        {
            var reference = InputActionReference.Create(action);
            _references.Add(reference);
            return reference;
        }

        private NavigationScope CreateScope(int order, bool remember, out Button first, out Button second,
            UIHolderObjectBase holder = null)
        {
            var root = new GameObject("navigation-input-scope", typeof(RectTransform), typeof(Canvas));
            _roots.Add(root);
            root.SetActive(false);
            if (holder != null) root.transform.SetParent(holder.transform, false);
            else holder = InputTestViews.Bind(root).GetHolder();
            root.layer = UIComponent.UIShowLayer;
            root.GetComponent<Canvas>().sortingOrder = order;
            var scope = root.AddComponent<NavigationScope>();
            Set(scope, "_holder", holder);
            first = CreateButton(root, "first");
            second = CreateButton(root, "second");
            first.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnRight = second };
            second.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = first };
            Set(scope, "_bakedSelectables", new Selectable[] { first, second });
            Set(scope, "_defaultSelectable", first);
            Set(scope, "_rememberLastSelection", remember);
            root.SetActive(true);
            return scope;
        }

        private static Button CreateButton(GameObject parent, string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(Button));
            root.transform.SetParent(parent.transform, false);
            return root.GetComponent<Button>();
        }

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private IEnumerator MoveToSecond(Selectable second)
        {
            yield return null;
            InputSystem.QueueStateEvent(_gamepad, new GamepadState().WithButton(GamepadButton.DpadRight));
            yield return null;
            InputSystem.QueueStateEvent(_gamepad, new GamepadState());
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject, Is.SameAs(second.gameObject));
        }

        [UnityTest]
        public IEnumerator WidgetSwitchAndReopenUseDefaultWithoutRemember() => WidgetSwitchAndReopen(false);

        [UnityTest]
        public IEnumerator WidgetSwitchAndReopenRestoreWithRemember() => WidgetSwitchAndReopen(true);

        private IEnumerator WidgetSwitchAndReopen(bool remember)
        {
            var windowRoot = new GameObject("logical-window", typeof(RectTransform), typeof(Canvas), typeof(TestHolder));
            _roots.Add(windowRoot);
            UIBase window = InputTestViews.Bind(windowRoot.GetComponent<TestHolder>());
            UIBase a = Widget(window, UIState.Opened);
            UIBase b = Widget(window, UIState.Closed);
            CreateScope(0, remember, out _, out var aSecond, a.GetHolder());
            CreateScope(0, remember, out var bFirst, out var bSecond, b.GetHolder());
            yield return MoveToSecond(aSecond);

            InputTestViews.SetState(a, UIState.Closing);
            InputTestViews.Raise(a.GetHolder(), nameof(UIHolderObjectBase.OnCloseEvent));
            InputTestViews.SetState(b, UIState.Opening);
            InputTestViews.Raise(b.GetHolder(), nameof(UIHolderObjectBase.OnShowEvent));
            yield return null;
            Assert.That(a.GetHolder().gameObject.activeInHierarchy, Is.True);
            Assert.That(b.GetHolder().gameObject.activeInHierarchy, Is.True);
            Assert.That(_eventSystem.currentSelectedGameObject, Is.SameAs(bFirst.gameObject));
            yield return MoveToSecond(bSecond);

            InputTestViews.SetState(b, UIState.Closing);
            InputTestViews.Raise(b.GetHolder(), nameof(UIHolderObjectBase.OnCloseEvent));
            Assert.That(_eventSystem.currentSelectedGameObject, Is.Null);
            InputTestViews.SetState(b, UIState.Opening);
            InputTestViews.Raise(b.GetHolder(), nameof(UIHolderObjectBase.OnShowEvent));
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject, Is.SameAs((remember ? bSecond : bFirst).gameObject));

            InputTestViews.SetState(window, UIState.Closing);
            InputTestViews.Raise(window.GetHolder(), nameof(UIHolderObjectBase.OnCloseEvent));
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject, Is.Null);
        }

        private UIBase Widget(UIBase parent, UIState state)
        {
            var root = new GameObject("logical-widget", typeof(RectTransform), typeof(CanvasGroup), typeof(TestHolder));
            root.transform.SetParent(parent.GetHolder().transform, false);
            return InputTestViews.Bind(root.GetComponent<TestHolder>(), parent, state);
        }

        [UnityTest]
        public IEnumerator MouseMotionPreservesSelectionWithoutRemember()
        {
            CreateScope(0, false, out _, out var second);
            yield return MoveToSecond(second);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(100, 100), delta = Vector2.right * 20 });
            yield return null;
            Assert.That(UXInput.Watch.CurrentInputType, Is.EqualTo(InputWatch.InputType.KeyboardMouse));
            Assert.That(_eventSystem.currentSelectedGameObject, Is.SameAs(second.gameObject));
            InputSystem.QueueStateEvent(_gamepad, new GamepadState().WithButton(GamepadButton.North));
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject, Is.SameAs(second.gameObject));
        }

        [UnityTest]
        public IEnumerator BackgroundClickRestoresRecentSelectionWithoutRemember() => BackgroundClick(false);

        [UnityTest]
        public IEnumerator BackgroundClickRestoresRecentSelectionWithRemember() => BackgroundClick(true);

        private IEnumerator BackgroundClick(bool remember)
        {
            CreateScope(0, remember, out _, out var second);
            yield return MoveToSecond(second);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(100, 100) }.WithButton(MouseButton.Left));
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject, Is.Null);
            InputSystem.QueueStateEvent(_mouse, new MouseState { position = new Vector2(100, 100) });
            yield return null;
            InputSystem.QueueStateEvent(_gamepad, new GamepadState().WithButton(GamepadButton.North));
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject, Is.SameAs(second.gameObject));
        }

        [UnityTest]
        public IEnumerator InteractableChangesRestoreFocusWithoutExplicitNotification()
        {
            CreateScope(0, false, out var first, out var second);
            yield return MoveToSecond(second);
            second.interactable = false;
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject, Is.SameAs(first.gameObject));
            first.interactable = false;
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject, Is.Null);
            second.interactable = true;
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject, Is.SameAs(second.gameObject));
        }

        [UnityTest]
        public IEnumerator ReopenWaitsForChildrenBeforeRestoringRememberedSelection() => Reopen(true);

        [UnityTest]
        public IEnumerator ReopenUsesDefaultWhenRememberIsDisabled() => Reopen(false);

        private IEnumerator Reopen(bool remember)
        {
            var scope = CreateScope(0, remember, out var first, out var second);
            yield return MoveToSecond(second);
            scope.gameObject.SetActive(false);
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject, Is.Null);
            scope.gameObject.SetActive(true);
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject,
                Is.SameAs((remember ? second : first).gameObject));
        }

        [UnityTest]
        public IEnumerator KeyboardSubmitCannotReachCoveredOrHiddenScope()
        {
            var lower = CreateScope(0, false, out var first, out _);
            int clicks = 0;
            first.onClick.AddListener(() => clicks++);
            yield return null;
            InputSystem.QueueStateEvent(_mouse, new MouseState { delta = Vector2.right * 20 });
            yield return null;
            var higher = CreateScope(10, false, out _, out _);
            Assert.That(_eventSystem.currentSelectedGameObject, Is.Null);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Enter));
            yield return null;
            Assert.That(clicks, Is.Zero);
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            higher.gameObject.SetActive(false);
            yield return null;
            _eventSystem.SetSelectedGameObject(first.gameObject);
            InputTestViews.SetState(lower.Holder, UIState.Closing);
            InputTestViews.Raise(lower.Holder, nameof(UIHolderObjectBase.OnCloseEvent));
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(Key.Enter));
            yield return null;
            Assert.That(_eventSystem.currentSelectedGameObject, Is.Null);
            Assert.That(clicks, Is.Zero);
        }

        [UnityTest]
        public IEnumerator UserSelectionCallbackCanCloseScopeWithoutReentrantSelection()
        {
            CreateScope(0, false, out var fallback, out _);
            var higher = CreateScope(10, false, out _, out var second);
            var trigger = second.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            entry.callback.AddListener(_ => higher.gameObject.SetActive(false));
            trigger.triggers.Add(entry);
            yield return null;
            InputSystem.QueueStateEvent(_gamepad, new GamepadState().WithButton(GamepadButton.DpadRight));
            yield return null;
            InputSystem.QueueStateEvent(_gamepad, new GamepadState());
            yield return null;
            Assert.That(higher.gameObject.activeSelf, Is.False);
            Assert.That(_eventSystem.currentSelectedGameObject, Is.SameAs(fallback.gameObject));
        }
    }
}
