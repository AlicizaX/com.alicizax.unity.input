#if INPUTSYSTEM_SUPPORT
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    public sealed class HotkeyInputPlayModeTests
    {
        private sealed class TestServiceRoot : AppServiceRoot { }
        private sealed class TestHolder : UIHolderObjectBase { }
        private sealed class CountingTrigger : HotkeyComponentBase
        {
            public int Count;
            public override void TriggerHotkey() => Count++;
        }

        private sealed class ActionTrigger : HotkeyComponentBase
        {
            public Action OnTrigger;
            public int Count;
            public override void TriggerHotkey()
            {
                Count++;
                OnTrigger?.Invoke();
            }
        }

        private sealed class SubmitCounter : MonoBehaviour, ISubmitHandler
        {
            public int Count;
            public void OnSubmit(BaseEventData eventData) => Count++;
        }

        private GameObject _serviceRoot;
        private GameObject _holderObject;
        private InputActionAsset _asset;
        private InputActionReference _reference;
        private InputActionReference _hotkeyAReference;
        private InputActionReference _hotkeyBReference;
        private Keyboard _keyboard;
        private CountingTrigger _trigger;
        private UIBase _view;
        private FocusService _focusService;
        private string _bindingFilePath;

        [SetUp]
        public void SetUp()
        {
            Assert.That(UXInput.IsInitialized, Is.False);
            Assert.That(AppServices.IsAvailable, Is.False);
            InputTestAssets.RemoveTestDevices();
            _serviceRoot = new GameObject("hotkey-service-root", typeof(TestServiceRoot));
            _holderObject = new GameObject("hotkey-holder", typeof(Canvas), typeof(TestHolder));
            _view = InputTestViews.Bind(_holderObject.GetComponent<TestHolder>());
            _holderObject.layer = UIComponent.UIShowLayer;
            _focusService = new FocusService(_holderObject.GetComponent<TestHolder>());
            AppServices.Register<IUIService>(_focusService);

            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = _asset.AddActionMap("UI");
            InputAction action = map.AddAction("Back", InputActionType.Button, "<Keyboard>/escape");
            map.AddAction("Confirm", InputActionType.Button, "<Keyboard>/enter");
            InputAction hotkeyA = map.AddAction("HotkeyA", InputActionType.Button, "<Keyboard>/f1");
            InputAction hotkeyB = map.AddAction("HotkeyB", InputActionType.Button, "<Keyboard>/f2");
            _reference = InputActionReference.Create(action);
            _hotkeyAReference = InputActionReference.Create(hotkeyA);
            _hotkeyBReference = InputActionReference.Create(hotkeyB);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _bindingFilePath = Path.Combine(Path.GetTempPath(), "hotkey-" + Guid.NewGuid().ToString("N") + ".json");
            UXInput.Initialize(_asset, null, _bindingFilePath);

            // 注册发生在动作禁用状态下，验证注册本身不依赖动作启用。
            _asset.Disable();

            var triggerObject = new GameObject("hotkey-trigger");
            triggerObject.SetActive(false);
            triggerObject.transform.SetParent(_holderObject.transform, false);
            _trigger = triggerObject.AddComponent<CountingTrigger>();
            _trigger.HotkeyAction = _reference;
            typeof(HotkeyComponentBase)
                .GetField("_holder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(_trigger, _holderObject.GetComponent<TestHolder>());
            triggerObject.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            UXInput.Shutdown();
            InputTestViews.UnbindAll();
            Object.DestroyImmediate(_holderObject);
            Object.DestroyImmediate(_serviceRoot);
            InputTestAssets.RemoveTestDevices();
            Object.DestroyImmediate(_reference);
            Object.DestroyImmediate(_hotkeyAReference);
            Object.DestroyImmediate(_hotkeyBReference);
            Object.DestroyImmediate(_asset);
        }

        [Test]
        public void DisabledActionCanRegisterAndRespondAfterEnable()
        {
            Assert.That(_trigger.IsRegistered, Is.True);
            Assert.That(_reference.action.enabled, Is.False);
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.Zero);

            _asset.Enable();
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));

            _asset.Disable();
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));
            _asset.Enable();
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(2));
        }

        [Test]
        public void DirectBindingOverrideUpdatesHotkeyWithoutReregistering()
        {
            _asset.Enable();
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));

            _reference.action.ApplyBindingOverride("<Keyboard>/space");
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));
            Tap(Key.Space);
            Assert.That(_trigger.Count, Is.EqualTo(2));
            Assert.That(_trigger.Registration.Action, Is.SameAs(_reference.action));
        }

        [Test]
        public void RebindUpdatesHotkeyWithoutReregistering()
        {
            _asset.Enable();
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));

            UXInput.Rebind.PrepareRebind(_reference.action, 0, "<Keyboard>/space");
            Assert.That(UXInput.Rebind.ConfirmApply(false, false), Is.True);
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));
            Tap(Key.Space);
            Assert.That(_trigger.Count, Is.EqualTo(2));
            Assert.That(_trigger.Registration.Action, Is.SameAs(_reference.action));
        }

        [Test]
        public void ShutdownReleasesRegistrationAndSameAssetCanResume()
        {
            _asset.Enable();
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));

            UXInput.Shutdown();
            Assert.That(_trigger.IsRegistered, Is.False);
            Assert.That(UXInput.Hotkey, Is.Null);
            Assert.That(_reference.action.enabled, Is.False);
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));

            UXInput.Initialize(_asset, null, _bindingFilePath);
            _trigger.gameObject.SetActive(false);
            _trigger.gameObject.SetActive(true);
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(2));
        }

        [Test]
        public void ComponentDisableAndEnableDoesNotDuplicateCallbacks()
        {
            _asset.Enable();
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));
            _trigger.gameObject.SetActive(false);
            Assert.That(_trigger.IsRegistered, Is.False);
            Assert.That(_reference.action.enabled, Is.True);
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));

            _trigger.gameObject.SetActive(true);
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(2));
        }

        [Test]
        public void ChangingReferenceUnsubscribesOldAction()
        {
            _asset.Enable();
            var otherReference = InputActionReference.Create(_asset.FindAction("UI/Confirm"));
            try
            {
                _trigger.HotkeyAction = otherReference;
                Tap(Key.Escape);
                Assert.That(_trigger.Count, Is.Zero);
                Tap(Key.Enter);
                Assert.That(_trigger.Count, Is.EqualTo(1));
            Assert.That(_trigger.Registration.Action, Is.SameAs(otherReference.action));
            }
            finally
            {
                _trigger.HotkeyAction = _reference;
                Object.DestroyImmediate(otherReference);
            }
        }

        [Test]
        public void ShutdownDetachesCallbacksWithoutDisablingAction()
        {
            _asset.Enable();
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));
            UXInput.Hotkey.Shutdown();
            Assert.That(_trigger.IsRegistered, Is.False);
            Assert.That(_reference.action.enabled, Is.True);
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));
        }

        private void Tap(Key key)
        {
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState(key));
            InputSystem.Update();
            InputSystem.QueueStateEvent(_keyboard, new KeyboardState());
            InputSystem.Update();
        }

        [Test]
        public void WidgetHotkeysFollowLogicalStateWithoutSelectableFocus()
        {
            _asset.Enable();
            var first = WidgetTrigger(UIState.Opened, out UIBase a);
            var second = WidgetTrigger(UIState.Closed, out UIBase b);
            Tap(Key.Escape);
            Assert.That(first.Count, Is.EqualTo(1));
            Assert.That(second.Count, Is.Zero);
            Assert.That(_trigger.Count, Is.Zero);

            InputTestViews.SetState(a, UIState.Closing);
            InputTestViews.Raise(a.GetHolder(), nameof(UIHolderObjectBase.OnCloseEvent));
            InputTestViews.SetState(b, UIState.Opening);
            InputTestViews.Raise(b.GetHolder(), nameof(UIHolderObjectBase.OnShowEvent));
            Tap(Key.Escape);
            Assert.That(first.Count, Is.EqualTo(1));
            Assert.That(second.Count, Is.EqualTo(1));
            Assert.That(a.Visible, Is.True);

            InputTestViews.SetState(_view, UIState.Closing);
            InputTestViews.Raise(_view.GetHolder(), nameof(UIHolderObjectBase.OnCloseEvent));
            Tap(Key.Escape);
            Assert.That(second.Count, Is.EqualTo(1));
            Assert.That(_trigger.Count, Is.Zero);
        }

        private CountingTrigger WidgetTrigger(UIState state, out UIBase view)
        {
            var root = new GameObject("hotkey-widget", typeof(RectTransform), typeof(CanvasGroup));
            root.transform.SetParent(_holderObject.transform, false);
            root.SetActive(false);
            var holder = root.AddComponent<TestHolder>();
            view = InputTestViews.Bind(holder, _view, state);
            var trigger = root.AddComponent<CountingTrigger>();
            trigger.HotkeyAction = _reference;
            typeof(HotkeyComponentBase).GetField("_holder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(trigger, holder);
            root.SetActive(true);
            return trigger;
        }

        private sealed class SameHolderGroup
        {
            public GameObject Root;
            public CountingTrigger HotkeyA;
            public CountingTrigger HotkeyB;
        }

        private sealed class WidgetGroup
        {
            public GameObject Root;
            public TestHolder Holder;
            public UIBase View;
            public CountingTrigger HotkeyA;
            public CountingTrigger HotkeyB;
        }

        [Test]
        public void SameHolderHotkeyGroupsFollowActiveGroup()
        {
            _asset.Enable();
            SameHolderGroup first = CreateSameHolderGroup("hotkey-group-a");
            SameHolderGroup second = CreateSameHolderGroup("hotkey-group-b");

            first.Root.SetActive(true);
            Tap(Key.F1);
            Tap(Key.F2);
            Assert.That(first.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(first.HotkeyB.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyA.Count, Is.Zero);
            Assert.That(second.HotkeyB.Count, Is.Zero);

            first.Root.SetActive(false);
            second.Root.SetActive(true);
            Assert.That(first.HotkeyA.IsRegistered, Is.False);
            Assert.That(second.HotkeyA.IsRegistered, Is.True);
            Tap(Key.F1);
            Tap(Key.F2);
            Assert.That(first.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(first.HotkeyB.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyB.Count, Is.EqualTo(1));

            second.Root.SetActive(false);
            first.Root.SetActive(true);
            Assert.That(first.HotkeyA.IsRegistered, Is.True);
            Assert.That(second.HotkeyA.IsRegistered, Is.False);
            Tap(Key.F1);
            Tap(Key.F2);
            Assert.That(first.HotkeyA.Count, Is.EqualTo(2));
            Assert.That(first.HotkeyB.Count, Is.EqualTo(2));
            Assert.That(second.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyB.Count, Is.EqualTo(1));
        }

        [Test]
        public void SameHolderLastActivatedGroupWins()
        {
            _asset.Enable();
            SameHolderGroup first = CreateSameHolderGroup("hotkey-group-a");
            SameHolderGroup second = CreateSameHolderGroup("hotkey-group-b");

            first.Root.SetActive(true);
            second.Root.SetActive(true);
            Tap(Key.F1);
            Tap(Key.F2);

            Assert.That(first.HotkeyA.Count, Is.Zero);
            Assert.That(first.HotkeyB.Count, Is.Zero);
            Assert.That(second.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyB.Count, Is.EqualTo(1));
        }

        [Test]
        public void WidgetHotkeysFollowOpenWidget()
        {
            _asset.Enable();
            WidgetGroup first = CreateWidgetGroup("hotkey-widget-a");
            WidgetGroup second = CreateWidgetGroup("hotkey-widget-b");

            OpenWidget(first);
            Tap(Key.F1);
            Tap(Key.F2);
            Assert.That(first.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(first.HotkeyB.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyA.Count, Is.Zero);
            Assert.That(second.HotkeyB.Count, Is.Zero);

            CloseWidget(first);
            OpenWidget(second);
            Tap(Key.F1);
            Tap(Key.F2);
            Assert.That(first.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(first.HotkeyB.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyB.Count, Is.EqualTo(1));

            CloseWidget(second);
            OpenWidget(first);
            Tap(Key.F1);
            Tap(Key.F2);
            Assert.That(first.HotkeyA.Count, Is.EqualTo(2));
            Assert.That(first.HotkeyB.Count, Is.EqualTo(2));
            Assert.That(second.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyB.Count, Is.EqualTo(1));
        }

        [Test]
        public void LaterOpenedWidgetWinsWhenBothOpen()
        {
            _asset.Enable();
            WidgetGroup first = CreateWidgetGroup("hotkey-widget-a");
            WidgetGroup second = CreateWidgetGroup("hotkey-widget-b");

            OpenWidget(first);
            OpenWidget(second);
            Tap(Key.F1);
            Tap(Key.F2);

            Assert.That(first.HotkeyA.Count, Is.Zero);
            Assert.That(first.HotkeyB.Count, Is.Zero);
            Assert.That(second.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyB.Count, Is.EqualTo(1));

            CloseWidget(second);
            Tap(Key.F1);
            Tap(Key.F2);
            Assert.That(first.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(first.HotkeyB.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyB.Count, Is.EqualTo(1));
        }

        [Test]
        public void StartedPressThatClosesWindowDoesNotLeakToAnotherWindow()
        {
            _asset.Enable();

            var lowerRoot = new GameObject("lower-window", typeof(TestHolder));
            lowerRoot.transform.SetParent(_holderObject.transform, false);
            var lowerHolder = lowerRoot.GetComponent<TestHolder>();
            InputTestViews.Bind(lowerHolder);
            CountingTrigger lowerTrigger = CreateCountingTrigger(lowerRoot, lowerHolder, _hotkeyAReference);

            var startedObject = new GameObject("started-trigger");
            startedObject.SetActive(false);
            startedObject.transform.SetParent(_holderObject.transform, false);
            var startedTrigger = startedObject.AddComponent<ActionTrigger>();
            startedTrigger.HotkeyAction = _hotkeyAReference;
            typeof(HotkeyComponentBase)
                .GetField("_hotkeyPressType", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(startedTrigger, EHotkeyPressType.Started);
            typeof(HotkeyComponentBase)
                .GetField("_holder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(startedTrigger, _holderObject.GetComponent<TestHolder>());
            startedTrigger.OnTrigger = () =>
            {
                InputTestViews.SetState(_view, UIState.Closing);
                InputTestViews.Raise(
                    _holderObject.GetComponent<TestHolder>(),
                    nameof(UIHolderObjectBase.OnCloseEvent));
                _focusService.SetHolder(lowerHolder);
            };
            startedObject.SetActive(true);

            Tap(Key.F1);

            Assert.That(startedTrigger.Count, Is.EqualTo(1));
            Assert.That(lowerTrigger.Count, Is.Zero,
                "Performed must not re-capture a different window after the pressed window closed");
        }

        [Test]
        public void InvalidReferenceKeepsPreviousRegistrationAndRollsBackField()
        {
            _asset.Enable();
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));

            var emptyReference = ScriptableObject.CreateInstance<InputActionReference>();
            try
            {
                _trigger.HotkeyAction = emptyReference;

                Assert.That(_trigger.HotkeyAction, Is.SameAs(_reference));
                Tap(Key.Escape);
                Assert.That(_trigger.Count, Is.EqualTo(2),
                    "an invalid new reference must not destroy the old registration");
            }
            finally
            {
                Object.DestroyImmediate(emptyReference);
            }
        }

        [Test]
        public void RegisteringAnotherHotkeyDoesNotStealWidgetPriority()
        {
            _asset.Enable();
            WidgetGroup first = CreateWidgetGroup("hotkey-widget-a");
            WidgetGroup second = CreateWidgetGroup("hotkey-widget-b");
            OpenWidget(first);
            OpenWidget(second);

            // 后打开的是 B；在 A 下再注册一个热键不能把 A 抬成“后显示”。
            CountingTrigger extra = CreateCountingTrigger(first.Root, first.Holder, _hotkeyAReference);

            Tap(Key.F1);
            Tap(Key.F2);

            Assert.That(extra.Count, Is.Zero);
            Assert.That(first.HotkeyA.Count, Is.Zero);
            Assert.That(first.HotkeyB.Count, Is.Zero);
            Assert.That(second.HotkeyA.Count, Is.EqualTo(1));
            Assert.That(second.HotkeyB.Count, Is.EqualTo(1));
        }

        [Test]
        public void MultipleHotkeyComponentsOnOneGameObjectDispatchIndependently()
        {
            _asset.Enable();
            var root = new GameObject("multi-hotkey-object", typeof(RectTransform));
            root.SetActive(false);
            root.transform.SetParent(_holderObject.transform, false);
            var submit = root.AddComponent<SubmitCounter>();
            var first = root.AddComponent<HotkeyComponent>();
            var second = root.AddComponent<HotkeyComponent>();
            first.HotkeyAction = _hotkeyAReference;
            second.HotkeyAction = _hotkeyBReference;

            var holder = _holderObject.GetComponent<TestHolder>();
            var holderField = typeof(HotkeyComponentBase)
                .GetField("_holder", BindingFlags.Instance | BindingFlags.NonPublic);
            holderField.SetValue(first, holder);
            holderField.SetValue(second, holder);
            var submitTargetField = typeof(HotkeyComponent)
                .GetField("_submitTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            submitTargetField.SetValue(first, submit);
            submitTargetField.SetValue(second, submit);

            var eventObject = new GameObject("multi-hotkey-event-system", typeof(EventSystem));
            EventSystem.current = eventObject.GetComponent<EventSystem>();
            try
            {
                root.SetActive(true);
                Tap(Key.F1);
                Tap(Key.F2);

                Assert.That(first.IsRegistered, Is.True);
                Assert.That(second.IsRegistered, Is.True);
                Assert.That(submit.Count, Is.EqualTo(2));
            }
            finally
            {
                Object.DestroyImmediate(eventObject);
            }
        }

        private SameHolderGroup CreateSameHolderGroup(string name)
        {
            var root = new GameObject(name);
            root.SetActive(false);
            root.transform.SetParent(_holderObject.transform, false);
            var holder = _holderObject.GetComponent<TestHolder>();
            return new SameHolderGroup
            {
                Root = root,
                HotkeyA = CreateCountingTrigger(root, holder, _hotkeyAReference),
                HotkeyB = CreateCountingTrigger(root, holder, _hotkeyBReference),
            };
        }

        private WidgetGroup CreateWidgetGroup(string name)
        {
            var root = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup));
            root.SetActive(false);
            root.transform.SetParent(_holderObject.transform, false);
            var holder = root.AddComponent<TestHolder>();
            UIBase view = InputTestViews.Bind(holder, _view, UIState.Closed);
            var group = new WidgetGroup
            {
                Root = root,
                Holder = holder,
                View = view,
                HotkeyA = CreateCountingTrigger(root, holder, _hotkeyAReference),
                HotkeyB = CreateCountingTrigger(root, holder, _hotkeyBReference),
            };
            root.SetActive(true);
            return group;
        }

        private CountingTrigger CreateCountingTrigger(
            GameObject parent,
            UIHolderObjectBase holder,
            InputActionReference action)
        {
            var triggerObject = new GameObject("counting-trigger");
            triggerObject.SetActive(false);
            triggerObject.transform.SetParent(parent.transform, false);
            var trigger = triggerObject.AddComponent<CountingTrigger>();
            trigger.HotkeyAction = action;
            typeof(HotkeyComponentBase)
                .GetField("_holder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(trigger, holder);
            triggerObject.SetActive(true);
            return trigger;
        }

        private void OpenWidget(WidgetGroup widget)
        {
            InputTestViews.SetState(widget.View, UIState.Opened);
            InputTestViews.Raise(widget.Holder, nameof(UIHolderObjectBase.OnShowEvent));
        }

        private void CloseWidget(WidgetGroup widget)
        {
            InputTestViews.SetState(widget.View, UIState.Closing);
            InputTestViews.Raise(widget.Holder, nameof(UIHolderObjectBase.OnCloseEvent));
        }

        // Only focus selection is exercised here; window loading and navigation belong to UI tests.
        private sealed class FocusService : ServiceBase, IUIService
        {
            private UIHolderObjectBase _holder;
            public FocusService(UIHolderObjectBase holder) { _holder = holder; }

            public void SetHolder(UIHolderObjectBase holder) => _holder = holder;

            public bool TryGetTopVisibleHolder(Predicate<UIHolderObjectBase> predicate, out UIHolderObjectBase holder)
            {
                holder = predicate == null || predicate(_holder) ? _holder : null;
                return holder != null;
            }

            public IUINavigationService Navigation => throw new NotSupportedException();
            public Camera UICamera => throw new NotSupportedException();
            public Transform UICanvasRoot => throw new NotSupportedException();
            public RectTransform GetLayer(UILayer layer) => throw new NotSupportedException();
            public UniTask<T> ShowUI<T>(object userData = null) where T : UIWindow => throw new NotSupportedException();
            public UniTask<T> ShowUI<T>(CancellationToken cancellationToken, object userData = null) where T : UIWindow => throw new NotSupportedException();
            public UniTask<UIWindow> ShowUI(string type, object userData = null) => throw new NotSupportedException();
            public UniTask<UIWindow> ShowUI(string type, CancellationToken cancellationToken, object userData = null) => throw new NotSupportedException();
            public UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, object userData = null) => throw new NotSupportedException();
            public UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, CancellationToken cancellationToken, object userData = null) => throw new NotSupportedException();
            public T ShowUISync<T>(object userData = null) where T : UIWindow => throw new NotSupportedException();
            public UIViewTransition CloseUI<T>(bool force = false, bool skipTransition = false) where T : UIWindow => throw new NotSupportedException();
            public UIViewTransition CloseUI(RuntimeTypeHandle handle, bool force = false, bool skipTransition = false) => throw new NotSupportedException();
            public bool IsOpen<T>() where T : UIWindow => throw new NotSupportedException();
            public bool IsOpen(RuntimeTypeHandle handle) => throw new NotSupportedException();
            public UniTask<bool> TryCloseTopAsync(Predicate<RuntimeTypeHandle> predicate, bool force = false) => throw new NotSupportedException();
            public T GetUI<T>() where T : UIWindow => throw new NotSupportedException();
            public void SetUIBlock(float timeDuration) => throw new NotSupportedException();
            public void ForceExitBlock() => throw new NotSupportedException();
        }
    }
}
#endif
