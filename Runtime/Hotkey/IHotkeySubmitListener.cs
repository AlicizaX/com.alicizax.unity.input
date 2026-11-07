using UnityEngine.EventSystems;

namespace UnityEngine.UI
{
    /// <summary>热键提交时额外通知的旁路监听者；不替代 ISubmitHandler。</summary>
    public interface IHotkeySubmitListener
    {
        void OnHotkeySubmit(BaseEventData eventData);
    }
}
