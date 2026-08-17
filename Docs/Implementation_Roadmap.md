# 实现路线与任务拆分

> 这份文档用于把设计总纲拆成后续可以逐条实现的工程任务。每个阶段都尽量形成一个可运行、可观察、可调试的小闭环。

## 0. 当前原则

- 先做可观察的模拟，再接入真正 AI。
- 先做少量高质量 NPC，再扩大居民数量。
- 所有核心系统先支持 Mock 数据和调试日志。
- AI 输出必须通过游戏规则层校验。
- 日程、地点、节日、事件尽量数据驱动。

## 1. 阶段一：角色移动与小镇切片

### 目标

玩家可以在一张小镇地图上以 2D 八方向移动，并进入几个基础地点。

### 当前落地脚本

- `Assets/Scripts/Movement/PlayerMovementController.cs`
- `Assets/Scripts/Movement/Direction8.cs`
- `Assets/Scripts/Movement/Direction8Utility.cs`
- `Assets/Scripts/Interactions/IInteractable.cs`
- `Assets/Scripts/Interactions/InteractionSensor.cs`
- `Assets/Scripts/Locations/LocationDefinition.cs`
- `Assets/Scripts/Locations/LocationSystem.cs`
- `Assets/Scripts/Locations/LocationTrigger.cs`
- `Assets/Scripts/Locations/LocationTriggerZone.cs`
- `Assets/Scripts/Locations/LocationMarker.cs`

### 推荐地点 prefab 结构

```text
LocationRoot
  LocationMarker
  LocationTrigger
  Visual
    SpriteRenderer
  EntryPoint
  TriggerZone
    Collider2D
    LocationTriggerZone
```

地点代码挂在父物体上；Collider 放在子物体上，通过 `LocationTriggerZone` 转发。

### 任务

- 创建玩家角色控制器。
- 创建八方向输入到动画方向的映射。
- 设置摄像机跟随。
- 创建测试地图。
- 添加碰撞和可交互触发器。
- 建立 LocationDefinition 数据结构。
- 实现地点进入、离开和当前地点显示。

### 验收

- 玩家可以流畅八方向移动。
- 角色朝向正确更新。
- 玩家进入广场、商店、住所等地点时，系统能识别当前地点。

## 2. 阶段二：游戏时间与日程

### 目标

游戏内时间流逝，NPC 能按照日程在地点之间移动或切换状态。

### 当前落地脚本

- `Assets/Scripts/Core/GameClock.cs`
- `Assets/Scripts/Core/GameDate.cs`
- `Assets/Scripts/Core/GameTime.cs`
- `Assets/Scripts/Core/DebugEventLogger.cs`
- `Assets/Scripts/NPC/NpcProfile.cs`
- `Assets/Scripts/NPC/NpcRuntimeState.cs`
- `Assets/Scripts/Schedule/NpcSchedule.cs`
- `Assets/Scripts/Schedule/NpcScheduleAgent.cs`
- `Assets/Scripts/Schedule/ScheduleEntry.cs`
- `Assets/Scripts/Schedule/ScheduleSystem.cs`

## 2.5 阶段二补充：NPC 行为模式与 AI 决策

### 当前落地脚本

- `Assets/Scripts/AI/INpcBrainProvider.cs`
- `Assets/Scripts/AI/NpcBrainProviderBehaviour.cs`
- `Assets/Scripts/AI/MockNpcBrainProvider.cs`
- `Assets/Scripts/AI/OpenAiNpcBrainProvider.cs`
- `Assets/Scripts/AI/NpcAiRequest.cs`
- `Assets/Scripts/AI/NpcAiDecision.cs`
- `Assets/Scripts/AI/NpcBehaviorMode.cs`
- `Assets/Scripts/AI/NpcIntentType.cs`
- `Assets/Scripts/Behavior/NpcBehaviorState.cs`
- `Assets/Scripts/Behavior/NpcBehaviorController.cs`
- `Assets/Scripts/Behavior/NpcActionExecutor.cs`
- `Assets/Scripts/Behavior/NpcBehaviorDebugLogger.cs`
- `Assets/Scripts/NPC/NpcMovementAgent.cs`
- `Assets/Scripts/NPC/NpcInteractable.cs`

### 当前运行效果

- NPC 根据日程目标地点，移动到对应 `LocationMarker` 的入口点。
- AI 决策会写入 `NpcBehaviorState`。
- 部分 AI 意图会被 `NpcActionExecutor` 转成移动或停止。
- 玩家靠近 NPC 并交互时，NPC 会停下、面向玩家并重新请求一次 AI 决策。

### 任务

