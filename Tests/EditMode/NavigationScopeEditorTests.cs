#if INPUTSYSTEM_SUPPORT
using System.Reflection;
using AlicizaX.UI.UXNavigation;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace AlicizaX.Input.Tests
{
    public sealed class NavigationScopeEditorTests
    {
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public void HolderAssignmentUsesNearestUi(bool widget, bool onHolder)
        {
            var root = new GameObject("window", typeof(RectTransform), typeof(Canvas));
            var owner = InputTestViews.Bind(root);
            if (widget)
            {
                var child = new GameObject("widget", typeof(RectTransform));
                child.transform.SetParent(root.transform, false);
                owner = InputTestViews.Bind(child, owner);
            }
            var scopeObject = owner.GetHolder().gameObject;
            if (!onHolder)
            {
                scopeObject = new GameObject("panel", typeof(RectTransform));
                scopeObject.transform.SetParent(owner.GetHolder().transform, false);
            }
            var scope = scopeObject.AddComponent<NavigationScope>();
            var editor = Editor.CreateEditor(scope);
            try
            {
                typeof(NavigationScopeEditor).GetMethod("AssignHolderIfMissing", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(editor, null);
                Assert.That(editor.serializedObject.FindProperty("_holder").objectReferenceValue,
                    Is.SameAs(owner.GetHolder()));
            }
            finally
            {
                InputTestViews.UnbindAll();
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void CollectionKeepsHierarchyOrderAndExcludesNestedScopesAndInvalidNavigation()
        {
            var root = new GameObject("scope-bake", typeof(RectTransform), typeof(NavigationScope));
            var scope = root.GetComponent<NavigationScope>();
            var first = Button(root.transform);
            var nestedChild = Button(first.transform);
            var second = Button(root.transform);
            var nested = Button(root.transform);
            nested.gameObject.AddComponent<NavigationScope>();
            var inactive = Button(root.transform);
            inactive.gameObject.SetActive(false);
            var noNavigation = Button(root.transform);
            noNavigation.navigation = new Navigation { mode = Navigation.Mode.None };
            var emptyExplicit = Button(root.transform);
            emptyExplicit.navigation = new Navigation { mode = Navigation.Mode.Explicit };
            var explicitLink = Button(root.transform);
            explicitLink.navigation = new Navigation { mode = Navigation.Mode.Explicit, selectOnLeft = first };
            var editor = Editor.CreateEditor(scope);
            try
            {
                editor.serializedObject.FindProperty("_defaultSelectable").objectReferenceValue = nested;
                editor.serializedObject.ApplyModifiedPropertiesWithoutUndo();
                typeof(NavigationScopeEditor).GetMethod("CollectBakedSelectables", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(editor, null);
                var baked = editor.serializedObject.FindProperty("_bakedSelectables");
                var actual = new Selectable[baked.arraySize];
                for (int i = 0; i < actual.Length; i++)
                    actual[i] = (Selectable)baked.GetArrayElementAtIndex(i).objectReferenceValue;
                Assert.That(actual, Is.EqualTo(new[] { first, nestedChild, second, inactive, explicitLink }));
                Assert.That(scope.DefaultSelectable, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(editor);
                Object.DestroyImmediate(root);
            }
        }

        private static Button Button(Transform parent)
        {
            var root = new GameObject("button", typeof(RectTransform), typeof(Button));
            root.transform.SetParent(parent, false);
            return root.GetComponent<Button>();
        }
    }
}
#endif
