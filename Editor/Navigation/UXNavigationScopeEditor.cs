#if INPUTSYSTEM_SUPPORT
using System.Collections.Generic;
using AlicizaX.Editor;
using AlicizaX.UI.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AlicizaX.UI.UXNavigation
{
    [CustomEditor(typeof(UXNavigationScope))]
    public sealed class UXNavigationScopeEditor : UnityEditor.Editor
    {
        private const float FieldRowHeight = 46f;
        private const float PolicyRowHeight = 46f;
        private const float BakedRowHeight = 22f;
        private const float BakedHeaderHeight = 22f;
        private const float BakedFooterHeight = 24f;
        private const float SymbolButtonWidth = 22f;
        private const float FieldLabelWidth = 54f;
        private const float PolicyToggleWidth = 86f;
        private const float CollectButtonSize = 22f;

        private readonly List<Selectable> _selectableBuffer = new List<Selectable>(64);
        private readonly List<GUIContent> _defaultSelectableOptions = new List<GUIContent>(64);
        private int _selectedBakedIndex = -1;
        private bool _bakedListHasKeyboardFocus;
        private SerializedProperty _defaultSelectable;
        private SerializedProperty _holder;
        private SerializedProperty _bakedSelectables;
        private SerializedProperty _rememberLastSelection;
        private SerializedProperty _blockLowerScopes;
        private GUIContent _collectContent;

        private void OnEnable()
        {
            _defaultSelectable = serializedObject.FindProperty("_defaultSelectable");
            _holder = serializedObject.FindProperty("_holder");
            _bakedSelectables = serializedObject.FindProperty("_bakedSelectables");
            _rememberLastSelection = serializedObject.FindProperty("_rememberLastSelection");
            _blockLowerScopes = serializedObject.FindProperty("_blockLowerScopes");
            _collectContent = EditorGUIUtility.IconContent("Refresh", "收集");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            UXNavigationScope scope = (UXNavigationScope)target;
            AutoBindHolder(scope, serializedObject, _holder);

            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(AlicizaEditorGUI.Styles.Panel);
            DrawReferenceRow();
            DrawPolicyRows();
            DrawBakedSelectableList();
            ClearDefaultSelectableIfMissing();
            EditorGUILayout.EndVertical();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawReferenceRow()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, FieldRowHeight);
            DrawFieldRowBackground(rect);

            Rect collectRect = new Rect(rect.xMax - CollectButtonSize - 6f, rect.y + 12f, CollectButtonSize, CollectButtonSize);
            Rect defaultRect = new Rect(rect.x + 6f, rect.y + 3f, collectRect.x - rect.x - 16f, 18f);
            Rect holderRect = new Rect(defaultRect.x, defaultRect.yMax + 3f, defaultRect.width, 18f);

            DrawDefaultSelectablePopup(defaultRect);
            DrawObjectReferenceProperty(holderRect, "Holder", _holder, typeof(UIHolderObjectBase));
            if (AlicizaEditorGUI.DrawToolbarButton(collectRect, _collectContent))
            {
                RefreshCurrentScopeBakeData();
            }
        }

        private void DrawDefaultSelectablePopup(Rect rect)
        {
            Rect labelRect = new Rect(rect.x, rect.y, FieldLabelWidth, rect.height);
            Rect fieldRect = new Rect(labelRect.xMax + 4f, rect.y, rect.xMax - labelRect.xMax - 4f, rect.height);
            GUI.Label(labelRect, "Default", AlicizaEditorGUI.Styles.FieldLabel);

            _defaultSelectableOptions.Clear();
            _defaultSelectableOptions.Add(new GUIContent("无"));

            Selectable currentDefault = _defaultSelectable.objectReferenceValue as Selectable;
            int selectedIndex = 0;
            for (int i = 0; i < _bakedSelectables.arraySize; i++)
            {
                Selectable selectable = _bakedSelectables.GetArrayElementAtIndex(i).objectReferenceValue as Selectable;
                _defaultSelectableOptions.Add(new GUIContent(BuildSelectableOptionName(i, selectable)));
                if (selectable != null && selectable == currentDefault)
                {
                    selectedIndex = i + 1;
                }
            }

            if (currentDefault != null && selectedIndex == 0)
            {
                _defaultSelectable.objectReferenceValue = null;
            }

            int newIndex = EditorGUI.Popup(fieldRect, selectedIndex, _defaultSelectableOptions.ToArray());
            if (newIndex != selectedIndex)
            {
                _defaultSelectable.objectReferenceValue = newIndex > 0
                    ? _bakedSelectables.GetArrayElementAtIndex(newIndex - 1).objectReferenceValue
                    : null;
            }
        }

        private static string BuildSelectableOptionName(int index, Selectable selectable)
        {
            return selectable != null
                ? $"{index}: {selectable.name}"
                : $"{index}: 引用丢失";
        }

        private void ClearDefaultSelectableIfMissing()
        {
            Selectable currentDefault = _defaultSelectable.objectReferenceValue as Selectable;
            if (currentDefault != null && !ContainsSelectable(_bakedSelectables, currentDefault))
            {
                _defaultSelectable.objectReferenceValue = null;
            }
        }

        private void DrawPolicyRows()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, PolicyRowHeight);
            DrawFieldRowBackground(rect);

            Rect rowRect = new Rect(rect.x + 6f, rect.y + 3f, rect.width - 12f, 18f);
            Rect blockRect = new Rect(rowRect.x, rowRect.yMax + 3f, rowRect.width, rowRect.height);
            DrawInlineToggleWithDescription(rowRect, _rememberLastSelection, "Remember", "重新打开时优先恢复上次选中的控件。");
            DrawInlineToggleWithDescription(blockRect, _blockLowerScopes, "Block", "当前导航域激活时阻断下层导航域。");
        }

        private static void DrawFieldRowBackground(Rect rect)
        {
            EditorGUI.DrawRect(rect, AlicizaEditorGUI.Colors.FieldRow);
            AlicizaEditorGUI.DrawOutline(rect);
        }

        private static void DrawObjectReferenceProperty(Rect rect, string label, SerializedProperty property, System.Type objectType)
        {
            Rect labelRect = new Rect(rect.x, rect.y, FieldLabelWidth, rect.height);
            Rect fieldRect = new Rect(labelRect.xMax + 4f, rect.y, rect.xMax - labelRect.xMax - 4f, rect.height);
            GUI.Label(labelRect, label, AlicizaEditorGUI.Styles.FieldLabel);
            property.objectReferenceValue = EditorGUI.ObjectField(fieldRect, property.objectReferenceValue, objectType, true);
        }

        private static void DrawInlineToggle(Rect rect, SerializedProperty property, string label)
        {
            property.boolValue = EditorGUI.ToggleLeft(rect, label, property.boolValue, AlicizaEditorGUI.Styles.FieldLabel);
        }

        private static void DrawInlineToggleWithDescription(Rect rect, SerializedProperty property, string label, string description)
        {
            Rect toggleRect = new Rect(rect.x, rect.y, PolicyToggleWidth, rect.height);
            Rect descriptionRect = new Rect(toggleRect.xMax + 4f, rect.y + 1f, rect.xMax - toggleRect.xMax - 4f, rect.height - 2f);
            DrawInlineToggle(toggleRect, property, label);
            GUI.Label(descriptionRect, description, AlicizaEditorGUI.Styles.MutedMiniLabel);
        }

        private void DrawBakedSelectableList()
        {
            int count = _bakedSelectables.arraySize;
            if (_selectedBakedIndex >= count)
            {
                _selectedBakedIndex = count - 1;
            }

            float listHeight = BakedHeaderHeight + Mathf.Max(1, count) * BakedRowHeight + BakedFooterHeight + 8f;
            Rect listRect = EditorGUILayout.GetControlRect(false, listHeight);
            AlicizaEditorGUI.DrawBodyBackground(listRect);

            Rect contentRect = new Rect(listRect.x + 4f, listRect.y + 4f, listRect.width - 8f, listRect.height - 8f);
            Rect headerRect = new Rect(contentRect.x, contentRect.y, contentRect.width, BakedHeaderHeight);
            DrawBakedListHeader(headerRect, count);

            Rect rowsRect = new Rect(contentRect.x, headerRect.yMax, contentRect.width, Mathf.Max(1, count) * BakedRowHeight);
            HandleBakedListMouseFocus(rowsRect);
            if (count == 0)
            {
                Rect emptyRect = new Rect(rowsRect.x, rowsRect.y, rowsRect.width, BakedRowHeight);
                AlicizaEditorGUI.DrawListItemBackground(emptyRect, false, false);
                GUI.Label(new Rect(emptyRect.x + 8f, emptyRect.y + 2f, emptyRect.width - 16f, emptyRect.height - 4f), "暂无烘焙控件", AlicizaEditorGUI.Styles.MutedMiniLabel);
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    Rect rowRect = new Rect(rowsRect.x, rowsRect.y + i * BakedRowHeight, rowsRect.width, BakedRowHeight);
                    DrawBakedSelectableRow(rowRect, i);
                }
            }

            HandleBakedListKeyboard(rowsRect, count);
            DrawBakedListFooter(contentRect);
        }

        private void HandleBakedListKeyboard(Rect rowsRect, int count)
        {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.KeyDown || !_bakedListHasKeyboardFocus || count == 0 || _selectedBakedIndex < 0 || _selectedBakedIndex >= count)
            {
                return;
            }

            if (currentEvent.keyCode != KeyCode.Delete && currentEvent.keyCode != KeyCode.Backspace)
            {
                return;
            }

            currentEvent.Use();
            RemoveBakedSelectableAt(_selectedBakedIndex);
        }

        private void HandleBakedListMouseFocus(Rect rowsRect)
        {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0)
            {
                return;
            }

            _bakedListHasKeyboardFocus = rowsRect.Contains(currentEvent.mousePosition);
        }

        private void DrawBakedListHeader(Rect rect, int count)
        {
            AlicizaEditorGUI.DrawToolbarBackground(rect);
            Rect labelRect = new Rect(rect.x + 8f, rect.y + 2f, rect.width - 16f, rect.height - 4f);
            GUI.Label(labelRect, $"Selectables    {count}", AlicizaEditorGUI.Styles.RowLabel);
        }

        private void DrawBakedSelectableRow(Rect rowRect, int index)
        {
            Event currentEvent = Event.current;
            bool selected = _selectedBakedIndex == index;
            bool hovered = rowRect.Contains(currentEvent.mousePosition);
            AlicizaEditorGUI.DrawListItemBackground(rowRect, selected, hovered);

            SerializedProperty element = _bakedSelectables.GetArrayElementAtIndex(index);
            Rect indexRect = new Rect(rowRect.x + 6f, rowRect.y + 2f, 28f, rowRect.height - 4f);
            Rect removeRect = new Rect(rowRect.xMax - SymbolButtonWidth, rowRect.y, SymbolButtonWidth, rowRect.height);
            Rect fieldRect = new Rect(indexRect.xMax + 4f, rowRect.y + 2f, removeRect.x - indexRect.xMax - 8f, rowRect.height - 4f);
            GUI.Label(indexRect, index.ToString(), AlicizaEditorGUI.Styles.MutedMiniLabel);
            EditorGUI.PropertyField(fieldRect, element, GUIContent.none);

            if (AlicizaEditorGUI.DrawSymbolButton(removeRect, "-"))
            {
                RemoveBakedSelectableAt(index);
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0 && rowRect.Contains(currentEvent.mousePosition))
            {
                _selectedBakedIndex = index;
                _bakedListHasKeyboardFocus = true;
                GUI.FocusControl(string.Empty);
                currentEvent.Use();
            }
        }

        private void DrawBakedListFooter(Rect contentRect)
        {
            Rect footerRect = new Rect(contentRect.x, contentRect.yMax - BakedFooterHeight, contentRect.width, BakedFooterHeight);
            AlicizaEditorGUI.DrawToolbarBackground(footerRect);
            Rect addRect = new Rect(footerRect.xMax - SymbolButtonWidth, footerRect.y + 1f, SymbolButtonWidth, footerRect.height - 2f);

            GUI.Label(new Rect(footerRect.x + 8f, footerRect.y + 2f, footerRect.width - 40f, footerRect.height - 4f), "刷新会自动绑定 Holder，并按层级顺序排序控件。", AlicizaEditorGUI.Styles.MutedMiniLabel);

            if (AlicizaEditorGUI.DrawSymbolButton(addRect, "+"))
            {
                AddBakedSelectableSlot();
            }
        }

        private void AddBakedSelectableSlot()
        {
            Undo.RecordObject(target, "添加 UX 导航控件槽位");
            int index = _bakedSelectables.arraySize;
            _bakedSelectables.arraySize++;
            _bakedSelectables.GetArrayElementAtIndex(index).objectReferenceValue = null;
            _selectedBakedIndex = index;
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
        }

        private void RemoveBakedSelectableAt(int index)
        {
            int count = _bakedSelectables.arraySize;
            if (count == 0 || index < 0 || index >= count)
            {
                return;
            }

            Undo.RecordObject(target, "移除 UX 导航控件槽位");
            Selectable removedSelectable = _bakedSelectables.GetArrayElementAtIndex(index).objectReferenceValue as Selectable;
            int oldSize = _bakedSelectables.arraySize;
            _bakedSelectables.DeleteArrayElementAtIndex(index);
            if (_bakedSelectables.arraySize == oldSize)
            {
                _bakedSelectables.DeleteArrayElementAtIndex(index);
            }

            if (_defaultSelectable.objectReferenceValue == removedSelectable)
            {
                _defaultSelectable.objectReferenceValue = null;
            }

            _selectedBakedIndex = Mathf.Min(index, _bakedSelectables.arraySize - 1);
            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(target);
            GUIUtility.ExitGUI();
        }

        private void RefreshCurrentScopeBakeData()
        {
            UXNavigationScope scope = (UXNavigationScope)target;
            RefreshScopeBakeData(scope, serializedObject, _holder, _bakedSelectables, _selectableBuffer);
        }

        private static readonly List<Selectable> StaticSelectableBuffer = new List<Selectable>(64);

        private static void RefreshScopeBakeData(UXNavigationScope scope, SerializedObject scopeObject, SerializedProperty holder, SerializedProperty bakedSelectables)
        {
            RefreshScopeBakeData(scope, scopeObject, holder, bakedSelectables, StaticSelectableBuffer);
        }

        private static void RefreshScopeBakeData(UXNavigationScope scope, SerializedObject scopeObject, SerializedProperty holder, SerializedProperty bakedSelectables, List<Selectable> scopeEditorBuffer)
        {
            AutoBindHolder(scope, scopeObject, holder);
            Selectable[] allSelectables = scope.GetComponentsInChildren<Selectable>(true);
            List<Selectable> ownedSelectables = scopeEditorBuffer;
            ownedSelectables.Clear();
            for (int i = 0; i < allSelectables.Length; i++)
            {
                Selectable selectable = allSelectables[i];
                if (selectable != null && selectable.GetComponentInParent<UXNavigationScope>(true) == scope && HasValidNavigation(selectable))
                {
                    ownedSelectables.Add(selectable);
                }
            }

            ownedSelectables.Sort(CompareSiblingPath);
            Undo.RecordObject(scope, "刷新 UX 导航域烘焙数据");
            bakedSelectables.arraySize = ownedSelectables.Count;
            for (int i = 0; i < ownedSelectables.Count; i++)
            {
                bakedSelectables.GetArrayElementAtIndex(i).objectReferenceValue = ownedSelectables[i];
            }

            SerializedProperty defaultSelectableProperty = scopeObject.FindProperty("_defaultSelectable");
            Selectable defaultSelectable = defaultSelectableProperty.objectReferenceValue as Selectable;
            if (defaultSelectable != null && !ownedSelectables.Contains(defaultSelectable))
            {
                defaultSelectableProperty.objectReferenceValue = null;
            }

            scopeObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(scope);
        }

        private static void AutoBindHolder(UXNavigationScope scope, SerializedObject scopeObject, SerializedProperty holder)
        {
            if (holder.objectReferenceValue != null)
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

            Undo.RecordObject(scope, "绑定 UX 导航域 Holder");
            holder.objectReferenceValue = foundHolder;
            scopeObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(scope);
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

            int leftDepth = GetDepth(left);
            int rightDepth = GetDepth(right);
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

        private static int GetDepth(Transform transform)
        {
            int depth = 0;
            Transform current = transform;
            while (current != null)
            {
                depth++;
                current = current.parent;
            }

            return depth;
        }

    }
}
#endif