- 实现 TimeSystem。
- 定义游戏日期、星期、季节、时间段。
- 创建 ScheduleEntry 数据结构。
- 创建 NpcProfile 和 NpcRuntimeState。
- 实现 ScheduleSystem，根据当前时间选择 NPC 行为。
- 添加 NPC 调试日志：当前目标、地点、状态、日程来源。
- 创建 2 个 NPC 的基础日程。

### 验收

- NPC 会在不同时间段改变目标地点和状态。
- 日程切换可以在日志或调试 UI 中看到。
- 日程无法执行时能使用 fallback。

## 3. 阶段三：基础交互与对话

### 目标

玩家可以与 NPC 对话，NPC 能根据当前上下文给出响应。

### 任务

- 实现 Interactable 接口。
- 实现对话 UI。
- 创建 DialogueContext。
- 创建本地 MockDialogueProvider。
- 实现基础关系数值。
- 对话后写入短期记忆或互动记录。
- 添加 NPC 头顶提示或可交互标识。

### 验收

- 玩家靠近 NPC 后可打开对话。
- NPC 回答能包含时间、地点、关系或当前状态。
- 对话结束后关系或记忆可以变化。

## 4. 阶段四：事件系统

### 目标

世界事件可以触发，相关 NPC 会收到事件并临时调整行为。

### 任务

- 创建 EventDefinition。
- 创建 RuntimeEventInstance。
- 实现 EventSystem 发布和订阅。
- 实现 NPC 感知事件的接口。
- 创建行为优先级仲裁器。
- 支持日程打断和恢复。
- 添加 2 个测试事件：广场争吵、商店缺货。

### 验收

- 事件触发后，相关 NPC 会改变目标或对话内容。
- 事件结束后，NPC 可以恢复原日程或进入新的后续行为。
- 事件可记录到城邦历史。

## 5. 阶段五：节日系统和相反日

### 目标

日历可以触发节日，节日能覆盖 NPC 行为和对话倾向。

### 任务

- 创建 FestivalDefinition。
- 创建 FestivalSystem。
- 支持节日开始和结束回调。
- 实现行为倾向反转规则。
- 为 4 个核心 NPC 配置相反日表现。
- 创建相反日专用活动或任务。
- 给对话上下文加入当前节日信息。

### 验收

- 到达指定日期后自动进入相反日。
- NPC 的行动或对话明显不同于平时。
- 节日结束后，NPC 产生一条相关记忆或关系后果。

## 6. 阶段六：商店与打工

### 目标

玩家可以在特定 NPC 的商店打工，工作结果影响金钱、关系和店铺状态。

### 任务

- 创建 ShopDefinition。
- 创建 JobDefinition。
- 实现申请打工条件。
- 实现排班和工作时间段。
- 设计第一个打工小游戏或任务流。
- 实现工作结算。
- 店主根据玩家表现生成评价。

### 验收

- 玩家可以在杂货铺申请打工。
- 工作期间出现可完成的小任务。
- 工作结束时获得报酬，并影响店主关系。

## 7. 阶段七：AI 接入

### 目标

在现有 Mock 对话基础上接入真实 AI，同时保留降级方案。

### 任务

- 定义 AI 请求结构。
- 定义 AI 响应 JSON Schema。
- 实现 DialogueProvider 接口。
- 实现响应校验。
- 实现失败降级到 Mock 或模板台词。
- 加入上下文压缩。
- 加入简单记忆检索。

### 验收

- NPC 对话能使用真实 AI 生成。
- AI 响应无效时不会破坏游戏状态。
- 同一 NPC 能体现稳定的性格和近期记忆。

## 8. 建议文件结构

```text
Assets/
  Scripts/
    Core/
      TimeSystem.cs
      GameClock.cs
    Locations/
      LocationDefinition.cs
      LocationTrigger.cs
      LocationSystem.cs
    NPC/
      NpcProfile.cs
      NpcRuntimeState.cs
      NpcController.cs
    Schedule/
      ScheduleEntry.cs
      ScheduleSystem.cs
      ScheduleResolver.cs
    Events/
      EventDefinition.cs
      EventSystem.cs
      RuntimeEventInstance.cs
    Dialogue/
      DialogueContext.cs
      DialogueProvider.cs
      MockDialogueProvider.cs
      DialogueUI.cs
    Festivals/
      FestivalDefinition.cs
      FestivalSystem.cs
    Jobs/
      ShopDefinition.cs
      JobDefinition.cs
      JobSystem.cs
```

## 9. 第一批数据资产

建议先创建这些 ScriptableObject 或 JSON 数据：

- `Location_PlayerHome`
- `Location_TownSquare`
- `Location_GroceryStore`
- `Location_Restaurant`
- `Npc_Lin`
- `Npc_Alan`
- `Npc_Baizhi`
- `Npc_Qiao`
- `Festival_OppositeDay`
- `Event_SquareArgument`
- `Event_ShopShortage`
- `Job_GroceryAssistant`

