#if INPUTSYSTEM_SUPPORT
public static class InputGlyphProfileIds
{
    public const string KeyboardMouse = "KeyboardMouse";
    public const string GenericGamepad = "GenericGamepad";
    public const string Xbox = "Xbox";
    public const string PlayStation = "PlayStation";
    public const string Switch = "Switch";
    public const string SteamDeck = "SteamDeck";

    public static bool IsKeyboardMouse(string profileId)
    {
        return InputGlyphStringUtility.EqualsIgnoreCase(profileId, KeyboardMouse);
    }

    public static bool IsGamepadFallback(string profileId)
    {
        return InputGlyphStringUtility.EqualsIgnoreCase(profileId, GenericGamepad);
    }
}
#endif
