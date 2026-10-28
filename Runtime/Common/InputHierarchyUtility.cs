#if INPUTSYSTEM_SUPPORT
using AlicizaX.UI.Runtime;
using UnityEngine;

/// <summary>
/// 输入包内部共用的 Transform / UIHolder 层级工具。
/// </summary>
internal static class InputHierarchyUtility
{
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
    /// 向上查找最近的父级 UIHolder。
    /// </summary>
    internal static UIHolderObjectBase FindParentHolder(UIHolderObjectBase holder)
    {
        if (holder == null)
        {
            return null;
        }

        Transform current = holder.transform.parent;
        while (current != null)
        {
            if (current.TryGetComponent(out UIHolderObjectBase parentHolder))
            {
                return parentHolder;
            }

            current = current.parent;
        }

        return null;
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
