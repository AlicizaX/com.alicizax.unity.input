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
            _root.AddComponent<Button>();
            CountingSubmitListener listener = _root.AddComponent<CountingSubmitListener>();
            var component = _root.AddComponent<HotkeyComponent>();

            typeof(HotkeyComponent)
                .GetMethod("ResolveSubmitTarget", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(component, null);

            component.TriggerHotkey();

            Assert.That(listener.Count, Is.EqualTo(1));
        }
    }
}
#endif
