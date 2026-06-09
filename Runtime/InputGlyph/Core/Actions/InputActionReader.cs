#if INPUTSYSTEM_SUPPORT
using System;
using System.Runtime.CompilerServices;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;

/// <summary>
/// 输入动作读取工具，提供值读取、按键一次触发、切换状态和组合按键读取。
/// </summary>
public static class InputActionReader
{
    private const int InitialStateCapacity = 64;

    private static readonly InputReadStateMap PressedKeys = new InputReadStateMap(InitialStateCapacity);
    private static readonly InputReadStateMap ToggledKeys = new InputReadStateMap(InitialStateCapacity);
    private static readonly CompositePartControlMap CompositePartControls = new CompositePartControlMap(32);

    /// <summary>
    /// 根据动作名称解析输入动作。
    /// </summary>
    /// <param name="actionName">要解析的输入动作名称。</param>
    /// <returns>解析到的输入动作；名称为空或未找到时返回 null。</returns>
    public static InputAction ResolveAction(string actionName)
    {
        return string.IsNullOrWhiteSpace(actionName) ? null : InputActionResolver.Action(actionName);
    }

    /// <summary>
    /// 清空组合按键控制缓存。
    /// </summary>
    public static void ClearBindingCaches()
    {
        CompositePartControls.Clear();
    }

    /// <summary>
    /// 读取指定输入动作的当前值。
    /// </summary>
    /// <typeparam name="T">要读取的值类型。</typeparam>
    /// <param name="action">要读取值的输入动作。</param>
    /// <returns>输入动作的当前值；动作为空时返回默认值。</returns>
    public static T ReadValue<T>(InputAction action) where T : struct
    {
        return action != null ? action.ReadValue<T>() : default;
    }

    /// <summary>
    /// 根据动作名称读取输入动作的当前值。
    /// </summary>
    /// <typeparam name="T">要读取的值类型。</typeparam>
    /// <param name="actionName">要读取值的输入动作名称。</param>
    /// <returns>输入动作的当前值；动作不存在时返回默认值。</returns>
    public static T ReadValue<T>(string actionName) where T : struct
    {
        return ReadValue<T>(ResolveAction(actionName));
    }

    /// <summary>
    /// 当输入动作处于按下状态时尝试读取当前值。
    /// </summary>
    /// <typeparam name="T">要读取的值类型。</typeparam>
    /// <param name="action">要读取值的输入动作。</param>
    /// <param name="value">输出输入动作的当前值；读取失败时为默认值。</param>
    /// <returns>动作存在且处于按下状态时返回 true，否则返回 false。</returns>
    public static bool TryReadValue<T>(InputAction action, out T value) where T : struct
    {
        if (action != null && action.IsPressed())
        {
            value = action.ReadValue<T>();
            return true;
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 根据动作名称，在输入动作处于按下状态时尝试读取当前值。
    /// </summary>
    /// <typeparam name="T">要读取的值类型。</typeparam>
    /// <param name="actionName">要读取值的输入动作名称。</param>
    /// <param name="value">输出输入动作的当前值；读取失败时为默认值。</param>
    /// <returns>动作存在且处于按下状态时返回 true，否则返回 false。</returns>
    public static bool TryReadValue<T>(string actionName, out T value) where T : struct
    {
        return TryReadValue(ResolveAction(actionName), out value);
    }

    /// <summary>
    /// 按拥有者区分状态，在输入动作本次按下时只读取一次值。
    /// </summary>
    /// <typeparam name="T">要读取的值类型。</typeparam>
    /// <param name="owner">用于隔离一次触发状态的拥有者对象。</param>
    /// <param name="action">要读取值的输入动作。</param>
    /// <param name="value">输出输入动作的当前值；读取失败时为默认值。</param>
    /// <returns>动作本次按下周期首次触发并成功读取值时返回 true，否则返回 false。</returns>
    public static bool TryReadValueOnce<T>(Object owner, InputAction action, out T value) where T : struct
    {
        if (owner == null)
        {
            value = default;
            return false;
        }

        return TryReadValueOnceInternal(action, null, null, owner.GetInstanceID(), null, out value);
    }

    /// <summary>
    /// 根据动作名称并按拥有者区分状态，在输入动作本次按下时只读取一次值。
    /// </summary>
    /// <typeparam name="T">要读取的值类型。</typeparam>
    /// <param name="owner">用于隔离一次触发状态的拥有者对象。</param>
    /// <param name="actionName">要读取值的输入动作名称。</param>
    /// <param name="value">输出输入动作的当前值；读取失败时为默认值。</param>
    /// <returns>动作本次按下周期首次触发并成功读取值时返回 true，否则返回 false。</returns>
    public static bool TryReadValueOnce<T>(Object owner, string actionName, out T value) where T : struct
    {
        if (owner == null)
        {
            value = default;
            return false;
        }

        InputAction action = ResolveAction(actionName);
        return TryReadValueOnceInternal(action, actionName, null, owner.GetInstanceID(), null, out value);
    }

    /// <summary>
    /// 判断按钮类型输入动作当前是否按下。
    /// </summary>
    /// <param name="action">要读取的按钮类型输入动作。</param>
    /// <returns>动作存在、类型为按钮且当前按下时返回 true，否则返回 false。</returns>
    public static bool ReadButton(InputAction action)
    {
        return action != null && action.type == InputActionType.Button && action.IsPressed();
    }

    /// <summary>
    /// 根据动作名称判断按钮类型输入动作当前是否按下。
    /// </summary>
    /// <param name="actionName">要读取的按钮类型输入动作名称。</param>
    /// <returns>动作存在、类型为按钮且当前按下时返回 true，否则返回 false。</returns>
    public static bool ReadButton(string actionName)
    {
        return ReadButton(ResolveAction(actionName));
    }

    /// <summary>
    /// 判断输入动作当前是否处于按下状态。
    /// </summary>
    /// <param name="action">要读取按下状态的输入动作。</param>
    /// <returns>动作存在且当前处于按下状态时返回 true，否则返回 false。</returns>
    public static bool ReadPressed(InputAction action)
    {
        return action != null && action.IsPressed();
    }

    /// <summary>
    /// 根据动作名称判断输入动作当前是否处于按下状态。
    /// </summary>
    /// <param name="actionName">要读取按下状态的输入动作名称。</param>
    /// <returns>动作存在且当前处于按下状态时返回 true，否则返回 false。</returns>
    public static bool ReadPressed(string actionName)
    {
        return ReadPressed(ResolveAction(actionName));
    }

    /// <summary>
    /// 按拥有者区分状态，判断输入动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="owner">用于隔离一次触发状态的拥有者对象。</param>
    /// <param name="action">要读取按下状态的输入动作。</param>
    /// <returns>动作在当前拥有者状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadPressedOnce(Object owner, InputAction action)
    {
        return owner != null && ReadPressedOnce(owner.GetInstanceID(), action);
    }

    /// <summary>
    /// 根据动作名称并按拥有者区分状态，判断输入动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="owner">用于隔离一次触发状态的拥有者对象。</param>
    /// <param name="actionName">要读取按下状态的输入动作名称。</param>
    /// <returns>动作在当前拥有者状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadPressedOnce(Object owner, string actionName)
    {
        return owner != null && ReadPressedOnce(owner.GetInstanceID(), actionName);
    }

    /// <summary>
    /// 按拥有者 ID 区分状态，判断输入动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="ownerId">用于隔离一次触发状态的拥有者 ID。</param>
    /// <param name="action">要读取按下状态的输入动作。</param>
    /// <returns>动作在当前拥有者 ID 状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadPressedOnce(int ownerId, InputAction action)
    {
        return ReadButtonOnceInternal(action, null, null, ownerId, null, ReadPressed(action));
    }

    /// <summary>
    /// 根据动作名称并按拥有者 ID 区分状态，判断输入动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="ownerId">用于隔离一次触发状态的拥有者 ID。</param>
    /// <param name="actionName">要读取按下状态的输入动作名称。</param>
    /// <returns>动作在当前拥有者 ID 状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadPressedOnce(int ownerId, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(action, actionName, null, ownerId, null, ReadPressed(action));
    }

    /// <summary>
    /// 按自定义键区分状态，判断输入动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="key">用于隔离一次触发状态的自定义键。</param>
    /// <param name="action">要读取按下状态的输入动作。</param>
    /// <returns>动作在当前自定义键状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadPressedOnce(string key, InputAction action)
    {
        return ReadButtonOnceInternal(action, null, null, 0, key, ReadPressed(action));
    }

    /// <summary>
    /// 根据动作名称并按自定义键区分状态，判断输入动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="key">用于隔离一次触发状态的自定义键。</param>
    /// <param name="actionName">要读取按下状态的输入动作名称。</param>
    /// <returns>动作在当前自定义键状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadPressedOnce(string key, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(action, actionName, null, 0, key, ReadPressed(action));
    }

