# Runtime Systems UI Hooks

This file lists code entry points intended for UI wiring. Scene setup is left manual.

## Dialogue

Component: `CityStateSim.Dialogue.DialogueController`

- `StartConversation(NpcRuntimeState npc)`
- `SubmitPlayerLine(string text)`
- `AddNpcLineFromBehavior()`
- `EndConversation()`

Events:

- `ConversationStarted`
- `LineAdded`
- `ConversationEnded`

## Messages

Component: `CityStateSim.UI.MessageDisplayer`

- Drag a `TMP_Text` into `Target Text`, or place the component on the same object as a TMP text.
- It can listen to dialogue lines, world events, and job results.
- `Show(string message)` can be called directly from UI code.

## World Events

Component: `CityStateSim.Events.WorldEventSystem`

- `Publish(WorldEventDefinition definition)`
- `Publish(WorldEventDefinition definition, LocationDefinition location, string summary, string[] targetNpcIds)`

Effects:

- Writes event memory to relevant NPCs.
- Sends event summary into NPC AI context.
- Optionally creates same-day temporary schedule overrides.

## Daily Plans

Component: `CityStateSim.Schedule.DailyPlanGenerator`

- Listens to `GameClock.DayEnding`.
- Generates tomorrow's runtime daily plan.
- Does not modify base `NpcSchedule` assets.

Schedule priority:

1. Temporary override
2. Generated daily plan
3. Base schedule asset

## Festivals

Component: `CityStateSim.Festivals.FestivalSystem`

- Assign `FestivalDefinition` assets.
- Active festival rule summary is written into NPC AI context.
- Opposite Day is represented by `FestivalDefinition.OppositeDay`.

## Jobs

Component: `CityStateSim.Jobs.JobSystem`

- `CanStartJob(JobDefinition job, out string reason)`
- `StartJob(JobDefinition job)`
- `CompleteTask(JobTaskDefinition task)`
- `EndActiveJob()`

Optional:

- Add `PlayerWallet` to the player or a system object.
- Job settlement can update money, owner relationship, and owner memory.
