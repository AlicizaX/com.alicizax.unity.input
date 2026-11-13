#if INPUTSYSTEM_SUPPORT
using UnityEngine;

namespace AlicizaX.Input
{
    /// <summary>
    /// 热键与导航共用的“谁在最上层”规则：Canvas 排序 → 显示序号（后显示优先）→ 层级深度。
    /// 两个模块各自维护显示序号，但比较规则只有这一份。
    /// </summary>
    internal static class InputLayerOrder
    {
        internal static bool IsHigher(
            int leftOrder, ulong leftSerial, int leftDepth,
            int rightOrder, ulong rightSerial, int rightDepth)
        {
            if (leftOrder != rightOrder) return leftOrder > rightOrder;
            if (leftSerial != rightSerial) return leftSerial > rightSerial;
            return leftDepth > rightDepth;
        }

        /// <summary>没有 Canvas 的作用域排在最底层。</summary>
        internal static int SortingOrderOf(Canvas canvas) => canvas != null ? canvas.sortingOrder : int.MinValue;
    }

    /// <summary>
    /// 实际决定排序的 Canvas 的惰性缓存。没有 Canvas 时也缓存 null，避免反复走原生查找；
    /// Canvas 被单独销毁时重新解析，层级变化由持有者调用 <see cref="Invalidate"/>。
    /// 作为可变字段使用，不要放进属性或 readonly 字段。
    /// </summary>
    internal struct InputSortingCanvas
    {
        private Canvas _canvas;
        private bool _resolved;

        internal Canvas Get(Component owner)
        {
            if (!_resolved || (object)_canvas != null && _canvas == null)
            {
                _resolved = true;
                _canvas = owner != null
                    ? InputHierarchyUtility.ResolveSortingCanvas(owner.GetComponentInParent<Canvas>(true))
                    : null;
            }

            return _canvas;
        }

        internal void Invalidate()
        {
            _resolved = false;
            _canvas = null;
        }
    }
}
#endif
