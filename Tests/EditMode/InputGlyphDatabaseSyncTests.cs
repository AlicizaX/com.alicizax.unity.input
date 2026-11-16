#if INPUTSYSTEM_SUPPORT
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Input.Tests
{
    [TestFixture]
    public sealed class InputGlyphDatabaseSyncTests
    {
        [Test]
        public void SyncAppendsMissingProfilesWithoutDroppingOrClearingData()
        {
            InputGlyphDatabase database = ScriptableObject.CreateInstance<InputGlyphDatabase>();
            try
            {
                SeedProfiles(database);

                Assert.That(InputGlyphDatabaseWindow.SyncFixedProfiles(database), Is.True);

                var serialized = new SerializedObject(database);
                SerializedProperty profiles = serialized.FindProperty("profiles");
                Assert.That(
                    profiles.arraySize,
                    Is.EqualTo(InputProfileCatalog.Editable.Count),
                    "catalog profiles are appended while the custom profile is preserved");

                SerializedProperty xbox = FindProfile(profiles, "Xbox");
                Assert.That(xbox, Is.Not.Null);
                Assert.That(
                    xbox.FindPropertyRelative("entries").arraySize,
                    Is.EqualTo(1),
                    "existing entries must not be cleared by a profile sync");
                Assert.That(
                    xbox.FindPropertyRelative("fallbackProfileIds").arraySize,
                    Is.EqualTo(1),
                    "missing fallback configuration is filled from the catalog");

                Assert.That(FindProfile(profiles, "Touch"), Is.Null, "Touch must not be part of the glyph profile set");
                Assert.That(FindProfile(profiles, "Custom"), Is.Not.Null, "unknown profiles must be preserved");

                Assert.That(
                    InputGlyphDatabaseWindow.SyncFixedProfiles(database),
                    Is.False,
                    "a second sync must be a no-op");
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void SyncOnEmptyDatabaseCreatesGlyphProfilesWithoutTouch()
        {
            InputGlyphDatabase database = ScriptableObject.CreateInstance<InputGlyphDatabase>();
            try
            {
                Assert.That(InputGlyphDatabaseWindow.SyncFixedProfiles(database), Is.True);

                var serialized = new SerializedObject(database);
                SerializedProperty profiles = serialized.FindProperty("profiles");
                Assert.That(profiles.arraySize, Is.EqualTo(InputProfileCatalog.Editable.Count - 1));

                foreach (InputProfileInfo profile in InputProfileCatalog.Editable)
                {
                    Assert.That(
                        FindProfile(profiles, profile.Id) != null,
                        Is.EqualTo(profile.Profile != InputWatch.InputProfile.Touch),
                        profile.Id);
                }
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        [TestCase("Touch")]
        [TestCase("touch")]
        public void SyncRemovesExistingTouchProfileAndRefreshesCache(string touchId)
        {
            InputGlyphDatabase database = ScriptableObject.CreateInstance<InputGlyphDatabase>();
            try
            {
                SeedProfiles(database);
                var serialized = new SerializedObject(database);
                SerializedProperty profiles = serialized.FindProperty("profiles");
                profiles.arraySize++;
                SerializedProperty touch = profiles.GetArrayElementAtIndex(profiles.arraySize - 1);
                touch.FindPropertyRelative("id").stringValue = touchId;
                SerializedProperty entries = touch.FindPropertyRelative("entries");
                entries.arraySize = 1;
                SerializedProperty entry = entries.GetArrayElementAtIndex(0);
                SerializedProperty paths = entry.FindPropertyRelative("controlPaths");
                paths.arraySize = 1;
                paths.GetArrayElementAtIndex(0).stringValue = "<Touchscreen>/primaryTouch/press";
                entry.FindPropertyRelative("tmpSpriteName").stringValue = "TouchPress";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                database.EditorRefreshCache();

                string glyphKey = InputGlyphPathUtility.GetGlyphKeyFromControlPath("<Touchscreen>/primaryTouch/press");
                Assert.That(database.TryGetGlyph(touchId, glyphKey, out _, out _), Is.True);

                Assert.That(InputGlyphDatabaseWindow.SyncFixedProfiles(database), Is.True);

                serialized.Update();
                profiles = serialized.FindProperty("profiles");
                Assert.That(FindProfile(profiles, touchId), Is.Null);
                Assert.That(FindProfile(profiles, "Touch"), Is.Null);
                Assert.That(database.TryGetGlyph(touchId, glyphKey, out _, out _), Is.False);
                Assert.That(FindProfile(profiles, "Xbox").FindPropertyRelative("entries").arraySize, Is.EqualTo(1));
                Assert.That(FindProfile(profiles, "Custom"), Is.Not.Null);
                Assert.That(InputGlyphDatabaseWindow.SyncFixedProfiles(database), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        [Test]
        public void ExistingProfileHintsAreNotOverwritten()
        {
            InputGlyphDatabase database = ScriptableObject.CreateInstance<InputGlyphDatabase>();
            try
            {
                var serialized = new SerializedObject(database);
                SerializedProperty profiles = serialized.FindProperty("profiles");
                profiles.arraySize = 1;
                SerializedProperty profile = profiles.GetArrayElementAtIndex(0);
                profile.FindPropertyRelative("id").stringValue = "Xbox";

                SerializedProperty hints = profile.FindPropertyRelative("bindingGroupHints");
                hints.arraySize = 1;
                hints.GetArrayElementAtIndex(0).stringValue = "custom-hint";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                InputGlyphDatabaseWindow.SyncFixedProfiles(database);

                var after = new SerializedObject(database);
                SerializedProperty xbox = FindProfile(after.FindProperty("profiles"), "Xbox");
                SerializedProperty afterHints = xbox.FindPropertyRelative("bindingGroupHints");
                Assert.That(afterHints.arraySize, Is.EqualTo(1));
                Assert.That(afterHints.GetArrayElementAtIndex(0).stringValue, Is.EqualTo("custom-hint"));
            }
            finally
            {
                Object.DestroyImmediate(database);
            }
        }

        private static void SeedProfiles(InputGlyphDatabase database)
        {
            var serialized = new SerializedObject(database);
            SerializedProperty profiles = serialized.FindProperty("profiles");
            profiles.arraySize = 2;

            SerializedProperty xbox = profiles.GetArrayElementAtIndex(0);
            xbox.FindPropertyRelative("id").stringValue = "Xbox";
            SerializedProperty entries = xbox.FindPropertyRelative("entries");
            entries.arraySize = 1;
            SerializedProperty entry = entries.GetArrayElementAtIndex(0);
            SerializedProperty paths = entry.FindPropertyRelative("controlPaths");
            paths.arraySize = 1;
            paths.GetArrayElementAtIndex(0).stringValue = "<Gamepad>/buttonSouth";

            SerializedProperty custom = profiles.GetArrayElementAtIndex(1);
            custom.FindPropertyRelative("id").stringValue = "Custom";

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SerializedProperty FindProfile(SerializedProperty profiles, string profileId)
        {
            for (int i = 0; i < profiles.arraySize; i++)
            {
                SerializedProperty profile = profiles.GetArrayElementAtIndex(i);
                if (profile.FindPropertyRelative("id").stringValue == profileId)
                {
                    return profile;
                }
            }

            return null;
        }
    }
}
#endif
