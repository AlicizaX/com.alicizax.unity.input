using System.Collections;
using AlicizaX.UI.Runtime;
using AlicizaX.UI.UXNavigation;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace AlicizaX.Input.Tests
{
    public sealed class NavigationScopePlayModeTests : NavigationScopeCases
    {
        [UnityTest]
        public IEnumerator DestroyingScopeUnregistersIt()
        {
            var scope = Scope(0);
            AddButton(scope);
            Assert.That(ScopeCount, Is.EqualTo(1));

            Object.Destroy(scope.gameObject);
            yield return null;

            Assert.That(ScopeCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ScopeLifecycleRestoresFocusAcrossFramesWithoutManualRefresh()
        {
            var root = Keep(new GameObject("automatic-navigation-scope", typeof(RectTransform), typeof(Canvas)));
            root.layer = UIComponent.UIShowLayer;
            var lower = root.AddComponent<UXNavigationScope>();            var first = AddButton(lower);
            var higher = Scope(10);
            var second = AddButton(higher);
            yield return null;
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(second.gameObject));
            higher.gameObject.SetActive(false);
            yield return null;
            Assert.That(lower.NavigationSuppressed, Is.False);
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(first.gameObject));
            higher.gameObject.SetActive(true);
            yield return null;
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(second.gameObject));
            Object.Destroy(higher.gameObject);
            yield return null;
            Assert.That(higher == null, Is.True);
            Assert.That(lower.NavigationSuppressed, Is.False);
            Assert.That(eventSystem.currentSelectedGameObject, Is.SameAs(first.gameObject));
        }
    }
}
