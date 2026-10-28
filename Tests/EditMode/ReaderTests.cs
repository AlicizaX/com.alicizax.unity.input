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
        public void ButtonStateOnceTriggersOnPressEdgeOnly()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            var state = new UXInput.Reader.ButtonState();

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
            var state = new UXInput.Reader.ButtonState();

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
            var first = new UXInput.Reader.ButtonState();
            var second = new UXInput.Reader.ButtonState();

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
            Assert.That(UXInput.Reader.ReadPressed(move), Is.True);
            Assert.That(UXInput.Reader.ReadButton(jump), Is.False);

            SetKeys(Key.Space);
            Assert.That(UXInput.Reader.ReadButton(jump), Is.True);
            Assert.That(UXInput.Reader.ReadPressed(jump), Is.True);
        }

        [Test]
        public void CompositePartReadsDirectionalButtons()
        {
            AddKeyboard();
            InputAction move = Action("Move");

            SetKeys(Key.W);
            Assert.That(UXInput.Reader.ReadCompositePartButton(move, "Up"), Is.True);
            Assert.That(UXInput.Reader.ReadCompositePartButton(move, "Down"), Is.False);

            SetKeys(Key.S);
            Assert.That(UXInput.Reader.ReadCompositePartButton(move, "Down"), Is.True);

            SetKeys();
            Assert.That(UXInput.Reader.ReadCompositePartButton(move, "Down"), Is.False);
        }

        [Test]
        public void ReaderSkipsCompositeReadsWhileActionDisabled()
        {
            AddKeyboard();
            InputAction move = Action("Move");
            SetKeys(Key.W);
            move.Disable();

            Assert.That(UXInput.Reader.ReadCompositePartButton(move, "Up"), Is.False);
            move.Enable();
        }

        [Test]
        public void TraceEventsReportDiscreteReads()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            var state = new UXInput.Reader.ButtonState();
            int onceCount = 0;
            UXInput.Reader.ReadTraceKind lastKind = default;

            void Handler(UXInput.Reader.ReadTrace trace)
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
                Assert.That(lastKind, Is.EqualTo(UXInput.Reader.ReadTraceKind.Once));
            }
            finally
            {
                UXInput.Reader.OnRead -= Handler;
            }
        }

        [Test]
        public void ResolveActionReturnsNullForUnknownPath()
        {
            InitializeProvider();
            try
            {
                Assert.That(UXInput.Reader.ResolveAction("Gameplay/Missing"), Is.Null);
                Assert.That(UXInput.Reader.ResolveAction("Gameplay/Jump"), Is.Not.Null);
            }
            finally
            {
                ShutdownProvider();
            }
        }

        [Test]
        public void ButtonStateHandlesCompositePartEdgeAndToggle()
        {
            AddKeyboard();
            InputAction move = Action("Move");
            var onceState = new UXInput.Reader.ButtonState();
            var toggleState = new UXInput.Reader.ButtonState();

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

            Assert.That(UXInput.Reader.ReadCompositePartButton(move, "up"), Is.True);
            Assert.That(UXInput.Reader.ReadCompositePartButton(move, "UP"), Is.True);
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
                UXInput.Reader.ReadCompositePartButton(move, "Up"),
                Is.True,
                "a matching control on any device must trigger the composite part");
        }

        [Test]
        public void NullActionsAreHandledWithoutThrowing()
        {
            Assert.That(UXInput.Reader.ReadValue<Vector2>(null as InputAction), Is.EqualTo(Vector2.zero));
            Assert.That(UXInput.Reader.TryReadValue<Vector2>(null as InputAction, out _), Is.False);
            Assert.That(UXInput.Reader.ReadButton(null as InputAction), Is.False);
            Assert.That(UXInput.Reader.ReadPressed(null as InputAction), Is.False);
            Assert.That(UXInput.Reader.ReadCompositePartButton(null as InputAction, "Up"), Is.False);
            Assert.That(UXInput.Reader.ResolveAction(null), Is.Null);
            Assert.That(UXInput.Reader.ResolveAction(" "), Is.Null);
        }

        [Test]
        public void ReadButtonRejectsValueActions()
        {
            AddKeyboard();
            InputAction move = Action("Move");
            SetKeys(Key.W);

            Assert.That(UXInput.Reader.ReadPressed(move), Is.True);
            Assert.That(UXInput.Reader.ReadButton(move), Is.False, "ReadButton only accepts InputActionType.Button");
        }

        [Test]
        public void ContinuousReadTraceFiresForHeldReads()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            int continuousCount = 0;

            void Handler(UXInput.Reader.ReadTrace trace)
            {
                continuousCount++;
            }

            UXInput.Reader.OnContinuousRead += Handler;
            try
            {
                SetKeys(Key.Space);
                Assert.That(UXInput.Reader.ReadPressed(jump), Is.True);
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
