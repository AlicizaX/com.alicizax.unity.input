#if INPUTSYSTEM_SUPPORT
using AlicizaX.UI.Runtime;
using UnityEngine;

/// <summary>
/// 输入包内部共用的 Transform / UIHolder 层级工具。
/// </summary>
internal static class InputHierarchyUtility
{
    /// <summary>找到实际决定绘制顺序的 Canvas，嵌套 Canvas 默认继承父级排序。</summary>
    internal static Canvas ResolveSortingCanvas(Canvas canvas)
    {
        while (canvas != null && !canvas.isRootCanvas && !canvas.overrideSorting)
            canvas = canvas.transform.parent.GetComponentInParent<Canvas>(true);
        return canvas;
    }

    /// <summary>
    /// 计算 Transform 在层级中的深度（根节点为 1）。
    /// </summary>
    internal static int GetDepth(Transform transform)
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

    /// <summary>
    /// 判断 holder 是否为 root 本身或其后代。
    /// </summary>
    internal static bool IsDescendantOrSelf(UIHolderObjectBase holder, UIHolderObjectBase root)
    {
        if (holder == null || root == null)
        {
            return false;
        }

        return IsDescendantOrSelf(holder.transform, root.transform);
    }

    /// <summary>
    /// 判断 transform 是否为 root 本身或其后代。
    /// </summary>
    internal static bool IsDescendantOrSelf(Transform transform, Transform root)
    {
        if (transform == null || root == null)
        {
            return false;
        }

        Transform current = transform;
        while (current != null)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }

            current = current.parent;
        }

        return false;
    }
}
#endif
