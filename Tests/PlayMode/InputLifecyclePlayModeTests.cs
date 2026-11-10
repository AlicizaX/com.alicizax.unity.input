#if INPUTSYSTEM_SUPPORT
using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using AlicizaX;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AlicizaX.Input.Tests
{
    public sealed class InputLifecyclePlayModeTests
    {
        private sealed class TestServiceRoot : AppServiceRoot
        {
        }

        private sealed class TestHolder : UIHolderObjectBase
        {
        }

        private sealed class TestGlyph : InputGlyphBehaviourBase
        {
            public int RefreshCount;
            protected override void RefreshGlyph() { RefreshCount++; }
        }

        private GameObject _serviceRoot;

        [SetUp]
        public void SetUp()
        {
            InputTestAssets.RemoveTestDevices();
            if (!AppServices.IsAvailable)
            {
                _serviceRoot = new GameObject("input-service-root", typeof(TestServiceRoot));
            }
        }

        [TearDown]
        public void TearDown()
        {
            // 用例可能在 yield 边界被判失败而中断，finally 不执行；在 TearDown 统一回收输入会话和测试设备。
            UXInput.Shutdown();
            InputTestAssets.RemoveTestDevices();
            InputTestViews.UnbindAll();
            if (_serviceRoot != null)
            {
                Object.DestroyImmediate(_serviceRoot);
                _serviceRoot = null;
            }
        }

        [UnityTest]
        public IEnumerator ProviderWithoutAssetLogsErrorAndDoesNotInitialize()
        {
            UXInput.Shutdown();
            var root = new GameObject("input-provider-without-asset");
            try
            {
                LogAssert.Expect(LogType.Error, new Regex("InputService requires an InputActionAsset"));
                root.AddComponent<InputService>();
                yield return null;
                Assert.That(UXInput.IsInitialized, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator ProviderDisableKeepsEnabledGlyphAndHotkeyConnected()
        {
            var asset = InputTestAssets.Create();
            var provider = CreateProvider(asset);
            var glyphObject = new GameObject("lifecycle-glyph");
            glyphObject.SetActive(false);
            var glyph = glyphObject.AddComponent<TestGlyph>();
            var holderObject = new GameObject("lifecycle-holder", typeof(Canvas));
            holderObject.layer = UIComponent.UIShowLayer;
            var holder = holderObject.AddComponent<TestHolder>();
            InputTestViews.Bind(holder);
            var triggerObject = new GameObject("lifecycle-hotkey");
            triggerObject.SetActive(false);
            triggerObject.transform.SetParent(holderObject.transform, false);
            var trigger = triggerObject.AddComponent<HotkeyComponent>();
            var reference = InputActionReference.Create(asset.FindAction("Gameplay/Jump"));
            typeof(HotkeyComponentBase).GetField("_holder", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(trigger, holder);
            typeof(HotkeyComponentBase).GetField("_hotkeyAction", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(trigger, reference);
            try
            {
                provider.SetActive(true);
                glyphObject.SetActive(true);
                triggerObject.SetActive(true);
                var watch = UXInput.Watch;
                provider.GetComponent<InputService>().enabled = false;
                Assert.That(UXInput.IsInitialized, Is.True);
                provider.GetComponent<InputService>().enabled = true;
                provider.SetActive(false);
                Assert.That(UXInput.Watch, Is.SameAs(watch));
                Assert.That(UXInput.IsInitialized, Is.True);
                provider.SetActive(true);
                Assert.That(UXInput.Watch, Is.SameAs(watch));
                Assert.That(trigger.IsRegistered, Is.True);
                int refreshes = glyph.RefreshCount;
                UXInput.Rebind.ResetAllBindings(save: false);
                Assert.That(glyph.RefreshCount, Is.EqualTo(refreshes + 1));
                Assert.That(trigger.RegisteredAction, Is.SameAs(asset.FindAction("Gameplay/Jump")));
                yield return null;
            }
            finally
            {
                Object.DestroyImmediate(provider);
                Object.DestroyImmediate(glyphObject);
                InputTestViews.UnbindAll();
                Object.DestroyImmediate(holderObject);
                Object.DestroyImmediate(reference);
                Object.DestroyImmediate(asset);
            }
        }

        private static GameObject CreateProvider(InputActionAsset asset)
        {
            var root = new GameObject("input-provider");
            root.SetActive(false);
            var provider = root.AddComponent<InputService>();
            typeof(InputService).GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(provider, asset);
            return root;
        }

        [UnityTest]
        public IEnumerator GlyphRefreshesForProfileChangesButNotKeyboardMouseSwitches()
        {
            var asset = InputTestAssets.Create();
            var provider = CreateProvider(asset);
            provider.SetActive(true);
            var keyboard = InputSystem.AddDevice<Keyboard>();
            var mouse = InputSystem.AddDevice<Mouse>();
            Gamepad gamepad = null;
            var glyphObject = new GameObject("profile-change-glyph");
            var glyph = glyphObject.AddComponent<TestGlyph>();
            try
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Space));
                InputSystem.Update();
                int refreshes = glyph.RefreshCount;
                InputSystem.QueueStateEvent(mouse, new MouseState().WithButton(MouseButton.Left));
                InputSystem.Update();
                Assert.That(glyph.RefreshCount, Is.EqualTo(refreshes));
                gamepad = InputSystem.AddDevice<Gamepad>();
                InputSystem.QueueStateEvent(gamepad, new GamepadState().WithButton(GamepadButton.South));
                InputSystem.Update();
                Assert.That(glyph.RefreshCount, Is.EqualTo(refreshes + 1));
                yield return null;
            }
            finally
            {
                Object.DestroyImmediate(glyphObject);
                Object.DestroyImmediate(provider);
                InputSystem.RemoveDevice(keyboard);
                InputSystem.RemoveDevice(mouse);
                if (gamepad != null) InputSystem.RemoveDevice(gamepad);
                Object.DestroyImmediate(asset);
            }
        }

        [UnityTest]
        public IEnumerator ProviderLifecycleInitializesAndTearsDownInputServices()
        {
            InputActionAsset asset = ScriptableObject.CreateInstance<InputActionAsset>();
            asset.name = "PlayModeInputTests";
            InputActionMap map = asset.AddActionMap("Gameplay");
            map.AddAction("Jump", InputActionType.Button, "<Keyboard>/space");

            var providerObject = new GameObject("input-provider");
            providerObject.SetActive(false);
            var provider = providerObject.AddComponent<InputService>();
            typeof(InputService)
                .GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(provider, asset);
            providerObject.SetActive(true);

            yield return null;

            Assert.That(UXInput.Reader.Actions, Is.SameAs(asset));
            Assert.That(UXInput.Watch.CurrentProfileId, Is.Not.Empty);
            Assert.That(UXInput.Rebind.IsInitialized, Is.True);

            var holderObject = new GameObject("hotkey-holder");
            holderObject.layer = UIComponent.UIShowLayer;
            var holder = holderObject.AddComponent<TestHolder>();
            InputTestViews.Bind(holder);

            var triggerObject = new GameObject("hotkey-trigger");
            triggerObject.SetActive(false);
            triggerObject.transform.SetParent(holderObject.transform, false);
            var trigger = triggerObject.AddComponent<HotkeyComponent>();
            InputActionReference reference = InputActionReference.Create(asset.FindAction("Gameplay/Jump"));
            typeof(HotkeyComponentBase)
                .GetField("_holder", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(trigger, holder);
            typeof(HotkeyComponentBase)
                .GetField("_hotkeyAction", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(trigger, reference);
            triggerObject.SetActive(true);

            yield return null;

            Assert.That(trigger.IsRegistered, Is.True, "enabled hotkey must register against the provider asset");
            InputHotkey hotkey = UXInput.Hotkey;
            Assert.That(InputHotkeyRegistryCount(hotkey, "_scopesByHolder"), Is.EqualTo(1));

            Object.Destroy(providerObject);
            yield return null;

            Assert.That(trigger.IsRegistered, Is.False, "provider teardown must unregister hotkeys");
            Assert.That(UXInput.Hotkey, Is.Null);
            Assert.That(InputHotkeyRegistryCount(hotkey, "_scopesByHolder"), Is.Zero);
            Assert.That(InputHotkeyRegistryCount(hotkey, "_actionStates"), Is.Zero);
            Assert.That(InputHotkeyRegistryCount(hotkey, "_scopeList"), Is.Zero);
            Assert.That(UXInput.Reader.Actions, Is.Null);
            Assert.That(UXInput.Rebind.IsInitialized, Is.False);

            InputTestViews.UnbindAll();
            Object.Destroy(holderObject);
            yield return null;

            if (asset != null)
            {
                Object.Destroy(asset);
            }
        }

        private static int InputHotkeyRegistryCount(InputHotkey hotkey, string fieldName)
        {
            FieldInfo field = typeof(InputHotkey).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "missing registry field " + fieldName);
            return field.GetValue(hotkey) is ICollection collection ? collection.Count : -1;
        }
    }
}
#endif
