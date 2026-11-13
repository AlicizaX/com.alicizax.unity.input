#if INPUTSYSTEM_SUPPORT
using System;
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    /// <summary>
    /// 标识重绑定、重置和显示文本接口使用的设备绑定集。
    /// </summary>
    public enum RebindTarget
    {
        /// <summary>
        /// 选择键盘鼠标绑定组。
        /// </summary>
        KeyboardMouse,

        /// <summary>
        /// 选择手柄绑定组。
        /// </summary>
        Gamepad
    }

    /// <summary>
    /// 一次重绑定/重置/显示查询的完整选项。不可变；通过 With 方法派生新值。
    /// 目标设备只体现在 BindingGroup / RequiredControlPath 上，不再单独存一份。
    /// </summary>
    public readonly struct RebindRequest
    {
        private static readonly string[] DefaultExcludedControls =
        {
            "<Mouse>/position",
            "<Mouse>/delta",
            "<Pointer>/position",
            "<Pointer>/delta"
        };

        private static readonly RebindRequest KeyboardMouseRequest = new RebindRequest(
            null,
            "<Keyboard>/escape",
            DefaultExcludedControls,
            0.1f,
            "KeyboardMouse",
            "<Keyboard>",
            true);

        private static readonly RebindRequest GamepadRequest = new RebindRequest(
            null,
            "<Keyboard>/escape",
            DefaultExcludedControls,
            0.1f,
            "Gamepad",
            "<Gamepad>",
            true);

        /// <summary>组合绑定部分名；为空表示普通绑定。</summary>
        public readonly string CompositePartName;

        /// <summary>取消重绑定的控制路径。</summary>
        public readonly string CancelKey;

        /// <summary>重绑定时排除的控制路径。数组为只读共享数据，调用方不得修改。</summary>
        public readonly string[] ExcludedControls;

        /// <summary>两次输入之间的等待时间。</summary>
        public readonly float WaitDelay;

        /// <summary>重绑定匹配的绑定分组。</summary>
        public readonly string BindingGroup;

        /// <summary>重绑定必须匹配的控制路径前缀。</summary>
        public readonly string RequiredControlPath;

        /// <summary>为 true 时只暂存变更，等待 ConfirmApply。</summary>
        public readonly bool PrepareOnly;

        private RebindRequest(
            string compositePartName,
            string cancelKey,
            string[] excludedControls,
            float waitDelay,
            string bindingGroup,
            string requiredControlPath,
            bool prepareOnly)
        {
            CompositePartName = compositePartName;
            CancelKey = cancelKey;
            ExcludedControls = excludedControls;
            WaitDelay = waitDelay;
            BindingGroup = bindingGroup;
            RequiredControlPath = requiredControlPath;
            PrepareOnly = prepareOnly;
        }

        /// <summary>
        /// 键鼠默认选项。
        /// </summary>
        public static RebindRequest Default => KeyboardMouseRequest;

        /// <summary>
        /// 手柄默认选项。
        /// </summary>
        public static RebindRequest ForGamepad => GamepadRequest;

        /// <summary>
        /// 获取指定设备绑定集的默认选项。
        /// </summary>
        public static RebindRequest ForTarget(RebindTarget target)
        {
            return target == RebindTarget.Gamepad ? GamepadRequest : KeyboardMouseRequest;
        }

        /// <summary>
        /// 派生出指定组合绑定部分的选项。
        /// </summary>
        public RebindRequest WithCompositePart(string compositePartName)
        {
            return new RebindRequest(
                compositePartName,
                CancelKey,
                ExcludedControls,
                WaitDelay,
                BindingGroup,
                RequiredControlPath,
                PrepareOnly);
        }

        /// <summary>
        /// 派生出自定义取消键的选项。
        /// </summary>
        public RebindRequest WithCancelKey(string cancelKey)
        {
            return new RebindRequest(
                CompositePartName,
                cancelKey,
                ExcludedControls,
                WaitDelay,
                BindingGroup,
                RequiredControlPath,
                PrepareOnly);
        }

        /// <summary>
        /// 派生出自定义排除控件的选项。
        /// </summary>
        public RebindRequest WithExcludedControls(string[] excludedControls)
        {
            return new RebindRequest(
                CompositePartName,
                CancelKey,
                excludedControls,
                WaitDelay,
                BindingGroup,
                RequiredControlPath,
                PrepareOnly);
        }

        /// <summary>
        /// 派生出自定义等待时间的选项。
        /// </summary>
        public RebindRequest WithWaitDelay(float waitDelay)
        {
            return new RebindRequest(
                CompositePartName,
                CancelKey,
                ExcludedControls,
                waitDelay,
                BindingGroup,
                RequiredControlPath,
                PrepareOnly);
        }

        /// <summary>
        /// 派生出自定义绑定分组的选项。
        /// </summary>
        public RebindRequest WithBindingGroup(string bindingGroup)
        {
            return new RebindRequest(
                CompositePartName,
                CancelKey,
                ExcludedControls,
                WaitDelay,
                bindingGroup,
                RequiredControlPath,
                PrepareOnly);
        }

        /// <summary>
        /// 派生出自定义必需控制路径的选项。
        /// </summary>
        public RebindRequest WithRequiredControlPath(string requiredControlPath)
        {
            return new RebindRequest(
                CompositePartName,
                CancelKey,
                ExcludedControls,
                WaitDelay,
                BindingGroup,
                requiredControlPath,
                PrepareOnly);
        }

        /// <summary>
        /// 派生出是否只暂存变更的选项。
        /// </summary>
        public RebindRequest WithPrepareOnly(bool prepareOnly)
        {
            return new RebindRequest(
                CompositePartName,
                CancelKey,
                ExcludedControls,
                WaitDelay,
                BindingGroup,
                RequiredControlPath,
                prepareOnly);
        }
    }

    /// <summary>
    /// 描述重绑定服务捕获到的暂存绑定覆盖。
    /// </summary>
    public readonly struct RebindChange
    {
        /// <summary>
        /// 表示空的或无效的绑定变更。
        /// </summary>
        public static readonly RebindChange Empty = new RebindChange(null, -1, null);

        /// <summary>
        /// 获取拥有此绑定变更的动作。
        /// </summary>
        public readonly InputAction Action;

        /// <summary>
        /// 获取绑定索引。
        /// </summary>
        internal readonly int BindingIndex;

        /// <summary>
        /// 获取重绑定操作捕获到的覆盖控制路径。
        /// </summary>
        public readonly string OverridePath;

        /// <summary>
        /// 获取此变更是否包含有效动作、绑定索引和覆盖路径。
        /// </summary>
        public bool IsValid => Action != null && BindingIndex >= 0 && !string.IsNullOrEmpty(OverridePath);

        internal RebindChange(InputAction action, int bindingIndex, string overridePath)
        {
            Action = action;
            BindingIndex = bindingIndex;
            OverridePath = overridePath;
        }

        /// <summary>
        /// 返回描述动作和覆盖路径的诊断字符串。
        /// </summary>
        public override string ToString()
        {
            string actionName = Action != null ? Action.name : "<null>";
            return string.Concat(actionName, "[", BindingIndex.ToString(), "] -> ", OverridePath);
        }
    }
}
#endif
