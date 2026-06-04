// using System;
// using System.Collections.Generic;
// using System.Threading.Tasks;
// using TMPro;
// using UnityEngine;
// using UnityEngine.InputSystem;
// using UnityEngine.UI;
//
// public class TestRebindScript : MonoBehaviour
// {
//     [Header("UI")] public UXButton btn;
//     public TextMeshProUGUI bindKeyText;
//     public Image targetImage;
//
//     [Tooltip("如果不使用 actionReference，则用 name 在全局 manager 查找")]
//     public string actionName = "movement";
//
//     [Header("Optional composite part (WASD style)")] [Tooltip("如果需要绑定 composite 的某一部分（例如 Up/Down/Left/Right），填这个；留空表示绑定非 composite 或整体 binding")]
//     public string compositePartName = "";
//
//     [Header("Behavior")] [Tooltip("如果 true，在 Prepare 后自动调用 ConfirmApply() 并保存；否则等待手动 ConfirmPrepared()/CancelPrepared()")]
//     public bool autoConfirm = false;
//
//     /// <summary>
//     /// 启动时初始化并订阅事件
//     /// </summary>
//     private void Start()
//     {
//         if (btn != null) btn.onClick.AddListener(OnBtnClicked);
//         InputDeviceWatcher.OnDeviceContextChanged += OnDeviceChanged;
//         InputBindingManager.BindingsChanged += OnBindingsChanged;
//         UpdateBindingText();
//
//         if (InputBindingManager.Instance != null)
//         {
//             // 订阅事件
//             InputBindingManager.Instance.OnRebindPrepare += OnRebindPrepareHandler;
//             InputBindingManager.Instance.OnApply += OnApplyHandler;
//             InputBindingManager.Instance.OnRebindEnd += OnRebindEndHandler;
//             InputBindingManager.Instance.OnRebindConflict += OnRebindConflictHandler;
//         }
//     }
//
//     /// <summary>
//     /// 禁用时取消订阅事件
//     /// </summary>
//     private void OnDisable()
//     {
//         if (btn != null) btn.onClick.RemoveListener(OnBtnClicked);
//         InputDeviceWatcher.OnDeviceContextChanged -= OnDeviceChanged;
//         InputBindingManager.BindingsChanged -= OnBindingsChanged;
//
//         if (InputBindingManager.Instance != null)
//         {
//             InputBindingManager.Instance.OnRebindPrepare -= OnRebindPrepareHandler;
//             InputBindingManager.Instance.OnApply -= OnApplyHandler;
//             InputBindingManager.Instance.OnRebindEnd -= OnRebindEndHandler;
//             InputBindingManager.Instance.OnRebindConflict -= OnRebindConflictHandler;
//         }
//     }
//
//     /// <summary>
//     /// 重新绑定准备完成的处理器
//     /// </summary>
//     private void OnRebindPrepareHandler(InputBindingManager.RebindContext ctx)
//     {
//         if (IsTargetContext(ctx))
//         {
//             var disp = ctx.overridePath == InputBindingManager.NULL_BINDING ? "<Cleared>" : ctx.overridePath;
//             bindKeyText.text = disp;
//             if (autoConfirm) _ = ConfirmPreparedAsync();
//         }
//     }
//
//     /// <summary>
//     /// 应用重新绑定的处理器
//     /// </summary>
//     private void OnApplyHandler(bool success, HashSet<InputBindingManager.RebindContext> appliedContexts)
//     {
//         if (appliedContexts != null)
//         {
//             // 仅当任何应用/丢弃的上下文与此实例匹配时才更新
//             foreach (var ctx in appliedContexts)
//             {
//                 if (IsTargetContext(ctx))
//                 {
//                     UpdateBindingText();
//                     break;
//                 }
//             }
//         }
//     }
//
//     /// <summary>
//     /// 重新绑定结束的处理器
//     /// </summary>
//     private void OnRebindEndHandler(bool success, InputBindingManager.RebindContext context)
//     {
//         if (IsTargetContext(context))
//         {
//             UpdateBindingText();
//         }
//     }
//
//     /// <summary>
//     /// 重新绑定冲突的处理器
//     /// </summary>
//     private void OnRebindConflictHandler(InputBindingManager.RebindContext prepared, InputBindingManager.RebindContext conflict)
//     {
//         // 如果准备的或冲突的上下文匹配此实例，则更新
//         if (IsTargetContext(prepared) || IsTargetContext(conflict))
//         {
//             UpdateBindingText();
//         }
//     }
//
//     /// <summary>
//     /// 设备变更的回调
//     /// </summary>
//     private void OnDeviceChanged(InputGlyphContext _)
//     {
//         UpdateBindingText();
//     }
//
//     private void OnBindingsChanged()
//     {
//         UpdateBindingText();
//     }
//
//     /// <summary>
//     /// 获取当前的输入操作
//     /// </summary>
//     private InputAction GetAction()
//     {
//         return InputBindingManager.Action(actionName);
//     }
//
//     /// <summary>
//     /// 判断上下文是否为目标上下文
//     /// </summary>
//     private bool IsTargetContext(InputBindingManager.RebindContext ctx)
//     {
//         if (ctx == null || ctx.action == null) return false;
//         var action = GetAction();
//         if (action == null) return false;
//
//         // 必须匹配操作
//         if (ctx.action != action) return false;
//
//         // 如果指定了复合部分，需要匹配绑定索引
//         if (!string.IsNullOrEmpty(compositePartName))
//         {
//             // 获取上下文索引处的绑定
//             if (ctx.bindingIndex < 0 || ctx.bindingIndex >= action.bindings.Count)
//                 return false;
//
//             var binding = action.bindings[ctx.bindingIndex];
//
//             // 检查绑定的名称是否与我们的复合部分匹配
//             return string.Equals(binding.name, compositePartName, StringComparison.OrdinalIgnoreCase);
//         }
//
//         // 如果未指定复合部分，仅匹配操作就足够了
//         return true;
//     }
//
//     /// <summary>
//     /// 按钮点击的回调
//     /// </summary>
//     private void OnBtnClicked()
//     {
//         // 使用管理器 API（我们传递部分名称，以便管理器可以在需要时选择适当的绑定）
//         InputBindingManager.StartRebind(actionName, string.IsNullOrEmpty(compositePartName) ? null : compositePartName);
//     }
//
//     /// <summary>
//     /// 确认准备好的重新绑定（公共方法）
//     /// </summary>
//     public async void ConfirmPrepared()
//     {
//         bool ok = await ConfirmPreparedAsync();
//         if (!ok) Debug.LogError("ConfirmPrepared: apply failed.");
//     }
//
//     /// <summary>
//     /// 确认准备好的重新绑定（异步）
//     /// </summary>
//     private async Task<bool> ConfirmPreparedAsync()
//     {
//         try
//         {
//             var task = InputBindingManager.ConfirmApply();
//             return await task;
//         }
//         catch (Exception ex)
//         {
//             Debug.LogError(ex);
//             return false;
//         }
//     }
//
//     /// <summary>
//     /// 取消准备好的重新绑定
//     /// </summary>
//     public void CancelPrepared()
//     {
//         InputBindingManager.DiscardPrepared();
//         // UpdateBindingText 将通过 OnApply 事件自动调用
//     }
//
//     /// <summary>
//     /// 更新绑定文本和图标显示
//     /// </summary>
//     private void UpdateBindingText()
//     {
//         var action = GetAction();
//         var context = InputDeviceWatcher.CurrentContext;
//         if (action == null)
//         {
//             bindKeyText.text = "<no action>";
//             if (targetImage != null) targetImage.sprite = null;
//             return;
//         }
//
//
//         bindKeyText.text = InputGlyphService.GetDisplayNameFromInputAction(action, compositePartName, context);
//
//
//         if (InputGlyphService.TryGetUISpriteForActionPath(action, compositePartName, context, out Sprite sprite))
//         {
//             if (targetImage != null) targetImage.sprite = sprite;
//         }
//         else
//         {
//             if (targetImage != null) targetImage.sprite = null;
//         }
//     }
// }
