#if INPUTSYSTEM_SUPPORT
using UnityEngine;
using UnityEngine.InputSystem;

namespace AlicizaX.Input
{
    [AddComponentMenu("Input/Input Service")]
    [DefaultExecutionOrder(-30000)]
    public sealed class InputService : MonoServiceBehaviour
    {
        [SerializeField] private InputActionAsset actions;
        [SerializeField] private InputGlyphDatabase glyphDatabase;

        /// <summary>
        /// 由代码创建输入层。已存在实例时直接返回，保证全局只有一份。
        /// </summary>
        public static void Initialize(InputActionAsset actions, InputGlyphDatabase glyphDatabase)
        {
            if (AppServices.TryGet(out InputService _))
                return;

            // AddComponent 会在激活的物体上同步执行 Awake；必须先失活，赋值后再激活，Awake 才能看到配置。
            var root = new GameObject(nameof(InputService));
            root.SetActive(false);
            var service = root.AddComponent<InputService>();
            service.actions = actions;
            service.glyphDatabase = glyphDatabase;
            root.SetActive(true);
        }

        protected override void Awake()
        {
            base.Awake();
            if (actions == null)
            {
                Debug.LogError("InputService requires an InputActionAsset. Input stays disabled.", this);
                return;
            }

            UXInput.Initialize(actions, glyphDatabase);
            DontDestroyOnLoad(gameObject);
        }

        protected override void OnDestroy()
        {
            UXInput.Shutdown();
            base.OnDestroy();
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) Suspend();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (pauseStatus) Suspend();
        }

        /// <summary>失焦 / 暂停：丢弃热键按下目标，停止震动，避免恢复后误派发或马达持续转动。</summary>
        private static void Suspend()
        {
            UXInput.Hotkey?.ResetPressTargets();
            UXInput.Haptics?.Stop();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetForPlayMode()
        {
            UXInput.Shutdown();
        }
    }
}
#endif
