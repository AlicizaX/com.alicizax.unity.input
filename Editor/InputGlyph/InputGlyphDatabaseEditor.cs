#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.InputSystem.Editor;
using UnityEngine.UIElements;

[CustomEditor(typeof(InputGlyphDatabase))]
public sealed class InputGlyphDatabaseEditor : Editor
{
    public override VisualElement CreateInspectorGUI()
    {
        VisualElement root = new VisualElement();
        root.style.paddingLeft = 6f;
        root.style.paddingRight = 6f;
        root.style.paddingTop = 6f;
        root.style.paddingBottom = 6f;

        Button openButton = new Button(OpenSelectedDatabase)
        {
            text = "Open Glyph Database Editor",
        };
        root.Add(openButton);

        HelpBox helpBox = new HelpBox(
            "Profiles are fixed by code. Use the editor window to assign sprites and binding-path aliases.",
            HelpBoxMessageType.Info);
        root.Add(helpBox);
        root.Bind(serializedObject);
        return root;
    }

    private void OpenSelectedDatabase()
    {
        InputGlyphDatabaseWindow.OpenFromAsset((InputGlyphDatabase)target);
    }

    [OnOpenAsset(0)]
    private static bool OpenAsset(int instanceID, int line)
    {
        InputGlyphDatabase database = EditorUtility.InstanceIDToObject(instanceID) as InputGlyphDatabase;
        if (database == null)
        {
            return false;
        }

        InputGlyphDatabaseWindow.OpenFromAsset(database);
        return true;
    }

    [MenuItem("AlicizaX/Extension/Input/Create Input Glyph Database", false, 80)]
    private static void CreateDatabaseAsset()
    {
        string path = EditorUtility.SaveFilePanelInProject(
            "Create Input Glyph Database",
            "InputGlyphDatabase",
            "asset",
            "Choose where to save the InputGlyphDatabase asset.");

        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        InputGlyphDatabase database = CreateInstance<InputGlyphDatabase>();
        database.EditorEnsureDefaultProfiles();
        AssetDatabase.CreateAsset(database, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Selection.activeObject = database;
        EditorGUIUtility.PingObject(database);
        InputGlyphDatabaseWindow.OpenFromAsset(database);
    }
}

internal sealed class InputGlyphDatabaseWindow : EditorWindow
{
    private const string MenuPath = "AlicizaX/Extension/Input/Input Glyph Database";
    private const string DefaultDatabaseName = "InputGlyphDatabase";
    private const string ProfilesPropertyName = "profiles";
    private const string PlaceholderSpritePropertyName = "placeholderSprite";
    private const string ProfileIdPropertyName = "profileId";
    private const string GlyphsPropertyName = "glyphs";
    private const string ControlPathsPropertyName = "controlPaths";
    private const string SpritePropertyName = "sprite";
    private const string TmpSpriteNamePropertyName = "tmpSpriteName";
    private const int SpriteFieldSize = 48;
    private const int BindingButtonSize = 18;
    private const int ToolbarHeight = 34;
    private const int SidebarInitialWidth = 216;

    private static readonly Color RootColor = new Color(0.18f, 0.18f, 0.18f);
    private static readonly Color ToolbarColor = new Color(0.15f, 0.15f, 0.15f);
    private static readonly Color SidebarColor = new Color(0.17f, 0.17f, 0.17f);
    private static readonly Color CardColor = new Color(0.20f, 0.20f, 0.20f);
    private static readonly Color CardBorderColor = new Color(0.27f, 0.27f, 0.27f);
    private static readonly Color SelectedColor = new Color(0.28f, 0.32f, 0.37f);
    private static readonly Color HoverColor = new Color(0.24f, 0.24f, 0.24f);
    private static readonly Color TextMutedColor = new Color(0.68f, 0.68f, 0.68f);

    private static readonly ProfileView[] ProfileViews =
    {
        new ProfileView(InputGlyphProfileIds.KeyboardMouse, "Keyboard & Mouse"),
        new ProfileView(InputGlyphProfileIds.GenericGamepad, "Generic Gamepad"),
        new ProfileView(InputGlyphProfileIds.Xbox, "Xbox"),
        new ProfileView(InputGlyphProfileIds.PlayStation, "PlayStation"),
        new ProfileView(InputGlyphProfileIds.Switch, "Switch"),
        new ProfileView(InputGlyphProfileIds.SteamDeck, "Steam Deck"),
    };

