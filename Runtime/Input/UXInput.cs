#if INPUTSYSTEM_SUPPORT
using System;
using AlicizaX.UI.UXNavigation;
using UnityEngine.InputSystem;

/// <summary>输入模块的统一入口。每次初始化创建新实例，关闭后保留实例供组件完成退订。</summary>
public static class UXInput
{
    public static InputWatch Watch { get; private set; }
    public static InputReader Reader { get; private set; }
    public static InputRebind Rebind { get; private set; }
    public static InputGlyph Glyph { get; private set; }
    public static InputHaptics Haptics { get; private set; }
    public static bool IsInitialized { get; private set; }

    internal static void Initialize(InputActionAsset actions, InputGlyphDatabase glyphDatabase)
    {
        Initialize(actions, glyphDatabase, null);
    }

    internal static void Initialize(InputActionAsset actions, InputGlyphDatabase glyphDatabase, string bindingFilePath)
    {
        if (IsInitialized)
            throw new InvalidOperationException("UXInput is already initialized. Only one InputService may own the input lifecycle.");

        Watch = new InputWatch();
        Reader = new InputReader(actions);
        Rebind = new InputRebind(actions, bindingFilePath);
        Glyph = new InputGlyph(glyphDatabase);
        Haptics = new InputHaptics();
        IsInitialized = true;
        try
        {
            Watch.Initialize();
            Rebind.Initialize();
            Reader.Initialize();
            UXNavigationSystem.Initialize();
        }
        catch
        {
            Shutdown();
            throw;
        }
    }

    internal static void Shutdown()
    {
        if (!IsInitialized) return;
        IsInitialized = false;
        UXNavigationSystem.Shutdown();
        Haptics.Shutdown();
        Reader.Shutdown();
        Rebind.Shutdown();
        Glyph.Shutdown();
        Watch.Shutdown();
    }
}
#endif
