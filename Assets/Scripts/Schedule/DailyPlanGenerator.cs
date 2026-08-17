using CityStateSim.Core;
using CityStateSim.Locations;
using CityStateSim.Memory;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Schedule
{
    public sealed class DailyPlanGenerator : MonoBehaviour
    {
        [SerializeField] private GameClock clock;
        [SerializeField] private ScheduleSystem scheduleSystem;
        [SerializeField] private MemorySystem memorySystem;

        [Header("Policy")]
        [SerializeField] private bool generateOnDayEnding;
        [SerializeField] private bool copyBaseScheduleIntoDailyPlan;
        [SerializeField] private bool logGeneratedPlans = true;

        [Header("Simple Rule Overrides")]
        [SerializeField] private LocationDefinition clinicLocation;
        [SerializeField] private LocationDefinition townSquareLocation;
        [SerializeField] private GameTime morningOverrideStart = new GameTime(8, 0);
        [SerializeField] private GameTime morningOverrideEnd = new GameTime(10, 0);

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

        private void OnEnable()
        {
            if (clock != null)
            {
                clock.DayEnding += HandleDayEnding;
            }
        }

        private void OnDisable()
        {
            if (clock != null)
            {
                clock.DayEnding -= HandleDayEnding;
            }
        }

        public void GenerateTomorrowPlans()
        {
            if (clock == null)
            {
                return;
            }

            GeneratePlansForDate(clock.GetNextDate(clock.CurrentDate));
        }

        public void GeneratePlansForDate(GameDate targetDate)
        {
            if (scheduleSystem == null)
            {
                return;
            }

            NpcScheduleAgent[] agents = FindObjectsByType<NpcScheduleAgent>(FindObjectsSortMode.None);
            for (int i = 0; i < agents.Length; i++)
            {
                GeneratePlanForAgent(agents[i], targetDate);
            }
        }

        private void GeneratePlanForAgent(NpcScheduleAgent agent, GameDate targetDate)
        {
            if (agent == null || agent.RuntimeState == null || agent.RuntimeState.Profile == null)
            {
                return;
            }

            string npcId = agent.RuntimeState.Profile.NpcId;
            string memorySummary = memorySystem != null ? memorySystem.BuildRecentSummary(npcId, 6) : string.Empty;
            NpcDailyPlan plan = new NpcDailyPlan(npcId, targetDate, BuildPlanSummary(memorySummary));

            if (copyBaseScheduleIntoDailyPlan && agent.Schedule != null && agent.Schedule.Entries != null)
            {
                ScheduleEntry[] entries = agent.Schedule.Entries;
                for (int i = 0; i < entries.Length; i++)
                {
                    RuntimeScheduleEntry entry = RuntimeScheduleEntry.FromBaseEntry(entries[i]);
                    if (entry != null)
                    {
                        plan.AddEntry(new RuntimeScheduleEntry(
                            entry.Label,
                            entry.StartTime,
                            entry.EndTime,
                            entry.TargetLocation,
                            entry.ActionName,
                            entry.Priority,
                            entry.Interruptible,
                            ScheduleSource.DailyPlan,
                            "copied from base schedule"));
                    }
                }
            }

            RuntimeScheduleEntry overrideEntry = BuildRuleOverride(memorySummary);
            if (overrideEntry != null)
            {
                plan.AddEntry(overrideEntry);
            }

            if (plan.Entries.Count == 0)
            {
                plan.AddIntent(new NpcDailyIntent(
                    "Keep normal routine",
                    new GameTime(0, 0),
                    new GameTime(0, 0),
                    string.Empty,
                    string.Empty,
                    "Keep normal routine unless perception or events suggest otherwise.",
                    "follow_schedule, observe, respond_to_events",
                    "day completed without special follow-up",
                    10,
                    false,
                    "rule fallback generated no special intent"));
            }

            scheduleSystem.SetDailyPlan(npcId, targetDate, plan);
            if (logGeneratedPlans)
            {
                Debug.Log($"[Daily Plan] {agent.RuntimeState.Profile.DisplayName}: generated {plan.Entries.Count} entries for {targetDate}. {plan.Summary}", agent);
            }
        }

        private RuntimeScheduleEntry BuildRuleOverride(string memorySummary)
        {
            if (string.IsNullOrWhiteSpace(memorySummary))
            {
                return null;
            }

            string normalized = memorySummary.ToLowerInvariant();
            if ((normalized.Contains("sick") || normalized.Contains("ill") || normalized.Contains("clinic") || normalized.Contains("hospital")) && clinicLocation != null)
            {
                return new RuntimeScheduleEntry(
                    "Morning clinic follow-up",
                    morningOverrideStart,
                    morningOverrideEnd,
                    clinicLocation,
                    "Check health after yesterday's events",
                    50,
                    true,
                    ScheduleSource.DailyPlan,
                    "yesterday involved health concerns");
            }

            if ((normalized.Contains("argument") || normalized.Contains("conflict") || normalized.Contains("festival")) && townSquareLocation != null)
            {
                return new RuntimeScheduleEntry(
                    "Morning town follow-up",
                    morningOverrideStart,
                    morningOverrideEnd,
                    townSquareLocation,
                    "Follow up on yesterday's public event",
                    50,
                    true,
                    ScheduleSource.DailyPlan,
                    "yesterday involved a public event");
            }

            return null;
        }

        private string BuildPlanSummary(string memorySummary)
        {
            if (string.IsNullOrWhiteSpace(memorySummary))
            {
                return "No special memories; keep mostly normal routine.";
            }

            return $"Plan adjusted from yesterday's memories: {memorySummary}";
        }

        private void HandleDayEnding(GameDate date)
        {
            if (generateOnDayEnding)
            {
                GeneratePlansForDate(clock != null ? clock.GetNextDate(date) : GameCalendar.GetNextDate(date));
            }
        }
    }
}
