#if INPUTSYSTEM_SUPPORT
using AlicizaX.Editor.UIToolkit;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AlicizaX.Input
{
    /// <summary>
    /// Global 模式只显示说明；Custom 模式才显示主题字段。Global 时主题引用由组件 OnValidate 清空，这里只管显示。
    /// </summary>
    [CustomEditor(typeof(InputFeedbackObserver), true)]
    [CanEditMultipleObjects]
    internal sealed class InputFeedbackObserverEditor : AlicizaUIEditor
    {
        internal const string ProfileRowName = "input-feedback-profile-row";
        internal const string HintName = "input-feedback-hint";

        protected override void BuildInspector(VisualElement root)
        {
            SerializedProperty mode = Find("_mode");
            SerializedProperty profile = Find("_profile");

            AlicizaPanel panel = AlicizaEditorUI.CreatePanel(null);
            root.Add(panel);
            AlicizaEditorUI.AddPropertyRow(panel.Body, "Mode", mode);
            AlicizaRow profileRow = AlicizaEditorUI.AddPropertyRow(panel.Body, "Profile", profile, true, typeof(InputFeedbackProfile));
            profileRow.name = ProfileRowName;
            Label hint = AlicizaEditorUI.CreateHint(string.Empty);
            hint.name = HintName;
            panel.Body.Add(hint);

            Refresh(mode, profileRow, hint);
            root.TrackPropertyValue(mode, changed => Refresh(changed, profileRow, hint));
        }

        private static void Refresh(SerializedProperty mode, VisualElement profileRow, Label hint)
        {
            bool mixed = mode.hasMultipleDifferentValues;
            bool custom = !mixed && mode.intValue == (int)InputFeedbackMode.Custom;
            profileRow.style.display = custom ? DisplayStyle.Flex : DisplayStyle.None;
            hint.text = mixed ? "所选对象的模式不一致。"
                : custom ? "使用本组件的主题；留空则静音，不回退全局主题。"
                : "使用全局主题（UXInput.Feedback.Profile）。";
        }
    }
}
#endif
