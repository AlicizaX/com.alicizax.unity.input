#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class HotkeyTests
    {
        private sealed class TestHolder : UIHolderObjectBase
        {
        }

        private sealed class CountingTrigger : HotkeyComponentBase
        {
            public int TriggerCount;

            public override void TriggerHotkey()
            {
                TriggerCount++;
            }
        }

        private readonly List<GameObject> _objects = new List<GameObject>();
        private InputActionAsset _asset;
        private InputActionReference _reference;
        private InputHotkey _hotkey;
        private TestHolder _holder;
        private HotkeyComponent _trigger;

        [SetUp]
        public void SetUp()
        {
            _asset = ScriptableObject.CreateInstance<InputActionAsset>();
            _asset.name = "HotkeyTests";
            InputActionMap map = _asset.AddActionMap("UI");
            InputAction back = map.AddAction("Back", InputActionType.Button, "<Keyboard>/escape");
            _asset.Enable();
            _reference = InputActionReference.Create(back);
            _hotkey = new InputHotkey(_asset);

            var holderObject = Track(new GameObject("hotkey-holder"));
            holderObject.layer = UIComponent.UIShowLayer;
            _holder = holderObject.AddComponent<TestHolder>();
            InputTestViews.Bind(_holder);

            var triggerObject = Track(new GameObject("hotkey-trigger"));
            _trigger = triggerObject.AddComponent<HotkeyComponent>();
        }

        [TearDown]
        public void TearDown()
        {
            _hotkey.Shutdown();
            _hotkey = null;
            InputTestViews.UnbindAll();

            for (int i = 0; i < _objects.Count; i++)
            {
                if (_objects[i] != null)
                {
                    Object.DestroyImmediate(_objects[i]);
                }
            }

            _objects.Clear();

            if (_reference != null)
            {
                Object.DestroyImmediate(_reference);
                _reference = null;
            }

            if (_asset != null)
            {
                Object.DestroyImmediate(_asset);
                _asset = null;
            }
        }

        [Test]
        public void RegistrationCreatesScopeAndSubscription()
        {
            _hotkey.RegisterHotkey(_trigger, _holder, _reference, EHotkeyPressType.Performed);

            Assert.That(_trigger.IsRegistered, Is.True);
            Assert.That(_trigger.RegisteredAction, Is.SameAs(_reference.action));
            Assert.That(RegistryCount("_scopesByHolder"), Is.EqualTo(1));
            Assert.That(RegistryCount("_actionStates"), Is.EqualTo(1));
            Assert.That(RegistryCount("_scopeList"), Is.EqualTo(1));
        }

        [Test]
        public void DuplicateRegistrationOnSameHolderIsRejected()
        {
            var secondObject = Track(new GameObject("hotkey-trigger-2"));
            var secondTrigger = secondObject.AddComponent<HotkeyComponent>();

            _hotkey.RegisterHotkey(_trigger, _holder, _reference, EHotkeyPressType.Performed);
            _hotkey.RegisterHotkey(secondTrigger, _holder, _reference, EHotkeyPressType.Performed);

            Assert.That(_trigger.IsRegistered, Is.True);
            Assert.That(secondTrigger.IsRegistered, Is.False);
            Assert.That(RegistryCount("_scopesByHolder"), Is.EqualTo(1));
            Assert.That(RegistryCount("_actionStates"), Is.EqualTo(1));
        }

        [Test]
        public void UnregisterRemovesEmptyScopeAndSubscription()
        {
            _hotkey.RegisterHotkey(_trigger, _holder, _reference, EHotkeyPressType.Performed);

            _hotkey.UnregisterHotkey(_trigger);

            Assert.That(_trigger.IsRegistered, Is.False);
            Assert.That(RegistryCount("_scopesByHolder"), Is.Zero);
            Assert.That(RegistryCount("_actionStates"), Is.Zero);
            Assert.That(RegistryCount("_scopeList"), Is.Zero);
        }

        [Test]
        public void DestroyScopeUnregistersAllTriggers()
        {
            _hotkey.RegisterHotkey(_trigger, _holder, _reference, EHotkeyPressType.Performed);

            _hotkey.DestroyScope(_holder);

            Assert.That(_trigger.IsRegistered, Is.False);
            Assert.That(RegistryCount("_scopesByHolder"), Is.Zero);
            Assert.That(RegistryCount("_scopeList"), Is.Zero);
        }

        [Test]
        public void ShutdownClearsRegistries()
        {
            _hotkey.RegisterHotkey(_trigger, _holder, _reference, EHotkeyPressType.Performed);

            _hotkey.Shutdown();

            Assert.That(_trigger.IsRegistered, Is.False);
            Assert.That(RegistryCount("_scopesByHolder"), Is.Zero);
            Assert.That(RegistryCount("_actionStates"), Is.Zero);
            Assert.That(RegistryCount("_scopeList"), Is.Zero);
            Assert.That(_reference.action.enabled, Is.True, "hotkey cleanup does not own action lifetime");
        }

        [Test]
        public void MissingActionDoesNotCreateRegistration()
        {
            var emptyReference = ScriptableObject.CreateInstance<InputActionReference>();
            try
            {
                _hotkey.RegisterHotkey(_trigger, _holder, emptyReference, EHotkeyPressType.Performed);
                Assert.That(_trigger.IsRegistered, Is.False);
                Assert.That(RegistryCount("_actionStates"), Is.Zero);
                Assert.That(RegistryCount("_scopesByHolder"), Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(emptyReference);
            }
        }

        [Test]
        public void MissingHolderDoesNotCreateRegistration()
        {
            _hotkey.RegisterHotkey(_trigger, null, _reference, EHotkeyPressType.Performed);

            Assert.That(_trigger.IsRegistered, Is.False);
            Assert.That(RegistryCount("_scopesByHolder"), Is.Zero);
        }

        [Test]
        public void ShutdownIsIdempotent()
        {
            _hotkey.Shutdown();
            _hotkey.Shutdown();
            _hotkey.Shutdown();

            Assert.That(RegistryCount("_scopesByHolder"), Is.Zero);
        }

        [Test]
        public void RegistrationAfterShutdownIsIgnored()
        {
            _hotkey.Shutdown();

            _hotkey.RegisterHotkey(_trigger, _holder, _reference, EHotkeyPressType.Performed);

            Assert.That(_trigger.IsRegistered, Is.False);
            Assert.That(RegistryCount("_scopesByHolder"), Is.Zero);
        }

        [Test]
        public void SubmitHandlerIsResolvedByComponentScan()
        {
            var targetObject = Track(new GameObject("hotkey-submit-target"));
            targetObject.AddComponent<Button>();
            var component = targetObject.AddComponent<HotkeyComponent>();

            MethodInfo resolve = typeof(HotkeyComponent).GetMethod(
                "ResolveSubmitTarget",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(resolve, Is.Not.Null);
            resolve.Invoke(component, null);

            FieldInfo handlerField = typeof(HotkeyComponent).GetField(
                "_submitHandler",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(handlerField, Is.Not.Null);
            Assert.That(handlerField.GetValue(component), Is.TypeOf<Button>());
        }

        [Test]
        public void ScopeChainCachesParentHolderAndFindsNearestScope()
        {
            var parentObject = Track(new GameObject("hotkey-parent-holder"));
            parentObject.layer = UIComponent.UIShowLayer;
            var parent = parentObject.AddComponent<TestHolder>();
            UIBase parentView = InputTestViews.Bind(parent);

            var childObject = Track(new GameObject("hotkey-child-holder"));
            childObject.layer = UIComponent.UIShowLayer;
            childObject.transform.SetParent(parentObject.transform, false);
            var child = childObject.AddComponent<TestHolder>();
            InputTestViews.Bind(child, parentView);

            var parentTrigger = Track(new GameObject("hotkey-parent-trigger")).AddComponent<HotkeyComponent>();
            var childTrigger = Track(new GameObject("hotkey-child-trigger")).AddComponent<HotkeyComponent>();

            _hotkey.RegisterHotkey(parentTrigger, parent, _reference, EHotkeyPressType.Performed);
            _hotkey.RegisterHotkey(childTrigger, child, _reference, EHotkeyPressType.Performed);

            MethodInfo find = typeof(InputHotkey).GetMethod(
                "FindTopScopeInsideHolder",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(find, Is.Not.Null);
            var leafScope = (HotkeyScope)find.Invoke(_hotkey, new object[] { child });

            Assert.That(leafScope, Is.Not.Null);
            Assert.That(leafScope.Holder, Is.SameAs(child));
            Assert.That(leafScope.ParentHolder, Is.SameAs(parent));
        }

        [Test]
        public void PressTypesShareOneActionSubscriptionAndUnsubscribeIndependently()
        {
            var secondObject = Track(new GameObject("hotkey-trigger-performed"));
            var secondTrigger = secondObject.AddComponent<HotkeyComponent>();

            _hotkey.RegisterHotkey(_trigger, _holder, _reference, EHotkeyPressType.Started);
            _hotkey.RegisterHotkey(secondTrigger, _holder, _reference, EHotkeyPressType.Performed);

            Assert.That(RegistryCount("_actionStates"), Is.EqualTo(1), "one action must have one subscription state");
            object state = GetActionState();
            Assert.That(ReadInt(state, "Started"), Is.EqualTo(1));
            Assert.That(ReadInt(state, "Performed"), Is.EqualTo(1));

            _hotkey.UnregisterHotkey(_trigger);
            state = GetActionState();
            Assert.That(ReadInt(state, "Started"), Is.Zero);
            Assert.That(ReadInt(state, "Performed"), Is.EqualTo(1));
            Assert.That(RegistryCount("_actionStates"), Is.EqualTo(1));

            _hotkey.UnregisterHotkey(secondTrigger);
            Assert.That(RegistryCount("_actionStates"), Is.Zero);
        }

        [Test]
        public void TransientPressTargetCanBeReset()
        {
            _hotkey.RegisterHotkey(_trigger, _holder, _reference, EHotkeyPressType.Performed);

            object state = GetActionState();
            HotkeyScope scope = GetScope(_holder);
            Assert.That(state, Is.Not.Null);
            Assert.That(scope, Is.Not.Null);

            SetField(state, "HasPressTarget", true);
            SetField(state, "FocusHolder", _holder);
            SetField(state, "LeafScope", scope);

            _hotkey.ResetPressTargets();

            Assert.That((bool)GetField(state, "HasPressTarget"), Is.False);
            Assert.That(GetField(state, "FocusHolder"), Is.Null);
            Assert.That(GetField(state, "LeafScope"), Is.Null);
        }

        [Test]
        public void DispatchUsesNearestScopeRegistration()
        {
            var parent = CreateHolder("dispatch-parent");
            var child = CreateHolder("dispatch-child", parent.transform);

            CountingTrigger parentTrigger = CreateCountingTrigger(parent, EHotkeyPressType.Performed);
            CountingTrigger childTrigger = CreateCountingTrigger(child, EHotkeyPressType.Performed);

            Dispatch(GetScope(child), child, EHotkeyPressType.Performed);

            Assert.That(childTrigger.TriggerCount, Is.EqualTo(1));
            Assert.That(parentTrigger.TriggerCount, Is.Zero, "the nearest registration must consume the input");
        }

        private TestHolder CreateHolder(string name, Transform parent = null)
        {
            var gameObject = Track(new GameObject(name));
            gameObject.layer = UIComponent.UIShowLayer;
            if (parent != null)
            {
                gameObject.transform.SetParent(parent, false);
            }

            var holder = gameObject.AddComponent<TestHolder>();
            InputTestViews.Bind(holder);
            return holder;
        }

        private CountingTrigger CreateCountingTrigger(
            UIHolderObjectBase holder,
            EHotkeyPressType pressType)
        {
            var gameObject = Track(new GameObject("counting-trigger-" + pressType));
            gameObject.SetActive(false);
            var trigger = gameObject.AddComponent<CountingTrigger>();
            gameObject.SetActive(true);
            _hotkey.RegisterHotkey(trigger, holder, _reference, pressType);
            return trigger;
        }

        private void Dispatch(HotkeyScope leafScope, UIHolderObjectBase stopHolder, EHotkeyPressType pressType)
        {
            MethodInfo dispatch = typeof(InputHotkey).GetMethod(
                "TryDispatchToScopeChain",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(dispatch, Is.Not.Null);
            dispatch.Invoke(_hotkey, new object[] { leafScope, stopHolder, _reference.action, pressType });
        }

        private GameObject Track(GameObject value)
        {
            _objects.Add(value);
            return value;
        }

        private object GetActionState()
        {
            FieldInfo field = typeof(InputHotkey).GetField("_actionStates", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            if (field.GetValue(_hotkey) is IDictionary dictionary)
            {
                foreach (DictionaryEntry entry in dictionary)
                {
                    return entry.Value;
                }
            }

            return null;
        }

        private HotkeyScope GetScope(TestHolder holder)
        {
            FieldInfo field = typeof(InputHotkey).GetField("_scopesByHolder", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            if (field.GetValue(_hotkey) is IDictionary dictionary && dictionary.Contains(holder))
            {
                return (HotkeyScope)dictionary[holder];
            }

            return null;
        }

        private static int ReadInt(object target, string fieldName)
        {
            return (int)FindField(target, fieldName).GetValue(target);
        }

        private static object GetField(object target, string fieldName)
        {
            return FindField(target, fieldName).GetValue(target);
        }

        private static void SetField(object target, string fieldName, object value)
        {
            FindField(target, fieldName).SetValue(target, value);
        }

        private static FieldInfo FindField(object target, string fieldName)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    return field;
                }
            }

            Assert.Fail("missing state field " + fieldName + " on " + target.GetType().Name);
            return null;
        }

        private int RegistryCount(string fieldName)
        {
            FieldInfo field = typeof(InputHotkey).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "missing registry field " + fieldName);
            return field.GetValue(_hotkey) is ICollection collection ? collection.Count : -1;
        }
    }
}
#endif
