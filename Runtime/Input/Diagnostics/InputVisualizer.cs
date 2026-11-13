#if INPUTSYSTEM_SUPPORT && (UNITY_EDITOR || DEVELOPMENT_BUILD)
using System;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    /// <summary>
    /// 显示输入动作、设备活动和输入上下文变化的运行时诊断组件。
    /// </summary>
    [AddComponentMenu("Input/Input Visualizer")]
    public sealed class InputVisualizer : MonoBehaviour
    {
        private const int DefaultHistoryCapacity = 16;
        private const string NoDeviceName = "No Device";
        private const string WatchActionName = "UXInput.Watch.AnyInput";
        private static readonly string[] ReadKindNames = Enum.GetNames(typeof(InputReader.ReadTraceKind));
        private static readonly string[] InputTypeNames = Enum.GetNames(typeof(InputWatch.InputType));
        private static readonly string[] InputProfileNames = Enum.GetNames(typeof(InputWatch.InputProfile));

        [Header("Sources")]
        [SerializeField] private bool logInputActions = true;
        [SerializeField] private bool logDeviceActivity = true;
        [SerializeField] private bool logContextChanges = true;
        [SerializeField] private bool logPointerMovementActions;
        [SerializeField] private bool logRepeatedDeviceActivity;
        [SerializeField] private float repeatedDeviceActivityInterval = 0.5f;
        [SerializeField] private bool logReaderReads = true;
        [SerializeField] private bool logReaderContinuousReads;

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
        [SerializeField] private bool mergeDuplicateEntries = true;
        [SerializeField] private float duplicateMergeWindow = 0.05f;

        [Header("Style")]
        [SerializeField] private Color backgroundColor = new Color(0f, 0f, 0f, 0.72f);
        [SerializeField] private Color textColor = Color.white;
        [SerializeField] private Color activeColor = new Color(0.4f, 1f, 0.45f, 1f);
        [SerializeField] private Color contextColor = new Color(0.45f, 0.8f, 1f, 1f);

        private readonly StringBuilder _textBuilder = new StringBuilder(128);
        private HistoryEntry[] _entries = new HistoryEntry[DefaultHistoryCapacity];
        private int _entryCount;
        private GUIStyle _backgroundStyle;
        private GUIStyle _labelStyle;
        private GUIStyle _activeStyle;
        private GUIStyle _contextStyle;
        private Texture2D _backgroundTexture;
        private bool _stylesInitialized;
        private InputWatch _watch;
        private InputReader _reader;
        private bool _hasLastActivityLog;
        private InputWatch.InputProfile _lastActivityProfile;
        private string _lastActivityDeviceName;
        private float _lastActivityLogTime;

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
            ResizeHistory();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            maxHistoryEntries = Mathf.Max(1, maxHistoryEntries);
            entryLifetime = Mathf.Max(0.01f, entryLifetime);
            entryHeight = Mathf.Max(8f, entryHeight);
            panelWidth = Mathf.Max(80f, panelWidth);
            fontSize = Mathf.Max(8, fontSize);
            repeatedDeviceActivityInterval = Mathf.Max(0f, repeatedDeviceActivityInterval);
            duplicateMergeWindow = Mathf.Max(0f, duplicateMergeWindow);
            _stylesInitialized = false;
        }
