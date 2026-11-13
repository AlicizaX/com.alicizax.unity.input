#if INPUTSYSTEM_SUPPORT
using System;
using System.Collections.Generic;
using AlicizaX.UI.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    /// <summary>
    /// 一个 Holder 的热键注册表。同一 action/pressType 允许注册多个 trigger，
    /// 派发时从后往前选择“最后注册/启用且仍可用”的一个；这对应动态显示时后显示的控件优先。
    /// </summary>
    internal sealed class HotkeyScope
    {
        private readonly InputHotkey _owner;
        private readonly Dictionary<InputAction, List<HotkeyBinding>> _bindingsByAction =
            new Dictionary<InputAction, List<HotkeyBinding>>();
        private UIHolderObjectBase[] _holderChain = Array.Empty<UIHolderObjectBase>();
        private int _holderChainCount;
        private InputSortingCanvas _sortingCanvas;

        public HotkeyScope(InputHotkey owner, UIHolderObjectBase holder)
        {
            _owner = owner;
            Holder = holder;
        }

        public readonly UIHolderObjectBase Holder;

        /// <summary>显示序号：越大越晚显示，同 Canvas 排序下优先。只由 <see cref="InputHotkey"/> 写入。</summary>
        internal ulong ActivationSerial { get; set; }

        public int HierarchyDepth { get; private set; }

        public bool IsEmpty => _bindingsByAction.Count == 0;

        /// <summary>从 Holder 自身到根的完整 Holder 链；结尾比较用，不参与 Transform 访问。</summary>
        internal ReadOnlySpan<UIHolderObjectBase> HolderChain =>
            new ReadOnlySpan<UIHolderObjectBase>(_holderChain, 0, _holderChainCount);

        /// <summary>实际决定排序的 Canvas；Holder 重新挂接由 RefreshHierarchy 失效。</summary>
        internal Canvas SortingCanvas => _sortingCanvas.Get(Holder);

        internal void AddBinding(InputAction action, EHotkeyPressType pressType, HotkeyComponentBase trigger)
        {
            if (!_bindingsByAction.TryGetValue(action, out List<HotkeyBinding> bindings))
            {
                bindings = new List<HotkeyBinding>(2);
                _bindingsByAction.Add(action, bindings);
            }

            bindings.Add(new HotkeyBinding(trigger, pressType));
        }

        internal bool RemoveBinding(InputAction action, EHotkeyPressType pressType, HotkeyComponentBase trigger)
        {
            if (!_bindingsByAction.TryGetValue(action, out List<HotkeyBinding> bindings))
                return false;

            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                HotkeyBinding binding = bindings[i];
                if (binding.PressType != pressType || !ReferenceEquals(binding.Trigger, trigger))
                    continue;

                bindings.RemoveAt(i);
                if (bindings.Count == 0)
                    _bindingsByAction.Remove(action);
                return true;
            }

            return false;
        }

        internal bool TryTrigger(InputAction action, EHotkeyPressType pressType)
        {
            if (!_bindingsByAction.TryGetValue(action, out List<HotkeyBinding> bindings))
                return false;

            // 返回 false 的三种含义都表示“继续向父 scope 冒泡”：无槽、已销毁、未启用。
            // 后注册/后启用的 trigger 优先；已销毁或已失活时继续尝试同 action 的较早注册。
            for (int i = bindings.Count - 1; i >= 0; i--)
            {
                HotkeyBinding binding = bindings[i];
                if (binding.PressType != pressType)
                    continue;

                HotkeyComponentBase trigger = binding.Trigger;
                if (trigger == null || !trigger.isActiveAndEnabled)
                    continue;

                trigger.TriggerHotkey();
                return true;
            }

            return false;
        }

        /// <summary>追加本 scope 的全部 trigger；清空责任由调用方承担，便于跨 scope 汇总。</summary>
        internal void AppendTriggers(List<HotkeyComponentBase> buffer)
        {
            foreach (List<HotkeyBinding> bindings in _bindingsByAction.Values)
            {
                for (int i = 0; i < bindings.Count; i++)
                    buffer.Add(bindings[i].Trigger);
            }
        }

        /// <summary>
        /// 重算祖先链、深度并失效 Canvas 缓存。只在创建、注册、窗口显示时调用；
        /// 一趟遍历同时得到链和深度，IsInside/父 scope 查找都复用这条链。
        /// </summary>
        public void RefreshHierarchy()
        {
            if (Holder == null)
            {
                _holderChainCount = 0;
                HierarchyDepth = 0;
                _sortingCanvas.Invalidate();
                return;
            }

            int depth = 0;
            int holderCount = 0;
            for (Transform current = Holder.transform; current != null; current = current.parent)
            {
                depth++;
                if (current.TryGetComponent(out UIHolderObjectBase ancestor))
                {
                    if (holderCount == _holderChain.Length)
                        Array.Resize(ref _holderChain, Math.Max(4, holderCount * 2));

                    _holderChain[holderCount++] = ancestor;
                }
            }

            _holderChainCount = holderCount;
            HierarchyDepth = depth;
            _sortingCanvas.Invalidate();

            Debug.Assert(holderCount > 0 && ReferenceEquals(_holderChain[0], Holder),
                "The holder chain must start with the scope holder.");
        }

        /// <summary>本作用域是否位于给定 Holder 自身或后代之下（沿缓存链比较，不访问 Transform）。</summary>
        public bool IsInside(UIHolderObjectBase holder)
        {
            for (int i = 0; i < _holderChainCount; i++)
            {
                if (ReferenceEquals(_holderChain[i], holder))
                    return true;
            }

            return false;
        }

        public void HandleHolderShow() => _owner.ActivateScope(this);

        public void HandleHolderClose() => _owner.RemovePressTargetsForHolder(Holder);

        public void HandleHolderDestroy() => _owner.DestroyScope(Holder);

        private readonly struct HotkeyBinding
        {
            public readonly HotkeyComponentBase Trigger;
            public readonly EHotkeyPressType PressType;

            public HotkeyBinding(HotkeyComponentBase trigger, EHotkeyPressType pressType)
            {
                Trigger = trigger;
                PressType = pressType;
            }
        }
    }
}
#endif
