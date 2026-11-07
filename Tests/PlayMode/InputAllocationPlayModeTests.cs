#if INPUTSYSTEM_SUPPORT && UNITY_EDITOR
using System;
using System.Collections;
using AlicizaX.UI.Runtime;
using AlicizaX.UI.UXNavigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class InputAllocationPlayModeTests : InputDeviceTestBase
    {
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

            var root = new GameObject("alloc-navigation-scope", typeof(RectTransform), typeof(Canvas));
            try
            {
                root.layer = UIComponent.UIShowLayer;
                var scope = root.AddComponent<UXNavigationScope>();
                var buttonObject = new GameObject("alloc-navigation-button", typeof(RectTransform), typeof(Button));
                buttonObject.transform.SetParent(root.transform, false);
                var button = buttonObject.GetComponent<Button>();

                Assert.That(scope.RegisterSelectable(button, false), Is.True);
                scope.DefaultSelectable = button;
                UXNavigationSystem.RequestRefresh(true);

                long bytes = 0;
                yield return InputAllocationCapture.Measure(
                    "navigation-refresh",
                    () =>
                    {
                        for (int i = 0; i < 2000; i++)
                        {
                            UXNavigationSystem.RequestRefresh(false);
                        }
                    },
                    (measuredBytes, _) => bytes = measuredBytes);

                Assert.That(bytes, Is.Zero, "steady-state navigation refresh must not allocate");
            }
            finally
            {
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                UXInput.Shutdown();
            }
        }
    }
}
#endif