    /// <summary>
    /// 按拥有者区分状态，在输入动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="owner">用于隔离切换状态的拥有者对象。</param>
    /// <param name="action">要读取按下状态的输入动作。</param>
    /// <returns>动作触发后当前缓存的切换状态。</returns>
    public static bool ReadPressedToggle(Object owner, InputAction action)
    {
        return owner != null && ReadPressedToggle(owner.GetInstanceID(), action);
    }

    /// <summary>
    /// 根据动作名称并按拥有者区分状态，在输入动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="owner">用于隔离切换状态的拥有者对象。</param>
    /// <param name="actionName">要读取按下状态的输入动作名称。</param>
    /// <returns>动作触发后当前缓存的切换状态。</returns>
    public static bool ReadPressedToggle(Object owner, string actionName)
    {
        return owner != null && ReadPressedToggle(owner.GetInstanceID(), actionName);
    }

    /// <summary>
    /// 按拥有者 ID 区分状态，在输入动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="ownerId">用于隔离切换状态的拥有者 ID。</param>
    /// <param name="action">要读取按下状态的输入动作。</param>
    /// <returns>动作触发后当前缓存的切换状态。</returns>
    public static bool ReadPressedToggle(int ownerId, InputAction action)
    {
        return ReadButtonToggleInternal(action, null, null, ownerId, null, ReadPressed(action));
    }

    /// <summary>
    /// 根据动作名称并按拥有者 ID 区分状态，在输入动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="ownerId">用于隔离切换状态的拥有者 ID。</param>
    /// <param name="actionName">要读取按下状态的输入动作名称。</param>
    /// <returns>动作触发后当前缓存的切换状态。</returns>
    public static bool ReadPressedToggle(int ownerId, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(action, actionName, null, ownerId, null, ReadPressed(action));
    }

    /// <summary>
    /// 按自定义键区分状态，在输入动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="key">用于隔离切换状态的自定义键。</param>
    /// <param name="action">要读取按下状态的输入动作。</param>
    /// <returns>动作触发后当前缓存的切换状态。</returns>
    public static bool ReadPressedToggle(string key, InputAction action)
    {
        return ReadButtonToggleInternal(action, null, null, 0, key, ReadPressed(action));
    }

    /// <summary>
    /// 根据动作名称并按自定义键区分状态，在输入动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="key">用于隔离切换状态的自定义键。</param>
    /// <param name="actionName">要读取按下状态的输入动作名称。</param>
    /// <returns>动作触发后当前缓存的切换状态。</returns>
    public static bool ReadPressedToggle(string key, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(action, actionName, null, 0, key, ReadPressed(action));
    }

    /// <summary>
    /// 按拥有者区分状态，判断按钮动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="owner">用于隔离一次触发状态的拥有者对象。</param>
    /// <param name="action">要读取的按钮类型输入动作。</param>
    /// <returns>按钮动作在当前拥有者状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadButtonOnce(Object owner, InputAction action)
    {
        return owner != null && ReadButtonOnce(owner.GetInstanceID(), action);
    }