#endif

        private void OnEnable()
        {
            Debug.Assert(UXInput.IsInitialized, "InputVisualizer requires UXInput to be initialized before OnEnable.", this);
            if (!UXInput.IsInitialized)
            {
                return;
            }

            _watch = UXInput.Watch;
            _reader = UXInput.Reader;

            if (logInputActions)
            {
                InputSystem.onActionChange += HandleActionChange;
            }

            if (logDeviceActivity)
            {
                _watch.OnInputActivity += HandleInputActivity;
            }

            if (logContextChanges)
            {
                _watch.OnContextChanged += HandleContextChanged;
            }

            if (logReaderReads)
            {
                _reader.OnRead += HandleReaderRead;
            }

            if (logReaderContinuousReads)
            {
                _reader.OnContinuousRead += HandleReaderRead;
            }
        }

        private void OnDisable()
        {
            if (_watch == null)
            {
                return;
            }

            // 退订订阅时的实例：UXInput 关闭后全局属性为 null。
            InputSystem.onActionChange -= HandleActionChange;
            _watch.OnInputActivity -= HandleInputActivity;
            _watch.OnContextChanged -= HandleContextChanged;
            _reader.OnRead -= HandleReaderRead;
            _reader.OnContinuousRead -= HandleReaderRead;
            _watch = null;
            _reader = null;
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
                string text = GetEntryText(i);
                HistoryEntry entry = _entries[i];
                Rect entryRect = new Rect(screenPosition.x + 6f, y, panelWidth - 12f, entryHeight);
                float alpha = Mathf.Clamp01(1f - ((currentTime - entry.Timestamp) / Mathf.Max(0.01f, entryLifetime)));

                GUIStyle style = GetStyle(entry.Kind);

                Color originalColor = GUI.color;
                GUI.color = new Color(1f, 1f, 1f, alpha);
                GUI.Label(entryRect, text, style);
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

            if (!logPointerMovementActions && IsPointerMovementAction(action))
            {
                return;
            }

            string deviceName = action.activeControl != null && action.activeControl.device != null
                ? action.activeControl.device.displayName
                : NoDeviceName;

            if (string.IsNullOrWhiteSpace(deviceName) && action.activeControl?.device != null)
            {
                deviceName = action.activeControl.device.name;
            }

            AddEntry(new HistoryEntry
            {
                Source = EntrySource.Action,
                Action = action,
                DeviceName = deviceName,
                Kind = EntryKind.Active
            });
        }

        private void HandleInputActivity(InputWatch.InputContext context)
        {
            if (!ShouldLogInputActivity(context))
            {
                return;
            }

            AddEntry(new HistoryEntry
            {
                Source = EntrySource.Activity,
                InputProfile = context.InputProfile,
                DeviceName = context.DeviceName,
                Kind = EntryKind.Normal
            });
        }

        private void HandleContextChanged(InputWatch.InputContext context)
        {
            AddEntry(new HistoryEntry
            {
                Source = EntrySource.Context,
                InputType = context.InputType,
                InputProfile = context.InputProfile,
                Kind = EntryKind.Context
            });
        }

        private void HandleReaderRead(InputReader.ReadTrace trace)
        {
            if (trace.Action == null)
            {
                return;
            }

            AddEntry(new HistoryEntry
            {
                Source = EntrySource.Reader,
                Action = trace.Action,
                ReadKind = trace.Kind,
                CompositePartName = trace.CompositePartName,
                BoolValue = IsToggleTrace(trace.Kind) && trace.BoolValue,
                Kind = EntryKind.Active
            });
        }

        private static bool IsToggleTrace(InputReader.ReadTraceKind kind)
        {
            return kind == InputReader.ReadTraceKind.Toggle
                   || kind == InputReader.ReadTraceKind.CompositePartToggle;
        }

        private bool ShouldLogInputActivity(InputWatch.InputContext context)
        {
            float currentTime = Time.unscaledTime;
            if (!_hasLastActivityLog
                || context.InputProfile != _lastActivityProfile
                || !string.Equals(context.DeviceName, _lastActivityDeviceName, StringComparison.Ordinal))
            {
                _hasLastActivityLog = true;
                _lastActivityProfile = context.InputProfile;
                _lastActivityDeviceName = context.DeviceName;
                _lastActivityLogTime = currentTime;
                return true;
            }

            if (!logRepeatedDeviceActivity)
            {
                return false;
            }

            if (currentTime - _lastActivityLogTime < repeatedDeviceActivityInterval)
            {
                return false;
            }

            _lastActivityLogTime = currentTime;
            return true;
        }

        private static bool IsPointerMovementAction(InputAction action)
        {
            return IsPointerMovementControl(action.activeControl);
        }

        private static bool IsPointerMovementControl(InputControl control)
        {
            if (control == null)
            {
                return false;
            }

            string controlName = control.name;
            if (!string.Equals(controlName, "delta", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(controlName, "position", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return control.device is Pointer
                   || control.device is Mouse
                   || control.device is Touchscreen;
        }

        private void AddEntry(string text, EntryKind kind)
        {
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            AddEntry(new HistoryEntry { Message = text, Kind = kind });
        }

        private void AddEntry(HistoryEntry entry)
        {
            ResizeHistory();
            if (TryMergeDuplicateEntry(entry))
            {
                return;
            }

            while (_entryCount >= maxHistoryEntries)
            {
                RemoveAt(0);
            }

            entry.Timestamp = Time.unscaledTime;
            _entries[_entryCount] = entry;
            _entryCount++;
        }

        private bool TryMergeDuplicateEntry(in HistoryEntry candidate)
        {
            if (!mergeDuplicateEntries || duplicateMergeWindow <= 0f)
            {
                return false;
            }

            float currentTime = Time.unscaledTime;
            for (int i = _entryCount - 1; i >= 0; i--)
            {
                HistoryEntry entry = _entries[i];
                if (currentTime - entry.Timestamp > duplicateMergeWindow)
                {
                    break;
                }

                if (entry.Source != candidate.Source || entry.Kind != candidate.Kind
                    || entry.Action != candidate.Action || entry.ReadKind != candidate.ReadKind
                    || entry.BoolValue != candidate.BoolValue || entry.InputType != candidate.InputType
                    || entry.InputProfile != candidate.InputProfile
                    || !string.Equals(entry.Message, candidate.Message, StringComparison.Ordinal)
                    || !string.Equals(entry.DeviceName, candidate.DeviceName, StringComparison.Ordinal)
                    || !string.Equals(entry.CompositePartName, candidate.CompositePartName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (showTimestamps && entry.Timestamp != currentTime) _entries[i].Text = null;
                _entries[i].Timestamp = currentTime;
                return true;
            }

            return false;
        }

        private string GetEntryText(int index)
        {
            ref HistoryEntry entry = ref _entries[index];
            if (entry.Text != null) return entry.Text;
            if (entry.Source == EntrySource.Message && !showTimestamps) return entry.Text = entry.Message;

            _textBuilder.Clear();
            if (showTimestamps)
            {
                Span<char> timestamp = stackalloc char[48];
                entry.Timestamp.TryFormat(timestamp, out int length, "0.00");
                _textBuilder.Append('[').Append(timestamp.Slice(0, length)).Append("] ");
            }
            switch (entry.Source)
            {
                case EntrySource.Message:
                    _textBuilder.Append(entry.Message);
                    break;
                case EntrySource.Action:
                    AppendActionName(entry.Action);
                    _textBuilder.Append(" [").Append(entry.DeviceName).Append(']');
                    break;
                case EntrySource.Activity:
                    _textBuilder.Append("Activity: ").Append(InputProfileNames[(int)entry.InputProfile])
                        .Append(" / ").Append(entry.DeviceName);
                    break;
                case EntrySource.Context:
                    _textBuilder.Append("Context: ").Append(InputTypeNames[(int)entry.InputType])
                        .Append(" / ").Append(InputProfileNames[(int)entry.InputProfile]);
                    break;
                case EntrySource.Reader:
                    _textBuilder.Append("Reader: ").Append(ReadKindNames[(int)entry.ReadKind]).Append(' ');
                    AppendActionName(entry.Action);
                    if (!string.IsNullOrEmpty(entry.CompositePartName))
                        _textBuilder.Append('/').Append(entry.CompositePartName);
                    if (IsToggleTrace(entry.ReadKind))
                        _textBuilder.Append(entry.BoolValue ? " = True" : " = False");
                    break;
            }
            return entry.Text = _textBuilder.ToString();
        }

        private void AppendActionName(InputAction action)
        {
            string mapName = action.actionMap?.name;
            if (!string.IsNullOrEmpty(mapName)) _textBuilder.Append(mapName).Append('/');
            _textBuilder.Append(action.name);
        }

        private void RemoveAt(int index)
        {
            int moveCount = _entryCount - index - 1;
            if (moveCount > 0)
            {
                Array.Copy(_entries, index + 1, _entries, index, moveCount);
            }

            _entryCount--;
            _entries[_entryCount] = default;
        }

        private void ResizeHistory()
        {
            maxHistoryEntries = Mathf.Max(1, maxHistoryEntries);
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

            if (_backgroundTexture != null)
            {
                Destroy(_backgroundTexture);
                _backgroundTexture = null;
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
            internal string Message;
            internal string DeviceName;
            internal string CompositePartName;
            internal InputAction Action;
            internal InputReader.ReadTraceKind ReadKind;
            internal InputWatch.InputType InputType;
            internal InputWatch.InputProfile InputProfile;
            internal bool BoolValue;
            internal float Timestamp;
            internal EntryKind Kind;
            internal EntrySource Source;
        }

        private enum EntrySource : byte
        {
            Message,
            Action,
            Activity,
            Context,
            Reader
        }

        private enum EntryKind : byte
        {
            Normal,
            Active,
            Context
        }
    }
}
#endif