    private InputGlyphDatabase _database;
    private SerializedObject _serializedDatabase;
    private SerializedProperty _profilesProperty;
    private VisualElement _root;
    private VisualElement _sidebar;
    private TwoPaneSplitView _splitView;
    private TextField _searchField;
    private ScrollView _glyphScrollView;
    private Button[] _profileButtons;
    private int _selectedProfileIndex;
    private bool _duplicateDatabaseWarningShown;
    private string _search = string.Empty;

    [MenuItem(MenuPath, false, 80)]
    private static void OpenFromMenu()
    {
        InputGlyphDatabaseWindow window = GetWindow<InputGlyphDatabaseWindow>("Input Glyph Database", true);
        window.minSize = new Vector2(920f, 560f);
        window.EnsureDatabaseForWindow();
        window.Show();
    }

    internal static void OpenFromAsset(InputGlyphDatabase database)
    {
        InputGlyphDatabaseWindow window = GetWindow<InputGlyphDatabaseWindow>("Input Glyph Database", true);
        window.minSize = new Vector2(920f, 560f);
        window.SetDatabase(database);
        window.Show();
    }

    private void OnEnable()
    {
        if (_database == null)
        {
            EnsureDatabaseForWindow();
        }
    }

    private void CreateGUI()
    {
        BuildVisualTree();
        RefreshWindow();
    }

    private void SetDatabase(InputGlyphDatabase database)
    {
        if (database == null)
        {
            return;
        }

        _database = database;
        Undo.RecordObject(_database, "Initialize Input Glyph Database");
        if (_database.EditorEnsureDefaultProfiles())
        {
            EditorUtility.SetDirty(_database);
        }

        _serializedDatabase = new SerializedObject(_database);
        _profilesProperty = _serializedDatabase.FindProperty(ProfilesPropertyName);
        titleContent = new GUIContent(database.name, EditorGUIUtility.IconContent("d_ScriptableObject Icon").image);
        RefreshWindow();
    }

    private void EnsureDatabaseForWindow()
    {
        InputGlyphDatabase database = null;
        string[] guids = AssetDatabase.FindAssets("t:InputGlyphDatabase");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            database = AssetDatabase.LoadAssetAtPath<InputGlyphDatabase>(path);
            if (guids.Length > 1 && !_duplicateDatabaseWarningShown)
            {
                _duplicateDatabaseWarningShown = true;
                EditorUtility.DisplayDialog(
                    "Duplicate InputGlyphDatabase Assets",
                    "More than one InputGlyphDatabase asset was found. The editor opened the first result. Delete duplicate database assets to avoid editing the wrong configuration.",
                    "OK");
            }
        }

        if (database == null)
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Input Glyph Database",
                DefaultDatabaseName,
                "asset",
                "Choose where to save the InputGlyphDatabase asset.");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            database = CreateInstance<InputGlyphDatabase>();
            database.EditorEnsureDefaultProfiles();
            AssetDatabase.CreateAsset(database, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        SetDatabase(database);
    }

