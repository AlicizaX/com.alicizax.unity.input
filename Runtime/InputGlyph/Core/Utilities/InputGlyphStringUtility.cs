#if INPUTSYSTEM_SUPPORT
using System;

public static class InputGlyphStringUtility
{
    public static int StableHash(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        unchecked
        {
            int hash = 5381;
            for (int i = 0; i < value.Length; i++)
            {
                hash = ((hash << 5) + hash) ^ ToUpperInvariantFast(value[i]);
            }

            return hash == 0 ? 1 : hash;
        }
    }

    public static int StableHashLowerAscii(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return 0;
        }

        unchecked
        {
            int hash = 5381;
            for (int i = 0; i < value.Length; i++)
            {
                hash = ((hash << 5) + hash) ^ ToLowerInvariantFast(value[i]);
            }

            return hash == 0 ? 1 : hash;
        }
    }

    public static bool EqualsOrdinal(string left, string right)
    {
        return string.Equals(left, right, StringComparison.Ordinal);
    }

    public static bool EqualsIgnoreCase(string left, string right)
    {
        return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
    }

    public static bool ContainsIgnoreCase(string source, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value) || value.Length > source.Length)
        {
            return false;
        }

        int end = source.Length - value.Length;
        for (int i = 0; i <= end; i++)
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

    public static bool StartsWithIgnoreCase(string source, string value)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(value) || value.Length > source.Length)
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            if (ToUpperInvariantFast(source[i]) != ToUpperInvariantFast(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    public static char ToLowerInvariantFast(char value)
    {
        return value >= 'A' && value <= 'Z' ? (char)(value + 32) : char.ToLowerInvariant(value);
    }

    public static char ToUpperInvariantFast(char value)
    {
        return value >= 'a' && value <= 'z' ? (char)(value - 32) : char.ToUpperInvariant(value);
    }
}
#endif
