#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace AlicizaX.Input.Tests
{
    internal sealed class GlyphProfileSpec
    {
        public string Id;
        public string[] Fallbacks = Array.Empty<string>();
        public string[] Hints = Array.Empty<string>();
        public GlyphEntrySpec[] Entries = Array.Empty<GlyphEntrySpec>();
    }

    internal sealed class GlyphEntrySpec
    {
        public string[] ControlPaths = Array.Empty<string>();
        public Sprite Sprite;
        public string TmpName;
    }

    /// <summary>
    /// 通过 SerializedObject 构造测试用图标数据库，避免依赖私有嵌套类型的反射。
    /// </summary>
    internal sealed class GlyphDatabaseBuilder : IDisposable
    {
        private readonly List<UnityEngine.Object> _owned = new List<UnityEngine.Object>();
        private InputGlyphDatabase _database;

        public Sprite CreateSprite(string name)
        {
            var texture = new Texture2D(2, 2);
            _owned.Add(texture);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f));
            sprite.name = name;
            _owned.Add(sprite);
            return sprite;
        }

        public InputGlyphDatabase Build(Sprite placeholder, params GlyphProfileSpec[] profiles)
        {
            _database = ScriptableObject.CreateInstance<InputGlyphDatabase>();
            _database.name = "TestGlyphDatabase";

            var serialized = new SerializedObject(_database);
            serialized.FindProperty("placeholderSprite").objectReferenceValue = placeholder;

            SerializedProperty profilesProperty = serialized.FindProperty("profiles");
            profilesProperty.arraySize = profiles.Length;
            for (int i = 0; i < profiles.Length; i++)
            {
                SerializedProperty profileProperty = profilesProperty.GetArrayElementAtIndex(i);
                profileProperty.FindPropertyRelative("id").stringValue = profiles[i].Id;
                SetStringArray(profileProperty.FindPropertyRelative("fallbackProfileIds"), profiles[i].Fallbacks);
                SetStringArray(profileProperty.FindPropertyRelative("bindingGroupHints"), profiles[i].Hints);

                SerializedProperty entries = profileProperty.FindPropertyRelative("entries");
                entries.arraySize = profiles[i].Entries.Length;
                for (int entryIndex = 0; entryIndex < profiles[i].Entries.Length; entryIndex++)
                {
                    GlyphEntrySpec entry = profiles[i].Entries[entryIndex];
                    SerializedProperty entryProperty = entries.GetArrayElementAtIndex(entryIndex);
                    SetStringArray(entryProperty.FindPropertyRelative("controlPaths"), entry.ControlPaths);
                    entryProperty.FindPropertyRelative("sprite").objectReferenceValue = entry.Sprite;
                    entryProperty.FindPropertyRelative("tmpSpriteName").stringValue = entry.TmpName ?? string.Empty;
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
            _database.EditorRefreshCache();
            return _database;
        }

        public void Dispose()
        {
            if (_database != null)
            {
                UnityEngine.Object.DestroyImmediate(_database);
                _database = null;
            }

            for (int i = _owned.Count - 1; i >= 0; i--)
            {
                if (_owned[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(_owned[i]);
                }
            }

            _owned.Clear();
        }

        private static void SetStringArray(SerializedProperty property, string[] values)
        {
            values ??= Array.Empty<string>();
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).stringValue = values[i];
            }
        }
    }
}
#endif
