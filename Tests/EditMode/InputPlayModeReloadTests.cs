#if INPUTSYSTEM_SUPPORT
using System.Collections;
using System.Reflection;
using AlicizaX;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace AlicizaX.Input.Tests
{
    public sealed class InputPlayModeReloadTests
    {
        private const string ProviderName = "input-reload-test-provider";
        private const string SettingsKey = "AlicizaX.Input.Tests.ReloadOptions";
        private static InputWatch _previousWatch;
        private static InputReader _previousReader;
        private static int _oldActivities;

        [UnityTest]
        public IEnumerator NormalReloadPairsProviderCallbacks() => RunCycles(EnterPlayModeOptions.None);

        [UnityTest]
        public IEnumerator DisabledDomainReloadPairsProviderCallbacks() =>
            RunCycles(EnterPlayModeOptions.DisableDomainReload);

        private static IEnumerator RunCycles(EnterPlayModeOptions options)
        {
            Assert.That(UXInput.IsInitialized, Is.False);
            SessionState.SetInt(SettingsKey, (int)EditorSettings.enterPlayModeOptions);
            SessionState.SetBool(SettingsKey + ".enabled", EditorSettings.enterPlayModeOptionsEnabled);
            EditorSettings.enterPlayModeOptions = options;
            EditorSettings.enterPlayModeOptionsEnabled = true;
            _previousWatch = null;
            _previousReader = null;
            _oldActivities = 0;
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject(ProviderName);
            root.AddComponent<RootModule>();
            var provider = root.AddComponent<InputService>();
            typeof(InputService).GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(provider, InputTestAssets.Create());

            // Keep the steps explicit: Unity restores the iterator's program counter, not local variables, across a domain reload.
            yield return new EnterPlayMode(options == EnterPlayModeOptions.None);
            AssertRunningSession();
            yield return new ExitPlayMode();
            AssertStoppedSession();
            yield return new EnterPlayMode(options == EnterPlayModeOptions.None);
            AssertRunningSession();
            yield return new ExitPlayMode();
            AssertStoppedSession();
        }

        private static void AssertRunningSession()
        {
            Assert.That(UXInput.IsInitialized, Is.True);
            Assert.That(UXInput.Watch, Is.Not.SameAs(_previousWatch));
            Assert.That(UXInput.Reader, Is.Not.SameAs(_previousReader));
            Assert.That(UXInput.Reader.ResolveAction("Gameplay/Jump").enabled, Is.True);
            int activities = 0;
            void OnActivity(InputWatch.InputContext _) => activities++;
            UXInput.Watch.OnInputActivity += OnActivity;
            var pad = InputSystem.AddDevice<Gamepad>();
            try
            {
                InputSystem.QueueStateEvent(pad, new GamepadState().WithButton(GamepadButton.South));
                InputSystem.Update();
                Assert.That(activities, Is.EqualTo(1), "one input must produce one activity in each session");
                Assert.That(_oldActivities, Is.Zero, "closed modules must not receive input in a later session");
            }
            finally
            {
                UXInput.Watch.OnInputActivity -= OnActivity;
                InputSystem.RemoveDevice(pad);
            }
            _previousWatch = UXInput.Watch;
            _previousReader = UXInput.Reader;
        }

        private static void AssertStoppedSession()
        {
            Assert.That(UXInput.IsInitialized, Is.False);
            if (_previousReader != null) Assert.That(_previousReader.Actions, Is.Null);
            _oldActivities = 0;
            if (_previousWatch != null) _previousWatch.OnInputActivity += CountOldActivity;
        }

        private static void CountOldActivity(InputWatch.InputContext _) { _oldActivities++; }

        [UnityTearDown]
        public IEnumerator RestoreEditorSettings()
        {
            if (EditorApplication.isPlaying) yield return new ExitPlayMode();
            EditorSettings.enterPlayModeOptions = (EnterPlayModeOptions)SessionState.GetInt(SettingsKey, 0);
            EditorSettings.enterPlayModeOptionsEnabled = SessionState.GetBool(SettingsKey + ".enabled", false);
            SessionState.EraseInt(SettingsKey);
            SessionState.EraseBool(SettingsKey + ".enabled");
            var root = GameObject.Find(ProviderName);
            if (root != null)
            {
                var asset = (InputActionAsset)typeof(InputService)
                    .GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(root.GetComponent<InputService>());
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(asset);
            }
            if (_previousWatch != null) _previousWatch.OnInputActivity -= CountOldActivity;
            _previousWatch = null;
            _previousReader = null;
        }
    }
}
#endif
