# 城邦模拟器文档入口

这个目录用于沉淀设计、系统拆分和后续实现路线。

## 文档

- [CityState_Sim_Design.md](./CityState_Sim_Design.md)：玩法设计总纲，包含核心体验、NPC、日程、事件、节日、打工、AI 接入原则和 MVP 范围。
- [Implementation_Roadmap.md](./Implementation_Roadmap.md)：工程实现路线，把设计拆成 Unity 可逐步完成的阶段任务。
- [AI_Behavior_Architecture.md](./AI_Behavior_Architecture.md)：NPC 行为模式和 AI 接入设计，包含 Provider 分层、OpenAI 配置和 UI 可监听事件。

## 推荐下一步

优先从 `Implementation_Roadmap.md` 的阶段一开始：

1. 玩家八方向移动。
2. 小镇测试地图。
3. 地点触发和 LocationSystem。

原因是后续 NPC 日程、事件、节日和打工都依赖“时间 + 地点 + 角色位置”这三个基础支点。
