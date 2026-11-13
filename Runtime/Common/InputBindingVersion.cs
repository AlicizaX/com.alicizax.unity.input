#if INPUTSYSTEM_SUPPORT
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    /// <summary>
    /// 绑定版本号，两个粒度：
    /// <see cref="Value"/> 只由 Rebind 的显式变更推进，驱动显示文本、图标绑定选择等与设备无关的缓存；
    /// <see cref="ControlsValue"/> 额外跟随 Input System 的 BoundControlsChanged（设备增删、直接 ApplyBindingOverride），
    /// 只给需要实际控件列表的组合键句柄使用。设备插拔不应清空显示文本缓存并重新分配字符串。
    /// </summary>
    internal sealed class InputBindingVersion
    {
        private bool _attached;

        public int Value { get; private set; }

        public int ControlsValue { get; private set; }

        public void Bump()
        {
            Value++;
            ControlsValue++;
        }

        public void Attach()
        {
            if (_attached) return;
            InputSystem.onActionChange += HandleActionChange;
            _attached = true;
        }

        public void Detach()
        {
            if (!_attached) return;
            InputSystem.onActionChange -= HandleActionChange;
            _attached = false;
        }

        private void HandleActionChange(object _, InputActionChange change)
        {
            if (change == InputActionChange.BoundControlsChanged) ControlsValue++;
        }
    }
}
#endif
