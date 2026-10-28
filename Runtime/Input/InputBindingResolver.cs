#if INPUTSYSTEM_SUPPORT
using System;
using UnityEngine.InputSystem;

/// <summary>
/// 输入包唯一的绑定选择实现。显示图标、重绑定、组合部分控制解析共用同一套候选枚举与匹配规则。
/// </summary>
internal static class InputBindingResolver
{
    /// <summary>
    /// 为交互式重绑定选择目标绑定：优先匹配分组/限定路径，否则回退到首个可用绑定。
    /// </summary>
    internal static int SelectForRebind(
        InputAction action,
        string compositePartName,
        string bindingGroup,
        string requiredControlPath)
    {
        if (action == null)
        {
            return -1;
        }

        bool searchingForCompositePart = !string.IsNullOrEmpty(compositePartName);
        int fallbackPart = -1;
        int fallbackNonComposite = -1;

        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];

            if (searchingForCompositePart)
            {
                if (!binding.isPartOfComposite
                    || !string.Equals(binding.name, compositePartName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            else if (binding.isComposite)
            {
                continue;
            }

            bool matchesTarget = MatchesRebindTarget(binding, bindingGroup, requiredControlPath);
            if (binding.isPartOfComposite)
            {
                if (fallbackPart < 0)
                {
                    fallbackPart = i;
                }

                if (matchesTarget)
                {
                    return i;
                }
            }
            else
            {
                if (fallbackNonComposite < 0)
                {
                    fallbackNonComposite = i;
                }

                if (matchesTarget)
                {
                    return i;
                }
            }
        }

        return fallbackNonComposite >= 0 ? fallbackNonComposite : fallbackPart;
    }

    /// <summary>
    /// 为图标/显示文本选择当前 Profile 下最匹配的绑定。
    /// </summary>
    internal static int SelectForDisplay(
        InputAction action,
        string compositePartName,
        UXInput.Watch.InputProfile profile,
        string[] bindingGroupHints)
    {
        if (action == null)
        {
            return -1;
        }

        bool requireCompositePart = !string.IsNullOrEmpty(compositePartName);
        int bestScore = int.MinValue;
        int bestBindingIndex = -1;

        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding candidate = action.bindings[i];
            if (candidate.isComposite)
            {
                continue;
            }

            if (requireCompositePart)
            {
                if (!candidate.isPartOfComposite
                    || !string.Equals(candidate.name, compositePartName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }
            else if (candidate.isPartOfComposite)
            {
                continue;
            }

            string path = InputStringUtility.GetEffectivePath(in candidate);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            int score = Score(candidate, path, profile, bindingGroupHints);
            if (score > bestScore)
            {
                bestScore = score;
                bestBindingIndex = i;
            }
        }

        return bestScore > int.MinValue ? bestBindingIndex : -1;
    }

    /// <summary>
    /// 查找组合绑定部分的绑定索引。返回 -1 表示不存在该部分。
    /// </summary>
    internal static int FindCompositePartBindingIndex(InputAction action, string compositePartName)
    {
        if (action == null || string.IsNullOrWhiteSpace(compositePartName))
        {
            return -1;
        }

        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (binding.isPartOfComposite
                && string.Equals(binding.name, compositePartName, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static int Score(
        in InputBinding binding,
        string path,
        UXInput.Watch.InputProfile profile,
        string[] bindingGroupHints)
    {
        int score = 0;
        if (MatchesBindingGroups(binding.groups, bindingGroupHints))
        {
            score += 100;
        }
        else if (!string.IsNullOrWhiteSpace(binding.groups))
        {
            score -= 20;
        }

        if (MatchesProfilePath(path, profile))
        {
            score += 60;
        }

        if (!binding.isPartOfComposite)
        {
            score += 5;
        }

        return score;
    }

    private static bool MatchesBindingGroups(string groups, string[] hints)
    {
        if (string.IsNullOrWhiteSpace(groups) || hints == null)
        {
            return false;
        }

        for (int i = 0; i < hints.Length; i++)
        {
            if (InputStringUtility.GroupsContain(groups, hints[i]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MatchesProfilePath(string path, UXInput.Watch.InputProfile profile)
    {
        switch (profile)
        {
            case UXInput.Watch.InputProfile.KeyboardMouse:
                return InputStringUtility.StartsWith(path, "<Keyboard>")
                       || InputStringUtility.StartsWith(path, "<Mouse>");
            case UXInput.Watch.InputProfile.Touch:
                return InputStringUtility.StartsWith(path, "<Touchscreen>")
                       || InputStringUtility.StartsWith(path, "<Pen>");
            default:
                return InputStringUtility.StartsWith(path, "<Gamepad>")
                       || InputStringUtility.StartsWith(path, "<Joystick>");
        }
    }

    private static bool MatchesRebindTarget(
        in InputBinding binding,
        string bindingGroup,
        string requiredControlPath)
    {
        if (!string.IsNullOrEmpty(requiredControlPath))
        {
            return InputStringUtility.StartsWith(binding.path, requiredControlPath)
                   || InputStringUtility.StartsWith(binding.effectivePath, requiredControlPath);
        }

        if (!string.IsNullOrEmpty(bindingGroup))
        {
            return InputStringUtility.GroupsContain(binding.groups, bindingGroup);
        }

        return true;
    }
}
#endif