    private void BuildVisualTree()
    {
        _root = rootVisualElement;
        _root.Clear();
        _root.style.flexDirection = FlexDirection.Column;
        _root.style.backgroundColor = RootColor;

        Toolbar toolbar = new Toolbar();
        toolbar.style.height = ToolbarHeight;
        toolbar.style.minHeight = ToolbarHeight;
        toolbar.style.alignItems = Align.Center;
        toolbar.style.paddingLeft = 6f;
        toolbar.style.paddingRight = 6f;
        toolbar.style.backgroundColor = ToolbarColor;
        toolbar.style.borderBottomWidth = 1f;
        toolbar.style.borderBottomColor = CardBorderColor;

        AddToolbarLabel(toolbar, "Placeholder");
        ObjectField placeholderField = new ObjectField
        {
            objectType = typeof(Sprite),
            allowSceneObjects = false,
            bindingPath = PlaceholderSpritePropertyName,
        };
        placeholderField.style.width = 190f;
        placeholderField.tooltip = "Fallback sprite used when a glyph cannot be resolved";
        placeholderField.Bind(_serializedDatabase);
        toolbar.Add(placeholderField);

        Button addGlyphButton = CreateFlatButton("+", "Add glyph to selected profile", AddGlyphToSelectedProfile);
        addGlyphButton.style.width = 28f;
        addGlyphButton.style.marginLeft = 6f;
        toolbar.Add(addGlyphButton);

        ToolbarSpacer spacer = new ToolbarSpacer();
        spacer.style.flexGrow = 1f;
        toolbar.Add(spacer);

        AddToolbarLabel(toolbar, "Search");
        _searchField = new TextField();
        _searchField.style.width = 200f;
        _searchField.style.marginRight = 2f;
        _searchField.tooltip = "Filter glyph rows by binding path or TMP sprite name";
        _searchField.SetValueWithoutNotify(_search);
        ApplyToolbarSearchStyle(_searchField);
        _searchField.RegisterValueChangedCallback(OnSearchChanged);
        toolbar.Add(_searchField);

        ToolbarButton ensureButton = new ToolbarButton(EnsureDefaults)
        {
            text = "Sync Profiles",
            tooltip = "Sync fixed profile structure and default glyph rows",
        };
        ApplyToolbarButtonStyle(ensureButton);
        toolbar.Add(ensureButton);

        ToolbarButton saveButton = new ToolbarButton(SaveAsset)
        {
            text = "Save",
            tooltip = "Save asset",
        };
        ApplyToolbarButtonStyle(saveButton);
        toolbar.Add(saveButton);
        _root.Add(toolbar);

        _splitView = new TwoPaneSplitView(0, SidebarInitialWidth, TwoPaneSplitViewOrientation.Horizontal);
        _splitView.style.flexGrow = 1f;
        _root.Add(_splitView);

        _sidebar = new VisualElement();
        _sidebar.style.flexShrink = 0f;
        _sidebar.style.borderRightWidth = 1f;
        _sidebar.style.borderRightColor = CardBorderColor;
        _sidebar.style.backgroundColor = SidebarColor;
        _splitView.Add(_sidebar);

        VisualElement main = new VisualElement();
        main.style.flexGrow = 1f;
        main.style.paddingLeft = 12f;
        main.style.paddingRight = 12f;
        main.style.paddingTop = 10f;
        main.style.paddingBottom = 10f;
        main.style.backgroundColor = RootColor;
        _splitView.Add(main);

        _glyphScrollView = new ScrollView();
        _glyphScrollView.style.flexGrow = 1f;
        main.Add(_glyphScrollView);
        BuildSidebar();
    }

    private void RefreshWindow()
    {
        if (_root == null || _database == null)
        {
            return;
        }

        if (_serializedDatabase == null)
        {
            _serializedDatabase = new SerializedObject(_database);
        }

        _serializedDatabase.Update();
        EnsureSerializedProfiles();
        RefreshProfileSelection();
        BuildProfileContent();
    }

    private void BuildSidebar()
    {
        _sidebar.Clear();
        _profileButtons = new Button[ProfileViews.Length];
        Label profileTitle = new Label("Profiles");
        profileTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
        profileTitle.style.marginLeft = 10f;
        profileTitle.style.marginTop = 10f;
        profileTitle.style.marginBottom = 4f;
        _sidebar.Add(profileTitle);

        for (int i = 0; i < ProfileViews.Length; i++)
        {
            int index = i;
            Button button = CreateSidebarButton(ProfileViews[i].Name, i == _selectedProfileIndex, () => SelectProfile(index));
            if (i == _selectedProfileIndex)
            {
                button.tooltip = ProfileViews[i].Id;
            }

            _profileButtons[i] = button;
            _sidebar.Add(button);
        }
    }

    private void RefreshProfileSelection()
    {
        if (_profileButtons == null)
        {
            return;
        }

        for (int i = 0; i < _profileButtons.Length; i++)
        {
            Button button = _profileButtons[i];
            if (button == null)
            {
                continue;
            }

            bool selected = i == _selectedProfileIndex;
            ApplySidebarButtonState(button, selected);
            button.tooltip = selected ? ProfileViews[i].Id : string.Empty;
        }
    }

    private void BuildProfileContent()
    {
        _glyphScrollView.Clear();
        SerializedProperty profile = GetProfileProperty(_selectedProfileIndex);
        if (profile == null)
        {
            HelpBox helpBox = new HelpBox("No fixed profile data found. Click Sync Profiles in the toolbar.", HelpBoxMessageType.Warning);
            _glyphScrollView.Add(helpBox);
            return;
        }

        SerializedProperty glyphs = profile.FindPropertyRelative(GlyphsPropertyName);

        bool hasVisibleGlyph = false;
        for (int i = 0; i < glyphs.arraySize; i++)
        {
            SerializedProperty glyph = glyphs.GetArrayElementAtIndex(i);
            if (!GlyphMatchesSearch(glyph))
            {
                continue;
            }

            hasVisibleGlyph = true;
            _glyphScrollView.Add(CreateGlyphCard(glyphs, glyph, i));
        }

        if (!hasVisibleGlyph)
        {
            HelpBox emptyBox = new HelpBox("No glyph rows match the current search.", HelpBoxMessageType.Info);
            _glyphScrollView.Add(emptyBox);
        }
    }

