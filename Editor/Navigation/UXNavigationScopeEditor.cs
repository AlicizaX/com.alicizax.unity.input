#if INPUTSYSTEM_SUPPORT
using System.Collections.Generic;
using AlicizaX.Editor.UIToolkit;
using AlicizaX.UI.Runtime;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;

namespace AlicizaX.UI.UXNavigation
{

    [CustomEditor(typeof(UXNavigationScope))]
    public sealed class UXNavigationScopeEditor : AlicizaUIEditor
    {
        private static readonly Color SelectedRowColor = new Color(0.173f, 0.365f, 0.529f, 0.32f);

        private readonly List<VisualElement> _bakedRows = new List<VisualElement>(16);

        private SerializedProperty _defaultSelectable;
        private SerializedProperty _holder;
        private SerializedProperty _bakedSelectables;
        private SerializedProperty _rememberLastSelection;
        private SerializedProperty _blockLowerScopes;
        private SerializedProperty _navigable;

        private VisualElement _root;
        private VisualElement _content;
        private VisualElement _defaultHost;
        private int _selectedBakedIndex = -1;
        private int _bakedRowCount;
        private bool _rebuilding;
        private bool _rebuildScheduled;
        private bool _defaultRefreshScheduled;

        private void OnEnable()
        {
            CacheProperties();
        }

        protected override void BuildInspector(VisualElement root)
        {
            _root = root;

            if (_defaultSelectable == null)
            {
                CacheProperties();
            }

            if (serializedObject.isEditingMultipleObjects)
            {
                AlicizaPanel unsupportedPanel = AlicizaEditorUI.CreatePanel(null);
                unsupportedPanel.Body.Add(AlicizaEditorUI.CreateHint(
                    "Multi-object editing is not supported for UX Navigation Scope."));
                root.Add(unsupportedPanel);
                return;
            }

            if (_defaultSelectable == null
                || _holder == null
                || _bakedSelectables == null
                || _rememberLastSelection == null
                || _navigable == null
                || _blockLowerScopes == null)
            {
                root.Add(AlicizaEditorUI.CreateEmptyState("UX Navigation Scope 属性不可用。"));
                return;
            }

            AlicizaPanel panel = AlicizaEditorUI.CreatePanel(null);
            root.Add(panel);

            _content = new VisualElement { name = "ux-navigation-scope-content" };
            panel.Body.Add(_content);

            root.TrackPropertyValue(_bakedSelectables, OnBakedSelectablesPropertyChanged);
            RebuildContent();
        }

        private void CacheProperties()
        {
            _defaultSelectable = serializedObject.FindProperty("_defaultSelectable");
            _holder = serializedObject.FindProperty("_holder");
            _bakedSelectables = serializedObject.FindProperty("_bakedSelectables");
            _rememberLastSelection = serializedObject.FindProperty("_rememberLastSelection");
            _blockLowerScopes = serializedObject.FindProperty("_blockLowerScopes");
            _navigable = serializedObject.FindProperty("_navigable");
        }

        private void RebuildContent()
        {
            if (_content == null || _rebuilding)
            {
                return;
            }

            _rebuilding = true;
            try
            {
                _bakedRows.Clear();
                _content.Clear();

                AutoBindHolder();
                if (ClearDefaultSelectableIfMissing())
                {
                    serializedObject.ApplyModifiedProperties();
                }

                BuildConfigurationSection(_content);
                BuildPolicySection(_content);
                BuildBakedSection(_content);

                _bakedRowCount = _bakedSelectables != null ? _bakedSelectables.arraySize : 0;
            }
            finally
            {
                _rebuilding = false;
            }
        }

        private void BuildConfigurationSection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Configuration");
            parent.Add(section);

            AlicizaRow defaultRow = AlicizaEditorUI.AddRow(section.Body, "Default");
            _defaultHost = defaultRow.Content;
            BuildDefaultSelectableField(_defaultHost);

            AlicizaEditorUI.AddPropertyRow(
                section.Body, "Holder", _holder, true, typeof(UIHolderObjectBase), true);
        }

