#if INPUTSYSTEM_SUPPORT
using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 显示输入动作、设备活动和输入上下文变化的运行时诊断组件。
/// </summary>
[AddComponentMenu("Input/Input Visualizer")]
public sealed class InputVisualizer : MonoBehaviour
{
    private const int DefaultHistoryCapacity = 16;
    private const string NoDeviceName = "No Device";
    private const string WatchActionName = "UXInput.Watch.AnyInput";

    [Header("Sources")]
    [SerializeField] private bool logInputActions = true;
    [SerializeField] private bool logDeviceActivity = true;
    [SerializeField] private bool logContextChanges = true;

    [Header("Display")]
    [SerializeField] private bool showOnScreen = true;
    [SerializeField] private Vector2 screenPosition = new Vector2(12f, 12f);
    [SerializeField] private float panelWidth = 360f;
    [SerializeField] private float entryHeight = 20f;
    [SerializeField] private int fontSize = 14;

    [Header("History")]
    [SerializeField] private int maxHistoryEntries = 12;
    [SerializeField] private float entryLifetime = 2.5f;
    [SerializeField] private bool showTimestamps;

    [Header("Style")]
    [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.72f);
    [SerializeField] private Color textColor = Color.white;
    [SerializeField] private Color activeColor = new Color(0.4f, 1f, 0.45f, 1f);
    [SerializeField] private Color contextColor = new Color(0.45f, 0.8f, 1f, 1f);

    private readonly HistoryEntry[] _history = new HistoryEntry[DefaultHistoryCapacity];
    private HistoryEntry[] _entries;
    private int _entryCount;
    private GUIStyle _backgroundStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _activeStyle;
    private GUIStyle _contextStyle;
    private Texture2D _backgroundTexture;
    private bool _stylesInitialized;

    /// <summary>
    /// 获取或设置是否在屏幕上绘制输入历史。
    /// </summary>
    public bool ShowOnScreen
    {
        get => showOnScreen;
        set => showOnScreen = value;
    }

    /// <summary>
    /// 获取当前保留的输入历史条目数量。
    /// </summary>
    public int EntryCount => _entryCount;

    private void Awake()
    {
        _entries = _history;
        EnsureHistoryCapacity();
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        maxHistoryEntries = Mathf.Max(1, maxHistoryEntries);
        entryLifetime = Mathf.Max(0.01f, entryLifetime);
        entryHeight = Mathf.Max(8f, entryHeight);
        panelWidth = Mathf.Max(80f, panelWidth);
        fontSize = Mathf.Max(8, fontSize);
        _stylesInitialized = false;
    }
#endif

    private void OnEnable()
    {
        if (logInputActions)
        {
            InputSystem.onActionChange += HandleActionChange;
        }

        if (logDeviceActivity)
        {
            UXInput.Watch.OnInputActivity += HandleInputActivity;
        }

        if (logContextChanges)
        {
            UXInput.Watch.OnContextChanged += HandleContextChanged;
        }
    }

    private void OnDisable()
    {
        InputSystem.onActionChange -= HandleActionChange;
        UXInput.Watch.OnInputActivity -= HandleInputActivity;
        UXInput.Watch.OnContextChanged -= HandleContextChanged;
    }

    private void Update()
    {
        float currentTime = Time.unscaledTime;
        float lifetime = Mathf.Max(0.01f, entryLifetime);
        for (int i = _entryCount - 1; i >= 0; i--)
        {
            if (currentTime - _entries[i].Timestamp > lifetime)
            {
                RemoveAt(i);
            }
        }
    }

    private void OnGUI()
    {
        if (!showOnScreen || _entryCount == 0)
        {
            return;
        }

        InitializeStyles();

        float panelHeight = _entryCount * entryHeight + 10f;
        Rect panelRect = new Rect(screenPosition.x, screenPosition.y, panelWidth, panelHeight);
        GUI.Box(panelRect, GUIContent.none, _backgroundStyle);

        float y = screenPosition.y + 5f;
        float currentTime = Time.unscaledTime;
        for (int i = 0; i < _entryCount; i++)
        {
            HistoryEntry entry = _entries[i];
            Rect entryRect = new Rect(screenPosition.x + 6f, y, panelWidth - 12f, entryHeight);
            float alpha = Mathf.Clamp01(1f - ((currentTime - entry.Timestamp) / Mathf.Max(0.01f, entryLifetime)));

            GUIStyle style = GetStyle(entry.Kind);

            Color originalColor = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.Label(entryRect, entry.Text, style);
            GUI.color = originalColor;

            y += entryHeight;
        }
    }

    private void OnDestroy()
    {
        if (_backgroundTexture != null)
        {
            Destroy(_backgroundTexture);
            _backgroundTexture = null;
        }
    }

