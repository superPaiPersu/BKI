# NPC 行为模式与 AI 接入设计

## 1. 核心原则

AI 是“建议层”，不是“执行层”。

AI 可以决定：

- NPC 当前倾向的意图。
- NPC 的行为模式。
- NPC 的语气、情绪和一句对话。
- 下一步动作偏好。
- 对关系变化的轻量建议。

AI 不可以直接决定：

- 修改金币、物品、任务状态。
- 修改真实关系数值。
- 强制传送 NPC。
- 绕过地点权限。
- 改写日程和存档。

## 2. 行为模式

代码位置：

- `Assets/Scripts/AI/NpcBehaviorMode.cs`
- `Assets/Scripts/AI/NpcIntentType.cs`
- `Assets/Scripts/Behavior/NpcBehaviorState.cs`
- `Assets/Scripts/Behavior/NpcBehaviorController.cs`

当前行为模式：

| 模式 | 用途 |
| --- | --- |
| FollowSchedule | 默认按日程行动 |
| Socialize | 主动社交 |
| Work | 工作状态 |
| Rest | 休息、睡眠、恢复 |
| Investigate | 观察事件、靠近异常 |
| Help | 帮助玩家或 NPC |
| Avoid | 避开某人或某地 |
| Celebrate | 节日庆祝 |
| OppositeDay | 相反日反向行为 |

当前意图：

| 意图 | 用途 |
| --- | --- |
| ContinueCurrentAction | 继续当前日程 |
| TalkToPlayer | 和玩家说话 |
| TalkToNpc | 和某个 NPC 说话 |
| MoveToLocation | 倾向去某地 |
| WorkAtLocation | 在地点工作 |
| RestAtLocation | 在地点休息 |
| ReactToEvent | 响应事件 |
| HelpActor | 帮助对象 |
| AvoidActor | 避开对象 |
| JoinFestival | 参加节日 |

## 3. AI Provider 分层

| 层 | 脚本 | 说明 |
| --- | --- | --- |
| 接口 | `INpcBrainProvider` | 统一请求 NPC 决策 |
| MonoBehaviour 基类 | `NpcBrainProviderBehaviour` | 方便挂到场景物体 |
| 离线实现 | `MockNpcBrainProvider` | 无网络也能测试行为闭环 |
| OpenAI 实现 | `OpenAiNpcBrainProvider` | 使用 Responses API |

## 4. OpenAI 配置

`OpenAiNpcBrainProvider` 默认模型字段为：

```text
gpt-5.5
```

它是 Inspector 可配置字段，不写死在系统逻辑里。

API Key 读取顺序：

1. `apiKeyOverride`
2. 环境变量 `OPENAI_API_KEY`

建议开发时使用环境变量，不要把 Key 写进场景或 prefab。

## 5. AI 请求内容

`NpcAiRequest` 包含：

- NPC id、姓名、职业、性格摘要
- 当前日期和时间
- 当前地点和动作
- 当前情绪
- 玩家关系摘要
- 最近记忆摘要
- 观察到的事件摘要
- 当前节日规则

## 6. AI 返回结构

`NpcAiDecision` 包含：

```json
{
  "intent": "ReactToEvent",
  "behaviorMode": "Investigate",
  "tone": "cautious",
  "dialogue": "我先过去看看，但别离我太远。",
  "emotion": "concerned",
  "nextActionPreference": "move_closer_and_observe",
  "targetLocationId": "",
  "targetActorId": "",
  "relationshipDeltaHint": 1,
  "confidence": 0.8
}
```

`relationshipDeltaHint` 会被限制在 `-2` 到 `2`，它只是建议，不会自动改关系。

## 7. 推荐场景挂法

在 `GameSystems` 上挂：

- `GameClock`
- `LocationSystem`
- `ScheduleSystem`
- `MockNpcBrainProvider` 或 `OpenAiNpcBrainProvider`