    private VisualElement CreateGlyphCard(
        SerializedProperty glyphs,
        SerializedProperty glyph,
        int index)
    {
        VisualElement card = new VisualElement();
        card.style.marginBottom = 8f;
        card.style.paddingLeft = 8f;
        card.style.paddingRight = 8f;
        card.style.paddingTop = 8f;
        card.style.paddingBottom = 8f;
        card.style.backgroundColor = CardColor;
        card.style.borderBottomWidth = 1f;
        card.style.borderTopWidth = 1f;
        card.style.borderLeftWidth = 1f;
        card.style.borderRightWidth = 1f;
        card.style.borderBottomColor = CardBorderColor;
        card.style.borderTopColor = CardBorderColor;
        card.style.borderLeftColor = CardBorderColor;
        card.style.borderRightColor = CardBorderColor;
        card.style.borderBottomLeftRadius = 4f;
        card.style.borderBottomRightRadius = 4f;
        card.style.borderTopLeftRadius = 4f;
        card.style.borderTopRightRadius = 4f;

        SerializedProperty sprite = glyph.FindPropertyRelative(SpritePropertyName);
        SerializedProperty tmpSpriteName = glyph.FindPropertyRelative(TmpSpriteNamePropertyName);
        SerializedProperty controlPaths = glyph.FindPropertyRelative(ControlPathsPropertyName);

        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.justifyContent = Justify.FlexEnd;
        header.style.marginBottom = 2f;
        card.Add(header);

        Button deleteButton = CreateFlatButton("x", "Delete glyph", new GlyphButtonAction(this, glyphs.propertyPath, index).Invoke);
        deleteButton.style.width = 28f;
        deleteButton.style.marginRight = 2f;
        header.Add(deleteButton);

        VisualElement spriteRow = CreateSpriteRow(sprite);
        card.Add(spriteRow);

        VisualElement pathList = CreatePathList(controlPaths);
        pathList.style.marginTop = 6f;
        card.Add(pathList);

        Foldout advanced = new Foldout
        {
            text = "Advanced",
            value = false,
        };
        advanced.style.marginTop = 4f;
        PropertyField tmpField = new PropertyField(tmpSpriteName, "TMP Sprite Name");
        tmpField.tooltip = "Override TMP sprite asset name. Leave empty to use Sprite.name.";
        tmpField.Bind(_serializedDatabase);
        advanced.Add(tmpField);
        card.Add(advanced);
        return card;
    }

    private VisualElement CreateSpriteRow(SerializedProperty sprite)
    {
        VisualElement row = new VisualElement();
        row.style.flexDirection = FlexDirection.Row;
        row.style.alignItems = Align.Center;
        row.style.marginBottom = 6f;

        VisualElement previewField = CreateSpritePreviewField(sprite, out Image preview, out Label emptyLabel);
        row.Add(previewField);

        ObjectField spriteField = new ObjectField
        {
            objectType = typeof(Sprite),
            allowSceneObjects = false,
            value = sprite.objectReferenceValue as Sprite,
            tooltip = "Assign or drag a Sprite here",
        };
        spriteField.style.flexGrow = 1f;
        spriteField.style.marginLeft = 8f;
        spriteField.style.height = 22f;
        SpritePreviewContext context = new SpritePreviewContext(this, sprite.propertyPath, preview, emptyLabel);
        spriteField.RegisterCallback<ChangeEvent<UnityEngine.Object>, SpritePreviewContext>(OnSpriteObjectFieldChanged, context);
        row.Add(spriteField);
        return row;
    }

    private VisualElement CreateSpritePreviewField(SerializedProperty sprite, out Image preview, out Label emptyLabel)
    {
        string spritePropertyPath = sprite.propertyPath;
        VisualElement field = new VisualElement();
        field.style.width = SpriteFieldSize;
        field.style.height = SpriteFieldSize;
        field.style.flexShrink = 0f;
        field.style.backgroundColor = ToolbarColor;
        field.style.borderBottomWidth = 1f;
        field.style.borderTopWidth = 1f;
        field.style.borderLeftWidth = 1f;
        field.style.borderRightWidth = 1f;
        field.style.borderBottomColor = CardBorderColor;
        field.style.borderTopColor = CardBorderColor;
        field.style.borderLeftColor = CardBorderColor;
        field.style.borderRightColor = CardBorderColor;
        field.tooltip = "Sprite preview";

        preview = new Image
        {
            scaleMode = ScaleMode.ScaleToFit,
        };
        preview.style.position = Position.Absolute;
        preview.style.left = 3f;
        preview.style.right = 3f;
        preview.style.top = 3f;
        preview.style.bottom = 3f;
        field.Add(preview);

        emptyLabel = new Label("None");
        emptyLabel.style.position = Position.Absolute;
        emptyLabel.style.left = 0f;
        emptyLabel.style.right = 0f;
        emptyLabel.style.top = 0f;
        emptyLabel.style.bottom = 0f;
        emptyLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
        emptyLabel.style.color = TextMutedColor;
        field.Add(emptyLabel);

        RefreshSpritePreview(spritePropertyPath, preview, emptyLabel);
        return field;
    }