        private void BuildDefaultSelectableField(VisualElement host)
        {
            if (host == null)
            {
                return;
            }

            host.Clear();

            if (_defaultSelectable == null)
            {
                host.Add(AlicizaEditorUI.CreateHint("默认选中控件属性不可用。"));
                return;
            }

            var field = new AlicizaEnumField(
                BuildDefaultSelectableOptionNames(), FindDefaultSelectableIndex());
            field.AddToClassList("alicia-control--grow");
            field.RegisterValueChangedCallback(OnDefaultSelectableChanged);
            host.Add(field);
        }

        private string[] BuildDefaultSelectableOptionNames()
        {
            int count = _bakedSelectables != null ? _bakedSelectables.arraySize : 0;
            var names = new string[count + 1];
            names[0] = "无";

            for (int i = 0; i < count; i++)
            {
                Selectable selectable =
                    _bakedSelectables.GetArrayElementAtIndex(i).objectReferenceValue as Selectable;
                names[i + 1] = selectable != null ? $"{i}: {selectable.name}" : $"{i}: 引用丢失";
            }

            return names;
        }

        private int FindDefaultSelectableIndex()
        {
            if (_defaultSelectable == null || _bakedSelectables == null)
            {
                return 0;
            }

            Selectable currentDefault = _defaultSelectable.objectReferenceValue as Selectable;
            if (currentDefault == null)
            {
                return 0;
            }

            for (int i = 0; i < _bakedSelectables.arraySize; i++)
            {
                Selectable selectable =
                    _bakedSelectables.GetArrayElementAtIndex(i).objectReferenceValue as Selectable;
                if (selectable != null && selectable == currentDefault)
                {
                    return i + 1;
                }
            }

            return 0;
        }

        private void OnDefaultSelectableChanged(ChangeEvent<int> evt)
        {
            if (_defaultSelectable == null || _bakedSelectables == null)
            {
                return;
            }

            int index = evt.newValue;
            UnityEngine.Object selectable = null;
            if (index > 0 && index <= _bakedSelectables.arraySize)
            {
                selectable = _bakedSelectables.GetArrayElementAtIndex(index - 1).objectReferenceValue;
            }

            _defaultSelectable.objectReferenceValue = selectable;
            serializedObject.ApplyModifiedProperties();

            if (index > 0 && selectable == null)
            {
                ScheduleDefaultSelectableRefresh();
            }
        }

        private void BuildPolicySection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Policy");
            parent.Add(section);

