#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;

/// <summary>
/// 单个输入 Profile 的静态元数据：稳定 ID、显示名、回退链和绑定组提示。
/// </summary>
public readonly struct InputProfileInfo
{
    /// <summary>对应的运行时 Profile 枚举。</summary>
    public readonly InputWatch.InputProfile Profile;

    /// <summary>稳定的 Profile ID，与图标数据库、序列化数据中的 profileId 一致。</summary>
    public readonly string Id;

    /// <summary>供编辑器下拉框使用的显示名。</summary>
    public readonly string DisplayName;

    /// <summary>图标解析回退链，按优先级排列。数组为只读共享数据，调用方不得修改。</summary>
    public readonly string[] FallbackIds;

    /// <summary>输入绑定分组提示，用于绑定评分。数组为只读共享数据，调用方不得修改。</summary>
    public readonly string[] BindingGroupHints;

    internal InputProfileInfo(
        InputWatch.InputProfile profile,
        string id,
        string displayName,
        string[] fallbackIds,
        string[] bindingGroupHints)
    {
        Profile = profile;
        Id = id;
        DisplayName = displayName;
        FallbackIds = fallbackIds;
        BindingGroupHints = bindingGroupHints;
    }
}

/// <summary>
/// 输入 Profile 的唯一定义源。运行时图标解析和编辑器配置都从这里读取，避免多份重复清单。
/// </summary>
public static class InputProfileCatalog
{
    private static readonly string[] NoFallbacks = Array.Empty<string>();
    private static readonly string[] NoHints = Array.Empty<string>();

    private static readonly InputProfileInfo[] ProfilesByValue =
    {
        new InputProfileInfo(InputWatch.InputProfile.Unknown, "Unknown", "Unknown", NoFallbacks, NoHints),
        new InputProfileInfo(
            InputWatch.InputProfile.Touch,
            "Touch",
            "Touch",
            NoFallbacks,
            new[] { "touch", "pen" }),
        new InputProfileInfo(
            InputWatch.InputProfile.KeyboardMouse,
            "KeyboardMouse",
            "Keyboard & Mouse",
            NoFallbacks,
            new[] { "keyboard", "mouse", "keyboard&mouse", "keyboardmouse", "kbm" }),
        new InputProfileInfo(
            InputWatch.InputProfile.GenericGamepad,
            "GenericGamepad",
            "Generic Gamepad",
            NoFallbacks,
            new[] { "gamepad", "controller" }),
        new InputProfileInfo(
            InputWatch.InputProfile.GenericJoystick,
            "GenericJoystick",
            "Generic Joystick",
            new[] { "GenericGamepad" },
            new[] { "joystick", "gamepad", "controller" }),
        new InputProfileInfo(
            InputWatch.InputProfile.Xbox,
            "Xbox",
            "Xbox",
            new[] { "GenericGamepad" },
            new[] { "xbox", "xinput", "gamepad", "controller" }),
        new InputProfileInfo(
            InputWatch.InputProfile.PlayStation,
            "PlayStation",
            "PlayStation",
            new[] { "GenericGamepad" },
            new[] { "playstation", "dualshock", "dualsense", "gamepad", "controller" }),
        new InputProfileInfo(
            InputWatch.InputProfile.Switch,
            "Switch",
            "Switch",
            new[] { "GenericGamepad", "Xbox" },
            new[] { "switch", "nintendo", "joy-con", "joycon", "gamepad", "controller" }),
        new InputProfileInfo(
            InputWatch.InputProfile.SteamDeck,
            "SteamDeck",
            "Steam Deck",
            new[] { "Xbox", "GenericGamepad" },
            new[] { "steamdeck", "steam", "xbox", "xinput", "gamepad", "controller" }),
        new InputProfileInfo(
            InputWatch.InputProfile.SteamController,
            "SteamController",
            "Steam Controller",
            new[] { "SteamDeck", "Xbox", "GenericGamepad" },
            new[] { "steam", "steamcontroller", "xbox", "xinput", "gamepad", "controller" })
    };

    private static readonly InputProfileInfo[] EditableProfiles = BuildEditableProfiles();
    private static readonly string[] IdsByValue = BuildIds();

    /// <summary>
    /// 可在编辑器中配置的 Profile（不含 Unknown）。
    /// </summary>
    public static IReadOnlyList<InputProfileInfo> Editable => EditableProfiles;

    /// <summary>
    /// 全部 Profile（含 Unknown）。
    /// </summary>
    public static IReadOnlyList<InputProfileInfo> All => ProfilesByValue;

    /// <summary>
    /// 获取指定 Profile 的稳定 ID。
    /// </summary>
    public static string GetId(InputWatch.InputProfile profile)
    {
        int index = (int)profile;
        return index >= 0 && index < IdsByValue.Length ? IdsByValue[index] : "Unknown";
    }

    /// <summary>
    /// 尝试按稳定 ID 查找 Profile 元数据。
    /// </summary>
    public static bool TryGet(string id, out InputProfileInfo info)
    {
        for (int i = 0; i < ProfilesByValue.Length; i++)
        {
            if (string.Equals(ProfilesByValue[i].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                info = ProfilesByValue[i];
                return true;
            }
        }

        info = default;
        return false;
    }

    /// <summary>
    /// 尝试按枚举查找 Profile 元数据。
    /// </summary>
    public static bool TryGet(InputWatch.InputProfile profile, out InputProfileInfo info)
    {
        int index = (int)profile;
        if (index >= 0 && index < ProfilesByValue.Length)
        {
            info = ProfilesByValue[index];
            return true;
        }

        info = default;
        return false;
    }

    private static InputProfileInfo[] BuildEditableProfiles()
    {
        InputProfileInfo[] result = new InputProfileInfo[ProfilesByValue.Length - 1];
        int count = 0;
        for (int i = 0; i < ProfilesByValue.Length; i++)
        {
            if (ProfilesByValue[i].Profile == InputWatch.InputProfile.Unknown)
            {
                continue;
            }

            result[count] = ProfilesByValue[i];
            count++;
        }

        return result;
    }

    private static string[] BuildIds()
    {
        string[] ids = new string[ProfilesByValue.Length];
        for (int i = 0; i < ProfilesByValue.Length; i++)
        {
            ids[i] = ProfilesByValue[i].Id;
        }

        return ids;
    }
}
#endif
