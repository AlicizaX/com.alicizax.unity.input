#if INPUTSYSTEM_SUPPORT
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    public sealed class HotkeySubmitListenerPlayModeTests
    {
        private sealed class CountingSubmitListener : MonoBehaviour, IHotkeySubmitListener
        {
            public int Count;

            public void OnHotkeySubmit(BaseEventData eventData)
            {
                Count++;
            }
        }

        private sealed class CountingDualListener : MonoBehaviour, ISubmitHandler, IHotkeySubmitListener
        {
            public int SubmitCount;
            public int ListenerCount;

            public void OnSubmit(BaseEventData eventData)
            {
                SubmitCount++;
            }

            public void OnHotkeySubmit(BaseEventData eventData)
            {
                ListenerCount++;
            }
        }

        private GameObject _root;
        private GameObject _eventObject;

        [TearDown]
        public void TearDown()
        {
            if (_root != null)
                Object.DestroyImmediate(_root);

            if (_eventObject != null)
                Object.DestroyImmediate(_eventObject);

            _root = null;
            _eventObject = null;
        }

        [Test]
        public void ListenerReceivesHotkeySubmitWithoutReplacingTarget()
        {
            _eventObject = new GameObject("hotkey-event-system", typeof(EventSystem));
            _root = new GameObject("hotkey-listener-target");
            var button = _root.AddComponent<Button>();
            CountingSubmitListener listener = _root.AddComponent<CountingSubmitListener>();
            var component = _root.AddComponent<HotkeyComponent>();
            SetSubmitFields(component, button, listener);

            component.TriggerHotkey();

            Assert.That(listener.Count, Is.EqualTo(1));
        }

        [Test]
        public void ListenerImplementingBothInterfacesReceivesOneNotification()
        {
            _eventObject = new GameObject("hotkey-event-system", typeof(EventSystem));
            _root = new GameObject("hotkey-dual-listener-target");
            CountingDualListener listener = _root.AddComponent<CountingDualListener>();
            var component = _root.AddComponent<HotkeyComponent>();
            SetSubmitFields(component, listener, listener);

            component.TriggerHotkey();

            Assert.That(listener.SubmitCount, Is.EqualTo(1));
            Assert.That(listener.ListenerCount, Is.Zero, "the fallback submit target must not be notified twice");
        }

        [Test]
        public void RuntimeDoesNotScanSubmitTargets()
        {
            _eventObject = new GameObject("hotkey-event-system", typeof(EventSystem));
            _root = new GameObject("hotkey-runtime-target");
            _root.AddComponent<Button>();
            CountingSubmitListener listener = _root.AddComponent<CountingSubmitListener>();
            var component = _root.AddComponent<HotkeyComponent>();

            // AddComponent 在 Editor 下可能触发 Reset；显式清空后 Trigger 不得再扫描。
            SetSubmitFields(component, null, new Component[0]);
            component.TriggerHotkey();

            Assert.That(listener.Count, Is.Zero,
                "submit targets are baked by the editor, not discovered at runtime");
        }

        [Test]
        public void DestroyedSubmitTargetDoesNotThrow()
        {
            _eventObject = new GameObject("hotkey-event-system", typeof(EventSystem));
            _root = new GameObject("hotkey-destroyed-target");
            var button = _root.AddComponent<Button>();
            var component = _root.AddComponent<HotkeyComponent>();
            SetSubmitFields(component, button);
            Object.DestroyImmediate(button);

            Assert.DoesNotThrow(() => component.TriggerHotkey());
        }

        private static void SetSubmitFields(
            HotkeyComponent component,
            Component target,
            params Component[] listeners)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(HotkeyComponent).GetField("_submitTarget", flags).SetValue(component, target);
            typeof(HotkeyComponent).GetField("_listenerComponents", flags).SetValue(component, listeners);
        }
    }
}
#endif
