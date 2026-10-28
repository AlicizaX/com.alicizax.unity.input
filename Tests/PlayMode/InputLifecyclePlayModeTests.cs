#if INPUTSYSTEM_SUPPORT
using System.Collections;
using System.Reflection;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AlicizaX.Input.Tests
{
    public sealed class InputLifecyclePlayModeTests
    {
        private sealed class TestHolder : UIHolderObjectBase
        {
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
            var provider = providerObject.AddComponent<InputActionProvider>();
            typeof(InputActionProvider)
                .GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(provider, asset);
            providerObject.SetActive(true);

            yield return null;

            Assert.That(InputActionProvider.Actions, Is.SameAs(asset));
            Assert.That(UXInput.Watch.CurrentProfileId, Is.Not.Empty);
            Assert.That(UXInput.Rebind.IsInitialized, Is.True);

            var holderObject = new GameObject("hotkey-holder");
            holderObject.layer = UIComponent.UIShowLayer;
            var holder = holderObject.AddComponent<TestHolder>();

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
            Assert.That(UXHotkeySystemRegistryCount("Scopes"), Is.EqualTo(1));

            Object.Destroy(providerObject);
            yield return null;

            Assert.That(trigger.IsRegistered, Is.False, "provider teardown must unregister hotkeys");
            Assert.That(UXHotkeySystemRegistryCount("Scopes"), Is.Zero);
            Assert.That(UXHotkeySystemRegistryCount("Actions"), Is.Zero);
            Assert.That(UXHotkeySystemRegistryCount("ScopeList"), Is.Zero);
            Assert.That(InputActionProvider.Actions, Is.Null);
            Assert.That(UXInput.Rebind.IsInitialized, Is.False);

            Object.Destroy(holderObject);
            yield return null;

            if (asset != null)
            {
                Object.Destroy(asset);
            }
        }

        private static int UXHotkeySystemRegistryCount(string fieldName)
        {
            FieldInfo field = typeof(UXHotkeySystem).GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "missing registry field " + fieldName);
            return field.GetValue(null) is System.Collections.ICollection collection ? collection.Count : -1;
        }
    }
}
#endif
