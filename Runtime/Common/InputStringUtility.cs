#if INPUTSYSTEM_SUPPORT
using System;
using UnityEngine.InputSystem;

/// <summary>
/// 输入包内部共用的字符串比较与绑定路径工具，全部方法均不产生托管分配。
/// </summary>
internal static class InputStringUtility
{
    /// <summary>
    /// 忽略大小写判断实例文本是否包含片段。
    /// </summary>
    internal static bool ContainsIgnoreCase(string source, string value)
    {
        return !string.IsNullOrEmpty(source)
               && !string.IsNullOrEmpty(value)
               && source.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
    }

    /// <summary>
    /// 在源字符串的指定区间内忽略大小写查找片段。
    /// </summary>
    internal static bool ContainsIgnoreCase(string source, int startIndex, int length, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value) || value.Length > length)
        {
            return false;
        }

        int end = startIndex + length - value.Length;
        for (int i = startIndex; i <= end; i++)
        {
            int valueIndex = 0;
            while (valueIndex < value.Length
                   && ToUpperInvariantFast(source[i + valueIndex]) == ToUpperInvariantFast(value[valueIndex]))
            {
                valueIndex++;
            }

            if (valueIndex == value.Length)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断绑定分组串（以分号分隔）是否包含指定分组，不拆分字符串、不分配数组。
    /// </summary>
    internal static bool GroupsContain(string groups, string group)
    {
        if (string.IsNullOrEmpty(groups) || string.IsNullOrEmpty(group))
        {
            return false;
        }

        int index = 0;
        while (index < groups.Length)
        {
            int separator = groups.IndexOf(';', index);
            int end = separator < 0 ? groups.Length : separator;
            int start = index;
            while (start < end && char.IsWhiteSpace(groups[start]))
            {
                start++;
            }

            while (end > start && char.IsWhiteSpace(groups[end - 1]))
            {
                end--;
            }

            if (end - start == group.Length
                && string.Compare(groups, start, group, 0, group.Length, StringComparison.OrdinalIgnoreCase) == 0)
            {
                return true;
            }

            if (separator < 0)
            {
                break;
            }

            index = separator + 1;
        }

        return false;
    }

    /// <summary>
    /// 读取绑定的有效控制路径，重绑定路径优先。
    /// </summary>
    internal static string GetEffectivePath(in InputBinding binding)
    {
        return string.IsNullOrWhiteSpace(binding.effectivePath) ? binding.path : binding.effectivePath;
    }

    /// <summary>
    /// 忽略大小写判断文本是否以指定前缀开头，空值返回 false。
    /// </summary>
    internal static bool StartsWith(string source, string prefix)
    {
        return !string.IsNullOrEmpty(source) && source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    internal static char ToUpperInvariantFast(char value)
    {
        return value >= 'a' && value <= 'z' ? (char)(value - 32) : char.ToUpperInvariant(value);
    }

    internal static char ToLowerInvariantFast(char value)
    {
        return value >= 'A' && value <= 'Z' ? (char)(value + 32) : char.ToLowerInvariant(value);
    }
}
#endif
