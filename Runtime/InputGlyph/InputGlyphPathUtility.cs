#if INPUTSYSTEM_SUPPORT
using System;

internal static class InputGlyphPathUtility
{
    /// <summary>
    /// 把 Input System 控制路径规范化成图标数据库使用的 glyph key。
    /// 使用两遍扫描 + string.Create，避免 ZString 首次使用时分配线程静态缓冲区。
    /// </summary>
    internal static string GetGlyphKeyFromControlPath(string controlPath)
    {
        if (string.IsNullOrWhiteSpace(controlPath))
        {
            return string.Empty;
        }

        int layoutStart = controlPath.IndexOf('<');
        int layoutEnd = controlPath.IndexOf('>');
        int pathStart = controlPath.IndexOf('/');
        if (layoutStart < 0 || layoutEnd <= layoutStart + 1 || pathStart < 0 || pathStart >= controlPath.Length - 1)
        {
            return string.Empty;
        }

        int layoutStartIndex = layoutStart + 1;
        int layoutLength = layoutEnd - layoutStart - 1;
        string layoutKey = NormalizeLayout(controlPath, layoutStartIndex, layoutLength);
        int pathLength = controlPath.Length - pathStart - 1;
        int totalLength = (layoutKey != null ? layoutKey.Length : layoutLength) + 1 + pathLength;

        var state = new GlyphKeyState(controlPath, layoutKey, layoutStartIndex, layoutLength, pathStart);
        return string.Create(totalLength, state, static (span, value) =>
        {
            int index = 0;
            if (value.LayoutKey != null)
            {
                value.LayoutKey.AsSpan().CopyTo(span);
                index = value.LayoutKey.Length;
            }
            else
            {
                for (int i = 0; i < value.LayoutLength; i++)
                {
                    span[index++] = InputStringUtility.ToLowerInvariantFast(value.ControlPath[value.LayoutStart + i]);
                }
            }

            span[index++] = '/';
            for (int i = value.PathStart + 1; i < value.ControlPath.Length; i++)
            {
                char character = value.ControlPath[i] == '\\' ? '/' : value.ControlPath[i];
                span[index++] = InputStringUtility.ToLowerInvariantFast(character);
            }
        });
    }

    private static string NormalizeLayout(string source, int startIndex, int length)
    {
        ReadOnlySpan<char> layout = source.AsSpan(startIndex, length);
        if (layout.IndexOf("keyboard".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "keyboard";
        }

        if (layout.IndexOf("mouse".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "mouse";
        }

        if (layout.IndexOf("gamepad".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0
            || layout.IndexOf("controller".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0
            || layout.IndexOf("xinput".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0
            || layout.IndexOf("dualshock".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0
            || layout.IndexOf("dualsense".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0
            || layout.IndexOf("switch".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0
            || layout.IndexOf("nintendo".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0
            || layout.IndexOf("joy-con".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0
            || layout.IndexOf("joycon".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "gamepad";
        }

        if (layout.IndexOf("joystick".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "joystick";
        }

        if (layout.IndexOf("touch".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "touch";
        }

        if (layout.IndexOf("pen".AsSpan(), StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return "pen";
        }

        return null;
    }

    private readonly struct GlyphKeyState
    {
        internal readonly string ControlPath;
        internal readonly string LayoutKey;
        internal readonly int LayoutStart;
        internal readonly int LayoutLength;
        internal readonly int PathStart;

        internal GlyphKeyState(string controlPath, string layoutKey, int layoutStart, int layoutLength, int pathStart)
        {
            ControlPath = controlPath;
            LayoutKey = layoutKey;
            LayoutStart = layoutStart;
            LayoutLength = layoutLength;
            PathStart = pathStart;
        }
    }
}
#endif
