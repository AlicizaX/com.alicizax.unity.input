#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class InputDefineGeneratorWindow : EditorWindow
{
    private const string MenuPath = "AlicizaX/Extension/Input/Generate Input Define";
    private const string SelectionMenuPath = "Assets/AlicizaX/Input/Generate Input Define";
    private const string DefaultOutputPath = "Assets/Generated/InputDefine.cs";
    private const string DefaultClassName = "InputDefine";

    [SerializeField] private InputActionAsset actions;
    [SerializeField] private string outputPath = DefaultOutputPath;
    [SerializeField] private string className = DefaultClassName;
    [SerializeField] private string namespaceName;
    [SerializeField] private bool includeButtonHelpers = true;
    [SerializeField] private bool includeActionProperties = true;

    [MenuItem(MenuPath, false, 81)]
    private static void Open()
    {
        InputDefineGeneratorWindow window = GetWindow<InputDefineGeneratorWindow>("Input Define");
        if (Selection.activeObject is InputActionAsset selectedActions)
        {
            window.actions = selectedActions;
        }

        window.Show();
    }

    [MenuItem(SelectionMenuPath, true)]
    private static bool ValidateGenerateFromSelection()
    {
        return Selection.activeObject is InputActionAsset;
    }

    [MenuItem(SelectionMenuPath, false, 81)]
    private static void GenerateFromSelection()
    {
        InputDefineGeneratorWindow window = GetWindow<InputDefineGeneratorWindow>("Input Define");
        window.actions = Selection.activeObject as InputActionAsset;
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Input Define Generator", EditorStyles.boldLabel);
        EditorGUILayout.Space(4f);

        actions = (InputActionAsset)EditorGUILayout.ObjectField("Input Actions", actions, typeof(InputActionAsset), false);
        className = EditorGUILayout.TextField("Class Name", className);
        namespaceName = EditorGUILayout.TextField("Namespace", namespaceName);
        includeActionProperties = EditorGUILayout.Toggle("Action Properties", includeActionProperties);
        includeButtonHelpers = EditorGUILayout.Toggle("Button Helpers", includeButtonHelpers);

        EditorGUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        outputPath = EditorGUILayout.TextField("Output Path", outputPath);
        if (GUILayout.Button("Browse", GUILayout.Width(72f)))
        {
            string selectedPath = EditorUtility.SaveFilePanelInProject(
                "Generate Input Define",
                string.IsNullOrWhiteSpace(className) ? DefaultClassName : className,
                "cs",
                "Choose where to generate the input define class.",
                string.IsNullOrWhiteSpace(outputPath) ? "Assets" : Path.GetDirectoryName(outputPath));

            if (!string.IsNullOrEmpty(selectedPath))
            {
                outputPath = selectedPath;
            }
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(8f);
        using (new EditorGUI.DisabledScope(actions == null))
        {
            if (GUILayout.Button("Generate", GUILayout.Height(28f)))
            {
                Generate();
            }
        }

        if (actions == null)
        {
            EditorGUILayout.HelpBox("Assign an InputActionAsset or select a .inputactions asset and open this window.", MessageType.Info);
            return;
        }

        EditorGUILayout.HelpBox(
            "The generated class reads actions through UXInput.Reader and toggles maps through IInputActionProvider.Actions.",
            MessageType.None);
    }

    private void Generate()
    {
        if (actions == null)
        {
            EditorUtility.DisplayDialog("Input Define Generator", "InputActionAsset is required.", "OK");
            return;
        }

        string resolvedClassName = InputDefineCodeGenerator.ToSafeIdentifier(className, true);
        if (string.IsNullOrWhiteSpace(resolvedClassName))
        {
            EditorUtility.DisplayDialog("Input Define Generator", "Class name is invalid.", "OK");
            return;
        }

        string resolvedPath = NormalizeAssetPath(outputPath);
        if (string.IsNullOrWhiteSpace(resolvedPath)
            || !resolvedPath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || !IsProjectRelativePath(resolvedPath))
        {
            EditorUtility.DisplayDialog("Input Define Generator", "Output path must be a .cs file under Assets or Packages.", "OK");
            return;
        }

        InputDefineCodeGenerator.Options options = new InputDefineCodeGenerator.Options
        {
            ClassName = resolvedClassName,
            NamespaceName = namespaceName,
            IncludeActionProperties = includeActionProperties,
            IncludeButtonHelpers = includeButtonHelpers,
        };

        string code = InputDefineCodeGenerator.Generate(actions, options);
        string directory = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(resolvedPath, code, new UTF8Encoding(false));
        AssetDatabase.ImportAsset(resolvedPath);
        AssetDatabase.Refresh();

        UnityEngine.Object generatedAsset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(resolvedPath);
        if (generatedAsset != null)
        {
            EditorGUIUtility.PingObject(generatedAsset);
        }
    }

    private static string NormalizeAssetPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return DefaultOutputPath;
        }

        return path.Replace('\\', '/');
    }

    private static bool IsProjectRelativePath(string path)
    {
        return path.StartsWith("Assets/", StringComparison.Ordinal)
               || path.StartsWith("Packages/", StringComparison.Ordinal);
    }
}

