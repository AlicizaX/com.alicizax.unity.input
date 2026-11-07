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
        private string _bindingFilePath;

        [SetUp]
        public void SetUp()
        {
            Assert.That(UXInput.IsInitialized, Is.False);
            Assert.That(AppServices.IsAvailable, Is.False);
            UXHotkeySystem.Shutdown();
            _serviceRoot = new GameObject("hotkey-service-root", typeof(TestServiceRoot));
            _holderObject = new GameObject("hotkey-holder", typeof(Canvas), typeof(TestHolder));
            _holderObject.layer = UIComponent.UIShowLayer;
            AppServices.Register<IUIService>(new FocusService(_holderObject.GetComponent<TestHolder>()));

            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            InputActionMap map = _asset.AddActionMap("UI");
            InputAction action = map.AddAction("Back", InputActionType.Button, "<Keyboard>/escape");
            map.AddAction("Confirm", InputActionType.Button, "<Keyboard>/enter");
            _reference = InputActionReference.Create(action);
            UXHotkeySystem.Initialize(_asset);
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _bindingFilePath = Path.Combine(Path.GetTempPath(), "hotkey-" + Guid.NewGuid().ToString("N") + ".json");

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
            UXHotkeySystem.Shutdown();
            UXInput.Shutdown();
            Object.DestroyImmediate(_holderObject);
            Object.DestroyImmediate(_serviceRoot);
            if (_keyboard != null && _keyboard.added) InputSystem.RemoveDevice(_keyboard);
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
            Assert.That(UXInput.IsInitialized, Is.False);

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
            Assert.That(UXInput.IsInitialized, Is.False);
        }

        [Test]
        public void RebindUpdatesHotkeyWithoutReregistering()
        {
            UXInput.Initialize(_asset, null, _bindingFilePath);
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
        public void InputShutdownKeepsRegistrationAndSameAssetCanResume()
        {
            UXInput.Initialize(_asset, null, _bindingFilePath);
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));

            UXInput.Shutdown();
            Assert.That(_trigger.IsRegistered, Is.True);
            Assert.That(_reference.action.enabled, Is.False);
            Tap(Key.Escape);
            Assert.That(_trigger.Count, Is.EqualTo(1));

            _asset.Enable();
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
            UXHotkeySystem.Shutdown();
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

            public void Initialize(Transform root, bool isOrthographic) => throw new NotSupportedException();
            public Camera UICamera => throw new NotSupportedException();
            public Transform UICanvasRoot => throw new NotSupportedException();
            public RectTransform GetLayer(UILayer layer) => throw new NotSupportedException();
            public UniTask<T> ShowUI<T>(params object[] userDatas) where T : UIWindow => throw new NotSupportedException();
            public UniTask<T> ShowUI<T>(CancellationToken cancellationToken, params object[] userDatas) where T : UIWindow => throw new NotSupportedException();
            public UniTask<UIWindow> ShowUI(string type, params object[] userDatas) => throw new NotSupportedException();
            public UniTask<UIWindow> ShowUI(string type, CancellationToken cancellationToken, params object[] userDatas) => throw new NotSupportedException();
            public UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, params object[] userDatas) => throw new NotSupportedException();
            public UniTask<UIWindow> ShowUI(RuntimeTypeHandle handle, CancellationToken cancellationToken, params object[] userDatas) => throw new NotSupportedException();
            public T ShowUISync<T>(params object[] userDatas) where T : UIWindow => throw new NotSupportedException();
            public UniTask CloseUI<T>(bool force = false, bool skipTransition = false) where T : UIWindow => throw new NotSupportedException();
            public UniTask CloseUI(RuntimeTypeHandle handle, bool force = false, bool skipTransition = false) => throw new NotSupportedException();
            public bool IsOpen<T>() where T : UIWindow => throw new NotSupportedException();
            public bool IsOpen(RuntimeTypeHandle handle) => throw new NotSupportedException();
            public UniTask<bool> TryCloseTopAsync(Predicate<RuntimeTypeHandle> predicate, bool force = false) => throw new NotSupportedException();
            public T GetUI<T>() where T : UIWindow => throw new NotSupportedException();
            public UniTask<UIRouteResult> NavigateTo<T>(params object[] args) where T : UIWindow => throw new NotSupportedException();
            public UniTask<UIRouteResult> Replace<T>(params object[] args) where T : UIWindow => throw new NotSupportedException();
            public UniTask<UIRouteResult> Back() => throw new NotSupportedException();
            public UniTask<UIRouteResult> CloseCurrent(bool force = false) => throw new NotSupportedException();
            public UniTask<UIRouteResult> BackToRoot() => throw new NotSupportedException();
            public UniTask<UIRouteResult> BackTo<T>(bool openIfMissing = true, params object[] args) where T : UIWindow => throw new NotSupportedException();
            public UniTask<UIRouteResult> ResetTo<T>(params object[] args) where T : UIWindow => throw new NotSupportedException();
            public UniTask<UIRouteResult> ResetHistory() => throw new NotSupportedException();
            public UniTask<UIRouteResult> SyncFromCurrentUI(Type currentPageType, params object[] args) => throw new NotSupportedException();
            public UniTask<UIRouteResult> SyncFromCurrentUI(RuntimeTypeHandle handle, params object[] args) => throw new NotSupportedException();
            public bool CanBack => throw new NotSupportedException();
            public Type Current => throw new NotSupportedException();
            public UIRouteEntry CurrentEntry => throw new NotSupportedException();
            public void SetUIBlock(float timeDuration) => throw new NotSupportedException();
            public void ForceExitBlock() => throw new NotSupportedException();
        }
    }
}
#endif
