using System;
using System.Collections.Generic;
using System.Reflection;
using AlicizaX.UI.Runtime;
using AlicizaX.UI.UXNavigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    public abstract class NavigationScopeCases
    {
        private sealed class TestHolder : UIHolderObjectBase { }
        private readonly List<GameObject> objects = new();
        private EventSystem previousEventSystem;
        protected EventSystem eventSystem;

        [SetUp]
        public void SetUp()
        {
            Assert.That(UXInput.Navigation, Is.Null);
            previousEventSystem = EventSystem.current;
            var root = Keep(new GameObject("navigation-test-event-system"));
            eventSystem = root.AddComponent<EventSystem>();
            if (!Application.isPlaying)
                typeof(EventSystem).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(eventSystem, null);
            EventSystem.current = eventSystem;
            UXInput.Initialize(null, null);
            UXInput.Navigation.SetRequireSelection(true, true);
        }

        [TearDown]
        public void TearDown()
        {
            UXInput.Shutdown();
            InputTestViews.UnbindAll();
            if (!Application.isPlaying && eventSystem != null)
                typeof(EventSystem).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(eventSystem, null);
            foreach (GameObject obj in objects)
            {
                if (obj == null) continue;
                var scope = obj.GetComponent<NavigationScope>();
                if (scope != null && !Application.isPlaying) InvokeScope(scope, "OnDisable");
                Object.DestroyImmediate(obj);
            }
            objects.Clear();
            if (previousEventSystem != null) EventSystem.current = previousEventSystem;
            Assert.That(UXInput.Navigation, Is.Null);
        }

        protected GameObject Keep(GameObject obj) { objects.Add(obj); return obj; }

        protected static int ScopeCount => UXInput.Navigation.ScopeCount;
        private static T ReadInstance<T>(object target, string field) =>
            (T)target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(target);
        protected static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
        protected static void InvokeScope(NavigationScope scope, string name) =>
            typeof(NavigationScope).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(scope, null);
        private static void Refresh() => UXInput.Navigation.Refresh();

        protected NavigationScope Scope(int order, bool block = true, bool navigable = true)
        {
            var root = Keep(new GameObject("navigation-scope", typeof(RectTransform), typeof(Canvas)));
            root.SetActive(false);
            root.layer = UIComponent.UIShowLayer;
            root.GetComponent<Canvas>().sortingOrder = order;
            var scope = root.AddComponent<NavigationScope>();
            Set(scope, "_holder", InputTestViews.Bind(root).GetHolder());
            Set(scope, "_blockLowerScopes", block);
            scope.Navigable = navigable;
            root.SetActive(true);
            if (!Application.isPlaying)
            {
                InvokeScope(scope, "OnEnable");
            }
            return scope;
        }

        protected static Button AddButton(NavigationScope scope, bool rememberable = true)
        {
            var obj = new GameObject("navigation-button", typeof(RectTransform), typeof(Button));
            obj.transform.SetParent(scope.transform, false);
            var button = obj.GetComponent<Button>();
            Assert.That(scope.RegisterSelectable(button, rememberable), Is.True);
            scope.DefaultSelectable = button;
            return button;
        }

        private UIBase View(UIBase parent = null, UIState state = UIState.Opened, int order = 0)
        {
            var root = Keep(new GameObject("navigation-view", typeof(RectTransform), typeof(Canvas), typeof(TestHolder)));
            if (parent != null) root.transform.SetParent(parent.GetHolder().transform, false);
            root.GetComponent<Canvas>().sortingOrder = order;
            return InputTestViews.Bind(root.GetComponent<TestHolder>(), parent, state);
        }

        private static NavigationScope BoundScope(UIHolderObjectBase holder, GameObject root = null)
        {
            if (root == null) root = holder.gameObject;
            root.SetActive(false);
            var scope = root.AddComponent<NavigationScope>();
            Set(scope, "_holder", holder);
            root.SetActive(true);
            if (!Application.isPlaying) InvokeScope(scope, "OnEnable");
            return scope;
        }

        private GameObject Panel(UIBase window)
        {
            var root = Keep(new GameObject("navigation-panel", typeof(RectTransform)));
            root.transform.SetParent(window.GetHolder().transform, false);
            return root;
        }

        [Test]
        public void BoundHolderUsesLogicalStateWithoutLayerOrCanvasInference()
        {
            var view = View(state: UIState.Closed);
            var scope = BoundScope(view.GetHolder());
            var button = AddButton(scope);
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);

            InputTestViews.SetState(view, UIState.Opening);
            InputTestViews.Raise(view.GetHolder(), nameof(UIHolderObjectBase.OnShowEvent));
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject));
            view.GetHolder().GetComponent<Canvas>().enabled = false;
            Refresh();
            Assert.That(scope.NavigationSuppressed, Is.False, "the bound view owns availability");

            InputTestViews.SetState(view, UIState.Closing);
            InputTestViews.Raise(view.GetHolder(), nameof(UIHolderObjectBase.OnCloseEvent));
            Assert.That(view.Visible, Is.True);
            Assert.That(view.GetHolder().gameObject.activeInHierarchy, Is.True);
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
            Assert.That(scope.NavigationSuppressed, Is.True);
        }

        [Test]
        public void DestroyedHolderDisablesScope()
        {
            var view = View();
            var holder = view.GetHolder();
            holder.gameObject.layer = UIComponent.UIShowLayer;
            var scope = BoundScope(holder, Panel(view));
            var button = AddButton(scope);
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject));
            InputTestViews.UnbindAll();
            Object.DestroyImmediate(holder);
            Refresh();
            Assert.That(scope.NavigationSuppressed, Is.True);
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void UnboundScopeCannotBlockOrTakeFocus(bool hasHolder)
        {
            var lower = Scope(0);
            var selected = AddButton(lower);
            var root = Keep(new GameObject("unbound-scope", typeof(RectTransform), typeof(Canvas)));
            root.SetActive(false);
            root.layer = UIComponent.UIShowLayer;
            root.GetComponent<Canvas>().sortingOrder = 10;
            var unbound = root.AddComponent<NavigationScope>();
            if (hasHolder) Set(unbound, "_holder", root.AddComponent<TestHolder>());
            root.SetActive(true);
            if (!Application.isPlaying) InvokeScope(unbound, "OnEnable");
            AddButton(unbound);

            Assert.That(unbound.NavigationSuppressed, Is.True);
            Assert.That(lower.NavigationSuppressed, Is.False);
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(selected.gameObject));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void OrdinaryScopesUseWidgetHolderWithoutSharingSelection(bool remember)
        {
            var window = View();
            var widget = View(window);
            var a = BoundScope(widget.GetHolder(), Panel(widget));
            var b = BoundScope(widget.GetHolder(), Panel(widget));
            Set(a, "_rememberLastSelection", remember);
            var aFirst = AddButton(a);
            var aSecond = AddButton(a);
            var bFirst = AddButton(b);
            var bSecond = AddButton(b);
            a.DefaultSelectable = aFirst;
            b.DefaultSelectable = bFirst;

            b.gameObject.SetActive(false);
            if (!Application.isPlaying) InvokeScope(b, "OnDisable");
            eventSystem.SetSelectedGameObject(aSecond.gameObject);
            a.gameObject.SetActive(false);
            if (!Application.isPlaying) InvokeScope(a, "OnDisable");
            b.gameObject.SetActive(true);
            // PlayMode 的 OnEnable 在激活途中，子控件尚未就绪；[Test] 不跨帧，这里补一次结算。
            if (!Application.isPlaying) InvokeScope(b, "OnEnable");
            else Refresh();
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(bFirst.gameObject));
            eventSystem.SetSelectedGameObject(bSecond.gameObject);
            b.gameObject.SetActive(false);
            if (!Application.isPlaying) InvokeScope(b, "OnDisable");
            a.gameObject.SetActive(true);
            if (!Application.isPlaying) InvokeScope(a, "OnEnable");
            else Refresh();
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs((remember ? aSecond : aFirst).gameObject));

            InputTestViews.SetState(widget, UIState.Closing);
            InputTestViews.Raise(widget.GetHolder(), nameof(UIHolderObjectBase.OnCloseEvent));
            Assert.That(window.GetHolder().IsEnabled, Is.True);
            Assert.That(a.gameObject.activeInHierarchy, Is.True);
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
            Assert.That(a.NavigationSuppressed, Is.True);
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void ChildPanelsSwitchWithOrWithoutWindowScope(bool widgets, bool windowScope)
        {
            var window = View();
            if (windowScope) BoundScope(window.GetHolder());
            UIBase a = widgets ? View(window) : window;
            UIBase b = widgets ? View(window, UIState.Closed) : window;
            var first = BoundScope(a.GetHolder(), widgets ? a.GetHolder().gameObject : Panel(window));
            var second = BoundScope(b.GetHolder(), widgets ? b.GetHolder().gameObject : Panel(window));
            if (!widgets) second.gameObject.SetActive(false);
            var buttonA = AddButton(first);
            var buttonB = AddButton(second);
            Refresh();
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(buttonA.gameObject));

            if (widgets)
            {
                InputTestViews.SetState(a, UIState.Closed);
                InputTestViews.Raise(a.GetHolder(), nameof(UIHolderObjectBase.OnCloseEvent));
                InputTestViews.SetState(b, UIState.Opening);
                InputTestViews.Raise(b.GetHolder(), nameof(UIHolderObjectBase.OnShowEvent));
            }
            else
            {
                first.gameObject.SetActive(false);
                second.gameObject.SetActive(true);
            }
            Refresh();
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(buttonB.gameObject));

            InputTestViews.SetState(window, UIState.Closing);
            InputTestViews.Raise(window.GetHolder(), nameof(UIHolderObjectBase.OnCloseEvent));
            Refresh();
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
            Assert.That(second.NavigationSuppressed, Is.True);
        }

        [Test]
        public void EmptyOpeningBlockerStopsLowerScopeAndClosingReleasesIt()
        {
            var lower = Scope(0);
            var button = AddButton(lower);
            var view = View(state: UIState.Closed, order: 10);
            BoundScope(view.GetHolder());
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject));
            InputTestViews.SetState(view, UIState.Opening);
            InputTestViews.Raise(view.GetHolder(), nameof(UIHolderObjectBase.OnShowEvent));
            Assert.That(lower.NavigationSuppressed, Is.True);
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
            InputTestViews.SetState(view, UIState.Closing);
            InputTestViews.Raise(view.GetHolder(), nameof(UIHolderObjectBase.OnCloseEvent));
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject));
        }

        [Test]
        public void TemporarilyUnavailableControlsKeepRecentSelectionWithoutRemember()
        {
            var scope = Scope(0);
            var first = AddButton(scope);
            var second = AddButton(scope);
            scope.DefaultSelectable = first;
            eventSystem.SetSelectedGameObject(second.gameObject);
            Refresh();
            first.interactable = false;
            second.interactable = false;
            Refresh();
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
            first.interactable = true;
            second.interactable = true;
            Refresh();
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(second.gameObject));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void WidgetCanvasUsesItsActualSortingCanvas(bool overrideSorting)
        {
            var window = View(order: 20);
            var widget = View(window);
            widget.GetHolder().GetComponent<Canvas>().overrideSorting = overrideSorting;
            var child = BoundScope(widget.GetHolder());
            var childButton = AddButton(child);
            var other = Scope(10);
            var otherButton = AddButton(other);
            Assert.That(eventSystem.currentSelectedGameObject,
                Is.SameAs((overrideSorting ? otherButton : childButton).gameObject));
        }

        [Test]
        public void SelectionContextIsLimitedToSynchronousNavigationCallbacks()
        {
            var scope = Scope(0);
            var button = AddButton(scope);
            eventSystem.SetSelectedGameObject(null);
            bool? applying = null;
            var trigger = button.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Select };
            entry.callback.AddListener(_ => applying = UXInput.Navigation.IsApplyingSelection);
            trigger.triggers.Add(entry);
            Refresh();
            Assert.That(applying, Is.True);
            Assert.That(UXInput.Navigation.IsApplyingSelection, Is.False);
            eventSystem.SetSelectedGameObject(null);
            eventSystem.SetSelectedGameObject(button.gameObject);
            Assert.That(applying, Is.False);
        }

        [Test]
        public void DeselectCallbackInvalidatesPreferredControlBeforeSelection()
        {
            var lower = Scope(0);
            var selected = AddButton(lower);
            var upperView = View(state: UIState.Closed, order: 10);
            var upper = BoundScope(upperView.GetHolder());
            var preferred = AddButton(upper);
            var fallback = AddButton(upper);
            upper.DefaultSelectable = preferred;
            var trigger = selected.gameObject.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry { eventID = EventTriggerType.Deselect };
            entry.callback.AddListener(_ => preferred.interactable = false);
            trigger.triggers.Add(entry);
            InputTestViews.SetState(upperView, UIState.Opening);
            InputTestViews.Raise(upperView.GetHolder(), nameof(UIHolderObjectBase.OnShowEvent));
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(fallback.gameObject));
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
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
            first.interactable = true;
            scope.NotifySelectableStateChanged();
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
        public void ShutdownReleasesScopesAndRestoresNavigation()
        {
            var scope = Scope(0);
            var button = AddButton(scope);
            var higher = Scope(10);
            AddButton(higher);
            var navigation = UXInput.Navigation;
            Assert.That(scope.NavigationSuppressed, Is.True);
            UXInput.Shutdown();
            Assert.That(button.navigation.mode, Is.EqualTo(Navigation.Mode.Automatic));
            Assert.That(scope.NavigationSuppressed, Is.False);
            Assert.That(UXInput.Navigation, Is.Null);
            Assert.That(navigation.ScopeCount, Is.Zero);
            Assert.That(scope.RuntimeIndex, Is.EqualTo(-1));
            Assert.That(higher.RuntimeIndex, Is.EqualTo(-1));

            eventSystem.SetSelectedGameObject(button.gameObject);
            // PlayMode 且编辑器失焦时，InputSystem.Update() 按 InputUpdateType.Editor 更新，不派发 onAfterUpdate。
            int afterUpdateCount = 0;
            Action countAfterUpdate = () => afterUpdateCount++;
            InputSystem.onAfterUpdate += countAfterUpdate;
            try
            {
                InputSystem.Update();
            }
            finally
            {
                InputSystem.onAfterUpdate -= countAfterUpdate;
            }

            Assume.That(afterUpdateCount, Is.GreaterThan(0), "the environment must dispatch onAfterUpdate");
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(button.gameObject),
                "shutdown must detach navigation from input updates");
        }

        [Test]
        public void BakedSelectablesJoinTheRuntimeOwnershipSet()
        {
            var root = Keep(new GameObject("navigation-scope-baked", typeof(RectTransform), typeof(Canvas)));
            root.layer = UIComponent.UIShowLayer;
            var scope = root.AddComponent<NavigationScope>();
            Set(scope, "_holder", InputTestViews.Bind(root).GetHolder());

            var bakedObject = Keep(new GameObject("navigation-baked-button", typeof(RectTransform), typeof(Button)));
            bakedObject.transform.SetParent(root.transform, false);
            var baked = bakedObject.GetComponent<Button>();

            Set(scope, "_bakedSelectables", new Selectable[] { baked });
            Set(scope, "_listsBuilt", false);
            InvokeScope(scope, "InitializeLists");
            UXInput.Navigation.RegisterScope(scope);

            Assert.That(scope.BakedSelectableCount, Is.EqualTo(1));
            Assert.That(scope.RuntimeSelectableCount, Is.Zero);
            Assert.That(scope.RegisterSelectable(baked), Is.False, "baked selectables are already owned");
            Assert.That(scope.UnregisterSelectable(baked), Is.False, "baked selectables cannot be unregistered");

            var runtime = AddButton(scope);
            Assert.That(scope.RuntimeSelectableCount, Is.EqualTo(1));
            Assert.That(scope.UnregisterSelectable(runtime), Is.True);
            Assert.That(scope.RuntimeSelectableCount, Is.Zero);
        }

        [Test]
        public void ScopeRegistryCompactsAfterUnregister()
        {
            var first = Scope(0);
            var second = Scope(1);
            var third = Scope(2);

            UXInput.Navigation.UnregisterScope(second);

            Assert.That(ScopeCount, Is.EqualTo(2));
            Assert.That(first.RuntimeIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(third.RuntimeIndex, Is.GreaterThanOrEqualTo(0));
            Assert.That(second.RuntimeIndex, Is.EqualTo(-1));
        }

        [Test]
        public void ScopeRegistryGrowsBeyondInitialCapacity()
        {
            const int scopeCount = 132;
            for (int i = 0; i < scopeCount; i++)
            {
                Scope(i);
            }

            Assert.That(ScopeCount, Is.EqualTo(scopeCount));
        }

        [Test]
        public void RememberLastSelectionRestoresPreviouslyFocusedControl()
        {
            var scope = Scope(0);
            var first = AddButton(scope);
            var second = AddButton(scope);
            Set(scope, "_rememberLastSelection", true);
            scope.DefaultSelectable = first;
            Refresh();

            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(first.gameObject));

            eventSystem.SetSelectedGameObject(second.gameObject);
            Refresh();

            eventSystem.SetSelectedGameObject(null);
            Refresh();

            Assert.That(
                eventSystem.currentSelectedGameObject,
                Is.SameAs(second.gameObject),
                "remember-last-selection must survive losing the current selection");
        }

        [Test]
        public void SuppressionRestoresEachSelectableOriginalNavigation()
        {
            var lower = Scope(0);
            var first = AddButton(lower);
            var second = AddButton(lower);

            Navigation firstNavigation = first.navigation;
            firstNavigation.mode = Navigation.Mode.Horizontal;
            first.navigation = firstNavigation;

            Navigation secondNavigation = second.navigation;
            secondNavigation.mode = Navigation.Mode.Vertical;
            second.navigation = secondNavigation;

            var higher = Scope(10);
            AddButton(higher);

            Assert.That(first.navigation.mode, Is.EqualTo(Navigation.Mode.None));
            Assert.That(second.navigation.mode, Is.EqualTo(Navigation.Mode.None));

            higher.gameObject.SetActive(false);
            Refresh();

            Assert.That(first.navigation.mode, Is.EqualTo(Navigation.Mode.Horizontal));
            Assert.That(second.navigation.mode, Is.EqualTo(Navigation.Mode.Vertical));
        }

        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void CoveredScopeRestoresAccordingToRemember(bool remember, bool block)
        {
            var lower = Scope(0);
            var first = AddButton(lower);
            var second = AddButton(lower);
            lower.DefaultSelectable = first;
            Set(lower, "_rememberLastSelection", remember);
            eventSystem.SetSelectedGameObject(second.gameObject);

            var higher = Scope(10, block);
            AddButton(higher);
            higher.gameObject.SetActive(false);
            if (!Application.isPlaying) InvokeScope(higher, "OnDisable");

            Assert.That(eventSystem.currentSelectedGameObject,
                Is.SameAs((remember ? second : first).gameObject));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void ReopenedScopeRestoresAccordingToRemember(bool remember)
        {
            var scope = Scope(0);
            var first = AddButton(scope);
            var second = AddButton(scope);
            scope.DefaultSelectable = first;
            Set(scope, "_rememberLastSelection", remember);
            eventSystem.SetSelectedGameObject(second.gameObject);

            scope.gameObject.SetActive(false);
            if (!Application.isPlaying) InvokeScope(scope, "OnDisable");
            Assert.That(eventSystem.currentSelectedGameObject, Is.Null);
            scope.gameObject.SetActive(true);
            // PlayMode 的 OnEnable 在激活途中，子控件尚未就绪；[Test] 不跨帧，这里补一次结算。
            if (!Application.isPlaying) InvokeScope(scope, "OnEnable");
            else Refresh();

            Assert.That(eventSystem.currentSelectedGameObject,
                Is.SameAs((remember ? second : first).gameObject));
        }

        [Test]
        public void UnrememberableControlRestoresWithinScopeButNotAfterCoverage()
        {
            var lower = Scope(0);
            var first = AddButton(lower);
            var second = AddButton(lower, false);
            lower.DefaultSelectable = first;
            Set(lower, "_rememberLastSelection", true);
            eventSystem.SetSelectedGameObject(second.gameObject);
            Refresh();
            eventSystem.SetSelectedGameObject(null);
            Refresh();
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(second.gameObject));

            var higher = Scope(10);
            AddButton(higher);
            higher.gameObject.SetActive(false);
            if (!Application.isPlaying) InvokeScope(higher, "OnDisable");
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(first.gameObject));
        }

        [Test]
        public void UnregisteringCurrentControlImmediatelySelectsFallback()
        {
            var scope = Scope(0);
            var first = AddButton(scope);
            var second = AddButton(scope);
            scope.DefaultSelectable = first;
            eventSystem.SetSelectedGameObject(second.gameObject);

            Assert.That(scope.UnregisterSelectable(second), Is.True);

            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(first.gameObject));
        }

        [Test]
        public void NonBlockingScopeDoesNotStealValidFocusOnEveryRefresh()
        {
            var lower = Scope(0, false);
            var first = AddButton(lower);
            var higher = Scope(10, false);
            AddButton(higher);
            eventSystem.SetSelectedGameObject(first.gameObject);

            Refresh();

            Assert.That(lower.NavigationSuppressed, Is.False);
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(first.gameObject));
        }

        [Test]
        public void MissingNavigableFieldMigratesToNavigable()
        {
            var root = Keep(new GameObject("navigation-migration", typeof(RectTransform), typeof(Canvas)));
            var scope = root.AddComponent<NavigationScope>();
            Set(scope, "_hasNavigableField", false);
            Set(scope, "_navigable", false);

            scope.OnAfterDeserialize();

            Assert.That(ReadInstance<bool>(scope, "_navigable"), Is.True);
            Assert.That(ReadInstance<bool>(scope, "_hasNavigableField"), Is.True);
        }
    }
}