## 10. 优先实现顺序

1. TimeSystem
2. LocationSystem
3. NpcProfile / NpcRuntimeState
4. ScheduleSystem
5. Dialogue Mock
6. EventSystem
7. FestivalSystem
8. JobSystem
9. AI Provider

## 11. 开发时要保留的调试能力

- 当前游戏时间显示。
- 每个 NPC 当前目标显示。
- 每个 NPC 当前日程来源显示。
- 手动触发事件按钮。
- 手动切换节日按钮。
- 对话上下文预览。
- AI 原始响应和校验错误日志。
- 记忆写入日志。

## 12. UI 绑定脚本

### 游戏时间显示

当前脚本：

- `Assets/Scripts/UI/GameClockTextBinder.cs`
- `Assets/Scripts/UI/GameTimeFormatter.cs`

用法：

1. 在 Canvas 下创建一个 TextMeshPro 文本。
2. 给这个 Text 物体挂 `GameClockTextBinder`。
3. `Target Text` 可以不拖，脚本会自动取同物体上的 `TMP_Text`。
4. `Clock` 可以不拖，脚本会自动找场景里的 `GameClock`。
5. `Display Mode` 可选：
   - `TimeOnly`
   - `DateAndTime`
   - `DateOnly`

`Prefix` 和 `Suffix` 可以用来加前后缀，例如 `Time: `。

## 13. 相机系统

当前脚本：

- `Assets/Scripts/Camera/CameraController2D.cs`
- `Assets/Scripts/Camera/CameraBounds2D.cs`
- `Assets/Scripts/Camera/LocationCameraRule.cs`
- `Assets/Scripts/Camera/LocationCameraRuleSystem.cs`

### 基础跟随

在 `Main Camera` 上挂 `CameraController2D`：

- `Target` 拖玩家；不拖也会自动找场景里的 `PlayerMovementController`。
- `Mode` 选 `FollowTarget`。
- `Smooth Time` 建议先设为 `0`，八方向 2D 默认硬跟随更干净。
- `Orthographic Size` 控制视野大小。
- 如果是像素画风，可以尝试开启 `Snap To Pixel Grid`，并把 `Pixels Per Unit` 设成素材实际 PPU。
- 如果已经使用 Unity Pixel Perfect Camera，通常不要再开 `Snap To Pixel Grid`，避免双重吸附。

### 地图边界

创建一个空物体，例如 `TownCameraBounds`：

1. 挂 `BoxCollider2D`。
2. 调整 Collider 覆盖当前地图可见区域。
3. 挂 `CameraBounds2D`。
4. `Camera Controller` 可以不拖，会自动找。

运行后相机会被限制在这个边界里。

### 地点切换镜头规则

如果某个地点需要固定镜头，在地点父物体上挂 `LocationCameraRule`：

- `Location` 拖该地点的 `LocationDefinition`。
- `Mode` 选 `FixedPosition`。
- `Fixed Point` 拖一个空子物体，作为镜头中心。
- 如果需要特殊视野，勾 `Override Orthographic Size`。

在 `game system` 或任意空物体上挂 `LocationCameraRuleSystem`：

- `Location System` 可以不拖。
- `Camera Controller` 可以不拖。

玩家进入对应地点时，会自动应用该地点的相机规则。

## 14. Tilemap 绘制资源

当前编辑器工具：

- `Assets/Editor/TileAssetGenerator.cs`
- `Assets/Editor/TilemapSceneSetup.cs`

### 生成 Tile 资产

你的瓦片图片放在：

```text
Assets/Resources/map
```

在 Unity 顶部菜单执行：

```text
City State Sim > Map > Generate Tiles From Resources Map
```

工具会扫描 `Assets/Resources/map` 下所有 Sprite，并生成可绘制的 Tile 资产到：

```text
Assets/Tiles/Generated
```

它会跳过文件名里包含 `preview` 或 `sample` 的图片。

### 创建基础 Tilemap 层

在 Unity 顶部菜单执行：

```text
City State Sim > Map > Create Basic Tilemap Layers
```

会在场景里创建：

```text
Grid
  Ground Tilemap
  Decoration Tilemap
  Collision Tilemap
```

`Collision Tilemap` 会自动带：

- `Rigidbody2D`，Static
- `TilemapCollider2D`
- `CompositeCollider2D`

### 开始绘制

1. 打开 Tile Palette。
2. 创建一个 Palette。
3. 把 `Assets/Tiles/Generated` 里的 Tile 资产拖进 Palette。
4. 选中场景里的目标 Tilemap，例如 `Ground Tilemap`。
5. 用 Palette 直接绘制。

建议：

- 地面、道路画在 `Ground Tilemap`。
- 草、花、小物件画在 `Decoration Tilemap`。
- 不可通行区域画在 `Collision Tilemap`。
