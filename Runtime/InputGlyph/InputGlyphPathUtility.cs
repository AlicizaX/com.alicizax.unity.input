#if INPUTSYSTEM_SUPPORT
using Cysharp.Text;

internal static class InputGlyphPathUtility
{
    internal static string GetGlyphKeyFromControlPath(string controlPath)
    {
        if (string.IsNullOrWhiteSpace(controlPath))
        {
            return string.Empty;
        }

        using (Utf16ValueStringBuilder sb = ZString.CreateStringBuilder())
        {
            int layoutStart = controlPath.IndexOf('<');
            int layoutEnd = controlPath.IndexOf('>');
            int pathStart = controlPath.IndexOf('/');
            if (layoutStart < 0 || layoutEnd <= layoutStart || pathStart < 0 || pathStart >= controlPath.Length - 1)
            {
                return string.Empty;
            }

            int layoutStartIndex = layoutStart + 1;
            int layoutLength = layoutEnd - layoutStart - 1;
            AppendNormalizedLayout(controlPath, layoutStartIndex, layoutLength, sb);

            sb.Append('/');
            for (int i = pathStart + 1; i < controlPath.Length; i++)
            {
                char value = controlPath[i] == '\\' ? '/' : controlPath[i];
                sb.Append(ToLowerInvariantFast(value));
            }

            return sb.ToString();
        }
    }

    private static void AppendNormalizedLayout(
        string source,
        int startIndex,
        int length,
        Utf16ValueStringBuilder builder)
    {
        if (ContainsIgnoreCase(source, startIndex, length, "keyboard"))
        {
            builder.Append("keyboard");
            return;
        }

        if (ContainsIgnoreCase(source, startIndex, length, "mouse"))
        {
            builder.Append("mouse");
            return;
        }

        if (ContainsIgnoreCase(source, startIndex, length, "gamepad")
            || ContainsIgnoreCase(source, startIndex, length, "controller")
            || ContainsIgnoreCase(source, startIndex, length, "xinput")
            || ContainsIgnoreCase(source, startIndex, length, "dualshock")
            || ContainsIgnoreCase(source, startIndex, length, "dualsense")
            || ContainsIgnoreCase(source, startIndex, length, "switch")
            || ContainsIgnoreCase(source, startIndex, length, "nintendo")
            || ContainsIgnoreCase(source, startIndex, length, "joy-con")
            || ContainsIgnoreCase(source, startIndex, length, "joycon"))
        {
            builder.Append("gamepad");
            return;
        }

        if (ContainsIgnoreCase(source, startIndex, length, "joystick"))
        {
            builder.Append("joystick");
            return;
        }

        if (ContainsIgnoreCase(source, startIndex, length, "touch"))
        {
            builder.Append("touch");
            return;
        }

        if (ContainsIgnoreCase(source, startIndex, length, "pen"))
        {
            builder.Append("pen");
            return;
        }

        for (int i = 0; i < length; i++)
        {
            builder.Append(ToLowerInvariantFast(source[startIndex + i]));
        }
    }

    private static bool ContainsIgnoreCase(string source, int startIndex, int length, string value)
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

    private static char ToLowerInvariantFast(char value)
    {
        return value >= 'A' && value <= 'Z' ? (char)(value + 32) : char.ToLowerInvariant(value);
    }

    private static char ToUpperInvariantFast(char value)
    {
        return value >= 'a' && value <= 'z' ? (char)(value - 32) : char.ToUpperInvariant(value);
    }
}
#endif
