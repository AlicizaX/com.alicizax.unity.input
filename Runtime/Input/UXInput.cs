#if INPUTSYSTEM_SUPPORT
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

namespace AlicizaX.Input
{
    /// <summary>
    /// 输入模块的统一入口与唯一组装点：创建各模块、注入依赖、统一启停。
    /// 关闭后所有模块属性都为 null；组件应缓存自己订阅的实例并对该实例退订。
    /// </summary>
    public static class UXInput
    {
        private static InputBindingVersion _bindingVersion;

        public static InputWatch Watch { get; private set; }
        public static InputReader Reader { get; private set; }
        public static InputRebind Rebind { get; private set; }
        public static InputGlyph Glyph { get; private set; }
        public static InputHaptics Haptics { get; private set; }
        public static InputNavigation Navigation { get; private set; }
        public static InputHotkey Hotkey { get; private set; }
        public static InputFeedback Feedback { get; private set; }
        public static bool IsInitialized { get; private set; }

        /// <param name="bindingFilePath">绑定覆盖文件路径；为 null 时使用默认位置。</param>
        internal static void Initialize(
            InputActionAsset actions,
            InputGlyphDatabase glyphDatabase,
            string bindingFilePath = null)
        {
            if (IsInitialized)
                throw new InvalidOperationException(
                    "UXInput is already initialized. Only one InputService may own the input lifecycle.");

            _bindingVersion = new InputBindingVersion();
            var displayCache = new InputBindingDisplayCache(_bindingVersion);

            Watch = new InputWatch();
            Reader = new InputReader(actions, _bindingVersion);
            Rebind = new InputRebind(Reader, _bindingVersion, displayCache, bindingFilePath);
            Glyph = new InputGlyph(glyphDatabase, Watch, _bindingVersion, displayCache);
            Haptics = new InputHaptics();
            Navigation = new InputNavigation(Watch);
            Hotkey = new InputHotkey(Reader);
            Feedback = new InputFeedback(Watch, Navigation);

            IsInitialized = true;
            try
            {
                _bindingVersion.Attach();
                Watch.Initialize();
                Rebind.Initialize();
                Reader.Initialize();
                Navigation.Initialize();
            }
            catch
            {
                Shutdown();
                throw;
            }
        }


        /// <summary>
        /// 按依赖逆序关闭：Rebind 需要在 Reader 释放资产之前恢复入口绑定。
        /// </summary>
        internal static void Shutdown()
        {
            if (!IsInitialized) return;
            IsInitialized = false;

            Hotkey.Shutdown();
            Navigation.Shutdown();
            Haptics.Shutdown();
            Rebind.Shutdown();
            Reader.Shutdown();
            Glyph.Shutdown();
            Watch.Shutdown();
            _bindingVersion.Detach();

            Feedback = null;
            Hotkey = null;
            Navigation = null;
            Haptics = null;
            Rebind = null;
            Reader = null;
            Glyph = null;
            Watch = null;
            _bindingVersion = null;
        }
    }
}
#endif