    /// <summary>
    /// 根据动作名称并按拥有者区分状态，判断按钮动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="owner">用于隔离一次触发状态的拥有者对象。</param>
    /// <param name="actionName">要读取的按钮类型输入动作名称。</param>
    /// <returns>按钮动作在当前拥有者状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadButtonOnce(Object owner, string actionName)
    {
        return owner != null && ReadButtonOnce(owner.GetInstanceID(), actionName);
    }

    /// <summary>
    /// 按拥有者 ID 区分状态，判断按钮动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="ownerId">用于隔离一次触发状态的拥有者 ID。</param>
    /// <param name="action">要读取的按钮类型输入动作。</param>
    /// <returns>按钮动作在当前拥有者 ID 状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadButtonOnce(int ownerId, InputAction action)
    {
        return ReadButtonOnceInternal(action, null, null, ownerId, null, ReadButton(action));
    }

    /// <summary>
    /// 根据动作名称并按拥有者 ID 区分状态，判断按钮动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="ownerId">用于隔离一次触发状态的拥有者 ID。</param>
    /// <param name="actionName">要读取的按钮类型输入动作名称。</param>
    /// <returns>按钮动作在当前拥有者 ID 状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadButtonOnce(int ownerId, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(action, actionName, null, ownerId, null, ReadButton(action));
    }

    /// <summary>
    /// 按自定义键区分状态，判断按钮动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="key">用于隔离一次触发状态的自定义键。</param>
    /// <param name="action">要读取的按钮类型输入动作。</param>
    /// <returns>按钮动作在当前自定义键状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadButtonOnce(string key, InputAction action)
    {
        return ReadButtonOnceInternal(action, null, null, 0, key, ReadButton(action));
    }

    /// <summary>
    /// 根据动作名称并按自定义键区分状态，判断按钮动作是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="key">用于隔离一次触发状态的自定义键。</param>
    /// <param name="actionName">要读取的按钮类型输入动作名称。</param>
    /// <returns>按钮动作在当前自定义键状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadButtonOnce(string key, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(action, actionName, null, 0, key, ReadButton(action));
    }

    /// <summary>
    /// 按拥有者区分状态，在按钮动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="owner">用于隔离切换状态的拥有者对象。</param>
    /// <param name="action">要读取的按钮类型输入动作。</param>
    /// <returns>按钮动作触发后当前缓存的切换状态。</returns>
    public static bool ReadButtonToggle(Object owner, InputAction action)
    {
        return owner != null && ReadButtonToggle(owner.GetInstanceID(), action);
    }

    /// <summary>
    /// 根据动作名称并按拥有者区分状态，在按钮动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="owner">用于隔离切换状态的拥有者对象。</param>
    /// <param name="actionName">要读取的按钮类型输入动作名称。</param>
    /// <returns>按钮动作触发后当前缓存的切换状态。</returns>
    public static bool ReadButtonToggle(Object owner, string actionName)
    {
        return owner != null && ReadButtonToggle(owner.GetInstanceID(), actionName);
    }

    /// <summary>
    /// 按拥有者 ID 区分状态，在按钮动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="ownerId">用于隔离切换状态的拥有者 ID。</param>
    /// <param name="action">要读取的按钮类型输入动作。</param>
    /// <returns>按钮动作触发后当前缓存的切换状态。</returns>
    public static bool ReadButtonToggle(int ownerId, InputAction action)
    {
        return ReadButtonToggleInternal(action, null, null, ownerId, null, ReadButton(action));
    }

    /// <summary>
    /// 根据动作名称并按拥有者 ID 区分状态，在按钮动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="ownerId">用于隔离切换状态的拥有者 ID。</param>
    /// <param name="actionName">要读取的按钮类型输入动作名称。</param>
    /// <returns>按钮动作触发后当前缓存的切换状态。</returns>
    public static bool ReadButtonToggle(int ownerId, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(action, actionName, null, ownerId, null, ReadButton(action));
    }

    /// <summary>
    /// 按自定义键区分状态，在按钮动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="key">用于隔离切换状态的自定义键。</param>
    /// <param name="action">要读取的按钮类型输入动作。</param>
    /// <returns>按钮动作触发后当前缓存的切换状态。</returns>
    public static bool ReadButtonToggle(string key, InputAction action)
    {
        return ReadButtonToggleInternal(action, null, null, 0, key, ReadButton(action));
    }

    /// <summary>
    /// 根据动作名称并按自定义键区分状态，在按钮动作每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="key">用于隔离切换状态的自定义键。</param>
    /// <param name="actionName">要读取的按钮类型输入动作名称。</param>
    /// <returns>按钮动作触发后当前缓存的切换状态。</returns>
    public static bool ReadButtonToggle(string key, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(action, actionName, null, 0, key, ReadButton(action));
    }

    /// <summary>
    /// 读取组合绑定中指定部分的按钮按下状态。
    /// </summary>
    /// <param name="action">包含组合绑定的输入动作。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分存在且当前按下时返回 true，否则返回 false。</returns>
    public static bool ReadCompositePartButton(InputAction action, string compositePartName)
    {
        if (action == null || !action.enabled || string.IsNullOrWhiteSpace(compositePartName))
        {
            return false;
        }

        if (CompositePartControls.TryGet(action, compositePartName, out InputControl cachedControl))
        {
            return cachedControl != null && cachedControl.IsPressed();
        }

        InputControl control = ResolveCompositePartControl(action, compositePartName);
        CompositePartControls.Set(action, compositePartName, control);
        return control != null && control.IsPressed();
    }

    /// <summary>
    /// 根据动作名称读取组合绑定中指定部分的按钮按下状态。
    /// </summary>
    /// <param name="actionName">包含组合绑定的输入动作名称。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分存在且当前按下时返回 true，否则返回 false。</returns>
    public static bool ReadCompositePartButton(string actionName, string compositePartName)
    {
        return ReadCompositePartButton(ResolveAction(actionName), compositePartName);
    }

    /// <summary>
    /// 按拥有者区分状态，判断组合绑定指定部分是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="owner">用于隔离一次触发状态的拥有者对象。</param>
    /// <param name="action">包含组合绑定的输入动作。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分在当前拥有者状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadCompositePartButtonOnce(Object owner, InputAction action, string compositePartName)
    {
        return owner != null && ReadCompositePartButtonOnce(owner.GetInstanceID(), action, compositePartName);
    }

    /// <summary>
    /// 根据动作名称并按拥有者区分状态，判断组合绑定指定部分是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="owner">用于隔离一次触发状态的拥有者对象。</param>
    /// <param name="actionName">包含组合绑定的输入动作名称。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分在当前拥有者状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadCompositePartButtonOnce(Object owner, string actionName, string compositePartName)
    {
        return owner != null && ReadCompositePartButtonOnce(owner.GetInstanceID(), actionName, compositePartName);
    }

    /// <summary>
    /// 按拥有者 ID 区分状态，判断组合绑定指定部分是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="ownerId">用于隔离一次触发状态的拥有者 ID。</param>
    /// <param name="action">包含组合绑定的输入动作。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分在当前拥有者 ID 状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadCompositePartButtonOnce(int ownerId, InputAction action, string compositePartName)
    {
        return ReadButtonOnceInternal(
            action,
            null,
            compositePartName,
            ownerId,
            null,
            ReadCompositePartButton(action, compositePartName));
    }

    /// <summary>
    /// 根据动作名称并按拥有者 ID 区分状态，判断组合绑定指定部分是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="ownerId">用于隔离一次触发状态的拥有者 ID。</param>
    /// <param name="actionName">包含组合绑定的输入动作名称。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分在当前拥有者 ID 状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadCompositePartButtonOnce(int ownerId, string actionName, string compositePartName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(
            action,
            actionName,
            compositePartName,
            ownerId,
            null,
            ReadCompositePartButton(action, compositePartName));
    }

    /// <summary>
    /// 按自定义键区分状态，判断组合绑定指定部分是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="key">用于隔离一次触发状态的自定义键。</param>
    /// <param name="action">包含组合绑定的输入动作。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分在当前自定义键状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadCompositePartButtonOnce(string key, InputAction action, string compositePartName)
    {
        return ReadButtonOnceInternal(
            action,
            null,
            compositePartName,
            0,
            key,
            ReadCompositePartButton(action, compositePartName));
    }

    /// <summary>
    /// 根据动作名称并按自定义键区分状态，判断组合绑定指定部分是否在本次按下周期首次按下。
    /// </summary>
    /// <param name="key">用于隔离一次触发状态的自定义键。</param>
    /// <param name="actionName">包含组合绑定的输入动作名称。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分在当前自定义键状态下本次按下周期首次按下时返回 true，否则返回 false。</returns>
    public static bool ReadCompositePartButtonOnce(string key, string actionName, string compositePartName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonOnceInternal(
            action,
            actionName,
            compositePartName,
            0,
            key,
            ReadCompositePartButton(action, compositePartName));
    }

    /// <summary>
    /// 按拥有者区分状态，在组合绑定指定部分每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="owner">用于隔离切换状态的拥有者对象。</param>
    /// <param name="action">包含组合绑定的输入动作。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分触发后当前缓存的切换状态。</returns>
    public static bool ReadCompositePartButtonToggle(Object owner, InputAction action, string compositePartName)
    {
        return owner != null && ReadCompositePartButtonToggle(owner.GetInstanceID(), action, compositePartName);
    }

    /// <summary>
    /// 根据动作名称并按拥有者区分状态，在组合绑定指定部分每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="owner">用于隔离切换状态的拥有者对象。</param>
    /// <param name="actionName">包含组合绑定的输入动作名称。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分触发后当前缓存的切换状态。</returns>
    public static bool ReadCompositePartButtonToggle(Object owner, string actionName, string compositePartName)
    {
        return owner != null && ReadCompositePartButtonToggle(owner.GetInstanceID(), actionName, compositePartName);
    }

    /// <summary>
    /// 按拥有者 ID 区分状态，在组合绑定指定部分每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="ownerId">用于隔离切换状态的拥有者 ID。</param>
    /// <param name="action">包含组合绑定的输入动作。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分触发后当前缓存的切换状态。</returns>
    public static bool ReadCompositePartButtonToggle(int ownerId, InputAction action, string compositePartName)
    {
        return ReadButtonToggleInternal(
            action,
            null,
            compositePartName,
            ownerId,
            null,
            ReadCompositePartButton(action, compositePartName));
    }

    /// <summary>
    /// 根据动作名称并按拥有者 ID 区分状态，在组合绑定指定部分每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="ownerId">用于隔离切换状态的拥有者 ID。</param>
    /// <param name="actionName">包含组合绑定的输入动作名称。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分触发后当前缓存的切换状态。</returns>
    public static bool ReadCompositePartButtonToggle(int ownerId, string actionName, string compositePartName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(
            action,
            actionName,
            compositePartName,
            ownerId,
            null,
            ReadCompositePartButton(action, compositePartName));
    }

    /// <summary>
    /// 按自定义键区分状态，在组合绑定指定部分每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="key">用于隔离切换状态的自定义键。</param>
    /// <param name="action">包含组合绑定的输入动作。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分触发后当前缓存的切换状态。</returns>
    public static bool ReadCompositePartButtonToggle(string key, InputAction action, string compositePartName)
    {
        return ReadButtonToggleInternal(
            action,
            null,
            compositePartName,
            0,
            key,
            ReadCompositePartButton(action, compositePartName));
    }

    /// <summary>
    /// 根据动作名称并按自定义键区分状态，在组合绑定指定部分每次首次按下时切换布尔状态。
    /// </summary>
    /// <param name="key">用于隔离切换状态的自定义键。</param>
    /// <param name="actionName">包含组合绑定的输入动作名称。</param>
    /// <param name="compositePartName">要读取的组合绑定部分名称。</param>
    /// <returns>组合绑定部分触发后当前缓存的切换状态。</returns>
    public static bool ReadCompositePartButtonToggle(string key, string actionName, string compositePartName)
    {
        InputAction action = ResolveAction(actionName);
        return ReadButtonToggleInternal(
            action,
            actionName,
            compositePartName,
            0,
            key,
            ReadCompositePartButton(action, compositePartName));
    }

    /// <summary>
    /// 重置指定自定义键和输入动作对应的切换状态。
    /// </summary>
    /// <param name="key">用于定位切换状态的自定义键。</param>
    /// <param name="action">要重置切换状态的输入动作。</param>
    public static void ResetToggledButton(string key, InputAction action)
    {
        Guid actionId = GetActionId(action);
        ToggledKeys.Remove(in actionId, string.Empty, string.Empty, 0, key);
    }

    /// <summary>
    /// 根据动作名称重置指定自定义键对应的切换状态。
    /// </summary>
    /// <param name="key">用于定位切换状态的自定义键。</param>
    /// <param name="actionName">要重置切换状态的输入动作名称。</param>
    public static void ResetToggledButton(string key, string actionName)
    {
        InputAction action = ResolveAction(actionName);
        Guid actionId = GetActionId(action);
        string actionKey = action == null ? Normalize(actionName) : string.Empty;
        ToggledKeys.Remove(in actionId, actionKey, string.Empty, 0, key);
    }

    /// <summary>
    /// 重置指定自定义键、输入动作和组合绑定部分对应的切换状态。
    /// </summary>
    /// <param name="key">用于定位切换状态的自定义键。</param>
    /// <param name="action">要重置切换状态的输入动作。</param>
    /// <param name="compositePartName">要重置切换状态的组合绑定部分名称。</param>
    public static void ResetToggledCompositePartButton(string key, InputAction action, string compositePartName)
    {
        Guid actionId = GetActionId(action);
        ToggledKeys.Remove(in actionId, string.Empty, compositePartName, 0, key);
    }

    /// <summary>
    /// 根据动作名称重置指定自定义键和组合绑定部分对应的切换状态。
    /// </summary>
    /// <param name="key">用于定位切换状态的自定义键。</param>
    /// <param name="actionName">要重置切换状态的输入动作名称。</param>
    /// <param name="compositePartName">要重置切换状态的组合绑定部分名称。</param>
    public static void ResetToggledCompositePartButton(string key, string actionName, string compositePartName)
    {
        InputAction action = ResolveAction(actionName);
        Guid actionId = GetActionId(action);
        string actionKey = action == null ? Normalize(actionName) : string.Empty;
        ToggledKeys.Remove(in actionId, actionKey, compositePartName, 0, key);
    }

    /// <summary>
    /// 重置指定输入动作的全部切换状态。
    /// </summary>
    /// <param name="action">要重置全部切换状态的输入动作。</param>
    public static void ResetToggledButton(InputAction action)
    {
        if (action == null)
        {
            return;
        }

        Guid actionId = action.id;
        ToggledKeys.RemoveByAction(in actionId);
    }

    /// <summary>
    /// 根据动作名称重置指定输入动作的全部切换状态。
    /// </summary>
    /// <param name="actionName">要重置全部切换状态的输入动作名称。</param>
    public static void ResetToggledButton(string actionName)
    {
        InputAction action = ResolveAction(actionName);
        if (action != null)
        {
            Guid actionId = action.id;
            ToggledKeys.RemoveByAction(in actionId);
            return;
        }

        ToggledKeys.RemoveByActionName(actionName);
    }

    /// <summary>
    /// 重置所有已记录的切换状态。
    /// </summary>
    public static void ResetToggledButtons()
    {
        ToggledKeys.Clear();
    }

    /// <summary>
    /// 内部实现：在按下周期首次触发时读取动作值。
    /// </summary>
    private static bool TryReadValueOnceInternal<T>(
        InputAction action,
        string actionName,
        string compositePartName,
        int ownerId,
        string ownerKey,
        out T value) where T : struct
    {
        Guid actionId = GetActionId(action);
        string actionKey = action != null ? string.Empty : Normalize(actionName);
        if (action != null && action.IsPressed())
        {
            if (PressedKeys.Add(in actionId, actionKey, compositePartName, ownerId, ownerKey))
            {
                value = action.ReadValue<T>();
                return true;
            }
        }
        else
        {
            PressedKeys.Remove(in actionId, actionKey, compositePartName, ownerId, ownerKey);
        }

        value = default;
        return false;
    }

    /// <summary>
    /// 内部实现：根据当前按下状态维护一次触发记录。
    /// </summary>
    private static bool ReadButtonOnceInternal(
        InputAction action,
        string actionName,
        string compositePartName,
        int ownerId,
        string ownerKey,
        bool isPressed)
    {
        Guid actionId = GetActionId(action);
        string actionKey = action != null ? string.Empty : Normalize(actionName);
        if (isPressed)
        {
            return PressedKeys.Add(in actionId, actionKey, compositePartName, ownerId, ownerKey);
        }

        PressedKeys.Remove(in actionId, actionKey, compositePartName, ownerId, ownerKey);
        return false;
    }

    /// <summary>
    /// 内部实现：在一次触发时切换并返回缓存的布尔状态。
    /// </summary>
    private static bool ReadButtonToggleInternal(
        InputAction action,
        string actionName,
        string compositePartName,
        int ownerId,
        string ownerKey,
        bool isPressed)
    {
        Guid actionId = GetActionId(action);
        string actionKey = action != null ? string.Empty : Normalize(actionName);
        if (ReadButtonOnceInternal(action, actionName, compositePartName, ownerId, ownerKey, isPressed))
        {
            if (!ToggledKeys.Add(in actionId, actionKey, compositePartName, ownerId, ownerKey))
            {
                ToggledKeys.Remove(in actionId, actionKey, compositePartName, ownerId, ownerKey);
            }
        }

        return ToggledKeys.Contains(in actionId, actionKey, compositePartName, ownerId, ownerKey);
    }

    /// <summary>
    /// 从动作绑定中解析指定组合部分实际对应的输入控制。
    /// </summary>
    private static InputControl ResolveCompositePartControl(InputAction action, string compositePartName)
    {
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding binding = action.bindings[i];
            if (!IsCompositePart(binding, compositePartName))
            {
                continue;
            }

            string path = GetEffectivePath(binding);
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            var controls = action.controls;
            for (int c = 0; c < controls.Count; c++)
            {
                InputControl control = controls[c];
                if (InputControlPath.Matches(path, control))
                {
                    return control;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 判断绑定是否为指定名称的组合绑定部分。
    /// </summary>
    private static bool IsCompositePart(InputBinding binding, string compositePartName)
    {
        return binding.isPartOfComposite
               && InputGlyphStringUtility.EqualsIgnoreCase(binding.name, compositePartName);
    }

    /// <summary>
    /// 获取绑定的有效控制路径，优先使用重绑定后的路径。
    /// </summary>
    private static string GetEffectivePath(InputBinding binding)
    {
        return string.IsNullOrWhiteSpace(binding.effectivePath) ? binding.path : binding.effectivePath;
    }

    /// <summary>
    /// 获取动作 ID，动作为空时返回空 Guid。
    /// </summary>
    private static Guid GetActionId(InputAction action)
    {
        return action != null ? action.id : Guid.Empty;
    }

    /// <summary>
    /// 将空字符串引用规范化为空字符串。
    /// </summary>
    private static string Normalize(string value)
    {
        return value ?? string.Empty;
    }

    /// <summary>
    /// 记录按键一次触发和切换状态的线性探测哈希表。
    /// </summary>
    private sealed class InputReadStateMap
    {
        private Guid[] _actionIds;
        private string[] _actionNames;
        private string[] _compositePartNames;
        private int[] _ownerIds;
        private string[] _ownerKeys;
        private int[] _hashes;
        private bool[] _occupied;
        private int _mask;
        private int _count;

        /// <summary>
        /// 初始化读取状态缓存表。
        /// </summary>
        /// <param name="capacity">缓存表的初始容量。</param>
        public InputReadStateMap(int capacity)
        {
            int resolvedCapacity = NextPowerOfTwo(capacity);
            _actionIds = new Guid[resolvedCapacity];
            _actionNames = new string[resolvedCapacity];
            _compositePartNames = new string[resolvedCapacity];
            _ownerIds = new int[resolvedCapacity];
            _ownerKeys = new string[resolvedCapacity];
            _hashes = new int[resolvedCapacity];
            _occupied = new bool[resolvedCapacity];
            _mask = resolvedCapacity - 1;
        }

        /// <summary>
        /// 清空所有读取状态记录。
        /// </summary>
        public void Clear()
        {
            Array.Clear(_actionIds, 0, _actionIds.Length);
            Array.Clear(_actionNames, 0, _actionNames.Length);
            Array.Clear(_compositePartNames, 0, _compositePartNames.Length);
            Array.Clear(_ownerIds, 0, _ownerIds.Length);
            Array.Clear(_ownerKeys, 0, _ownerKeys.Length);
            Array.Clear(_hashes, 0, _hashes.Length);
            Array.Clear(_occupied, 0, _occupied.Length);
            _count = 0;
        }

        /// <summary>
        /// 判断指定动作、组合部分和拥有者对应的状态是否存在。
        /// </summary>
        /// <param name="actionId">用于定位状态记录的输入动作 ID。</param>
        /// <param name="actionName">用于定位状态记录的输入动作名称。</param>
        /// <param name="compositePartName">用于定位状态记录的组合绑定部分名称。</param>
        /// <param name="ownerId">用于隔离状态记录的拥有者 ID。</param>
        /// <param name="ownerKey">用于隔离状态记录的自定义键。</param>
        /// <returns>找到状态记录时返回 true，否则返回 false。</returns>
        public bool Contains(
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            int hash = BuildHash(in actionId, actionName, compositePartName, ownerId, ownerKey);
            return FindSlot(hash, in actionId, actionName, compositePartName, ownerId, ownerKey) >= 0;
        }

        /// <summary>
        /// 添加一条读取状态记录，已存在时返回 false。
        /// </summary>
        /// <param name="actionId">用于定位状态记录的输入动作 ID。</param>
        /// <param name="actionName">用于定位状态记录的输入动作名称。</param>
        /// <param name="compositePartName">用于定位状态记录的组合绑定部分名称。</param>
        /// <param name="ownerId">用于隔离状态记录的拥有者 ID。</param>
        /// <param name="ownerKey">用于隔离状态记录的自定义键。</param>
        /// <returns>成功添加新状态记录时返回 true；记录已存在时返回 false。</returns>
        public bool Add(
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            if ((_count + 1) * 2 >= _occupied.Length)
            {
                Resize(_occupied.Length << 1);
            }

            int hash = BuildHash(in actionId, actionName, compositePartName, ownerId, ownerKey);
            if (FindSlot(hash, in actionId, actionName, compositePartName, ownerId, ownerKey) >= 0)
            {
                return false;
            }

            SetInternal(hash, in actionId, actionName, compositePartName, ownerId, ownerKey);
            return true;
        }

        /// <summary>
        /// 移除指定动作、组合部分和拥有者对应的状态记录。
        /// </summary>
        /// <param name="actionId">用于定位状态记录的输入动作 ID。</param>
        /// <param name="actionName">用于定位状态记录的输入动作名称。</param>
        /// <param name="compositePartName">用于定位状态记录的组合绑定部分名称。</param>
        /// <param name="ownerId">用于隔离状态记录的拥有者 ID。</param>
        /// <param name="ownerKey">用于隔离状态记录的自定义键。</param>
        /// <returns>成功移除状态记录时返回 true，否则返回 false。</returns>
        public bool Remove(
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            int hash = BuildHash(in actionId, actionName, compositePartName, ownerId, ownerKey);
            int slot = FindSlot(hash, in actionId, actionName, compositePartName, ownerId, ownerKey);
            if (slot < 0)
            {
                return false;
            }

            RemoveAt(slot);
            return true;
        }

        /// <summary>
        /// 移除指定动作 ID 对应的全部状态记录。
        /// </summary>
        /// <param name="actionId">要移除全部状态记录的输入动作 ID。</param>
        public void RemoveByAction(in Guid actionId)
        {
            for (int i = 0; i < _occupied.Length;)
            {
                if (_occupied[i] && _actionIds[i].Equals(actionId))
                {
                    RemoveAt(i);
                    continue;
                }

                i++;
            }
        }

        /// <summary>
        /// 移除指定动作名称对应的全部状态记录。
        /// </summary>
        /// <param name="actionName">要移除全部状态记录的输入动作名称。</param>
        public void RemoveByActionName(string actionName)
        {
            string normalizedActionName = Normalize(actionName);
            for (int i = 0; i < _occupied.Length;)
            {
                if (_occupied[i] && InputGlyphStringUtility.EqualsOrdinal(_actionNames[i], normalizedActionName))
                {
                    RemoveAt(i);
                    continue;
                }

                i++;
            }
        }

        /// <summary>
        /// 在线性探测哈希表中查找匹配记录所在槽位。
        /// </summary>
        private int FindSlot(
            int hash,
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            int slot = hash & _mask;
            int startSlot = slot;
            do
            {
                if (!_occupied[slot])
                {
                    return -1;
                }

                if (_hashes[slot] == hash
                    && Matches(slot, in actionId, actionName, compositePartName, ownerId, ownerKey))
                {
                    return slot;
                }

                slot = (slot + 1) & _mask;
            }
            while (slot != startSlot);

            return -1;
        }

        /// <summary>
        /// 判断指定槽位中的记录是否与查询键完全匹配。
        /// </summary>
        private bool Matches(
            int slot,
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            return _actionIds[slot].Equals(actionId)
                   && _ownerIds[slot] == ownerId
                   && InputGlyphStringUtility.EqualsOrdinal(_actionNames[slot], Normalize(actionName))
                   && InputGlyphStringUtility.EqualsOrdinal(_compositePartNames[slot], Normalize(compositePartName))
                   && InputGlyphStringUtility.EqualsOrdinal(_ownerKeys[slot], Normalize(ownerKey));
        }

        /// <summary>
        /// 在线性探测哈希表中插入一条状态记录。
        /// </summary>
        private void SetInternal(
            int hash,
            in Guid actionId,
            string actionName,
            string compositePartName,
            int ownerId,
            string ownerKey)
        {
            int slot = hash & _mask;
            while (_occupied[slot])
            {
                slot = (slot + 1) & _mask;
            }

            _actionIds[slot] = actionId;
            _actionNames[slot] = Normalize(actionName);
            _compositePartNames[slot] = Normalize(compositePartName);
            _ownerIds[slot] = ownerId;
            _ownerKeys[slot] = Normalize(ownerKey);
            _hashes[slot] = hash;
            _occupied[slot] = true;
            _count++;
        }

        /// <summary>
        /// 删除指定槽位并重新整理后续探测链。
        /// </summary>
        private void RemoveAt(int slot)
        {
            ClearSlot(slot);
            _count--;

            int next = (slot + 1) & _mask;
            while (_occupied[next])
            {
                Guid actionId = _actionIds[next];
                string actionName = _actionNames[next];
                string compositePartName = _compositePartNames[next];
                int ownerId = _ownerIds[next];
                string ownerKey = _ownerKeys[next];
                int hash = _hashes[next];
                ClearSlot(next);
                _count--;
                SetInternal(hash, in actionId, actionName, compositePartName, ownerId, ownerKey);
                next = (next + 1) & _mask;
            }
        }

        /// <summary>
        /// 清理指定槽位中的状态数据。
        /// </summary>
        private void ClearSlot(int slot)
        {
            _actionIds[slot] = Guid.Empty;
            _actionNames[slot] = null;
            _compositePartNames[slot] = null;
            _ownerIds[slot] = 0;
            _ownerKeys[slot] = null;
            _hashes[slot] = 0;
            _occupied[slot] = false;
        }

        /// <summary>
        /// 扩容状态缓存表并重新插入已有记录。
        /// </summary>
        private void Resize(int capacity)
        {
            Guid[] oldActionIds = _actionIds;
            string[] oldActionNames = _actionNames;
            string[] oldCompositePartNames = _compositePartNames;
            int[] oldOwnerIds = _ownerIds;
            string[] oldOwnerKeys = _ownerKeys;
            int[] oldHashes = _hashes;
            bool[] oldOccupied = _occupied;

            _actionIds = new Guid[capacity];
            _actionNames = new string[capacity];
            _compositePartNames = new string[capacity];
            _ownerIds = new int[capacity];
            _ownerKeys = new string[capacity];
            _hashes = new int[capacity];
            _occupied = new bool[capacity];
            _mask = capacity - 1;
            _count = 0;

            for (int i = 0; i < oldOccupied.Length; i++)
            {
                if (oldOccupied[i])
                {
                    Guid actionId = oldActionIds[i];
                    SetInternal(
                        oldHashes[i],
                        in actionId,
                        oldActionNames[i],
                        oldCompositePartNames[i],
                        oldOwnerIds[i],
                        oldOwnerKeys[i]);
                }
            }
        }
    }

    /// <summary>
    /// 缓存动作组合绑定部分到输入控制的解析结果。
    /// </summary>
    private sealed class CompositePartControlMap
    {
        private int[] _actionHashes;
        private Guid[] _actionIds;
        private string[] _partNames;
        private InputControl[] _controls;
        private int[] _hashes;
        private bool[] _occupied;
        private int _mask;
        private int _count;

        /// <summary>
        /// 初始化组合绑定控制缓存表。
        /// </summary>
        /// <param name="capacity">缓存表的初始容量。</param>
        public CompositePartControlMap(int capacity)
        {
            int resolvedCapacity = NextPowerOfTwo(capacity);
            _actionHashes = new int[resolvedCapacity];
            _actionIds = new Guid[resolvedCapacity];
            _partNames = new string[resolvedCapacity];
            _controls = new InputControl[resolvedCapacity];
            _hashes = new int[resolvedCapacity];
            _occupied = new bool[resolvedCapacity];
            _mask = resolvedCapacity - 1;
        }

        /// <summary>
        /// 清空所有组合绑定控制缓存。
        /// </summary>
        public void Clear()
        {
            Array.Clear(_actionHashes, 0, _actionHashes.Length);
            Array.Clear(_actionIds, 0, _actionIds.Length);
            Array.Clear(_partNames, 0, _partNames.Length);
            Array.Clear(_controls, 0, _controls.Length);
            Array.Clear(_hashes, 0, _hashes.Length);
            Array.Clear(_occupied, 0, _occupied.Length);
            _count = 0;
        }

        /// <summary>
        /// 尝试读取指定动作和组合部分对应的缓存输入控制。
        /// </summary>
        /// <param name="action">用于定位缓存记录的输入动作。</param>
        /// <param name="partName">用于定位缓存记录的组合绑定部分名称。</param>
        /// <param name="control">输出缓存的输入控制，未命中时为 null。</param>
        /// <returns>找到缓存记录时返回 true，否则返回 false。</returns>
        public bool TryGet(InputAction action, string partName, out InputControl control)
        {
            control = null;
            int actionHash = RuntimeHelpers.GetHashCode(action);
            Guid actionId = action.id;
            string normalizedPartName = Normalize(partName);
            int hash = BuildCompositeHash(actionHash, in actionId, normalizedPartName);
            int slot = hash & _mask;
            int startSlot = slot;
            do
            {
                if (!_occupied[slot])
                {
                    return false;
                }

                if (_hashes[slot] == hash
                    && _actionHashes[slot] == actionHash
                    && _actionIds[slot].Equals(actionId)
                    && InputGlyphStringUtility.EqualsOrdinal(_partNames[slot], normalizedPartName))
                {
                    control = _controls[slot];
                    return true;
                }

                slot = (slot + 1) & _mask;
            }
            while (slot != startSlot);

            return false;
        }

        /// <summary>
        /// 写入或更新指定动作和组合部分对应的输入控制缓存。
        /// </summary>
        /// <param name="action">用于定位缓存记录的输入动作。</param>
        /// <param name="partName">用于定位缓存记录的组合绑定部分名称。</param>
        /// <param name="control">要缓存的输入控制。</param>
        public void Set(InputAction action, string partName, InputControl control)
        {
            if ((_count + 1) * 2 >= _occupied.Length)
            {
                Resize(_occupied.Length << 1);
            }

            int actionHash = RuntimeHelpers.GetHashCode(action);
            Guid actionId = action.id;
            string normalizedPartName = Normalize(partName);
            int hash = BuildCompositeHash(actionHash, in actionId, normalizedPartName);
            SetInternal(hash, actionHash, in actionId, normalizedPartName, control);
        }

        /// <summary>
        /// 在线性探测哈希表中插入或更新一条组合绑定控制缓存。
        /// </summary>
        private void SetInternal(int hash, int actionHash, in Guid actionId, string partName, InputControl control)
        {
            int slot = hash & _mask;
            while (_occupied[slot])
            {
                if (_hashes[slot] == hash
                    && _actionHashes[slot] == actionHash
                    && _actionIds[slot].Equals(actionId)
                    && InputGlyphStringUtility.EqualsOrdinal(_partNames[slot], partName))
                {
                    _controls[slot] = control;
                    return;
                }

                slot = (slot + 1) & _mask;
            }

            _hashes[slot] = hash;
            _actionHashes[slot] = actionHash;
            _actionIds[slot] = actionId;
            _partNames[slot] = partName;
            _controls[slot] = control;
            _occupied[slot] = true;
            _count++;
        }

        /// <summary>
        /// 扩容组合绑定控制缓存表并重新插入已有记录。
        /// </summary>
        private void Resize(int capacity)
        {
            int[] oldActionHashes = _actionHashes;
            Guid[] oldActionIds = _actionIds;
            string[] oldPartNames = _partNames;
            InputControl[] oldControls = _controls;
            int[] oldHashes = _hashes;
            bool[] oldOccupied = _occupied;

            _actionHashes = new int[capacity];
            _actionIds = new Guid[capacity];
            _partNames = new string[capacity];
            _controls = new InputControl[capacity];
            _hashes = new int[capacity];
            _occupied = new bool[capacity];
            _mask = capacity - 1;
            _count = 0;

            for (int i = 0; i < oldOccupied.Length; i++)
            {
                if (oldOccupied[i])
                {
                    Guid actionId = oldActionIds[i];
                    SetInternal(oldHashes[i], oldActionHashes[i], in actionId, oldPartNames[i], oldControls[i]);
                }
            }
        }
    }

    /// <summary>
    /// 构建读取状态缓存使用的稳定哈希值。
    /// </summary>
    private static int BuildHash(
        in Guid actionId,
        string actionName,
        string compositePartName,
        int ownerId,
        string ownerKey)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + actionId.GetHashCode();
            hash = (hash * 31) + InputGlyphStringUtility.StableHash(Normalize(actionName));
            hash = (hash * 31) + InputGlyphStringUtility.StableHash(Normalize(compositePartName));
            hash = (hash * 31) + ownerId;
            hash = (hash * 31) + InputGlyphStringUtility.StableHash(Normalize(ownerKey));
            return hash == 0 ? 1 : hash;
        }
    }

    /// <summary>
    /// 构建组合绑定控制缓存使用的稳定哈希值。
    /// </summary>
    private static int BuildCompositeHash(int actionHash, in Guid actionId, string partName)
    {
        unchecked
        {
            int hash = 17;
            hash = (hash * 31) + actionHash;
            hash = (hash * 31) + actionId.GetHashCode();
            hash = (hash * 31) + InputGlyphStringUtility.StableHash(partName);
            return hash == 0 ? 1 : hash;
        }
    }

    /// <summary>
    /// 计算大于等于指定值的最小 2 的幂。
    /// </summary>
    private static int NextPowerOfTwo(int value)
    {
        int result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }
}
#endif
