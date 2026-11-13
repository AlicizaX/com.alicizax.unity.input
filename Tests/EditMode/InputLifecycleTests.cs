#if INPUTSYSTEM_SUPPORT
using System;
using System.IO;
using NUnit.Framework;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using UnityEngine.InputSystem.Utilities;

namespace AlicizaX.Input.Tests
{
    public sealed class InputLifecycleTests : InputDeviceTestBase
    {
        [Test]
        public void ShutdownReleasesNavigationInstance()
        {
            UXInput.Navigation.SetRequireSelection(false, true);
            UXInput.Shutdown();
            Assert.That(UXInput.Navigation, Is.Null);
        }

        [Test]
        public void DuplicateInitializationLeavesCurrentSessionRunning()
        {
            var watch = UXInput.Watch;
            Assert.Throws<InvalidOperationException>(() => UXInput.Initialize(Asset, null));
            Assert.That(UXInput.Watch, Is.SameAs(watch));
            Assert.That(UXInput.IsInitialized, Is.True);
            Assert.That(Action("Jump").enabled, Is.True);
        }

        [Test]
        public void RestartReplacesAllModulesAndDetachesOldListeners()
        {
            AddKeyboard();
            var watch = UXInput.Watch;
            var reader = UXInput.Reader;
            var rebind = UXInput.Rebind;
            var glyph = UXInput.Glyph;
            var haptics = UXInput.Haptics;
            int oldEvents = 0;
            int newEvents = 0;
            void OldHandler(InputWatch.InputContext _) => oldEvents++;
            watch.OnInputActivity += OldHandler;
            UXInput.Shutdown();
            UXInput.Shutdown();
            watch.OnInputActivity -= OldHandler;
            // A listener attached to the closed instance must not receive device input either.
            watch.OnInputActivity += OldHandler;
            UXInput.Initialize(Asset, null, BindingFilePath);
            UXInput.Watch.OnInputActivity += _ => newEvents++;
            SetKeys(Key.A);
            Assert.That(newEvents, Is.EqualTo(1));
            Assert.That(oldEvents, Is.Zero);
            Assert.That(UXInput.Watch, Is.Not.SameAs(watch));
            Assert.That(UXInput.Reader, Is.Not.SameAs(reader));
            Assert.That(UXInput.Rebind, Is.Not.SameAs(rebind));
            Assert.That(UXInput.Glyph, Is.Not.SameAs(glyph));
            Assert.That(UXInput.Haptics, Is.Not.SameAs(haptics));
            Assert.That(reader.Actions, Is.Null);
            Assert.That(rebind.IsInitialized, Is.False);
            watch.OnInputActivity -= OldHandler;
        }

        [Test]
        public void CorruptBindingFileIsQuarantinedAndInputKeepsWorking()
        {
            AddKeyboard();
            var jump = Action("Jump");
            UXInput.Shutdown();
            jump.ApplyBindingOverride("<Keyboard>/tab");
            Directory.CreateDirectory(Path.GetDirectoryName(BindingFilePath));
            File.WriteAllText(BindingFilePath, "{ invalid json");

            LogAssert.Expect(LogType.Warning, new Regex("Ignored unreadable binding file"));
            UXInput.Initialize(Asset, null, BindingFilePath);

            Assert.That(UXInput.IsInitialized, Is.True, "a broken player save must not disable input");
            Assert.That(jump.enabled, Is.True);
            Assert.That(jump.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"), "entry overrides are restored");
            Assert.That(File.Exists(BindingFilePath), Is.False);
            Assert.That(File.Exists(BindingFilePath + ".corrupt"), Is.True, "the broken file is kept for diagnosis");

            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/f");
            UXInput.Rebind.ConfirmApply(false, true);
            Assert.That(File.ReadAllText(BindingFilePath), Does.Contain("<Keyboard>/f"));
            Assert.That(File.Exists(BindingFilePath + ".tmp"), Is.False, "saves go through a temp file that is moved into place");
        }

        [Test]
        public void ShutdownCancelsRebindAndRestoresEntryOverrides()
        {
            AddKeyboard();
            var jump = Action("Jump");
            UXInput.Shutdown();
            jump.ApplyBindingOverride("<Keyboard>/tab");
            UXInput.Initialize(Asset, null, BindingFilePath);
            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/f");
            UXInput.Rebind.ConfirmApply(false, false);
            Assert.That(UXInput.Rebind.Begin(jump), Is.True);
            var rebind = UXInput.Rebind;
            UXInput.Shutdown();
            Assert.That(rebind.IsRebinding, Is.False);
            Assert.That(jump.enabled, Is.False);
            Assert.That(jump.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));
            Assert.That(File.Exists(BindingFilePath), Is.False);
            UXInput.Initialize(Asset, null, BindingFilePath);
            Assert.That(UXInput.Rebind.IsRebinding, Is.False);
            Assert.That(jump.bindings[0].overridePath, Is.EqualTo("<Keyboard>/tab"));
        }

        [Test]
        public void SavedBindingsLoadBeforeActionsAreEnabledAndTemporaryChangesAreDiscarded()
        {
            var jump = Action("Jump");
            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/tab");
            UXInput.Rebind.ConfirmApply(false, true);
            string saved = File.ReadAllText(BindingFilePath);
            UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/f");
            UXInput.Rebind.ConfirmApply(false, false);
            UXInput.Shutdown();
            Assert.That(jump.bindings[0].overridePath, Is.Null.Or.Empty);
            string enabledPath = null;
            void OnActionChange(object value, InputActionChange change)
            {
                if (change == InputActionChange.ActionMapEnabled && value == jump.actionMap)
                    enabledPath = jump.bindings[0].effectivePath;
            }
            InputSystem.onActionChange += OnActionChange;
            try
            {
                UXInput.Initialize(Asset, null, BindingFilePath);
                Assert.That(enabledPath, Is.EqualTo("<Keyboard>/tab"));
                Assert.That(File.ReadAllText(BindingFilePath), Is.EqualTo(saved));
            }
            finally
            {
                InputSystem.onActionChange -= OnActionChange;
            }
        }

        [Test]
        public unsafe void ShutdownStopsOwnedGamepadEvenAfterCurrentDeviceChanges()
        {
            var pad = AddGamepad();
            float lowMotor = -1f;
            float highMotor = -1f;
            long? Capture(InputDevice device, InputDeviceCommand* command)
            {
                if (device != pad || command->type != new FourCC('R', 'M', 'B', 'L')) return null;
                // RMBL stores the low and high motor speeds after the public command header.
                float* motors = (float*)command->payloadPtr;
                lowMotor = motors[0];
                highMotor = motors[1];
                return InputDeviceCommand.GenericSuccess;
            }
            InputSystem.onDeviceCommand += Capture;
            try
            {
                UXInput.Haptics.Play(0.8f, 0.4f, 100f);
                Assert.That(lowMotor, Is.EqualTo(0.8f));
                Track(InputSystem.AddDevice<Gamepad>());
                var haptics = UXInput.Haptics;
                UXInput.Shutdown();
                Assert.That(lowMotor, Is.Zero);
                Assert.That(highMotor, Is.Zero);
                Assert.That(haptics.IsPlaying, Is.False);
                InputSystem.Update();
                Assert.That(lowMotor, Is.Zero);
            }
            finally
            {
                InputSystem.onDeviceCommand -= Capture;
            }
        }
    }
}
#endif