    /// <summary>
    /// 记录一条输入动作历史。
    /// </summary>
    /// <param name="actionName">动作名称或显示名称。</param>
    /// <param name="value">可选的输入值。</param>
    /// <param name="isActive">为 true 时使用激活样式显示。</param>
    public void LogInput(string actionName, object value = null, bool isActive = true)
    {
        string text = value == null
            ? actionName
            : string.Concat(actionName, ": ", value);

        AddEntry(text, isActive ? EntryKind.Active : EntryKind.Normal);
    }

    /// <summary>
    /// 记录一条普通诊断消息。
    /// </summary>
    /// <param name="message">要显示的消息文本。</param>
    public void LogMessage(string message)
    {
        AddEntry(message, EntryKind.Normal);
    }

    /// <summary>
    /// 清空当前输入历史。
    /// </summary>
    public void ClearHistory()
    {
        Array.Clear(_entries, 0, _entryCount);
        _entryCount = 0;
    }

    private void HandleActionChange(object obj, InputActionChange change)
    {
        if (change != InputActionChange.ActionPerformed)
        {
            return;
        }

        InputAction action = obj as InputAction;
        if (action == null)
        {
            return;
        }

        if (action.name == WatchActionName)
        {
            return;
        }

        string mapName = action.actionMap != null ? action.actionMap.name : string.Empty;
        string actionName = string.IsNullOrEmpty(mapName) ? action.name : string.Concat(mapName, "/", action.name);
        string deviceName = action.activeControl != null && action.activeControl.device != null
            ? action.activeControl.device.displayName
            : NoDeviceName;

        if (string.IsNullOrWhiteSpace(deviceName) && action.activeControl?.device != null)
        {
            deviceName = action.activeControl.device.name;
        }

        AddEntry(string.Concat(actionName, " [", deviceName, "]"), EntryKind.Active);
    }

    private void HandleInputActivity(UXInput.Watch.InputContext context)
    {
        AddEntry(string.Concat("Activity: ", context.InputProfile, " / ", context.DeviceName), EntryKind.Normal);
    }

    private void HandleContextChanged(UXInput.Watch.InputContext context)
    {
        AddEntry(string.Concat("Context: ", context.InputType, " / ", context.InputProfile), EntryKind.Context);
    }

    private void AddEntry(string text, EntryKind kind)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        EnsureHistoryCapacity();

        while (_entryCount >= maxHistoryEntries)
        {
            RemoveAt(0);
        }

        string displayText = showTimestamps
            ? string.Concat("[", Time.unscaledTime.ToString("0.00"), "] ", text)
            : text;

        _entries[_entryCount] = new HistoryEntry
        {
            Text = displayText,
            Timestamp = Time.unscaledTime,
            Kind = kind
        };
        _entryCount++;
    }

    private void RemoveAt(int index)
    {
        if (index < 0 || index >= _entryCount)
        {
            return;
        }

        int moveCount = _entryCount - index - 1;
        if (moveCount > 0)
        {
            Array.Copy(_entries, index + 1, _entries, index, moveCount);
        }

        _entryCount--;
        _entries[_entryCount] = default;
    }

    private void EnsureHistoryCapacity()
    {
        maxHistoryEntries = Mathf.Max(1, maxHistoryEntries);
        if (_entries == null)
        {
            _entries = _history;
        }

        if (_entries.Length >= maxHistoryEntries)
        {
            return;
        }

        int newCapacity = _entries.Length;
        while (newCapacity < maxHistoryEntries)
        {
            newCapacity <<= 1;
        }

        Array.Resize(ref _entries, newCapacity);
    }

    private void InitializeStyles()
    {
        if (_stylesInitialized)
        {
            return;
        }

        _backgroundTexture = MakeTexture(backgroundColor);
        _backgroundStyle = new GUIStyle(GUI.skin.box)
        {
            normal = { background = _backgroundTexture }
        };

        _labelStyle = CreateLabelStyle(textColor, FontStyle.Normal);
        _activeStyle = CreateLabelStyle(activeColor, FontStyle.Bold);
        _contextStyle = CreateLabelStyle(contextColor, FontStyle.Bold);
        _stylesInitialized = true;
    }

    private GUIStyle GetStyle(EntryKind kind)
    {
        switch (kind)
        {
            case EntryKind.Active:
                return _activeStyle;
            case EntryKind.Context:
                return _contextStyle;
            default:
                return _labelStyle;
        }
    }

    private GUIStyle CreateLabelStyle(Color color, FontStyle fontStyle)
    {
        return new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            clipping = TextClipping.Clip,
            normal = { textColor = color },
            fontStyle = fontStyle
        };
    }

    private static Texture2D MakeTexture(Color color)
    {
        Texture2D texture = new Texture2D(2, 2)
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        Color[] pixels = { color, color, color, color };
        texture.SetPixels(pixels);
        texture.Apply();
        return texture;
    }

    private struct HistoryEntry
    {
        internal string Text;
        internal float Timestamp;
        internal EntryKind Kind;
    }

    private enum EntryKind
    {
        Normal,
        Active,
        Context
    }
}
#endif