在 NPC 上挂：

- `NpcRuntimeState`
- `NpcScheduleAgent`
- `NpcMovementAgent`
- `NpcBehaviorState`
- `NpcBehaviorController`
- `NpcActionExecutor`
- `NpcInteractable`（需要玩家靠近交互时）
- `NpcBehaviorDebugLogger`（可选，用于 Console 调试）

`NpcBehaviorController` 会在日程解析后请求 AI 决策。你也可以通过代码手动调用：

```csharp
npcBehaviorController.RequestDecision();
```

玩家交互时，`NpcInteractable` 会：

1. 停止 NPC 当前移动。
2. 让 NPC 面向玩家。
3. 写入一条“玩家靠近交谈”的事件摘要。
4. 强制请求一次 AI 决策。

## 8. 行为执行层

代码位置：

- `Assets/Scripts/NPC/NpcMovementAgent.cs`
- `Assets/Scripts/Behavior/NpcActionExecutor.cs`
- `Assets/Scripts/NPC/NpcInteractable.cs`

当前执行规则：

| 来源 | 行为 |
| --- | --- |
| 日程目标地点 | NPC 直线移动到该地点的 `LocationMarker.EntryPoint` |
| `TalkToPlayer` / `TalkToNpc` | NPC 停下，等待后续对话 UI |
| `MoveToLocation` | 如果 AI 给出 `targetLocationId`，移动到对应地点 |
| `WorkAtLocation` | 如果 AI 给出 `targetLocationId`，移动到对应地点 |
| `RestAtLocation` | 如果 AI 给出 `targetLocationId`，移动到对应地点 |
| `JoinFestival` | 如果 AI 给出 `targetLocationId`，移动到对应地点 |

这版没有做复杂寻路，NPC 会按直线移动。地图有障碍物后，应替换或扩展 `NpcMovementAgent`，但上层行为接口可以保持不变。

### 地点层级结构

推荐地点 prefab 使用父子结构：

```text
Hospital
  LocationMarker
  LocationTrigger
  Visual
    SpriteRenderer
  EntryPoint
  TriggerZone
    BoxCollider2D 或 CircleCollider2D，Is Trigger = true
    LocationTriggerZone
```

父物体 `Hospital` 负责地点逻辑：

- `LocationMarker.Definition` 拖“医院”的 `LocationDefinition`
- `LocationMarker.EntryPoint` 拖子物体 `EntryPoint`
- `LocationTrigger.Location` 拖同一个 `LocationDefinition`

子物体职责：

- `Visual` 只放图片和表现。
- `EntryPoint` 只是一个空物体，表示 NPC 要走到哪里。
- `TriggerZone` 放 Collider 和 `LocationTriggerZone`，负责把触发事件转发给父物体的 `LocationTrigger`。

兼容旧结构：如果 `LocationTrigger` 和 Collider 仍在同一个物体上，也可以继续工作。

## 9. UI 接口

UI 可以监听：

- `NpcBehaviorController.DecisionRequested`
- `NpcBehaviorController.DecisionReceived`
- `NpcBehaviorController.DecisionFailed`
- `NpcBehaviorState.DecisionApplied`

常见 UI 用法：

- NPC 头顶情绪图标：读 `NpcBehaviorState.Emotion`
- 对话气泡：读 `NpcBehaviorState.LastDialogue`
- 调试面板：显示 `BehaviorMode`、`CurrentIntent`、`NextActionPreference`

## 10. 后续扩展点

下一步建议：

1. 加入 `WorldEvent`，让事件系统给 NPC 写入 `observedEventSummary`。
2. 加入 `FestivalSystem`，相反日时给 NPC 写入 `festivalRuleSummary`。
3. 加入 `MemorySystem`，把重要互动压缩成 `recentMemorySummary`。
4. 加入行为执行器，将 `MoveToLocation`、`TalkToPlayer` 等意图转为寻路或交互。
