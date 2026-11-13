#if INPUTSYSTEM_SUPPORT
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class ReaderTests : InputDeviceTestBase
    {
        [Test]
        public void DisabledCompositePartDoesNotReadAnotherPartsControl()
        {
            AddKeyboard();
            InputAction move = Action("Move");
            move.ApplyBindingOverride(1, string.Empty);
            move.ApplyBindingOverride(2, "<Keyboard>/w");
            SetKeys(Key.W);

            Assert.That(UXInput.Reader.IsCompositePartPressed(move, "Down"), Is.True);
            Assert.That(UXInput.Reader.IsCompositePartPressed(move, "Up"), Is.False);
            Assert.That(UXInput.Glyph.GetBindingControlPath(move, "Up"), Is.Empty);
        }

        [Test]
        public void ButtonStateOnceTriggersOnPressEdgeOnly()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            var state = new InputReader.ButtonState();

            Assert.That(state.Once(jump), Is.False, "idle state must not report a press");

            SetKeys(Key.Space);
            Assert.That(state.Once(jump), Is.True, "first frame of a press must report once");
            Assert.That(state.Once(jump), Is.False, "held key must not report again");

            SetKeys();
            Assert.That(state.Once(jump), Is.False, "release must not report");

            SetKeys(Key.Space);
            Assert.That(state.Once(jump), Is.True, "re-press must report again");
        }

        [Test]
        public void ButtonStateToggleFlipsOnEachPressAndResetClears()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            var state = new InputReader.ButtonState();

            Assert.That(state.Toggle(jump), Is.False);

            SetKeys(Key.Space);
            Assert.That(state.Toggle(jump), Is.True);
            Assert.That(state.Toggle(jump), Is.True, "holding must not flip again");

            SetKeys();
            Assert.That(state.Toggle(jump), Is.True, "release keeps the toggle state");

            SetKeys(Key.Space);
            Assert.That(state.Toggle(jump), Is.False);

            state.Reset();
            SetKeys();
            Assert.That(state.Toggle(jump), Is.False, "reset must clear the cached toggle");
        }

        [Test]
        public void ButtonStatesAreOwnedPerCaller()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            var first = new InputReader.ButtonState();
            var second = new InputReader.ButtonState();

            SetKeys(Key.Space);
            Assert.That(first.Once(jump), Is.True);
            Assert.That(second.Once(jump), Is.True, "each caller must observe the press independently");
        }

        [Test]
        public void ValuePressedAndButtonHelpersReadInputSystem()
        {
            AddKeyboard();
            InputAction move = Action("Move");
            InputAction jump = Action("Jump");
            InputAction moveValue = Action("Move");

            SetKeys(Key.W);
            Assert.That(UXInput.Reader.ReadValue<Vector2>(moveValue), Is.EqualTo(Vector2.up));
            Assert.That(UXInput.Reader.TryReadValue<Vector2>(move, out Vector2 value), Is.True);
            Assert.That(value, Is.EqualTo(Vector2.up));
            Assert.That(UXInput.Reader.IsPressed(move), Is.True);
            Assert.That(UXInput.Reader.IsPressed(jump), Is.False);

            SetKeys(Key.Space);
            Assert.That(UXInput.Reader.IsPressed(jump), Is.True);
        }

        [Test]
        public void CompositePartReadsDirectionalButtons()
        {
            AddKeyboard();
            InputAction move = Action("Move");

            SetKeys(Key.W);
            Assert.That(UXInput.Reader.IsCompositePartPressed(move, "Up"), Is.True);
            Assert.That(UXInput.Reader.IsCompositePartPressed(move, "Down"), Is.False);

            SetKeys(Key.S);
            Assert.That(UXInput.Reader.IsCompositePartPressed(move, "Down"), Is.True);

            SetKeys();
            Assert.That(UXInput.Reader.IsCompositePartPressed(move, "Down"), Is.False);
        }

        [Test]
        public void ReaderSkipsCompositeReadsWhileActionDisabled()
        {
            AddKeyboard();
            InputAction move = Action("Move");
            SetKeys(Key.W);
            move.Disable();

            Assert.That(UXInput.Reader.IsCompositePartPressed(move, "Up"), Is.False);
            move.Enable();
        }

        [Test]
        public void TraceEventsReportDiscreteReads()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            var state = new InputReader.ButtonState();
            int onceCount = 0;
            InputReader.ReadTraceKind lastKind = default;

            void Handler(InputReader.ReadTrace trace)
            {
                onceCount++;
                lastKind = trace.Kind;
            }

            UXInput.Reader.OnRead += Handler;
            try
            {
                SetKeys(Key.Space);
                Assert.That(state.Once(jump), Is.True);
                Assert.That(onceCount, Is.EqualTo(1));
                Assert.That(lastKind, Is.EqualTo(InputReader.ReadTraceKind.Once));
            }
            finally
            {
                UXInput.Reader.OnRead -= Handler;
            }
        }

        [Test]
        public void ResolveActionReturnsNullForUnknownPath()
        {
            try
            {
                Assert.That(UXInput.Reader.ResolveAction("Gameplay/Missing"), Is.Null);
                Assert.That(UXInput.Reader.ResolveAction("Gameplay/Jump"), Is.Not.Null);
            }
            finally
            {
                UXInput.Shutdown();
            }
        }

        [Test]
        public void ButtonStateHandlesCompositePartEdgeAndToggle()
        {
            AddKeyboard();
            InputAction move = Action("Move");
            var onceState = new InputReader.ButtonState();
            var toggleState = new InputReader.ButtonState();

            SetKeys(Key.W);
            Assert.That(onceState.Once(move, "Up"), Is.True);
            Assert.That(onceState.Once(move, "Up"), Is.False);
            Assert.That(toggleState.Toggle(move, "Up"), Is.True);
            Assert.That(toggleState.Toggle(move, "Up"), Is.True, "holding must not flip again");

            SetKeys();
            Assert.That(onceState.Once(move, "Up"), Is.False);
            Assert.That(toggleState.Toggle(move, "Up"), Is.True, "release keeps the toggle");

            SetKeys(Key.W);
            Assert.That(onceState.Once(move, "Up"), Is.True);
            Assert.That(toggleState.Toggle(move, "Up"), Is.False);
        }

        [Test]
        public void CompositePartNameIsCaseInsensitive()
        {
            AddKeyboard();
            InputAction move = Action("Move");
            SetKeys(Key.W);

            Assert.That(UXInput.Reader.IsCompositePartPressed(move, "up"), Is.True);
            Assert.That(UXInput.Reader.IsCompositePartPressed(move, "UP"), Is.True);
        }

        [Test]
        public void CompositePartAggregatesAcrossMatchingDevices()
        {
            AddKeyboard();
            Keyboard second = AddExtraKeyboard("SecondKeyboard");
            InputAction move = Action("Move");

            InputSystem.QueueStateEvent(second, new KeyboardState(Key.W));
            InputSystem.Update();

            Assert.That(
                UXInput.Reader.IsCompositePartPressed(move, "Up"),
                Is.True,
                "a matching control on any device must trigger the composite part");
        }

        [Test]
        public void NullActionsAreHandledWithoutThrowing()
        {
            Assert.That(UXInput.Reader.ReadValue<Vector2>(null as InputAction), Is.EqualTo(Vector2.zero));
            Assert.That(UXInput.Reader.TryReadValue<Vector2>(null as InputAction, out _), Is.False);
            Assert.That(UXInput.Reader.IsPressed(null as InputAction), Is.False);
            Assert.That(UXInput.Reader.IsPressed(null as InputCompositePart), Is.False);
            Assert.That(UXInput.Reader.IsCompositePartPressed(null as InputAction, "Up"), Is.False);
            Assert.That(UXInput.Reader.ResolveAction(null), Is.Null);
            Assert.That(UXInput.Reader.ResolveAction(" "), Is.Null);
        }

        [Test]
        public void CompositePartHandleIsCachedAndFollowsOverrides()
        {
            AddKeyboard();
            InputAction move = Action("Move");
            InputCompositePart up = UXInput.Reader.ResolveCompositePart(move, "Up");

            Assert.That(up, Is.Not.Null);
            Assert.That(UXInput.Reader.ResolveCompositePart(move, "UP"), Is.SameAs(up), "lookup is case-insensitive and cached");
            Assert.That(UXInput.Reader.ResolveCompositePart(move, "Missing"), Is.Null);

            SetKeys(Key.W);
            Assert.That(up.IsPressed(), Is.True);

            int upIndex = InputBindingResolver.FindCompositePartBindingIndex(move, "Up");
            UXInput.Rebind.PrepareRebind(move, upIndex, "<Keyboard>/i");
            UXInput.Rebind.ConfirmApply(false, false);
            SetKeys(Key.W);
            Assert.That(up.IsPressed(), Is.False, "the cached controls must be rebuilt after an override");
            SetKeys(Key.I);
            Assert.That(up.IsPressed(), Is.True);

            var state = new InputReader.ButtonState();
            SetKeys();
            Assert.That(state.Once(up), Is.False);
            SetKeys(Key.I);
            Assert.That(state.Once(up), Is.True);
            Assert.That(state.Once(up), Is.False);
        }

        [Test]
        public void DirectOverrideWithoutRebindStillInvalidatesCompositeHandle()
        {
            AddKeyboard();
            InputAction move = Action("Move");
            InputCompositePart up = UXInput.Reader.ResolveCompositePart(move, "Up");
            SetKeys(Key.W);
            Assert.That(up.IsPressed(), Is.True);

            move.ApplyBindingOverride(InputBindingResolver.FindCompositePartBindingIndex(move, "Up"), "<Keyboard>/i");
            SetKeys(Key.I);
            Assert.That(up.IsPressed(), Is.True, "BoundControlsChanged advances the binding version");
        }

        [Test]
        public void ContinuousReadTraceFiresForHeldReads()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            int continuousCount = 0;

            void Handler(InputReader.ReadTrace trace)
            {
                continuousCount++;
            }

            UXInput.Reader.OnContinuousRead += Handler;
            try
            {
                SetKeys(Key.Space);
                Assert.That(UXInput.Reader.IsPressed(jump), Is.True);
                Assert.That(continuousCount, Is.GreaterThan(0));
            }
            finally
            {
                UXInput.Reader.OnContinuousRead -= Handler;
            }
        }
    }
}
#endif