            AddPolicyRow(
                section.Body, "Remember", _rememberLastSelection,
                "重新打开时优先恢复上次选中的控件。");
            AddPolicyRow(
                section.Body, "Focus", _navigable,
                "开启：本域可以成为手柄/键盘焦点。关闭：本域不接收选中。");
            AddPolicyRow(
                section.Body, "Block", _blockLowerScopes,
                "开启：本域存活时挡住下层导航。关闭：不挡下层。");
        }

        private static void AddPolicyRow(
            VisualElement parent, string caption, SerializedProperty property, string description)
        {
            if (property == null)
            {
                return;
            }

            var pill = new AlicizaPillToggle();
            pill.BindProperty(property);

            var content = new VisualElement();
            content.style.flexDirection = FlexDirection.Row;
            content.style.alignItems = Align.Center;
            content.style.flexGrow = 1f;
            content.style.flexShrink = 1f;
            content.style.minWidth = 0f;
            content.Add(pill);

            Label hint = AlicizaEditorUI.CreateHint(description);
            hint.style.marginLeft = 8f;
            hint.style.flexGrow = 1f;
            hint.style.flexShrink = 1f;
            hint.style.whiteSpace = WhiteSpace.Normal;
            content.Add(hint);

            AlicizaEditorUI.AddRow(parent, caption, content);
        }

        private void BuildBakedSection(VisualElement parent)
        {
            AlicizaSection section = AlicizaEditorUI.CreateStaticSection("Selectables");
            parent.Add(section);

            int count = _bakedSelectables != null ? _bakedSelectables.arraySize : 0;
            if (_selectedBakedIndex >= count)
            {
                _selectedBakedIndex = count - 1;
            }

            section.Header.Add(AlicizaEditorUI.CreateBadge(
                count.ToString(),
                count > 0 ? AlicizaValueTone.Accent : AlicizaValueTone.Muted));

            var spacer = new VisualElement { pickingMode = PickingMode.Ignore };
            spacer.style.flexGrow = 1f;
            section.Header.Add(spacer);

            var refreshButton = AlicizaEditorUI.CreateTextButton(
                "Refresh", "收集当前域内可导航的 Selectable，并自动绑定 Holder", AlicizaButtonTone.Accent);
            refreshButton.style.marginRight = 4f;
            refreshButton.clicked += CollectBakedSelectables;
            section.Header.Add(refreshButton);

            var addButton = AlicizaEditorUI.CreateIconButton("+", "添加空槽位");
            addButton.clicked += AddBakedSelectableSlot;
            section.Header.Add(addButton);

            if (_bakedSelectables == null)
            {
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("无法访问烘焙控件列表。"));
                return;
            }

            if (count > 0)
            {
                var list = new VisualElement { name = "ux-navigation-baked-list" };
                list.AddToClassList("alicia-string-list__items");
                list.style.marginTop = 6f;

                for (int i = 0; i < count; i++)
                {
                    VisualElement row = CreateBakedSelectableRow(i, count);
                    _bakedRows.Add(row);
                    list.Add(row);
                }

                section.Body.Add(list);
            }
            else
            {
                _selectedBakedIndex = -1;
                section.Body.Add(AlicizaEditorUI.CreateEmptyState("暂无烘焙控件"));

                Label emptyHint = AlicizaEditorUI.CreateHint(
                    "点击 Refresh 自动收集，或点击 + 手动添加槽位。");
                emptyHint.style.marginTop = 4f;
                emptyHint.style.marginBottom = 4f;
                section.Body.Add(emptyHint);
            }

            Label footer = AlicizaEditorUI.CreateHint("刷新会自动绑定 Holder，并按层级顺序排序控件。");
            footer.style.marginTop = 6f;
            footer.style.whiteSpace = WhiteSpace.Normal;
            section.Body.Add(footer);

            Label keyboardHint = AlicizaEditorUI.CreateHint(
                "选中列表行后按 Delete 或 Backspace 可移除槽位。");
            keyboardHint.style.marginTop = 2f;
            keyboardHint.style.whiteSpace = WhiteSpace.Normal;
            section.Body.Add(keyboardHint);
        }

        private VisualElement CreateBakedSelectableRow(int index, int count)
        {
            var row = new VisualElement
            {
                name = "ux-navigation-baked-row-" + index,
                focusable = true
            };
            row.AddToClassList("alicia-string-list__item");
            if (index == count - 1)
            {
                row.AddToClassList("alicia-string-list__item--last");
            }

            if (index == _selectedBakedIndex)
            {
                row.style.backgroundColor = new StyleColor(SelectedRowColor);
            }

            var indexLabel = new Label(index.ToString());
            indexLabel.AddToClassList("alicia-string-list__index");
            row.Add(indexLabel);

            var field = new ObjectField
            {
                objectType = typeof(Selectable),
                allowSceneObjects = true,
                label = string.Empty
            };
            field.AddToClassList("alicia-control");
            field.style.flexGrow = 1f;
            field.style.flexShrink = 1f;
            field.style.minWidth = 0f;
            field.BindProperty(_bakedSelectables.GetArrayElementAtIndex(index));
            field.RegisterValueChangedCallback(_ => ScheduleDefaultSelectableRefresh());
            row.Add(field);

            var removeButton = AlicizaEditorUI.CreateIconButton("-", "移除槽位");
            removeButton.clicked += () => RemoveBakedSelectableAt(index);
            row.Add(removeButton);

            row.RegisterCallback<FocusInEvent>(_ => SelectBakedRow(index));
            row.RegisterCallback<PointerDownEvent>(
                evt => OnBakedSelectableRowPointerDown(evt, index, row, indexLabel));
            row.RegisterCallback<KeyDownEvent>(
                evt => OnBakedSelectableRowKeyDown(evt, index, row));
            return row;
        }

        private void OnBakedSelectableRowPointerDown(
            PointerDownEvent evt, int index, VisualElement row, Label indexLabel)
        {
            if (evt.button != 0)
            {
                return;
            }

            SelectBakedRow(index);

            if (ReferenceEquals(evt.target, row) || ReferenceEquals(evt.target, indexLabel))
            {
                row.Focus();
            }
        }

        private void OnBakedSelectableRowKeyDown(KeyDownEvent evt, int index, VisualElement row)
        {
            if (!ReferenceEquals(evt.target, row))
            {
                return;
            }

            if (evt.keyCode != KeyCode.Delete && evt.keyCode != KeyCode.Backspace)
            {
                return;
            }

            evt.StopPropagation();
            RemoveBakedSelectableAt(index);
        }

        private void SelectBakedRow(int index)
        {
            if (_selectedBakedIndex == index)
            {
                return;
            }

            _selectedBakedIndex = index;
            for (int i = 0; i < _bakedRows.Count; i++)
            {
                _bakedRows[i].style.backgroundColor = i == index
                    ? new StyleColor(SelectedRowColor)
                    : new StyleColor(StyleKeyword.Null);
            }
        }

        private void AddBakedSelectableSlot()
        {
            if (_bakedSelectables == null)
            {
                return;
            }

            int index = _bakedSelectables.arraySize;
            _bakedSelectables.arraySize = index + 1;
            _bakedSelectables.GetArrayElementAtIndex(index).objectReferenceValue = null;
            _selectedBakedIndex = index;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            RequestRebuild();
        }

        private void RemoveBakedSelectableAt(int index)
        {
            if (_bakedSelectables == null)
            {
                return;
            }

            int count = _bakedSelectables.arraySize;
            if (count == 0 || index < 0 || index >= count)
            {
                return;
            }

            Selectable removedSelectable =
                _bakedSelectables.GetArrayElementAtIndex(index).objectReferenceValue as Selectable;

            int oldSize = _bakedSelectables.arraySize;
            _bakedSelectables.DeleteArrayElementAtIndex(index);
            if (_bakedSelectables.arraySize == oldSize)
            {
                _bakedSelectables.DeleteArrayElementAtIndex(index);
            }

            if (_defaultSelectable != null && _defaultSelectable.objectReferenceValue == removedSelectable)
            {
                _defaultSelectable.objectReferenceValue = null;
            }

            _selectedBakedIndex = Mathf.Min(index, _bakedSelectables.arraySize - 1);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            RequestRebuild();
        }

        private void CollectBakedSelectables()
        {
            if (_bakedSelectables == null || _defaultSelectable == null)
            {
                return;
            }

            UXNavigationScope scope = target as UXNavigationScope;
            if (scope == null)
            {
                return;
            }

            serializedObject.Update();
            AutoBindHolder();

            Selectable[] allSelectables = scope.GetComponentsInChildren<Selectable>(true);
            var ownedSelectables = new List<Selectable>(allSelectables.Length);
            for (int i = 0; i < allSelectables.Length; i++)
            {
                Selectable selectable = allSelectables[i];
                if (selectable != null
                    && selectable.GetComponentInParent<UXNavigationScope>(true) == scope
                    && HasValidNavigation(selectable))
                {
                    ownedSelectables.Add(selectable);
                }
            }

            ownedSelectables.Sort(CompareSiblingPath);
            _bakedSelectables.arraySize = ownedSelectables.Count;
            for (int i = 0; i < ownedSelectables.Count; i++)
            {
                _bakedSelectables.GetArrayElementAtIndex(i).objectReferenceValue = ownedSelectables[i];
            }

            Selectable defaultSelectable = _defaultSelectable.objectReferenceValue as Selectable;
            if (defaultSelectable != null && !ownedSelectables.Contains(defaultSelectable))
            {
                _defaultSelectable.objectReferenceValue = null;
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(scope);
            RequestRebuild();
        }

        private void AutoBindHolder()
        {
            if (_holder == null || _holder.objectReferenceValue != null)
            {
                return;
            }

            UXNavigationScope scope = target as UXNavigationScope;
            if (scope == null)
            {
                return;
            }

            UIHolderObjectBase foundHolder = scope.GetComponent<UIHolderObjectBase>();
            if (foundHolder == null)
            {
                foundHolder = scope.GetComponentInParent<UIHolderObjectBase>(true);
            }

            if (foundHolder == null)
            {
                return;
            }

            _holder.objectReferenceValue = foundHolder;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(scope);
        }

        private void RefreshDefaultSelectable()
        {
            if (_defaultHost == null)
            {
                return;
            }

            if (ClearDefaultSelectableIfMissing())
            {
                serializedObject.ApplyModifiedProperties();
            }

            BuildDefaultSelectableField(_defaultHost);
        }

        private bool ClearDefaultSelectableIfMissing()
        {
            if (_defaultSelectable == null || _bakedSelectables == null)
            {
                return false;
            }

            Selectable currentDefault = _defaultSelectable.objectReferenceValue as Selectable;
            if (currentDefault == null || ContainsSelectable(_bakedSelectables, currentDefault))
            {
                return false;
            }

            _defaultSelectable.objectReferenceValue = null;
            return true;
        }

        private void OnBakedSelectablesPropertyChanged(SerializedProperty property)
        {
            if (_rebuilding || property == null)
            {
                return;
            }

            if (property.arraySize != _bakedRowCount)
            {
                RequestRebuild();
            }
        }

        private void RequestRebuild()
        {
            if (_root == null || _rebuildScheduled)
            {
                return;
            }

            _rebuildScheduled = true;
            _root.schedule.Execute(() =>
            {
                _rebuildScheduled = false;
                RebuildContent();
            }).ExecuteLater(0);
        }

        private void ScheduleDefaultSelectableRefresh()
        {
            if (_root == null || _defaultRefreshScheduled)
            {
                return;
            }

            _defaultRefreshScheduled = true;
            _root.schedule.Execute(() =>
            {
                _defaultRefreshScheduled = false;
                if (_defaultHost == null || _defaultHost.panel == null)
                {
                    return;
                }

                RefreshDefaultSelectable();
            }).ExecuteLater(0);
        }

        private static bool ContainsSelectable(SerializedProperty selectables, Selectable targetSelectable)
        {
            for (int i = 0; i < selectables.arraySize; i++)
            {
                if (selectables.GetArrayElementAtIndex(i).objectReferenceValue == targetSelectable)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasValidNavigation(Selectable selectable)
        {
            if (selectable == null)
            {
                return false;
            }

            Navigation navigation = selectable.navigation;
            if (navigation.mode == Navigation.Mode.None)
            {
                return false;
            }

            if (navigation.mode != Navigation.Mode.Explicit)
            {
                return true;
            }

            return navigation.selectOnUp != null
                   || navigation.selectOnDown != null
                   || navigation.selectOnLeft != null
                   || navigation.selectOnRight != null;
        }

        private static int CompareSiblingPath(Selectable left, Selectable right)
        {
            if (left == right)
            {
                return 0;
            }

            Transform leftTransform = left != null ? left.transform : null;
            Transform rightTransform = right != null ? right.transform : null;
            return CompareSiblingPath(leftTransform, rightTransform);
        }

        private static int CompareSiblingPath(Transform left, Transform right)
        {
            if (left == right)
            {
                return 0;
            }

            if (left == null)
            {
                return -1;
            }

            if (right == null)
            {
                return 1;
            }

            int leftDepth = InputHierarchyUtility.GetDepth(left);
            int rightDepth = InputHierarchyUtility.GetDepth(right);
            Transform leftCursor = left;
            Transform rightCursor = right;

            while (leftDepth > rightDepth)
            {
                leftCursor = leftCursor.parent;
                leftDepth--;
            }

            while (rightDepth > leftDepth)
            {
                rightCursor = rightCursor.parent;
                rightDepth--;
            }

            while (leftCursor.parent != rightCursor.parent)
            {
                leftCursor = leftCursor.parent;
                rightCursor = rightCursor.parent;
            }

            return leftCursor.GetSiblingIndex().CompareTo(rightCursor.GetSiblingIndex());
        }
    }
}
#endif
