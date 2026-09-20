using System;
using System.Collections.Generic;
using System.Reflection;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.UI.UXNavigation.Tests
{
    public abstract class NavigationScopeCases
    {
        private readonly List<GameObject> objects = new();
        private EventSystem previousEventSystem;
        protected EventSystem eventSystem;
        private bool gamepadSelection;
        private bool keyboardSelection;

        [SetUp]
        public void SetUp()
        {
            Assert.That(Read<int>(typeof(UXNavigationSystem), null, "_scopeCount"), Is.Zero);
            Assert.That(Read<bool>(typeof(UXNavigationSystem), null, "_initialized"), Is.False);
            previousEventSystem = EventSystem.current;
            gamepadSelection = UXNavigationSystem.GamepadRequireSelection;
            keyboardSelection = UXNavigationSystem.KeyboardRequireSelection;
            var root = Keep(new GameObject("navigation-test-event-system"));
            eventSystem = root.AddComponent<EventSystem>();
            if (!Application.isPlaying)
                typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(eventSystem, null);
            EventSystem.current = eventSystem;
            UXNavigationSystem.SetRequireSelection(true, true);
            Invoke("Initialize");
        }

        [TearDown]
        public void TearDown()
        {
            Invoke("Shutdown");
            if (!Application.isPlaying && eventSystem != null)
                typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(eventSystem, null);
            foreach (GameObject obj in objects)
            {
                if (obj == null) continue;
                var scope = obj.GetComponent<UXNavigationScope>();
                if (scope != null) Invoke("UnregisterScope", scope);
                Object.DestroyImmediate(obj);
            }
            objects.Clear();
            UXNavigationSystem.SetRequireSelection(gamepadSelection, keyboardSelection);
            if (previousEventSystem != null) EventSystem.current = previousEventSystem;
            Assert.That(Read<int>(typeof(UXNavigationSystem), null, "_scopeCount"), Is.Zero);
            Assert.That(Read<bool>(typeof(UXNavigationSystem), null, "_isFlushingState"), Is.False);
        }

        protected GameObject Keep(GameObject obj) { objects.Add(obj); return obj; }
        private static T Read<T>(Type type, object target, string field) =>
            (T)type.GetField(field, BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        private static void Invoke(string name, params object[] args) =>
            typeof(UXNavigationSystem).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
        private static void Refresh() => Invoke("RequestRefresh", true);

        protected UXNavigationScope Scope(int order, bool block = true, bool navigable = true)
        {
            var root = Keep(new GameObject("navigation-scope", typeof(RectTransform), typeof(Canvas)));
            root.SetActive(false);
            root.layer = UIComponent.UIShowLayer;
            root.GetComponent<Canvas>().sortingOrder = order;
            var scope = root.AddComponent<UXNavigationScope>();
            Set(scope, "_blockLowerScopes", block);
            scope.Navigable = navigable;
            root.SetActive(true);
            Invoke("RegisterScope", scope);
            return scope;
        }

        protected static Button AddButton(UXNavigationScope scope)
        {
            var obj = new GameObject("navigation-button", typeof(RectTransform), typeof(Button));
            obj.transform.SetParent(scope.transform, false);
            var button = obj.GetComponent<Button>();
            Assert.That(scope.RegisterSelectable(button, true), Is.True);
            scope.DefaultSelectable = button;
            Refresh();
            return button;
        }

        [Test]
        public void RegisterRejectsDuplicatesAndForeignScopeWithoutLosingExistingSelection()
        {
            var scope = Scope(10);
            var selected = AddButton(scope);
            var other = Scope(0, false);
            Assert.That(scope.RegisterSelectable(selected), Is.False);
            Assert.That(other.RegisterSelectable(selected), Is.False);
            Assert.That(scope.RuntimeSelectableCount, Is.EqualTo(1));
            Assert.That(other.RuntimeSelectableCount, Is.Zero);
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(selected.gameObject));
        }

        [Test]
        public void HigherBlockingScopeSuppressesLowerAndRestoreKeepsOriginalNavigation()
        {
            var lower = Scope(0);
            var first = AddButton(lower);
            var navigation = first.navigation;
            navigation.mode = Navigation.Mode.Horizontal;
            first.navigation = navigation;
            var higher = Scope(10);
            var second = AddButton(higher);
            Assert.That(lower.NavigationSuppressed, Is.True);
            Assert.That(first.navigation.mode, Is.EqualTo(Navigation.Mode.None));
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(second.gameObject));
            higher.gameObject.SetActive(false);
            Refresh();
            Assert.That(lower.NavigationSuppressed, Is.False);
            Assert.That(first.navigation.mode, Is.EqualTo(Navigation.Mode.Horizontal));
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(first.gameObject));
        }

        [Test]
        public void NonNavigableBlockerDoesNotHideFocusableScopeAboveIt()
        {
            var lowerBlocker = Scope(0, navigable: false);
            var higher = Scope(10, block: false);
            var button = AddButton(higher);
            Assert.That(lowerBlocker.NavigationSuppressed, Is.True);
            Assert.That(higher.NavigationSuppressed, Is.False);
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject));
        }

        [Test]
        public void NonNavigableBlockerAboveCurrentScopeClearsSelection()
        {
            var lower = Scope(0);
            AddButton(lower);
            Scope(10, navigable: false);
            Refresh();
            Assert.That(lower.NavigationSuppressed, Is.True);
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
        }

        [Test]
        public void UnregisterWhileSuppressedRestoresNavigationAndAllowsIndependentReuse()
        {
            var lower = Scope(0);
            var first = AddButton(lower);
            var higher = Scope(10);
            AddButton(higher);
            Assert.That(lower.UnregisterSelectable(first), Is.True);
            Assert.That(lower.UnregisterSelectable(first), Is.False);
            Assert.That(first.navigation.mode, Is.EqualTo(Navigation.Mode.Automatic));
            Assert.That(lower.RuntimeSelectableCount, Is.Zero);
            Assert.That(lower.RegisterSelectable(first), Is.True);
            Assert.That(first.navigation.mode, Is.EqualTo(Navigation.Mode.None));
        }

        [Test]
        public void AvailabilityNotificationChoosesAnInteractableFallback()
        {
            var scope = Scope(0);
            var first = AddButton(scope);
            var second = AddButton(scope);
            first.interactable = false;
            second.interactable = false;
            scope.NotifySelectableStateChanged();
            Refresh();
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
            first.interactable = true;
            scope.NotifySelectableStateChanged();
            Refresh();
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(first.gameObject));
        }

        [Test]
        public void SelectionCallbackRefreshIsNotLostWhileResolvingScopes()
        {
            var lower = Scope(0);
            var fallback = AddButton(lower);
            var higher = Scope(10);
            var button = AddButton(higher);
            eventSystem.SetSelectedGameObject(null);
            var trigger = button.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            entry.callback.AddListener(_ => { higher.gameObject.SetActive(false); Refresh(); });
            trigger.triggers.Add(entry);
            Refresh();
            Assert.That(higher.gameObject.activeSelf, Is.False);
            Assert.That(lower.NavigationSuppressed, Is.False);
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(fallback.gameObject));
        }

        [Test]
        public void ShutdownAndReinitializeRestoreRegisteredScopes()
        {
            var scope = Scope(0);
            var button = AddButton(scope);
            Invoke("Shutdown");
            Assert.That(button.navigation.mode, Is.EqualTo(Navigation.Mode.Automatic));
            Invoke("Initialize");
            Assert.That(scope.NavigationSuppressed, Is.False);
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject));
        }
    }
}
