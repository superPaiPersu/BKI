using System;
using CityStateSim.Behavior;
using CityStateSim.Core;
using CityStateSim.Locations;
using CityStateSim.Memory;
using CityStateSim.NPC;
using CityStateSim.Schedule;
using UnityEngine;

namespace CityStateSim.Events
{
    public sealed class WorldEventSystem : MonoBehaviour
    {
        [SerializeField] private GameClock clock;
        [SerializeField] private ScheduleSystem scheduleSystem;
        [SerializeField] private MemorySystem memorySystem;
        [SerializeField] private bool requestNpcDecisionOnEvent = true;
        [SerializeField] private bool logEvents = true;

        public event Action<WorldEventInstance> EventPublished;

        private void Awake()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (scheduleSystem == null)
            {
                scheduleSystem = FindFirstObjectByType<ScheduleSystem>();
            }

            if (memorySystem == null)
            {
                memorySystem = FindFirstObjectByType<MemorySystem>();
            }
        }

        public WorldEventInstance Publish(WorldEventDefinition definition)
        {
            return Publish(definition, null, string.Empty, Array.Empty<string>());
        }

        public WorldEventInstance Publish(WorldEventDefinition definition, LocationDefinition location, string summary, string[] targetNpcIds)
        {
            if (definition == null)
            {
                return null;
            }

            LocationDefinition eventLocation = location != null ? location : definition.TargetLocation;
            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            GameTime time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            WorldEventInstance instance = new WorldEventInstance(definition, eventLocation, summary, targetNpcIds, date, time);
            ApplyToNpcs(instance);
            EventPublished?.Invoke(instance);

            if (logEvents)
            {
                Debug.Log($"[World Event] {instance.BuildMemorySummary()}", this);
            }

            return instance;
        }

        private void ApplyToNpcs(WorldEventInstance instance)
        {
            NpcRuntimeState[] npcs = FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcRuntimeState npc = npcs[i];
                if (npc == null || npc.Profile == null || !TargetsNpc(instance, npc.Profile.NpcId))
                {
                    continue;
                }

                string memorySummary = instance.BuildMemorySummary();
                memorySystem?.AddMemory(npc.Profile.NpcId, memorySummary, "event", 5);

                NpcBehaviorController behaviorController = npc.GetComponent<NpcBehaviorController>();
                if (behaviorController != null)
                {
                    WorldEventDefinition definition = instance.Definition;
                    behaviorController.SetObservedWorldEventContext(
                        memorySummary,
                        definition != null ? definition.BuildResponseTemplateSummary() : "(none)");
                    behaviorController.RefreshContextFromSystems();
                    if (requestNpcDecisionOnEvent)
                    {
                        behaviorController.ForceRequestDecision();
                    }
                }

                ApplyTemporaryScheduleOverride(instance, npc);
            }
        }

        private void ApplyTemporaryScheduleOverride(WorldEventInstance instance, NpcRuntimeState npc)
        {
            WorldEventDefinition definition = instance.Definition;
            LocationDefinition targetLocation = instance.Location != null ? instance.Location : definition != null ? definition.TargetLocation : null;
            if (definition == null || !definition.CreatesTemporaryScheduleOverride || targetLocation == null || scheduleSystem == null || npc.Profile == null)
            {
                return;
            }

            scheduleSystem.AddTemporaryOverride(
                npc.Profile.NpcId,
                definition.OverrideStart,
                definition.OverrideEnd,
                targetLocation,
                definition.OverrideAction,
                definition.Priority,
                instance.BuildMemorySummary());
        }

        private static bool TargetsNpc(WorldEventInstance instance, string npcId)
        {
            if (instance.TargetNpcIds == null || instance.TargetNpcIds.Length == 0)
            {
                return true;
            }

            for (int i = 0; i < instance.TargetNpcIds.Length; i++)
            {
                if (string.Equals(instance.TargetNpcIds[i], npcId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
