#if INPUTSYSTEM_SUPPORT && UNITY_EDITOR
using System;
using System.Collections;
using System.Reflection;
using AlicizaX.UI.Runtime;
using AlicizaX.UI.UXNavigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class InputAllocationPlayModeTests : InputDeviceTestBase
    {
        private sealed class TestHolder : UIHolderObjectBase { }
        private static object _allocationSink;

        [UnityTest]
        public IEnumerator AllocationCaptureDetectsKnownAllocation()
        {
            _allocationSink = null;
            long bytes = 0;
            int allocations = 0;

            yield return InputAllocationCapture.Measure(
                "self-check",
                () => _allocationSink = new byte[4096],
                (measuredBytes, measuredAllocations) =>
                {
                    bytes = measuredBytes;
                    allocations = measuredAllocations;
                });

            _allocationSink = null;
            Assert.That(bytes, Is.GreaterThanOrEqualTo(4096), "the profiler capture must detect known allocations");
            Assert.That(allocations, Is.GreaterThan(0));
        }

        [UnityTest]
        public IEnumerator ReaderSteadyStateAllocatesZeroBytes()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            InputAction move = Action("Move");
            SetKeys(Key.Space, Key.W);

            var state = new InputReader.ButtonState();
            void Loop()
            {
                for (int i = 0; i < 2000; i++)
                {
                    UXInput.Reader.IsPressed(jump);
                    UXInput.Reader.IsButtonPressed(jump);
                    UXInput.Reader.ReadValue<Vector2>(move);
                    state.Once(jump);
                    state.Toggle(jump);
                    UXInput.Reader.IsCompositePartPressed(move, "Up");
                }
            }

            Loop();

            long bytes = 0;
            int allocations = 0;
            yield return InputAllocationCapture.Measure(
                "reader-steady",
                Loop,
                (measuredBytes, measuredAllocations) =>
                {
                    bytes = measuredBytes;
                    allocations = measuredAllocations;
                });

            Assert.That(bytes, Is.Zero, "Reader steady-state reads must not allocate");
            Assert.That(allocations, Is.Zero);
        }

        [UnityTest]
        public IEnumerator WatchContextResolutionAllocatesZeroBytes()
        {
            Gamepad gamepad = AddGamepad();
            UXInput.Watch.ResolveContext(gamepad);

            long bytes = 0;
            yield return InputAllocationCapture.Measure(
                "watch-context",
                () =>
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        UXInput.Watch.ResolveContext(gamepad);
                        _ = UXInput.Watch.CurrentProfileId;
                    }
                },
                (measuredBytes, _) => bytes = measuredBytes);

            Assert.That(bytes, Is.Zero, "the per-device context cache must be allocation free");
        }

        [UnityTest]
        public IEnumerator WatchAlternatingDevicesAllocateZeroBytes()
        {
            var keyboard = AddKeyboard();
            var mouse = Track(InputSystem.AddDevice<Mouse>());
            var gamepad = AddGamepad();
            void Loop()
            {
                for (int i = 0; i < 2000; i++)
                {
                    UXInput.Watch.ResolveContext(keyboard);
                    UXInput.Watch.ResolveContext(mouse);
                    UXInput.Watch.ResolveContext(gamepad);
                }
            }

            Loop();
            yield return InputAllocationCapture.Measure("watch-alternating", Loop,
                (bytes, _) => Assert.That(bytes, Is.Zero));
        }

        [UnityTest]
        public IEnumerator WatchSameDeviceCallbacksAllocateZeroBytes()
        {
            var gamepad = AddGamepad();
            var pressed = new GamepadState().WithButton(GamepadButton.South);
            var released = new GamepadState();
            int activityCount = 0;
            void OnActivity(InputWatch.InputContext _) => activityCount++;
            void Loop()
            {
                for (int i = 0; i < 2000; i++)
                {
                    InputSystem.QueueStateEvent(gamepad, pressed);
                    InputSystem.Update();
                    InputSystem.QueueStateEvent(gamepad, released);
                    InputSystem.Update();
                }
            }

            UXInput.Watch.OnInputActivity += OnActivity;
            try
            {
                Loop();
                activityCount = 0;
                yield return InputAllocationCapture.Measure("watch-same-device-callbacks", Loop,
                    (bytes, _) => Assert.That(bytes, Is.Zero));
                Assert.That(activityCount, Is.EqualTo(2000));
            }
            finally
            {
                UXInput.Watch.OnInputActivity -= OnActivity;
            }
        }

        [UnityTest]
        public IEnumerator WatchInputCallbacksAllocateZeroBytes()
        {
            var keyboard = AddKeyboard();
            var gamepad = AddGamepad();
            var pressedKey = new KeyboardState(Key.A);
            var releasedKey = new KeyboardState();
            var pressedPad = new GamepadState().WithButton(GamepadButton.South);
            var releasedPad = new GamepadState();
            int activityCount = 0;
            void OnActivity(InputWatch.InputContext _) => activityCount++;
            void Loop()
            {
                for (int i = 0; i < 2000; i++)
                {
                    InputSystem.QueueStateEvent(keyboard, pressedKey);
                    InputSystem.Update();
                    InputSystem.QueueStateEvent(keyboard, releasedKey);
                    InputSystem.Update();
                    InputSystem.QueueStateEvent(gamepad, pressedPad);
                    InputSystem.Update();
                    InputSystem.QueueStateEvent(gamepad, releasedPad);
                    InputSystem.Update();
                }
            }

            UXInput.Watch.OnInputActivity += OnActivity;
            try
            {
                Loop();
                activityCount = 0;
                yield return InputAllocationCapture.Measure("watch-input-callbacks", Loop,
                    (bytes, _) => Assert.That(bytes, Is.Zero));
                Assert.That(activityCount, Is.EqualTo(4000));
            }
            finally
            {
                UXInput.Watch.OnInputActivity -= OnActivity;
            }
        }

        [UnityTest]
        public IEnumerator GlyphQueriesAllocateZeroBytes()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            UXInput.Glyph.GetDisplayName("<Keyboard>/space");
            UXInput.Glyph.GetGlyphKeyFromControlPath("<Keyboard>/space");
            UXInput.Glyph.GetDisplayName(jump);

            long bytes = 0;
            yield return InputAllocationCapture.Measure(
                "glyph-cached-queries",
                () =>
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        _ = UXInput.Glyph.CurrentProfileId;
                        _ = UXInput.Glyph.GetDisplayName("<Keyboard>/space");
                        _ = UXInput.Glyph.GetGlyphKeyFromControlPath("<Keyboard>/space");
                        _ = UXInput.Glyph.GetDisplayName(jump);
                        _ = UXInput.Glyph.GetBindingControlPath(jump);
                    }
                },
                (measuredBytes, _) => bytes = measuredBytes);

            Assert.That(bytes, Is.Zero, "cached glyph queries must not allocate");
        }

        [UnityTest]
        public IEnumerator RebindRequestAndDisplayStringAllocateZeroBytes()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            UXInput.Rebind.GetDisplayString(jump);

            try
            {
                long bytes = 0;
                yield return InputAllocationCapture.Measure(
                    "rebind-cached-queries",
                    () =>
                    {
                        for (int i = 0; i < 2000; i++)
                        {
                            RebindRequest request = RebindRequest.ForTarget(RebindTarget.Gamepad);
                            _ = UXInput.Rebind.GetDisplayString(jump, request);
                            _ = UXInput.Rebind.GetDisplayString(jump);
                        }
                    },
                    (measuredBytes, _) => bytes = measuredBytes);

                Assert.That(bytes, Is.Zero, "request factories and cached display strings must not allocate");
            }
            finally
            {
                UXInput.Shutdown();
            }
        }

        [UnityTest]
        public IEnumerator FirstUseAndSteadyStateAreMeasuredSeparately()
        {
            AddKeyboard();
            InputAction move = Action("Move");

            long firstCompositeBytes = -1;
            yield return InputAllocationCapture.Measure(
                "reader-first-composite",
                () => UXInput.Reader.IsCompositePartPressed(move, "Down"),
                (measuredBytes, _) => firstCompositeBytes = measuredBytes);

            TestContext.WriteLine("FIRST_USE,reader-composite," + firstCompositeBytes); UnityEngine.Debug.Log("FIRST_USE,reader-composite," + firstCompositeBytes);
            Assert.That(firstCompositeBytes, Is.GreaterThanOrEqualTo(0L));
            Assert.That(firstCompositeBytes, Is.LessThanOrEqualTo(16384L), "first composite lookup stays a small fixed cache cost");

            long steadyCompositeBytes = -1;
            yield return InputAllocationCapture.Measure(
                "reader-steady-composite",
                () =>
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        UXInput.Reader.IsCompositePartPressed(move, "Down");
                    }
                },
                (measuredBytes, _) => steadyCompositeBytes = measuredBytes);
            Assert.That(steadyCompositeBytes, Is.Zero);

            long firstGlyphKeyBytes = -1;
            yield return InputAllocationCapture.Measure(
                "glyph-first-new-key",
                () => UXInput.Glyph.GetGlyphKeyFromControlPath("<Keyboard>/a"),
                (measuredBytes, _) => firstGlyphKeyBytes = measuredBytes);

            TestContext.WriteLine("FIRST_USE,glyph-key," + firstGlyphKeyBytes); UnityEngine.Debug.Log("FIRST_USE,glyph-key," + firstGlyphKeyBytes);
            Assert.That(firstGlyphKeyBytes, Is.GreaterThanOrEqualTo(0L));
            Assert.That(firstGlyphKeyBytes, Is.LessThanOrEqualTo(4096L), "a new glyph key allocates only its cached string");

            long newGlyphKeyBytes = -1;
            yield return InputAllocationCapture.Measure(
                "glyph-second-new-key",
                () => UXInput.Glyph.GetGlyphKeyFromControlPath("<Keyboard>/b"),
                (measuredBytes, _) => newGlyphKeyBytes = measuredBytes);

            TestContext.WriteLine("FIRST_USE,glyph-key-second," + newGlyphKeyBytes); UnityEngine.Debug.Log("FIRST_USE,glyph-key-second," + newGlyphKeyBytes);
            Assert.That(newGlyphKeyBytes, Is.LessThanOrEqualTo(4096L), "a warmed new glyph key allocates only its cached string");

            long steadyGlyphKeyBytes = -1;
            yield return InputAllocationCapture.Measure(
                "glyph-steady-key",
                () =>
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        UXInput.Glyph.GetGlyphKeyFromControlPath("<Keyboard>/a");
                    }
                },
                (measuredBytes, _) => steadyGlyphKeyBytes = measuredBytes);
            Assert.That(steadyGlyphKeyBytes, Is.Zero);

            long firstDisplayBytes = -1;
            yield return InputAllocationCapture.Measure(
                "glyph-first-display-name",
                () => UXInput.Glyph.GetDisplayName("<Keyboard>/enter"),
                (measuredBytes, _) => firstDisplayBytes = measuredBytes);

            TestContext.WriteLine("FIRST_USE,glyph-display," + firstDisplayBytes);
            Assert.That(firstDisplayBytes, Is.GreaterThanOrEqualTo(0L));
            Assert.That(
                firstDisplayBytes,
                Is.LessThanOrEqualTo(262144L),
                "the first human-readable name initializes Unity Input System display-name tables (one-time, ~130KB)");

            long steadyDisplayBytes = -1;
            yield return InputAllocationCapture.Measure(
                "glyph-steady-display-name",
                () =>
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        UXInput.Glyph.GetDisplayName("<Keyboard>/enter");
                    }
                },
                (measuredBytes, _) => steadyDisplayBytes = measuredBytes);
            Assert.That(steadyDisplayBytes, Is.Zero);

            InputAction jump = Action("Jump");
            long firstRebindDisplayBytes = -1;
            yield return InputAllocationCapture.Measure(
                "rebind-first-display",
                () => UXInput.Rebind.GetDisplayString(jump),
                (measuredBytes, _) => firstRebindDisplayBytes = measuredBytes);

            TestContext.WriteLine("FIRST_USE,rebind-display," + firstRebindDisplayBytes);
            Assert.That(firstRebindDisplayBytes, Is.GreaterThanOrEqualTo(0L));
            Assert.That(firstRebindDisplayBytes, Is.LessThanOrEqualTo(16384L), "the first binding display string is a bounded one-time cost");

            long steadyRebindDisplayBytes = -1;
            yield return InputAllocationCapture.Measure(
                "rebind-steady-display",
                () =>
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        UXInput.Rebind.GetDisplayString(jump);
                    }
                },
                (measuredBytes, _) => steadyRebindDisplayBytes = measuredBytes);
            Assert.That(steadyRebindDisplayBytes, Is.Zero);
        }

        [UnityTest]
        public IEnumerator NavigationRefreshAllocatesZeroBytes()
        {
            EventSystem previousEventSystem = EventSystem.current;
            var eventRoot = new GameObject("alloc-navigation-events", typeof(EventSystem));
            var eventSystem = eventRoot.GetComponent<EventSystem>();
            EventSystem.current = eventSystem;
            UXInput.Navigation.SetRequireSelection(true, true);
            var root = new GameObject("alloc-navigation-scope", typeof(RectTransform), typeof(Canvas));
            root.SetActive(false);
            try
            {
                root.layer = UIComponent.UIShowLayer;
                var scope = root.AddComponent<NavigationScope>();
                SetScope(scope, "_holder", InputTestViews.Bind(root).GetHolder());
                root.SetActive(true);
                var buttonObject = new GameObject("alloc-navigation-button", typeof(RectTransform), typeof(Button));
                buttonObject.transform.SetParent(root.transform, false);
                var button = buttonObject.GetComponent<Button>();

                scope.DefaultSelectable = button;
                bool registered = false;
                yield return InputAllocationCapture.Measure(
                    "navigation-first-register",
                    () => registered = scope.RegisterSelectable(button, false));
                Assert.That(registered, Is.True);
                Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(buttonObject));

                Button growthButton = null;
                for (int i = 1; i <= 16; i++)
                {
                    var child = new GameObject("alloc-navigation-growth", typeof(RectTransform), typeof(Button));
                    child.transform.SetParent(root.transform, false);
                    growthButton = child.GetComponent<Button>();
                    if (i < 16) Assert.That(scope.RegisterSelectable(growthButton), Is.True);
                }
                yield return InputAllocationCapture.Measure(
                    "navigation-register-growth",
                    () => registered = scope.RegisterSelectable(growthButton));
                Assert.That(registered, Is.True);
                Assert.That(scope.RuntimeSelectableCount, Is.EqualTo(17));

                long bytes = 0;
                yield return InputAllocationCapture.Measure(
                    "navigation-refresh",
                    () =>
                    {
                        for (int i = 0; i < 2000; i++)
                        {
                            UXInput.Navigation.Refresh();
                        }
                    },
                    (measuredBytes, _) => bytes = measuredBytes);

                Assert.That(bytes, Is.Zero, "steady-state navigation refresh must not allocate");

                yield return InputAllocationCapture.Measure(
                    "navigation-focus-recovery",
                    () =>
                    {
                        for (int i = 0; i < 200; i++)
                        {
                            eventSystem.SetSelectedGameObject(null);
                            UXInput.Navigation.Refresh();
                        }
                    },
                    (measuredBytes, _) => bytes = measuredBytes);
                Assert.That(bytes, Is.Zero, "steady-state selection recovery must not allocate");
            }
            finally
            {
                InputTestViews.UnbindAll();
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                UXInput.Shutdown();
                UnityEngine.Object.DestroyImmediate(eventRoot);
                if (previousEventSystem != null) EventSystem.current = previousEventSystem;
            }
        }

        [UnityTest]
        public IEnumerator NavigationMultipleScopesAndParentChainAllocateZeroBytes()
        {
            EventSystem previous = EventSystem.current;
            var events = new GameObject("alloc-navigation-events", typeof(EventSystem));
            EventSystem.current = events.GetComponent<EventSystem>();
            var root = new GameObject("alloc-navigation-window", typeof(RectTransform), typeof(Canvas), typeof(TestHolder));
            var scopes = new NavigationScope[16];
            try
            {
                UXInput.Navigation.SetRequireSelection(true, true);
                UIBase view = InputTestViews.Bind(root.GetComponent<TestHolder>());
                for (int i = 0; i < 3; i++)
                {
                    var widget = new GameObject("alloc-navigation-widget", typeof(RectTransform), typeof(TestHolder));
                    widget.transform.SetParent(view.GetHolder().transform, false);
                    view = InputTestViews.Bind(widget.GetComponent<TestHolder>(), view);
                }

                for (int i = 0; i < scopes.Length; i++)
                {
                    var panel = new GameObject("alloc-navigation-panel", typeof(RectTransform));
                    panel.SetActive(false);
                    panel.transform.SetParent(view.GetHolder().transform, false);
                    var scope = panel.AddComponent<NavigationScope>();
                    SetScope(scope, "_holder", view.GetHolder());
                    SetScope(scope, "_blockLowerScopes", false);
                    for (int j = 0; j < 32; j++)
                    {
                        var child = new GameObject("alloc-navigation-candidate", typeof(RectTransform), typeof(Button));
                        child.transform.SetParent(panel.transform, false);
                        var button = child.GetComponent<Button>();
                        button.interactable = j == 31;
                        scope.RegisterSelectable(button);
                    }
                    panel.SetActive(true);
                    scopes[i] = scope;
                }
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Not.Null);
                yield return InputAllocationCapture.Measure("navigation-16-scopes-32-candidates", () =>
                {
                    for (int i = 0; i < 2000; i++) UXInput.Navigation.Refresh();
                }, (bytes, allocations) =>
                {
                    Assert.That(bytes, Is.Zero);
                    Assert.That(allocations, Is.Zero);
                });

                var overlay = new GameObject("alloc-navigation-blocker", typeof(RectTransform), typeof(Canvas));
                overlay.transform.SetParent(root.transform, false);
                var canvas = overlay.GetComponent<Canvas>();
                canvas.overrideSorting = true;
                canvas.sortingOrder = 100;
                overlay.SetActive(false);
                var blocker = overlay.AddComponent<NavigationScope>();
                SetScope(blocker, "_holder", root.GetComponent<TestHolder>());
                blocker.Navigable = false;
                overlay.SetActive(true);
                Assert.That(canvas.overrideSorting, Is.True);
                Assert.That(blocker.IsAlive, Is.True);
                Assert.That(blocker.SortingCanvas, Is.SameAs(canvas));
                Assert.That(canvas.sortingOrder, Is.EqualTo(100));
                foreach (var scope in scopes) Assert.That(scope.NavigationSuppressed, Is.True);
                Assert.That(EventSystem.current.currentSelectedGameObject, Is.Null);
                yield return InputAllocationCapture.Measure("navigation-blocked-16-scopes", () =>
                {
                    for (int i = 0; i < 2000; i++) UXInput.Navigation.Refresh();
                }, (bytes, allocations) =>
                {
                    Assert.That(bytes, Is.Zero);
                    Assert.That(allocations, Is.Zero);
                });
            }
            finally
            {
                UXInput.Shutdown();
                InputTestViews.UnbindAll();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(events);
                if (previous != null) EventSystem.current = previous;
            }
        }

        private static void SetScope(NavigationScope scope, string name, object value) =>
            typeof(NavigationScope).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(scope, value);
    }
}
#endif
