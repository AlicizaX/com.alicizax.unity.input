# com.alicizax.unity.input

AlicizaX 输入扩展包，基于 Unity Input System，包含设备监听、动作读取、运行时重绑定、输入图标、快捷键路由和导航焦点管理。

详细使用文档见仓库 `Books/Extension/UXInput.md`、`InputGlyph.md`、`HotkeyComponent.md`、`Navigation.md`。

## 启动

在常驻节点上挂载 `InputActionProvider` 并指定 `InputActionAsset`。它负责初始化：

1. `UXInput.Watch` 设备监听
2. `UXInput.Glyph` 图标数据库
3. 动作路径查询表
4. `UXInput.Rebind` 绑定覆盖加载
5. `UXNavigationSystem` 导航域

销毁时按相反顺序关闭所有输入服务，并清空快捷键注册表。

## 主要 API

| 模块 | 用途 |
| --- | --- |
| `UXInput.Watch` | 当前设备/Profile/上下文，`BuildContext`、`CurrentProfileId` |
| `UXInput.Reader` | 轮询读取动作值、按住状态、组合部分；`Reader.ButtonState` 提供边沿/开关 |
| `UXInput.Rebind` | 交互式重绑定、冲突处理、绑定覆盖持久化 |
| `UXInput.Glyph` | 控制路径 → 图标键/显示名/TMP 标签/UI Sprite |
| `UXInput.Haptics` | 手柄马达反馈 |
| `UXHotkeySystem` / `HotkeyComponent` | 按 UI 层级路由热键 |
| `UXNavigationScope` | UI 导航域焦点、抑制和选中记忆 |
| `InputProfileCatalog` | Profile 稳定 ID、显示名、回退链、绑定组提示的唯一来源 |

## 分配约定

- `Reader` 稳态读取（值、按住、`ButtonState.Once/Toggle`、组合部分）为 **0 B/次**。复合部分只在首次遇到新 `(action, part)` 组合时写一次缓存，绑定变化后按版本号失效。
- `Watch` 按设备缓存上下文；同一设备的输入事件不再拼接字符串。设备增删/重连时重建一次。
- `Rebind` 的 `RebindRequest` 默认值共享静态数组，不随调用分配；绑定显示字符串按 `(action, bindingIndex)` 缓存，绑定变化时清空。
- `Glyph` 的 Profile ID、图标键、显示名、绑定索引均缓存；绑定变化时按 `Rebind.BindingVersion` 失效。
- `InputVisualizer` 是诊断组件，只在 `UNITY_EDITOR` 或 `DEVELOPMENT_BUILD` 中编译，不在发布包中分配。
- 允许的低频分配：首次创建缓存、容量增长、交互式重绑定、绑定保存/加载、UI 打开注册快捷键。这些都有明确上界，不使用对象池。

### 实测数据（Unity 2022.3.62f3，Editor PlayMode）

Unity 2022.3 的 Mono 中 `GC.GetAllocatedBytesForCurrentThread()` 恒为 0，不能用于分配断言。输入包使用 `UnityEngine.Profiling` 的 `GC.Alloc` 原始帧数据测量（[Tests/PlayMode/InputAllocationCapture.cs](Tests/PlayMode/InputAllocationCapture.cs)），并用 4096 字节校准用例先证明采样器有效。

| 场景 | 首次/新 key | 稳态（2000 次循环） |
| --- | ---: | ---: |
| `Reader` 混合读取（值/按住/Once/Toggle/组合部分） | — | **0 B** |
| `Reader` 新组合部分 | 916 B / 10 次分配（缓存写入） | **0 B** |
| `Watch.ResolveContext` 同设备 | 0 B | **0 B** |
| `Glyph` 新 glyph key | 1,016 B / 26 次分配（首次 `string.Create`/Span 路径初始化） | **0 B** |
| `Glyph` 第二个新 key | 118 B / 3 次分配 | **0 B** |
| `Glyph` 首次可读显示名 | 133,420 B / 959 次分配（Unity Input System 显示名表一次性初始化） | **0 B** |
| `Rebind` 首次绑定显示串 | 1,534 B / 31 次分配（Unity 绑定显示串） | **0 B** |
| `RebindRequest.ForTarget` + 缓存显示串 | — | **0 B** |
| `UXNavigationSystem.RequestRefresh` | — | **0 B** |

说明：
- 组合部分首次缓存、glyph key 缓存、绑定显示串缓存都是每个 `(action, key)` 只付一次，释放责任在静态缓存的生命周期内；绑定变化时按版本号失效。
- 133 KB 的首次显示名开销来自 Unity Input System 自身的布局/显示名表初始化，不是输入包的分配；它只在第一次真正需要人类可读文本时发生。若不能接受首帧峰值，可在 `InputActionProvider` 启动阶段主动调用一次 `UXInput.Glyph.GetDisplayNameFromControlPath` 预热，把一次性成本移到加载阶段。
- 包内已移除对 ZString 的运行时依赖（glyph key 使用两遍扫描 + `string.Create`），避免其线程静态缓冲区首次分配（原为约 66 KB）。

## 从 2.x 迁移

| 变更 | 迁移方式 |
| --- | --- |
| `Reader.Read*Once(Object owner, ...)` / `Read*Toggle(...)` / `ResetToggled*` 移除 | 声明 `UXInput.Reader.ButtonState` 字段，调用 `Once(action)` / `Toggle(action)` / `Reset()` |
| `ReadPressedOnce(ulong ownerId, ...)` 等公开 ownerId 重载移除 | 状态归调用方所有，不再使用实例 ID |
| `Rebind.BeginRebind` / `BeginCompositePartRebind` 重命名 | `Rebind.Begin(action, RebindRequest...)` |
| `Rebind.ResetBinding` / `ResetActionBindings` / `ResetCompositePartBinding` 合并 | `Rebind.Reset(action)`（全部）或 `Rebind.Reset(action, request)`（单条） |
| `Rebind.GetBindingDisplayString` 重命名 | `Rebind.GetDisplayString` |
| `RebindOptions` 改为公开的 `RebindRequest` | `RebindRequest.Default.WithCompositePart("Up")` |
| `RebindTarget.Joystick` / `Any` 移除 | 只保留 `KeyboardMouse` 与 `Gamepad` |
| `Haptics.SetIntensity` 移除 | 使用 `Haptics.Intensity` 属性 |
| `Watch.InputContext` 精简为 6 个字段 | 移除 `VendorId`/`ProductId`/`Layout`/`InterfaceName`/`Manufacturer`/`Product` |
| `Reader.ReadTrace` 移除 `ActionName`/`OwnerId`/`OwnerKey` | 使用 `Action` 与 `CompositePartName` |
| `InputDefineGenerator` 不再生成 `Once/Toggle/Reset` 辅助方法 | 生成类只暴露无状态访问器，边沿状态由业务持有 `ButtonState` |
