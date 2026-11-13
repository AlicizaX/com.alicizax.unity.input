#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class RebindTests : InputDeviceTestBase
    {
        [Test]
        public void ConfirmDisablesConflictingDefaultBinding()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            InputAction submit = Action("Submit");
            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/enter");
            UXInput.Rebind.ConfirmApply(save: false);
            SetKeys(Key.Enter);

            Assert.That(submit.bindings[0].effectivePath, Is.Empty);
            Assert.That(jump.IsPressed(), Is.True);
            Assert.That(submit.IsPressed(), Is.False);
            Assert.That(UXInput.Glyph.GetBindingControlPath(submit), Is.Empty);
        }

        [Test]
        public void CompletionListenerCanStartNextRebind()
        {
            AddKeyboard();
            InputAction jump = Action("Jump");
            InputAction submit = Action("Submit");
            void OnEnded(bool completed, RebindChange change)
            {
                if (completed) UXInput.Rebind.Begin(submit);
            }
            UXInput.Rebind.OnRebindEnded += OnEnded;
            try
            {
                UXInput.Rebind.Begin(jump, RebindRequest.Default.WithWaitDelay(0f));
                SetKeys(Key.Tab);
                Assert.That(UXInput.Rebind.IsRebinding, Is.True);
                Assert.That(jump.enabled, Is.True);
                Assert.That(submit.enabled, Is.False);
                UXInput.Rebind.Cancel();
                Assert.That(submit.enabled, Is.True);
            }
            finally
            {
                UXInput.Rebind.OnRebindEnded -= OnEnded;
            }
        }

        [Test]
        public void StartedListenerCanCancelTheRunningOperation()
        {
            AddKeyboard();
            UXInput.Rebind.OnRebindStarted += UXInput.Rebind.Cancel;
            UXInput.Rebind.Begin(Action("Jump"));
            Assert.That(UXInput.Rebind.IsRebinding, Is.False);
            Assert.That(Action("Jump").enabled, Is.True);
        }

        [Test]
        public void ThrowingCancelListenerCannotKeepOperationAlive()
        {
            AddKeyboard();
            void Throw(bool completed, RebindChange change) => throw new InvalidOperationException("listener failure");
            UXInput.Rebind.Begin(Action("Jump"));
            UXInput.Rebind.OnRebindEnded += Throw;
            try
            {
                Assert.Throws<InvalidOperationException>(() => UXInput.Rebind.Cancel());
                Assert.That(UXInput.Rebind.IsRebinding, Is.False);
                Assert.That(Action("Jump").enabled, Is.True);
            }
            finally
            {
                UXInput.Rebind.OnRebindEnded -= Throw;
            }
        }

        [Test]
        public void RequestDefaultsAreShared()
        {
            Assert.That(
                RebindRequest.ForTarget(RebindTarget.KeyboardMouse).ExcludedControls,
                Is.SameAs(RebindRequest.Default.ExcludedControls),
                "default options must share the fixed excluded-controls array");

            RebindRequest probe = RebindRequest.ForTarget(RebindTarget.Gamepad);
            Assert.That(probe.BindingGroup, Is.EqualTo("Gamepad"));
            Assert.That(probe.RequiredControlPath, Is.EqualTo("<Gamepad>"));
        }

        [Test]
        public void RequestDerivationsDoNotMutateTheSharedDefault()
        {
            RebindRequest custom = RebindRequest.Default
                .WithCompositePart("Up")
                .WithWaitDelay(0f)
                .WithPrepareOnly(false);

            Assert.That(custom.CompositePartName, Is.EqualTo("Up"));
            Assert.That(custom.WaitDelay, Is.Zero);
            Assert.That(custom.PrepareOnly, Is.False);
            Assert.That(RebindRequest.Default.CompositePartName, Is.Null);
            Assert.That(RebindRequest.Default.WaitDelay, Is.GreaterThan(0f));
            Assert.That(RebindRequest.Default.PrepareOnly, Is.True);
        }

        [Test]
        public void PrepareAndConfirmAppliesOverride()
        {
            InputAction jump = Action("Jump");

            RebindChange change = UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
            Assert.That(change.IsValid, Is.True);
            Assert.That(UXInput.Rebind.HasPreparedRebinds, Is.True);
            Assert.That(jump.bindings[0].overridePath, Is.Null.Or.Empty, "prepare must not apply");

            Assert.That(UXInput.Rebind.ConfirmApply(clearConflicts: true, save: false), Is.True);
            Assert.That(UXInput.Rebind.HasPreparedRebinds, Is.False);
            Assert.That(jump.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));
        }

        [Test]
        public void DiscardLeavesAssetUnchanged()
        {
            InputAction jump = Action("Jump");

            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
            UXInput.Rebind.DiscardPrepared();

            Assert.That(UXInput.Rebind.HasPreparedRebinds, Is.False);
            Assert.That(jump.bindings[0].overridePath, Is.Null.Or.Empty);
        }

        [Test]
        public void ConflictIsReportedAndConfirmClearsTheCompetingOverride()
        {
            InputAction jump = Action("Jump");
            InputAction secondary = Action("Secondary");

            UXInput.Rebind.PrepareRebind(secondary, 0, "<Keyboard>/tab");
            Assert.That(UXInput.Rebind.ConfirmApply(clearConflicts: true, save: false), Is.True);
            Assert.That(secondary.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));

            int conflictCount = 0;
            RebindChange reported = default;
            void Handler(RebindChange prepared, IReadOnlyList<RebindChange> conflicts)
            {
                reported = prepared;
                conflictCount = conflicts.Count;
            }

            UXInput.Rebind.OnBindingConflict += Handler;
            try
            {
                UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
                Assert.That(conflictCount, Is.GreaterThan(0));
                Assert.That(reported.OverridePath, Is.EqualTo("<Keyboard>/tab"));

                Assert.That(UXInput.Rebind.ConfirmApply(clearConflicts: true, save: false), Is.True);
            }
            finally
            {
                UXInput.Rebind.OnBindingConflict -= Handler;
            }

            Assert.That(jump.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));
            Assert.That(secondary.bindings[0].overridePath, Is.Null.Or.Empty);
        }

        [Test]
        public void BeginAndCancelRestoresActionEnabledState()
        {
            InputAction jump = Action("Jump");
            Assert.That(jump.enabled, Is.True);

            Assert.That(UXInput.Rebind.Begin(jump), Is.True);
            Assert.That(UXInput.Rebind.IsRebinding, Is.True);
            Assert.That(jump.enabled, Is.False, "the action is disabled while listening");

            UXInput.Rebind.Cancel();

            Assert.That(UXInput.Rebind.IsRebinding, Is.False);
            Assert.That(jump.enabled, Is.True);
        }

        [Test]
        public void BeginCompletesWithTheCapturedKey()
        {
            Keyboard keyboard = AddKeyboard();
            InputAction jump = Action("Jump");

            bool ended = false;
            RebindChange endedChange = default;
            void Handler(bool completed, RebindChange change)
            {
                ended = completed;
                endedChange = change;
            }

            UXInput.Rebind.OnRebindEnded += Handler;
            try
            {
                Assert.That(
                    UXInput.Rebind.Begin(jump, RebindRequest.Default.WithWaitDelay(0f)),
                    Is.True);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.Tab));
                InputSystem.Update();
            }
            finally
            {
                UXInput.Rebind.OnRebindEnded -= Handler;
            }

            Assert.That(ended, Is.True);
            Assert.That(endedChange.OverridePath, Is.EqualTo("<Keyboard>/tab"));
            Assert.That(UXInput.Rebind.HasPreparedRebinds, Is.True);

            Assert.That(UXInput.Rebind.ConfirmApply(clearConflicts: true, save: false), Is.True);
            Assert.That(jump.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));
        }

        [Test]
        public void BeginRejectsNullAction()
        {
            Assert.That(UXInput.Rebind.Begin(null as InputAction), Is.False);
        }

        [Test]
        public void DisplayStringIsStableAfterWarmup()
        {
            InputAction jump = Action("Jump");

            string display = UXInput.Rebind.GetDisplayString(jump);
            string second = UXInput.Rebind.GetDisplayString(jump);
            Assert.That(display, Does.Contain("Space").IgnoreCase);
            Assert.That(second, Is.SameAs(display), "display strings are cached until bindings change");
        }

        [Test]
        public void ResetRemovesSelectedAndAllOverrides()
        {
            InputAction jump = Action("Jump");

            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
            UXInput.Rebind.ConfirmApply(clearConflicts: false, save: false);
            Assert.That(jump.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));

            UXInput.Rebind.Reset(jump, RebindRequest.Default, save: false);
            Assert.That(jump.bindings[0].overridePath, Is.Null.Or.Empty);

            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
            UXInput.Rebind.ConfirmApply(clearConflicts: false, save: false);
            UXInput.Rebind.Reset(jump, save: false);
            Assert.That(jump.bindings[0].overridePath, Is.Null.Or.Empty);
        }

        [Test]
        public void SaveAndReloadRoundTripsOverride()
        {
            InputAction jump = Action("Jump");

            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
            Assert.That(UXInput.Rebind.ConfirmApply(clearConflicts: false, save: true), Is.True);
            Assert.That(File.Exists(UXInput.Rebind.BindingFilePath), Is.True);

            jump.RemoveAllBindingOverrides();
            Assert.That(jump.bindings[0].overridePath, Is.Null.Or.Empty);

            UXInput.Shutdown();
            UXInput.Initialize(Asset, null, BindingFilePath);

            Assert.That(jump.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));
        }

        [Test]
        public void BeginCompositePartCompletesWithCapturedKey()
        {
            Keyboard keyboard = AddKeyboard();
            InputAction move = Asset.FindAction("Gameplay/Move");

            bool ended = false;
            RebindChange endedChange = default;
            void Handler(bool completed, RebindChange change)
            {
                ended = completed;
                endedChange = change;
            }

            UXInput.Rebind.OnRebindEnded += Handler;
            try
            {
                Assert.That(
                    UXInput.Rebind.Begin(
                        move,
                        RebindRequest.Default.WithCompositePart("Up").WithWaitDelay(0f)),
                    Is.True);

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.I));
                InputSystem.Update();
            }
            finally
            {
                UXInput.Rebind.OnRebindEnded -= Handler;
            }

            Assert.That(ended, Is.True);
            Assert.That(endedChange.BindingIndex, Is.EqualTo(1));
            Assert.That(endedChange.OverridePath, Is.EqualTo("<Keyboard>/i"));

            Assert.That(UXInput.Rebind.ConfirmApply(true, false), Is.True);
            Assert.That(move.bindings[1].overridePath, Is.EqualTo("<Keyboard>/i"));
            Assert.That(move.bindings[0].overridePath, Is.Null.Or.Empty);
        }

        [Test]
        public void ConfirmWithoutClearingKeepsCompetingOverride()
        {
            InputAction jump = Action("Jump");
            InputAction secondary = Action("Secondary");

            UXInput.Rebind.PrepareRebind(secondary, 0, "<Keyboard>/tab");
            UXInput.Rebind.ConfirmApply(clearConflicts: false, save: false);
            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
            UXInput.Rebind.ConfirmApply(clearConflicts: false, save: false);

            Assert.That(jump.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));
            Assert.That(secondary.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));
        }

        [Test]
        public void ConfirmTwiceAndDiscardWithoutPreparedAreNoOps()
        {
            InputAction jump = Action("Jump");
            int applyCount = 0;
            void Handler(bool applied, IReadOnlyList<RebindChange> changes)
            {
                applyCount++;
            }

            UXInput.Rebind.OnApply += Handler;
            try
            {
                UXInput.Rebind.DiscardPrepared();
                Assert.That(applyCount, Is.Zero, "discard without prepared changes must not raise OnApply");

                UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
                Assert.That(UXInput.Rebind.ConfirmApply(true, false), Is.True);
                Assert.That(UXInput.Rebind.ConfirmApply(true, false), Is.False);
                Assert.That(applyCount, Is.EqualTo(1));
            }
            finally
            {
                UXInput.Rebind.OnApply -= Handler;
            }
        }

        [Test]
        public void ResetToDefaultRestoresInitialState()
        {
            InputAction jump = Action("Jump");

            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
            UXInput.Rebind.ConfirmApply(false, false);
            Assert.That(jump.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));

            UXInput.Rebind.ResetToDefault(false);

            Assert.That(jump.bindings[0].overridePath, Is.Null.Or.Empty);
            Assert.That(UXInput.Rebind.HasPreparedRebinds, Is.False);
        }

        [Test]
        public void ExportImportRoundTripsOverrides()
        {
            InputAction jump = Action("Jump");

            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
            UXInput.Rebind.ConfirmApply(false, false);
            string json = UXInput.Rebind.ExportBindingsJson();
            Assert.That(json, Is.Not.Empty);

            UXInput.Rebind.ResetAllBindings(false);
            Assert.That(jump.bindings[0].overridePath, Is.Null.Or.Empty);

            Assert.That(UXInput.Rebind.ImportBindingsJson(json, save: false), Is.True);
            Assert.That(jump.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));
        }

        [Test]
        public void BeginWhileRebindingCancelsPreviousAndRestoresItsAction()
        {
            InputAction jump = Action("Jump");
            InputAction secondary = Action("Secondary");

            Assert.That(UXInput.Rebind.Begin(jump), Is.True);
            Assert.That(jump.enabled, Is.False);

            Assert.That(UXInput.Rebind.Begin(secondary), Is.True);
            Assert.That(jump.enabled, Is.True, "the previous action must be re-enabled when a new rebind starts");
            Assert.That(secondary.enabled, Is.False);
            Assert.That(UXInput.Rebind.IsRebinding, Is.True);

            UXInput.Rebind.Cancel();
            Assert.That(secondary.enabled, Is.True);
            Assert.That(UXInput.Rebind.IsRebinding, Is.False);
        }

        [Test]
        public void PrepareRejectsInvalidInput()
        {
            InputAction jump = Action("Jump");

            Assert.That(UXInput.Rebind.PrepareRebind(null, 0, "<Keyboard>/tab").IsValid, Is.False);
            Assert.That(UXInput.Rebind.PrepareRebind(jump, -1, "<Keyboard>/tab").IsValid, Is.False);
            Assert.That(UXInput.Rebind.PrepareRebind(jump, 99, "<Keyboard>/tab").IsValid, Is.False);
            Assert.That(UXInput.Rebind.PrepareRebind(jump, 0, null).IsValid, Is.False);
            Assert.That(UXInput.Rebind.HasPreparedRebinds, Is.False);
        }

        [Test]
        public void BindingFilePathIsCachedAndSavedFlagFollowsFile()
        {
            string first = UXInput.Rebind.BindingFilePath;
            string second = UXInput.Rebind.BindingFilePath;

            Assert.That(second, Is.SameAs(first));
            Assert.That(UXInput.Rebind.HasSavedBindings, Is.False);

            InputAction jump = Action("Jump");
            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
            UXInput.Rebind.ConfirmApply(false, save: true);

            Assert.That(UXInput.Rebind.HasSavedBindings, Is.True);
        }
    }
}
#endif