    private VisualElement CreatePathList(SerializedProperty controlPaths)
    {
        VisualElement container = new VisualElement();
        container.style.borderBottomWidth = 1f;
        container.style.borderTopWidth = 1f;
        container.style.borderLeftWidth = 1f;
        container.style.borderRightWidth = 1f;
        container.style.borderBottomColor = CardBorderColor;
        container.style.borderTopColor = CardBorderColor;
        container.style.borderLeftColor = CardBorderColor;
        container.style.borderRightColor = CardBorderColor;
        container.style.paddingLeft = 6f;
        container.style.paddingRight = 6f;
        container.style.paddingTop = 4f;
        container.style.paddingBottom = 6f;
        container.style.backgroundColor = new Color(0.18f, 0.18f, 0.18f);

        VisualElement header = new VisualElement();
        header.style.flexDirection = FlexDirection.Row;
        header.style.alignItems = Align.Center;
        header.style.marginBottom = 4f;

        Label title = new Label("Bindings");
        title.style.unityFontStyleAndWeight = FontStyle.Bold;
        title.style.flexGrow = 1f;
        header.Add(title);

        string controlPathsPropertyPath = controlPaths.propertyPath;
        Button addButton = CreateFlatButton("+", "Add binding", new AddBindingAction(this, controlPathsPropertyPath).Invoke);
        ApplySmallBindingButtonStyle(addButton);
        header.Add(addButton);
        container.Add(header);

        for (int i = 0; i < controlPaths.arraySize; i++)
        {
            int pathIndex = i;
            SerializedProperty path = controlPaths.GetArrayElementAtIndex(i);
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 2f;
            row.style.minHeight = 20f;

            VisualElement pathField = CreateInputControlPathField(path);
            pathField.style.flexGrow = 1f;
            row.Add(pathField);

            Button removeButton = CreateFlatButton("x", "Remove binding", new RemoveBindingAction(this, controlPathsPropertyPath, pathIndex).Invoke);
            ApplySmallBindingButtonStyle(removeButton);
            removeButton.style.marginLeft = 4f;
            row.Add(removeButton);
            container.Add(row);
        }
        return container;
    }

    private void AddGlyph(SerializedProperty glyphs, string path = null)
    {
        _serializedDatabase.Update();
        int index = glyphs.arraySize;
        glyphs.InsertArrayElementAtIndex(index);
        SerializedProperty glyph = glyphs.GetArrayElementAtIndex(index);
        glyph.FindPropertyRelative(SpritePropertyName).objectReferenceValue = null;
        glyph.FindPropertyRelative(TmpSpriteNamePropertyName).stringValue = string.Empty;
        SerializedProperty paths = glyph.FindPropertyRelative(ControlPathsPropertyName);
        paths.ClearArray();
        if (!string.IsNullOrEmpty(path))
        {
            AddControlPath(paths, path);
        }

        if (_serializedDatabase.ApplyModifiedProperties())
        {
            MarkDirtyAndRebuild();
        }
    }

    private void AddGlyphToSelectedProfile()
    {
        _serializedDatabase.Update();
        SerializedProperty profile = GetProfileProperty(_selectedProfileIndex);
        if (profile == null)
        {
            return;
        }

        AddGlyph(profile.FindPropertyRelative(GlyphsPropertyName));
    }

    private void DeleteGlyph(string glyphsPropertyPath, int index)
    {
        _serializedDatabase.Update();
        SerializedProperty glyphs = _serializedDatabase.FindProperty(glyphsPropertyPath);
        if (glyphs == null || index < 0 || index >= glyphs.arraySize)
        {
            return;
        }

        glyphs.DeleteArrayElementAtIndex(index);
        if (_serializedDatabase.ApplyModifiedProperties())
        {
            MarkDirtyAndRebuild();
        }
    }

