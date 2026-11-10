#if INPUTSYSTEM_SUPPORT
using System;
using System.IO;
using System.Reflection;
using System.Threading;
using AlicizaX.UI.Runtime;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
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

        private GameObject _serviceRoot;
        private GameObject _holderObject;
        private InputActionAsset _asset;
        private InputActionReference _reference;
        private Keyboard _keyboard;
        private CountingTrigger _trigger;
        private UIBase _view;
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
            AppServices.Register<IUIService>(new FocusService(_holderObject.GetComponent<TestHolder>()));

            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = _asset.AddActionMap("UI");
            InputAction action = map.AddAction("Back", InputActionType.Button, "<Keyboard>/escape");
            map.AddAction("Confirm", InputActionType.Button, "<Keyboard>/enter");
            _reference = InputActionReference.Create(action);
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
            Assert.That(_trigger.RegisteredAction, Is.SameAs(_reference.action));
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
            Assert.That(_trigger.RegisteredAction, Is.SameAs(_reference.action));
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
                Assert.That(_trigger.RegisteredAction, Is.SameAs(otherReference.action));
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

        // Only focus selection is exercised here; window loading and navigation belong to UI tests.
        private sealed class FocusService : ServiceBase, IUIService
        {
            private readonly UIHolderObjectBase _holder;
            public FocusService(UIHolderObjectBase holder) { _holder = holder; }

            public bool TryGetTopVisibleHolder(Predicate<UIHolderObjectBase> predicate, out UIHolderObjectBase holder)
            {
                holder = predicate == null || predicate(_holder) ? _holder : null;
                return holder != null;
            }

            public bool Initialize(Transform root, bool isOrthographic) => throw new NotSupportedException();
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
            public UniTask<UIRouteResult> NavigateTo<T>(object userData = null) where T : UIWindow => throw new NotSupportedException();
            public UniTask<UIRouteResult> Replace<T>(object userData = null) where T : UIWindow => throw new NotSupportedException();
            public UniTask<UIRouteResult> Back() => throw new NotSupportedException();
            public UniTask<UIRouteResult> CloseCurrent(bool force = false) => throw new NotSupportedException();
            public UniTask<UIRouteResult> BackToRoot() => throw new NotSupportedException();
            public UniTask<UIRouteResult> BackTo<T>(bool openIfMissing = true, object userData = null) where T : UIWindow => throw new NotSupportedException();
            public UniTask<UIRouteResult> ResetTo<T>(object userData = null) where T : UIWindow => throw new NotSupportedException();
            public UniTask<UIRouteResult> ResetHistory() => throw new NotSupportedException();
            public UniTask<UIRouteResult> SyncFromCurrentUI(Type currentPageType, object userData = null) => throw new NotSupportedException();
            public UniTask<UIRouteResult> SyncFromCurrentUI(RuntimeTypeHandle handle, object userData = null) => throw new NotSupportedException();
            public bool CanBack => throw new NotSupportedException();
            public Type Current => throw new NotSupportedException();
            public UIRouteEntry CurrentEntry => throw new NotSupportedException();
            public void SetUIBlock(float timeDuration) => throw new NotSupportedException();
            public void ForceExitBlock() => throw new NotSupportedException();
        }
    }
}
#endif
