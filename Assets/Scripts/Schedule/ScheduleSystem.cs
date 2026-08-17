using System;
using System.Collections.Generic;
using System.Text;
using CityStateSim.Core;
using CityStateSim.Locations;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Schedule
{
    public sealed class ScheduleSystem : MonoBehaviour
    {
        [SerializeField] private GameClock clock;
        [SerializeField] private bool resolveEveryMinute = true;
        [SerializeField] private bool logMissingSchedules = true;
        [SerializeField] private bool clearTemporaryOverridesOnDayChanged = true;

        public event Action<NpcRuntimeState, ScheduleEntry> ScheduleResolved;
        public event Action<NpcRuntimeState, RuntimeScheduleEntry> RuntimeScheduleResolved;

        private readonly Dictionary<string, NpcDailyPlan> dailyPlans = new Dictionary<string, NpcDailyPlan>();
        private readonly Dictionary<string, List<RuntimeScheduleEntry>> temporaryOverrides = new Dictionary<string, List<RuntimeScheduleEntry>>();
        private GameDate lastAutoResolveDate;
        private GameTime lastAutoResolveTime;
        private bool hasLastAutoResolveTime;

        private void Awake()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }
        }

        private void OnEnable()
        {
            if (clock != null)
            {
                clock.MinuteChanged += HandleMinuteChanged;
                clock.DayChanged += HandleDayChanged;
            }
        }

        private void OnDisable()
        {
            if (clock != null)
            {
                clock.MinuteChanged -= HandleMinuteChanged;
                clock.DayChanged -= HandleDayChanged;
            }
        }

        private void Start()
        {
            ResolveAll();
        }

        public void ResolveAll()
        {
            NpcScheduleAgent[] agents = FindObjectsByType<NpcScheduleAgent>(FindObjectsSortMode.None);
            for (int i = 0; i < agents.Length; i++)
            {
                Resolve(agents[i]);
            }
        }

        public bool Resolve(NpcScheduleAgent agent)
        {
            if (agent == null)
            {
                return false;
            }

            if (clock == null)
            {
                Warn(agent, "No GameClock found. Schedule cannot resolve.");
                return false;
            }

            if (agent.RuntimeState == null)
            {
                Warn(agent, "NpcScheduleAgent has no NpcRuntimeState.");
                return false;
            }

            if (agent.Schedule == null)
            {
                Warn(agent, "NpcScheduleAgent has no schedule assigned.");
                return false;
            }

            if (agent.Schedule.Entries == null || agent.Schedule.Entries.Length == 0)
            {
                Warn(agent, $"Schedule '{agent.Schedule.name}' has no entries.");
                return false;
            }

            RuntimeScheduleEntry runtimeEntry = FindBestRuntimeEntry(agent, clock.CurrentDate, clock.CurrentTime);
            if (runtimeEntry != null)
            {
                agent.RuntimeState.ApplyScheduleTarget(runtimeEntry.TargetLocation, runtimeEntry.ActionName);
                RuntimeScheduleResolved?.Invoke(agent.RuntimeState, runtimeEntry);
                ScheduleResolved?.Invoke(agent.RuntimeState, FindBestEntry(agent.Schedule, clock.CurrentDate, clock.CurrentTime));
                return true;
            }

            ScheduleEntry entry = FindBestEntry(agent.Schedule, clock.CurrentDate, clock.CurrentTime);
            if (entry == null)
            {
                Warn(agent, $"No schedule entry matches {clock.CurrentTime} in '{agent.Schedule.name}'.");
                return false;
            }

            if (entry.TargetLocation == null)
            {
                Warn(agent, $"Matched schedule entry '{entry.Label}' has no target location.");
                return false;
            }

            agent.RuntimeState.ApplyScheduleTarget(entry.TargetLocation, entry.GetActionName());
            ScheduleResolved?.Invoke(agent.RuntimeState, entry);
            RuntimeScheduleResolved?.Invoke(agent.RuntimeState, RuntimeScheduleEntry.FromBaseEntry(entry));
            return true;
        }

        public void SetDailyPlan(string npcId, GameDate date, NpcDailyPlan plan)
        {
            if (string.IsNullOrWhiteSpace(npcId) || plan == null)
            {
                return;
            }

            dailyPlans[BuildPlanKey(npcId, date)] = plan;
        }

        public bool TryGetDailyPlan(string npcId, GameDate date, out NpcDailyPlan plan)
        {
            plan = null;
            if (string.IsNullOrWhiteSpace(npcId))
            {
                return false;
            }

            return dailyPlans.TryGetValue(BuildPlanKey(npcId, date), out plan);
        }

        public RuntimeScheduleEntry GetCurrentRuntimeEntry(NpcScheduleAgent agent)
        {
            if (agent == null || clock == null)
            {
                return null;
            }

            return FindBestRuntimeEntry(agent, clock.CurrentDate, clock.CurrentTime);
        }

        public NpcDailyIntent GetCurrentIntent(NpcScheduleAgent agent)
        {
            if (agent == null || clock == null)
            {
                return null;
            }

            string npcId = GetNpcId(agent);
            string planKey = BuildPlanKey(npcId, clock.CurrentDate);
            if (!dailyPlans.TryGetValue(planKey, out NpcDailyPlan plan))
            {
                return null;
            }

            NpcDailyIntent best = null;
            IReadOnlyList<NpcDailyIntent> intents = plan.Intents;
            for (int i = 0; i < intents.Count; i++)
            {
                NpcDailyIntent intent = intents[i];
                if (intent == null || !intent.IsActiveAt(clock.CurrentTime))
                {
                    continue;
                }

                if (best == null || intent.Priority > best.Priority)
                {
                    best = intent;
                }
            }

            return best;
        }

        public string BuildDebugScheduleText(NpcScheduleAgent agent, GameDate date)
        {
            if (agent == null || agent.RuntimeState == null || agent.RuntimeState.Profile == null)
            {
                return "No NPC schedule agent selected.";
            }

            string npcId = agent.RuntimeState.Profile.NpcId;
            StringBuilder builder = new StringBuilder();
            builder.AppendLine($"{agent.RuntimeState.Profile.DisplayName} schedule for {date.ToLongString()}");

            if (TryGetDailyPlan(npcId, date, out NpcDailyPlan dailyPlan))
            {
                builder.AppendLine($"Daily plan: {dailyPlan.Summary}");
                AppendIntents(builder, dailyPlan.Intents);
                AppendRuntimeEntries(builder, dailyPlan.Entries);
            }
            else
            {
                builder.AppendLine("Daily plan: none");
            }

            builder.AppendLine("Base schedule:");
            AppendBaseEntries(builder, agent.Schedule, date);
            return builder.ToString();
        }

        public NpcDailyPlan GetOrCreateDailyPlan(string npcId, GameDate date, string summary = "")
        {
            string key = BuildPlanKey(npcId, date);
            if (!dailyPlans.TryGetValue(key, out NpcDailyPlan plan))
            {
                plan = new NpcDailyPlan(npcId, date, summary);
                dailyPlans.Add(key, plan);
            }

            return plan;
        }

        public void AddDailyPlanEntry(string npcId, GameDate date, RuntimeScheduleEntry entry)
        {
            GetOrCreateDailyPlan(npcId, date).AddEntry(entry);
        }

        public void AddTemporaryOverride(string npcId, RuntimeScheduleEntry entry)
        {
            if (string.IsNullOrWhiteSpace(npcId) || entry == null)
            {
                return;
            }

            if (!temporaryOverrides.TryGetValue(npcId, out List<RuntimeScheduleEntry> entries))
            {
                entries = new List<RuntimeScheduleEntry>();
                temporaryOverrides.Add(npcId, entries);
            }

            entries.Add(entry);
            ResolveAll();
        }

        public void AddTemporaryOverride(
            string npcId,
            GameTime startTime,
            GameTime endTime,
            LocationDefinition targetLocation,
            string actionName,
            int priority,
            string reason)
        {
            AddTemporaryOverride(npcId, new RuntimeScheduleEntry(
                reason,
                startTime,
                endTime,
                targetLocation,
                actionName,
                priority,
                true,
                ScheduleSource.TemporaryOverride,
                reason));
        }

        public void ClearTemporaryOverrides(string npcId)
        {
            if (!string.IsNullOrWhiteSpace(npcId))
            {
                temporaryOverrides.Remove(npcId);
            }
        }

        private void Warn(NpcScheduleAgent agent, string message)
        {
            if (!logMissingSchedules)
            {
                return;
            }

            Debug.LogWarning($"[Schedule] {agent.name}: {message}", agent);
        }

        private static ScheduleEntry FindBestEntry(NpcSchedule schedule, GameDate date, GameTime time)
        {
            ScheduleEntry best = null;
            ScheduleEntry[] entries = schedule.Entries;
            if (entries == null)
            {
                return null;
            }

            for (int i = 0; i < entries.Length; i++)
            {
                ScheduleEntry entry = entries[i];
                if (entry == null || !entry.Matches(date, time))
                {
                    continue;
                }

                if (best == null || entry.Priority > best.Priority)
                {
                    best = entry;
                }
            }

            return best;
        }

        private RuntimeScheduleEntry FindBestRuntimeEntry(NpcScheduleAgent agent, GameDate date, GameTime time)
        {
            string npcId = GetNpcId(agent);
            RuntimeScheduleEntry best = FindBestRuntimeEntry(temporaryOverrides.TryGetValue(npcId, out List<RuntimeScheduleEntry> overrides) ? overrides : null, time);

            string planKey = BuildPlanKey(npcId, date);
            if (dailyPlans.TryGetValue(planKey, out NpcDailyPlan plan))
            {
                RuntimeScheduleEntry dailyEntry = FindBestRuntimeEntry(plan.Entries, time);
                if (IsBetterRuntimeEntry(dailyEntry, best))
                {
                    best = dailyEntry;
                }
            }

            return best;
        }

        private static RuntimeScheduleEntry FindBestRuntimeEntry(IReadOnlyList<RuntimeScheduleEntry> entries, GameTime time)
        {
            if (entries == null)
            {
                return null;
            }

            RuntimeScheduleEntry best = null;
            for (int i = 0; i < entries.Count; i++)
            {
                RuntimeScheduleEntry entry = entries[i];
                if (entry == null || !entry.Contains(time))
                {
                    continue;
                }

                if (IsBetterRuntimeEntry(entry, best))
                {
                    best = entry;
                }
            }

            return best;
        }

        private static void AppendRuntimeEntries(StringBuilder builder, IReadOnlyList<RuntimeScheduleEntry> entries)
        {
            builder.AppendLine("Runtime entries:");
            if (entries == null || entries.Count == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                RuntimeScheduleEntry entry = entries[i];
                if (entry == null)
                {
                    continue;
                }

                string locationName = entry.TargetLocation != null ? entry.TargetLocation.DisplayName : "None";
                builder.AppendLine($"- {entry.StartTime}-{entry.EndTime} {entry.Label} @ {locationName} action={entry.ActionName} priority={entry.Priority} source={entry.Source} reason={entry.Reason}");
            }
        }

        private static void AppendIntents(StringBuilder builder, IReadOnlyList<NpcDailyIntent> intents)
        {
            builder.AppendLine("Intents:");
            if (intents == null || intents.Count == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            for (int i = 0; i < intents.Count; i++)
            {
                NpcDailyIntent intent = intents[i];
                if (intent != null)
                {
                    builder.AppendLine("- " + intent.ToSummaryLine());
                }
            }
        }

        private static void AppendBaseEntries(StringBuilder builder, NpcSchedule schedule, GameDate date)
        {
            if (schedule == null || schedule.Entries == null || schedule.Entries.Length == 0)
            {
                builder.AppendLine("- none");
                return;
            }

            for (int i = 0; i < schedule.Entries.Length; i++)
            {
                ScheduleEntry entry = schedule.Entries[i];
                if (entry == null)
                {
                    continue;
                }

                string locationName = entry.TargetLocation != null ? entry.TargetLocation.DisplayName : "None";
                string activeMarker = entry.AppliesToDate(date) ? "" : " inactive-for-date";
                builder.AppendLine($"- {entry.StartTime}-{entry.EndTime} {entry.Label} @ {locationName} action={entry.GetActionName()} priority={entry.Priority} dateRule={entry.GetDateRuleSummary()}{activeMarker}");
            }
        }

        private static bool IsBetterRuntimeEntry(RuntimeScheduleEntry candidate, RuntimeScheduleEntry current)
        {
            if (candidate == null)
            {
                return false;
            }

            if (current == null)
            {
                return true;
            }

            if (candidate.Source != current.Source)
            {
                return candidate.Source > current.Source;
            }

            return candidate.Priority > current.Priority;
        }

        private static string BuildPlanKey(string npcId, GameDate date)
        {
            return $"{npcId}:{date.Key}";
        }

        private static string GetNpcId(NpcScheduleAgent agent)
        {
            NpcProfile profile = agent != null && agent.RuntimeState != null ? agent.RuntimeState.Profile : null;
            return profile != null && !string.IsNullOrWhiteSpace(profile.NpcId) ? profile.NpcId : agent != null ? agent.name : "unknown_npc";
        }

        private void HandleMinuteChanged(GameDate date, GameTime time)
        {
            if (resolveEveryMinute)
            {
                ResolveAllOnceForTime(date, time);
            }
        }

        private void HandleDayChanged(GameDate date)
        {
            if (!clearTemporaryOverridesOnDayChanged)
            {
                return;
            }

            temporaryOverrides.Clear();
            ResolveAllOnceForTime(date, clock != null ? clock.CurrentTime : new GameTime(0, 0));
        }

        private void ResolveAllOnceForTime(GameDate date, GameTime time)
        {
            if (hasLastAutoResolveTime && lastAutoResolveDate.Equals(date) && lastAutoResolveTime.Equals(time))
            {
                return;
            }

            hasLastAutoResolveTime = true;
            lastAutoResolveDate = date;
            lastAutoResolveTime = time;
            ResolveAll();
        }
    }
}