    private void RemoveControlPath(string pathsPropertyPath, int index)
    {
        _serializedDatabase.Update();
        SerializedProperty paths = _serializedDatabase.FindProperty(pathsPropertyPath);
        if (paths == null)
        {
            return;
        }

        if (index < 0 || index >= paths.arraySize)
        {
            return;
        }

        paths.DeleteArrayElementAtIndex(index);
        if (_serializedDatabase.ApplyModifiedProperties())
        {
            MarkDirtyAndRebuild();
        }
    }

    private void AddControlPathAndApply(string pathsPropertyPath, string value)
    {
        _serializedDatabase.Update();
        SerializedProperty paths = _serializedDatabase.FindProperty(pathsPropertyPath);
        if (paths == null)
        {
            return;
        }

        AddControlPath(paths, value);
        if (_serializedDatabase.ApplyModifiedProperties())
        {
            MarkDirtyAndRebuild();
        }
    }

    private static void AddControlPath(SerializedProperty paths, string value)
    {
        int index = paths.arraySize;
        paths.InsertArrayElementAtIndex(index);
        paths.GetArrayElementAtIndex(index).stringValue = value;
    }

    private VisualElement CreateInputControlPathField(SerializedProperty path)
    {
        InputControlPathElement element = new InputControlPathElement(
            _serializedDatabase,
            path.propertyPath,
            HandleInputControlPathModified);
        element.style.height = 20f;
        element.style.flexGrow = 1f;
        return element;
    }

    private void HandleInputControlPathModified()
    {
        _serializedDatabase.ApplyModifiedProperties();
        MarkDirtyAndRebuild();
    }