internal static class InputDefineCodeGenerator
{
    private static readonly HashSet<string> CSharpKeywords = new HashSet<string>
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is",
        "lock", "long", "namespace", "new", "null", "object", "operator", "out", "override",
        "params", "private", "protected", "public", "readonly", "ref", "return", "sbyte",
        "sealed", "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
        "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe",
        "ushort", "using", "virtual", "void", "volatile", "while"
    };

    public struct Options
    {
        public string ClassName;
        public string NamespaceName;
        public bool IncludeActionProperties;
        public bool IncludeButtonHelpers;
    }

    private enum AccessorKind
    {
        Value,
        Button,
        Pressed
    }

    public static string Generate(InputActionAsset actions, Options options)
    {
        string className = ToSafeIdentifier(options.ClassName, true);
        string namespaceName = NormalizeNamespace(options.NamespaceName);
        StringBuilder builder = new StringBuilder(8192);

        builder.AppendLine("// <auto-generated>");
        builder.Append("// Generated by ");
        builder.Append(nameof(InputDefineGeneratorWindow));
        builder.Append(" from ");
        builder.Append(actions != null ? actions.name : "InputActionAsset");
        builder.AppendLine(".");
        builder.AppendLine("//");
        builder.AppendLine("// 按钮辅助接口说明：");
        builder.AppendLine("// - 持续：无后缀属性，按钮按住期间每帧为真；值类型输入返回当前向量、浮点数等。");
        builder.AppendLine("// - 单次：单次方法只在按下当帧为真；松开后再次按下可再次触发。");
        builder.AppendLine("// - 开关：开关方法每次按下都会切换并保存一个布尔状态。");
        builder.AppendLine("// - 重置：重置方法用于清除字符串键版本保存的开关状态。");
        builder.AppendLine("// </auto-generated>");
        builder.AppendLine("using AlicizaX;");
        builder.AppendLine("using UnityEngine;");
        builder.AppendLine("using UnityEngine.InputSystem;");
        builder.AppendLine();

        bool hasNamespace = !string.IsNullOrEmpty(namespaceName);
        if (hasNamespace)
        {
            builder.Append("namespace ");
            builder.AppendLine(namespaceName);
            builder.AppendLine("{");
        }

        AppendIndent(builder, hasNamespace ? 1 : 0);
        builder.Append("public static class ");
        builder.AppendLine(className);
        AppendIndent(builder, hasNamespace ? 1 : 0);
        builder.AppendLine("{");

        AppendHelpers(builder, hasNamespace ? 2 : 1);
        AppendMaps(builder, actions, options, hasNamespace ? 2 : 1);

        AppendIndent(builder, hasNamespace ? 1 : 0);
        builder.AppendLine("}");

        if (hasNamespace)
        {
            builder.AppendLine("}");
        }

        return builder.ToString();
    }

    public static string ToSafeIdentifier(string value, bool pascalCase)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        StringBuilder builder = new StringBuilder(value.Length);
        bool capitalizeNext = pascalCase;
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            bool valid = char.IsLetterOrDigit(c) || c == '_';
            if (!valid)
            {
                capitalizeNext = true;
                continue;
            }

            if (builder.Length == 0 && char.IsDigit(c))
            {
                builder.Append('_');
            }

            if (capitalizeNext)
            {
                builder.Append(char.ToUpperInvariant(c));
                capitalizeNext = false;
            }
            else
            {
                builder.Append(c);
            }
        }

        string identifier = builder.ToString();
        if (identifier.Length == 0)
        {
            return string.Empty;
        }

        if (CSharpKeywords.Contains(identifier))
        {
            identifier += "_";
        }

        return identifier;
    }

    private static void AppendHelpers(StringBuilder builder, int indent)
    {
        AppendIndent(builder, indent);
        builder.AppendLine("private static InputActionAsset Actions");
        AppendIndent(builder, indent);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("get");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("if (!AppServices.TryGet(out IInputActionProvider provider))");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 3);
        builder.AppendLine("return null;");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("}");
        builder.AppendLine();
        AppendIndent(builder, indent + 2);
        builder.AppendLine("return provider.Actions;");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("}");
        AppendIndent(builder, indent);
        builder.AppendLine("}");
        builder.AppendLine();

        AppendIndent(builder, indent);
        builder.AppendLine("private static InputActionMap FindActionMap(string mapName)");
        AppendIndent(builder, indent);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("InputActionAsset actions = Actions;");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("return actions != null ? actions.FindActionMap(mapName, false) : null;");
        AppendIndent(builder, indent);
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendMaps(StringBuilder builder, InputActionAsset actions, Options options, int indent)
    {
        if (actions == null)
        {
            return;
        }

        HashSet<string> mapNames = new HashSet<string>(StringComparer.Ordinal);
        for (int mapIndex = 0; mapIndex < actions.actionMaps.Count; mapIndex++)
        {
            InputActionMap map = actions.actionMaps[mapIndex];
            string mapClassName = MakeUnique(ToSafeIdentifier(map.name, true), mapNames);
            if (string.IsNullOrEmpty(mapClassName))
            {
                mapClassName = MakeUnique("ActionMap", mapNames);
            }

            AppendMap(builder, map, mapClassName, options, indent);
            if (mapIndex + 1 < actions.actionMaps.Count)
            {
                builder.AppendLine();
            }
        }
    }

    private static void AppendMap(StringBuilder builder, InputActionMap map, string mapClassName, Options options, int indent)
    {
        HashSet<string> usedNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Enable",
            "Map",
            "MapName"
        };

        AppendIndent(builder, indent);
        builder.Append("public static class ");
        builder.AppendLine(mapClassName);
        AppendIndent(builder, indent);
        builder.AppendLine("{");

        AppendIndent(builder, indent + 1);
        builder.Append("public const string MapName = ");
        AppendStringLiteral(builder, map.name);
        builder.AppendLine(";");
        builder.AppendLine();

        AppendMapEnableProperty(builder, indent + 1);

        for (int actionIndex = 0; actionIndex < map.actions.Count; actionIndex++)
        {
            InputAction action = map.actions[actionIndex];
            builder.AppendLine();
            AppendAction(builder, map, action, options, usedNames, indent + 1);
        }

        AppendIndent(builder, indent);
        builder.AppendLine("}");
    }

    private static void AppendMapEnableProperty(StringBuilder builder, int indent)
    {
        AppendIndent(builder, indent);
        builder.AppendLine("public static InputActionMap Map");
        AppendIndent(builder, indent);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("get");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("return FindActionMap(MapName);");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("}");
        AppendIndent(builder, indent);
        builder.AppendLine("}");
        builder.AppendLine();

        AppendIndent(builder, indent);
        builder.AppendLine("public static bool Enable");
        AppendIndent(builder, indent);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("get");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("InputActionMap map = Map;");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("return map != null && map.enabled;");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("}");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("set");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("InputActionMap map = Map;");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("if (map == null)");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 3);
        builder.AppendLine("return;");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("}");
        builder.AppendLine();
        AppendIndent(builder, indent + 2);
        builder.AppendLine("if (value)");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 3);
        builder.AppendLine("map.Enable();");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("}");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("else");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("{");
        AppendIndent(builder, indent + 3);
        builder.AppendLine("map.Disable();");
        AppendIndent(builder, indent + 2);
        builder.AppendLine("}");
        AppendIndent(builder, indent + 1);
        builder.AppendLine("}");
        AppendIndent(builder, indent);
        builder.AppendLine("}");
    }

    private static void AppendAction(
        StringBuilder builder,
        InputActionMap map,
        InputAction action,
        Options options,
        HashSet<string> usedNames,
        int indent)
    {
        string baseName = MakeUnique(ToSafeIdentifier(action.name, true), usedNames);
        if (string.IsNullOrEmpty(baseName))
        {
            baseName = MakeUnique("Action", usedNames);
        }

        string nameConstant = MakeUnique(baseName + "Name", usedNames);
        string pathConstant = MakeUnique(baseName + "Path", usedNames);
        string actionProperty = MakeUnique(baseName + "Action", usedNames);
        string fullPath = map.name + "/" + action.name;

        AppendIndent(builder, indent);
        builder.Append("/// <summary>Input action ");
        builder.Append(EscapeXml(fullPath));
        builder.AppendLine(".</summary>");
        AppendIndent(builder, indent);
        builder.Append("public const string ");
        builder.Append(nameConstant);
        builder.Append(" = ");
        AppendStringLiteral(builder, action.name);
        builder.AppendLine(";");

        AppendIndent(builder, indent);
        builder.Append("public const string ");
        builder.Append(pathConstant);
        builder.Append(" = ");
        AppendStringLiteral(builder, fullPath);
        builder.AppendLine(";");

        if (options.IncludeActionProperties)
        {
            AppendIndent(builder, indent);
            builder.Append("public static InputAction ");
            builder.Append(actionProperty);
            builder.Append(" => UXInput.Reader.ResolveAction(");
            builder.Append(pathConstant);
            builder.AppendLine(");");
        }

        AccessorKind accessorKind = ResolveAccessorKind(action, out string valueType);
        AppendIndent(builder, indent);
        builder.Append("public static ");
        builder.Append(accessorKind == AccessorKind.Value ? valueType : "bool");
        builder.Append(' ');
        builder.Append(baseName);
        builder.Append(" => ");
        switch (accessorKind)
        {
            case AccessorKind.Button:
                builder.Append("UXInput.Reader.ReadButton(");
                break;
            case AccessorKind.Pressed:
                builder.Append("UXInput.Reader.ReadPressed(");
                break;
            default:
                builder.Append("UXInput.Reader.ReadValue<");
                builder.Append(valueType);
                builder.Append(">(");
                break;
        }

        builder.Append(pathConstant);
        builder.AppendLine(");");

        if (options.IncludeButtonHelpers && accessorKind != AccessorKind.Value)
        {
            AppendButtonHelpers(builder, pathConstant, baseName, accessorKind == AccessorKind.Button, usedNames, indent);
        }
    }

    private static void AppendButtonHelpers(
        StringBuilder builder,
        string pathConstant,
        string baseName,
        bool strictButton,
        HashSet<string> usedNames,
        int indent)
    {
        string onceMethod = MakeUnique(baseName + "Once", usedNames);
        string toggleMethod = MakeUnique(baseName + "Toggle", usedNames);
        string resetToggleMethod = MakeUnique("Reset" + baseName + "Toggle", usedNames);
        string onceReader = strictButton ? "ReadButtonOnce" : "ReadPressedOnce";
        string toggleReader = strictButton ? "ReadButtonToggle" : "ReadPressedToggle";

        AppendIndent(builder, indent);
        builder.Append("public static bool ");
        builder.Append(onceMethod);
        builder.Append("(UnityEngine.Object owner) => UXInput.Reader.");
        builder.Append(onceReader);
        builder.Append("(owner, ");
        builder.Append(pathConstant);
        builder.AppendLine(");");

        AppendIndent(builder, indent);
        builder.Append("public static bool ");
        builder.Append(onceMethod);
        builder.Append("(string key) => UXInput.Reader.");
        builder.Append(onceReader);
        builder.Append("(key, ");
        builder.Append(pathConstant);
        builder.AppendLine(");");

        AppendIndent(builder, indent);
        builder.Append("public static bool ");
        builder.Append(toggleMethod);
        builder.Append("(UnityEngine.Object owner) => UXInput.Reader.");
        builder.Append(toggleReader);
        builder.Append("(owner, ");
        builder.Append(pathConstant);
        builder.AppendLine(");");

        AppendIndent(builder, indent);
        builder.Append("public static bool ");
        builder.Append(toggleMethod);
        builder.Append("(string key) => UXInput.Reader.");
        builder.Append(toggleReader);
        builder.Append("(key, ");
        builder.Append(pathConstant);
        builder.AppendLine(");");

        AppendIndent(builder, indent);
        builder.Append("public static void ");
        builder.Append(resetToggleMethod);
        builder.Append("(string key) => UXInput.Reader.ResetToggledButton(key, ");
        builder.Append(pathConstant);
        builder.AppendLine(");");
    }

    private static AccessorKind ResolveAccessorKind(InputAction action, out string valueType)
    {
        string expectedType = NormalizeExpectedType(action.expectedControlType);
        if (IsButtonExpectedType(expectedType))
        {
            valueType = "bool";
            return action.type == InputActionType.Button ? AccessorKind.Button : AccessorKind.Pressed;
        }

        if (TryResolveValueType(action, expectedType, out valueType))
        {
            return AccessorKind.Value;
        }

        if (action.type == InputActionType.Button)
        {
            valueType = "bool";
            return AccessorKind.Button;
        }

        valueType = ResolveValueType(action, expectedType);
        return AccessorKind.Value;
    }

    private static string ResolveValueType(InputAction action, string expectedType)
    {
        return TryResolveValueType(action, expectedType, out string valueType) ? valueType : "float";
    }

    private static bool TryResolveValueType(InputAction action, string expectedType, out string valueType)
    {
        switch (expectedType)
        {
            case "Vector2":
                valueType = "Vector2";
                return true;
            case "Vector3":
                valueType = "Vector3";
                return true;
            case "Quaternion":
                valueType = "Quaternion";
                return true;
            case "Integer":
                valueType = "int";
                return true;
            case "Double":
                valueType = "double";
                return true;
            case "Axis":
            case "Analog":
            case "Float":
                valueType = "float";
                return true;
        }

        if (HasComposite(action, "2DVector") || HasPathFragment(action, "leftStick") || HasPathFragment(action, "rightStick"))
        {
            valueType = "Vector2";
            return true;
        }

        if (HasComposite(action, "Axis") || HasPathFragment(action, "trigger"))
        {
            valueType = "float";
            return true;
        }

        valueType = null;
        return false;
    }

    private static bool IsButtonExpectedType(string expectedType)
    {
        return expectedType == "Button" || expectedType == "Key";
    }

    private static string NormalizeExpectedType(string expectedType)
    {
        if (string.IsNullOrWhiteSpace(expectedType))
        {
            return string.Empty;
        }

        int lastSlash = expectedType.LastIndexOf('/');
        if (lastSlash >= 0 && lastSlash + 1 < expectedType.Length)
        {
            expectedType = expectedType.Substring(lastSlash + 1);
        }

        return expectedType.Replace(" ", string.Empty);
    }

    private static bool HasComposite(InputAction action, string compositeName)
    {
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (binding.isComposite && ContainsLooseIgnoreCase(binding.name, compositeName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasPathFragment(InputAction action, string pathFragment)
    {
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (ContainsIgnoreCase(binding.path, pathFragment) || ContainsIgnoreCase(binding.effectivePath, pathFragment))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsIgnoreCase(string value, string fragment)
    {
        return !string.IsNullOrEmpty(value)
               && !string.IsNullOrEmpty(fragment)
               && value.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool ContainsLooseIgnoreCase(string value, string fragment)
    {
        return ContainsIgnoreCase(RemoveWhitespace(value), RemoveWhitespace(fragment));
    }

    private static string RemoveWhitespace(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        StringBuilder builder = null;
        for (int i = 0; i < value.Length; i++)
        {
            if (!char.IsWhiteSpace(value[i]))
            {
                builder?.Append(value[i]);
                continue;
            }

            if (builder == null)
            {
                builder = new StringBuilder(value.Length);
                builder.Append(value, 0, i);
            }
        }

        return builder != null ? builder.ToString() : value;
    }

    private static string NormalizeNamespace(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string[] parts = value.Split('.');
        StringBuilder builder = new StringBuilder(value.Length);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = ToSafeIdentifier(parts[i], false);
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            builder.Append(part);
        }

        return builder.ToString();
    }

    private static string MakeUnique(string identifier, HashSet<string> usedNames)
    {
        if (string.IsNullOrEmpty(identifier))
        {
            identifier = "Value";
        }

        string unique = identifier;
        int suffix = 2;
        while (!usedNames.Add(unique))
        {
            unique = identifier + suffix.ToString();
            suffix++;
        }

        return unique;
    }

    private static void AppendStringLiteral(StringBuilder builder, string value)
    {
        builder.Append('"');
        if (!string.IsNullOrEmpty(value))
        {
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\':
                        builder.Append("\\\\");
                        break;
                    case '"':
                        builder.Append("\\\"");
                        break;
                    case '\r':
                        builder.Append("\\r");
                        break;
                    case '\n':
                        builder.Append("\\n");
                        break;
                    case '\t':
                        builder.Append("\\t");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }
        }

        builder.Append('"');
    }

    private static string EscapeXml(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    private static void AppendIndent(StringBuilder builder, int indent)
    {
        for (int i = 0; i < indent; i++)
        {
            builder.Append("    ");
        }
    }
}
#endif
