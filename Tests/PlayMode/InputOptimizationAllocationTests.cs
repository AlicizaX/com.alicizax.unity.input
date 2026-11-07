#if INPUTSYSTEM_SUPPORT && UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using AlicizaX.UI.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    public sealed class InputOptimizationAllocationTests : InputDeviceTestBase
    {
        private sealed class TestHolder : UIHolderObjectBase { }
        private sealed class SubmitCounter : MonoBehaviour, ISubmitHandler
        {
            public int Count;
            public void OnSubmit(BaseEventData eventData) { Count++; }
        }

        [UnityTest]
        public IEnumerator SpriteAndTmpQueriesMeasureFirstUseAndSteadyState()
        {
            AddKeyboard();
            var texture = new Texture2D(2, 2);
            var sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
            sprite.name = "SpaceButton";
            var database = ScriptableObject.CreateInstance<InputGlyphDatabase>();
            try
            {
                var serialized = new SerializedObject(database);
                var profiles = serialized.FindProperty("profiles");
                profiles.arraySize = 1;
                var profile = profiles.GetArrayElementAtIndex(0);
                profile.FindPropertyRelative("id").stringValue = "KeyboardMouse";
                var entries = profile.FindPropertyRelative("entries");
                entries.arraySize = 1;
                var entry = entries.GetArrayElementAtIndex(0);
                entry.FindPropertyRelative("sprite").objectReferenceValue = sprite;
                entry.FindPropertyRelative("tmpSpriteName").stringValue = string.Empty;
                var paths = entry.FindPropertyRelative("controlPaths");
                paths.arraySize = 1;
                paths.GetArrayElementAtIndex(0).stringValue = "<Keyboard>/space";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                UXInput.Glyph.SetDatabase(database);
                var jump = Action("Jump");

                void Query()
                {
                    UXInput.Glyph.TryGetSprite(jump, null, out _);
                    UXInput.Glyph.TryGetTag(jump, null, out _, out _);
                }

                yield return InputAllocationCapture.Measure("glyph-sprite-tmp-first", Query);
                Assert.That(UXInput.Glyph.TryGetTag(jump, null, out string tag, out string fallback), Is.True);
                Assert.That(tag, Is.EqualTo("<sprite name=\"SpaceButton\">"));
                Assert.That(fallback, Is.Empty, "a sprite hit does not need a readable-name fallback");
                yield return InputAllocationCapture.Measure("glyph-sprite-tmp-2000", () =>
                {
                    for (int i = 0; i < 2000; i++) Query();
                }, AssertZero);
            }
            finally
            {
                UXInput.Glyph.SetDatabase(null);
                Object.DestroyImmediate(database);
                Object.DestroyImmediate(sprite);
                Object.DestroyImmediate(texture);
            }
        }

        [UnityTest]
        public IEnumerator BindingCacheMeasuresCapacityGrowth()
        {
            AddKeyboard();
            Asset.Disable();
            var actions = new InputAction[80];
            var map = Asset.FindActionMap("Gameplay");
            for (int i = 0; i < actions.Length; i++)
                actions[i] = map.AddAction("Capacity" + i, InputActionType.Button, "<Keyboard>/space");
            Asset.Enable();

            yield return InputAllocationCapture.Measure("glyph-binding-first-key", () =>
                UXInput.Glyph.GetBindingControlPath(actions[0]));
            yield return InputAllocationCapture.Measure("glyph-binding-new-key", () =>
                UXInput.Glyph.GetBindingControlPath(actions[1]), AssertZero);
            yield return InputAllocationCapture.Measure("glyph-binding-grow-to-80", () =>
            {
                for (int i = 2; i < actions.Length; i++) UXInput.Glyph.GetBindingControlPath(actions[i]);
            });
            yield return InputAllocationCapture.Measure("glyph-binding-80-steady", () =>
            {
                for (int pass = 0; pass < 25; pass++)
                    for (int i = 0; i < actions.Length; i++) UXInput.Glyph.GetBindingControlPath(actions[i]);
            }, AssertZero);
        }

        [UnityTest]
        public IEnumerator WatchMeasuresNewDeviceTypeAndCapacityGrowth()
        {
            AddGamepad();
            var devices = new Joystick[20];
            for (int i = 0; i < devices.Length; i++) devices[i] = Track(InputSystem.AddDevice<Joystick>());
            yield return InputAllocationCapture.Measure("watch-first-joystick-context", () =>
                UXInput.Watch.ResolveContext(devices[0]));
            yield return InputAllocationCapture.Measure("watch-context-grow-to-20", () =>
            {
                for (int i = 1; i < devices.Length; i++) UXInput.Watch.ResolveContext(devices[i]);
            });
            yield return InputAllocationCapture.Measure("watch-20-devices-steady", () =>
            {
                for (int pass = 0; pass < 100; pass++)
                    for (int i = 0; i < devices.Length; i++) UXInput.Watch.ResolveContext(devices[i]);
            }, AssertZero);
        }

        [UnityTest]
        public IEnumerator VisualizerRecordsContinuousReadsWithoutAllocations()
        {
            AddKeyboard();
            SetKeys(Key.Space);
            var root = new GameObject("allocation-visualizer");
            root.SetActive(false);
            var visualizer = root.AddComponent<InputVisualizer>();
            var settings = new SerializedObject(visualizer);
            settings.FindProperty("logReaderContinuousReads").boolValue = true;
            settings.FindProperty("showOnScreen").boolValue = false;
            settings.ApplyModifiedPropertiesWithoutUndo();
            root.SetActive(true);
            try
            {
                var jump = Action("Jump");
                yield return InputAllocationCapture.Measure("visualizer-first-read", () =>
                    UXInput.Reader.IsPressed(jump), AssertZero);
                Assert.That(visualizer.EntryCount, Is.EqualTo(1));
                yield return InputAllocationCapture.Measure("reader-first-float-with-visualizer", () =>
                    UXInput.Reader.TryReadValue<float>(jump, out _));
                visualizer.ClearHistory();
                yield return InputAllocationCapture.Measure("visualizer-2000-continuous-reads", () =>
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        UXInput.Reader.IsPressed(jump);
                        UXInput.Reader.TryReadValue<float>(jump, out _);
                    }
                }, AssertZero);
                Assert.That(visualizer.EntryCount, Is.EqualTo(2), "different read kinds remain distinct");

                var getText = (Func<int, string>)typeof(InputVisualizer)
                    .GetMethod("GetEntryText", BindingFlags.Instance | BindingFlags.NonPublic)
                    .CreateDelegate(typeof(Func<int, string>), visualizer);
                string text = null;
                yield return InputAllocationCapture.Measure("visualizer-first-row-text", () => text = getText(0));
                Assert.That(text, Is.EqualTo("Reader: Pressed Gameplay/Jump"));
                yield return InputAllocationCapture.Measure("visualizer-cached-row-text-2000", () =>
                {
                    for (int i = 0; i < 2000; i++) getText(0);
                }, AssertZero);

                settings.Update();
                settings.FindProperty("showTimestamps").boolValue = true;
                settings.ApplyModifiedPropertiesWithoutUndo();
                visualizer.ClearHistory();
                yield return InputAllocationCapture.Measure("visualizer-first-timestamp-text", () =>
                {
                    UXInput.Reader.IsPressed(jump);
                    text = getText(0);
                });
                Assert.That(text, Does.StartWith("[").And.EndWith("Reader: Pressed Gameplay/Jump"));
                visualizer.ClearHistory();
                yield return InputAllocationCapture.Measure("visualizer-2000-same-frame-timestamp-records", () =>
                {
                    for (int i = 0; i < 2000; i++)
                    {
                        UXInput.Reader.IsPressed(jump);
                        getText(0);
                    }
                }, (_, allocations) => Assert.That(allocations, Is.EqualTo(1),
                    "one row string for the changed frame, reused by all other calls"));

                visualizer.ClearHistory();
                Assert.That(visualizer.EntryCount, Is.Zero);
                settings.Update();
                settings.FindProperty("maxHistoryEntries").intValue = 32;
                settings.ApplyModifiedPropertiesWithoutUndo();
                yield return InputAllocationCapture.Measure("visualizer-grow-to-32", () =>
                    visualizer.LogMessage("capacity-first"));
                Assert.That(visualizer.EntryCount, Is.EqualTo(1));
            }
            finally { Object.DestroyImmediate(root); }
        }

        [UnityTest]
        public IEnumerator HotkeySlotRegistrationAndDispatchAreMeasuredSeparately()
        {
            AddKeyboard();
            var root = new GameObject("allocation-hotkey", typeof(Canvas));
            root.layer = UIComponent.UIShowLayer;
            var holder = root.AddComponent<TestHolder>();
            var target = new GameObject("submit-target");
            target.SetActive(false);
            target.transform.SetParent(root.transform, false);
            var submit = target.AddComponent<SubmitCounter>();
            var trigger = target.AddComponent<HotkeyComponent>();
            var action = Action("Jump");
            var reference = InputActionReference.Create(action);
            var eventObject = new GameObject("allocation-event-system", typeof(EventSystem));
            EventSystem.current = eventObject.GetComponent<EventSystem>();
            try
            {
                target.SetActive(true);
                yield return InputAllocationCapture.Measure("hotkey-first-register", () =>
                    UXHotkeySystem.RegisterHotkey(trigger, holder, reference, EHotkeyPressType.Performed));
                Assert.That(trigger.IsRegistered, Is.True);
                var scopes = (Dictionary<UIHolderObjectBase, HotkeyScope>)typeof(UXHotkeySystem)
                    .GetField("Scopes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
                var scope = scopes[holder];
                var dispatch = (Action<HotkeyScope, UIHolderObjectBase, InputAction, EHotkeyPressType>)
                    typeof(UXHotkeySystem).GetMethod("TryDispatchToScopeChain", BindingFlags.Static | BindingFlags.NonPublic)
                        .CreateDelegate(typeof(Action<HotkeyScope, UIHolderObjectBase, InputAction, EHotkeyPressType>));
                yield return InputAllocationCapture.Measure("hotkey-first-submit", () =>
                    dispatch(scope, holder, action, EHotkeyPressType.Performed));
                Assert.That(submit.Count, Is.EqualTo(1));
                yield return InputAllocationCapture.Measure("hotkey-2000-dispatches", () =>
                {
                    for (int i = 0; i < 2000; i++) dispatch(scope, holder, action, EHotkeyPressType.Performed);
                }, AssertZero);
                Assert.That(submit.Count, Is.EqualTo(2001));
                yield return InputAllocationCapture.Measure("hotkey-20-unregister-register", () =>
                {
                    for (int i = 0; i < 20; i++)
                    {
                        UXHotkeySystem.UnregisterHotkey(trigger);
                        UXHotkeySystem.RegisterHotkey(trigger, holder, reference, EHotkeyPressType.Performed);
                    }
                });
            }
            finally
            {
                Object.DestroyImmediate(eventObject);
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(reference);
            }
        }

        [UnityTest]
        public IEnumerator RebindAndSessionCostsAreMeasured()
        {
            AddKeyboard();
            var jump = Action("Jump");
            yield return InputAllocationCapture.Measure("rebind-first-start-cancel", () =>
            {
                UXInput.Rebind.Begin(jump);
                UXInput.Rebind.Cancel();
            });
            yield return InputAllocationCapture.Measure("rebind-20-start-cancel", () =>
            {
                for (int i = 0; i < 20; i++)
                {
                    UXInput.Rebind.Begin(jump);
                    UXInput.Rebind.Cancel();
                }
            });
            yield return InputAllocationCapture.Measure("rebind-prepare-confirm-save", () =>
            {
                UXInput.Rebind.PrepareRebind(jump, 0, "<Keyboard>/enter");
                UXInput.Rebind.ConfirmApply();
            });
            Assert.That(jump.bindings[0].effectivePath, Is.EqualTo("<Keyboard>/enter"));
            yield return InputAllocationCapture.Measure("input-session-shutdown", UXInput.Shutdown);
            Assert.That(jump.enabled, Is.False);
            yield return InputAllocationCapture.Measure("input-session-init-load", () =>
                UXInput.Initialize(Asset, null, BindingFilePath));
            Assert.That(jump.bindings[0].effectivePath, Is.EqualTo("<Keyboard>/enter"));
            Assert.That(jump.enabled, Is.True);
        }

        private static void AssertZero(long bytes, int allocations)
        {
            Assert.That(bytes, Is.Zero);
            Assert.That(allocations, Is.Zero);
        }

    }
}
#endif