    private void RefreshSpritePreview(string propertyPath, Image preview, Label emptyLabel)
    {
        if (_serializedDatabase == null)
        {
            preview.sprite = null;
            preview.image = null;
            emptyLabel.style.display = DisplayStyle.Flex;
            return;
        }

        _serializedDatabase.Update();
        SerializedProperty property = _serializedDatabase.FindProperty(propertyPath);
        Sprite sprite = property != null ? property.objectReferenceValue as Sprite : null;
        preview.sprite = sprite;
        preview.image = null;
        emptyLabel.style.display = sprite == null ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void OnSpriteObjectFieldChanged(ChangeEvent<UnityEngine.Object> evt, SpritePreviewContext context)
    {
        Sprite sprite = evt.newValue as Sprite;
        context.Owner.AssignSpriteFromObjectField(context.PropertyPath, sprite, context.Preview, context.EmptyLabel);
    }

    private void AssignSpriteFromObjectField(string propertyPath, Sprite sprite, Image preview, Label emptyLabel)
    {
        if (string.IsNullOrEmpty(propertyPath) || _serializedDatabase == null)
        {
            return;
        }

        _serializedDatabase.Update();
        SerializedProperty property = _serializedDatabase.FindProperty(propertyPath);
        if (property == null)
        {
            return;
        }

        property.objectReferenceValue = sprite;
        if (_serializedDatabase.ApplyModifiedProperties())
        {
            MarkDirtyAndRebuild();
        }

        RefreshSpritePreview(propertyPath, preview, emptyLabel);
    }

    private static Button CreateFlatButton(string text, string tooltip, Action clicked)
    {
        Button button = new Button(clicked)
        {
            text = text,
            tooltip = tooltip,
        };
        ApplyFlatButtonStyle(button);
        return button;
    }

    private static void AddToolbarLabel(VisualElement toolbar, string text)
    {
        Label label = new Label(text);
        label.style.marginLeft = 8f;
        label.style.marginRight = 4f;
        label.style.color = TextMutedColor;
        label.style.unityTextAlign = TextAnchor.MiddleLeft;
        toolbar.Add(label);
    }

    private static void ApplyToolbarSearchStyle(TextField field)
    {
        field.style.height = 22f;
        field.style.marginLeft = 6f;
        field.style.paddingLeft = 0f;
        field.style.paddingRight = 0f;

        VisualElement input = field.Q(TextField.textInputUssName);
        if (input == null)
        {
            return;
        }

        input.style.height = 22f;
        input.style.paddingLeft = 7f;
        input.style.paddingRight = 7f;
        input.style.borderBottomWidth = 1f;
        input.style.borderTopWidth = 1f;
        input.style.borderLeftWidth = 1f;
        input.style.borderRightWidth = 1f;
        input.style.borderBottomColor = CardBorderColor;
        input.style.borderTopColor = CardBorderColor;
        input.style.borderLeftColor = CardBorderColor;
        input.style.borderRightColor = CardBorderColor;
        input.style.borderBottomLeftRadius = 3f;
        input.style.borderBottomRightRadius = 3f;
        input.style.borderTopLeftRadius = 3f;
        input.style.borderTopRightRadius = 3f;
        input.style.backgroundColor = RootColor;
    }

    private static void ApplyToolbarButtonStyle(Button button)
    {
        button.style.height = 22f;
        button.style.minWidth = 46f;
        button.style.marginLeft = 4f;
        button.style.paddingLeft = 8f;
        button.style.paddingRight = 8f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.borderBottomWidth = 0f;
        button.style.borderTopWidth = 0f;
        button.style.borderLeftWidth = 0f;
        button.style.borderRightWidth = 0f;
        button.style.backgroundColor = HoverColor;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
    }

    private static void ApplySmallBindingButtonStyle(Button button)
    {
        button.style.width = BindingButtonSize;
        button.style.height = BindingButtonSize;
        button.style.minWidth = BindingButtonSize;
        button.style.minHeight = BindingButtonSize;
        button.style.paddingLeft = 0f;
        button.style.paddingRight = 0f;
        button.style.paddingTop = 0f;
        button.style.paddingBottom = 0f;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
    }

    private static Button CreateSidebarButton(string text, bool selected, Action clicked)
    {
        Button button = new Button(clicked)
        {
            text = text,
        };
        ApplyFlatButtonStyle(button);
        button.style.height = 32f;
        button.style.marginLeft = 6f;
        button.style.marginRight = 6f;
        button.style.marginBottom = 2f;
        button.style.unityTextAlign = TextAnchor.MiddleLeft;
        ApplySidebarButtonState(button, selected);
        return button;
    }

    private static void ApplySidebarButtonState(Button button, bool selected)
    {
        button.style.paddingLeft = selected ? 12f : 10f;
        button.style.borderLeftWidth = selected ? 3f : 0f;
        button.style.borderLeftColor = SelectedColor;
        button.style.backgroundColor = selected ? SelectedColor : SidebarColor;
        button.style.unityFontStyleAndWeight = selected ? FontStyle.Bold : FontStyle.Normal;
    }

    private static void ApplyFlatButtonStyle(Button button)
    {
        button.style.borderBottomWidth = 0f;
        button.style.borderTopWidth = 0f;
        button.style.borderLeftWidth = 0f;
        button.style.borderRightWidth = 0f;
        button.style.backgroundColor = HoverColor;
        button.style.height = 22f;
        button.style.paddingLeft = 8f;
        button.style.paddingRight = 8f;
        button.style.unityTextAlign = TextAnchor.MiddleCenter;
    }

    private bool GlyphMatchesSearch(SerializedProperty glyph)
    {
        if (string.IsNullOrWhiteSpace(_search))
        {
            return true;
        }

        SerializedProperty tmpName = glyph.FindPropertyRelative(TmpSpriteNamePropertyName);
        if (ContainsIgnoreCase(tmpName.stringValue, _search))
        {
            return true;
        }

        SerializedProperty paths = glyph.FindPropertyRelative(ControlPathsPropertyName);
        for (int i = 0; i < paths.arraySize; i++)
        {
            if (ContainsIgnoreCase(paths.GetArrayElementAtIndex(i).stringValue, _search))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsIgnoreCase(string source, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value))
        {
            return false;
        }

        return source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private void SelectProfile(int index)
    {
        int nextIndex = Mathf.Clamp(index, 0, ProfileViews.Length - 1);
        if (_selectedProfileIndex == nextIndex)
        {
            return;
        }

        _selectedProfileIndex = nextIndex;
        RefreshProfileSelection();
        BuildProfileContent();
    }

    private void EnsureDefaults()
    {
        if (_database == null)
        {
            return;
        }

        Undo.RecordObject(_database, "Sync Input Glyph Fixed Profiles");
        if (_database.EditorEnsureDefaultProfiles())
        {
            EditorUtility.SetDirty(_database);
        }

        _serializedDatabase.Update();
        RefreshWindow();
    }

    private void SaveAsset()
    {
        if (_database == null)
        {
            return;
        }

        MarkDirty();
        AssetDatabase.SaveAssets();
    }

    private void OnSearchChanged(ChangeEvent<string> evt)
    {
        _search = evt.newValue ?? string.Empty;
        BuildProfileContent();
    }

    private void MarkDirtyAndRebuild()
    {
        MarkDirty();
        _database.EditorRefreshCache();
        EditorApplication.delayCall += DelayedRefresh;
    }

    private void DelayedRefresh()
    {
        if (this != null)
        {
            RefreshWindow();
        }
    }

    private void MarkDirty()
    {
        EditorUtility.SetDirty(_database);
    }

    private void EnsureSerializedProfiles()
    {
        if (_database.EditorEnsureDefaultProfiles())
        {
            EditorUtility.SetDirty(_database);
            _serializedDatabase.Update();
        }

        _profilesProperty = _serializedDatabase.FindProperty(ProfilesPropertyName);
        _selectedProfileIndex = Mathf.Clamp(_selectedProfileIndex, 0, ProfileViews.Length - 1);
    }

    private SerializedProperty GetProfileProperty(int profileIndex)
    {
        if (_profilesProperty == null || profileIndex < 0 || profileIndex >= _profilesProperty.arraySize)
        {
            return null;
        }

        SerializedProperty profile = _profilesProperty.GetArrayElementAtIndex(profileIndex);
        SerializedProperty profileId = profile.FindPropertyRelative(ProfileIdPropertyName);
        if (!InputGlyphStringUtility.EqualsOrdinal(profileId.stringValue, ProfileViews[profileIndex].Id))
        {
            return null;
        }

        return profile;
    }

    private readonly struct ProfileView
    {
        public readonly string Id;
        public readonly string Name;

        public ProfileView(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }

    private sealed class GlyphButtonAction
    {
        private readonly InputGlyphDatabaseWindow _owner;
        private readonly string _glyphsPropertyPath;
        private readonly int _index;

        public GlyphButtonAction(InputGlyphDatabaseWindow owner, string glyphsPropertyPath, int index)
        {
            _owner = owner;
            _glyphsPropertyPath = glyphsPropertyPath;
            _index = index;
        }

        public void Invoke()
        {
            _owner.DeleteGlyph(_glyphsPropertyPath, _index);
        }
    }

    private sealed class AddBindingAction
    {
        private readonly InputGlyphDatabaseWindow _owner;
        private readonly string _pathsPropertyPath;

        public AddBindingAction(InputGlyphDatabaseWindow owner, string pathsPropertyPath)
        {
            _owner = owner;
            _pathsPropertyPath = pathsPropertyPath;
        }

        public void Invoke()
        {
            _owner.AddControlPathAndApply(_pathsPropertyPath, string.Empty);
        }
    }

    private sealed class RemoveBindingAction
    {
        private readonly InputGlyphDatabaseWindow _owner;
        private readonly string _pathsPropertyPath;
        private readonly int _index;

        public RemoveBindingAction(InputGlyphDatabaseWindow owner, string pathsPropertyPath, int index)
        {
            _owner = owner;
            _pathsPropertyPath = pathsPropertyPath;
            _index = index;
        }

        public void Invoke()
        {
            _owner.RemoveControlPath(_pathsPropertyPath, _index);
        }
    }

    private readonly struct SpritePreviewContext
    {
        public readonly InputGlyphDatabaseWindow Owner;
        public readonly string PropertyPath;
        public readonly Image Preview;
        public readonly Label EmptyLabel;

        public SpritePreviewContext(InputGlyphDatabaseWindow owner, string propertyPath, Image preview, Label emptyLabel)
        {
            Owner = owner;
            PropertyPath = propertyPath;
            Preview = preview;
            EmptyLabel = emptyLabel;
        }
    }

    private sealed class InputControlPathElement : IMGUIContainer
    {
        private readonly SerializedObject _serializedObject;
        private readonly string _propertyPath;
        private readonly Action _onModified;
        private readonly InputControlPickerState _pickerState;
        private InputControlPathEditor _editor;

        public InputControlPathElement(SerializedObject serializedObject, string propertyPath, Action onModified)
        {
            _serializedObject = serializedObject;
            _propertyPath = propertyPath;
            _onModified = onModified;
            _pickerState = new InputControlPickerState();
            onGUIHandler = DrawPathEditor;
            RegisterCallback<DetachFromPanelEvent>(OnDetachedFromPanel);
        }

        private void DrawPathEditor()
        {
            if (_serializedObject == null)
            {
                return;
            }

            _serializedObject.Update();
            SerializedProperty property = _serializedObject.FindProperty(_propertyPath);
            if (property == null)
            {
                return;
            }

            if (_editor == null)
            {
                _editor = new InputControlPathEditor(property, _pickerState, _onModified, GUIContent.none);
            }

            Rect rect = GUILayoutUtility.GetRect(0f, EditorGUIUtility.singleLineHeight, GUILayout.ExpandWidth(true));
            float oldLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 0f;
            _editor.OnGUI(rect, GUIContent.none, property, _onModified);
            EditorGUIUtility.labelWidth = oldLabelWidth;
        }

        private void OnDetachedFromPanel(DetachFromPanelEvent evt)
        {
            _editor?.Dispose();
            _editor = null;
        }
    }
}
#endif
