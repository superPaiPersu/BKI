using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using CityStateSim.AI;
using CityStateSim.Core;
using CityStateSim.Locations;
using CityStateSim.Memory;
using CityStateSim.NPC;
using UnityEngine;
using UnityEngine.Networking;

namespace CityStateSim.Schedule
{
    public sealed class AiDailyPlanGenerator : MonoBehaviour, IDayEndSettlementTask
    {
        private const string ApiUrl = "https://cf.ai-pixel.online/v1/responses";

        [Header("References")]
        [SerializeField] private GameClock clock;
        [SerializeField] private ScheduleSystem scheduleSystem;
        [SerializeField] private MemorySystem memorySystem;
        [SerializeField] private LocationSystem locationSystem;
        [SerializeField] private AiMemoryConsolidator memoryConsolidator;

        [Header("OpenAI Compatible API")]
        [SerializeField] private string model = AiModelDefaults.FastModel;
        [SerializeField] private string apiKey;
        [SerializeField, Min(1)] private int timeoutSeconds = 45;

        [Header("Policy")]
        [SerializeField] private bool generateOnDayEnding = true;
        [SerializeField] private bool copyBaseScheduleWhenAiFails = true;
        [SerializeField, Min(1)] private int maxMemoryLines = 20;
        [SerializeField, Min(1)] private int maxIntents = 8;
        [SerializeField, Min(0)] private int requestRetryCount = 1;
        [SerializeField, Min(0f)] private float retryDelaySeconds = 1f;
        [SerializeField] private bool logPlans = true;
        [SerializeField] private bool logRawResponse;

        private readonly Dictionary<string, LocationDefinition> locationsById = new Dictionary<string, LocationDefinition>();
        private int pendingPlanningRoutines;
        private int activePlanningJobs;

        public bool IsDayEndSettlementRunning => pendingPlanningRoutines > 0 || activePlanningJobs > 0;

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

            if (locationSystem == null)
            {
                locationSystem = FindFirstObjectByType<LocationSystem>();
            }

            if (memoryConsolidator == null)
            {
                memoryConsolidator = FindFirstObjectByType<AiMemoryConsolidator>();
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

            GeneratePlansForDate(clock.CurrentDate, clock.GetNextDate(clock.CurrentDate));
        }

        public void GeneratePlansForDate(GameDate memoryDate, GameDate targetDate)
        {
            StartCoroutine(GeneratePlansForDateRoutine(memoryDate, targetDate, false));
        }

        private IEnumerator GeneratePlansForDateRoutine(GameDate memoryDate, GameDate targetDate, bool waitForMemoryConsolidation)
        {
            pendingPlanningRoutines++;
            if (waitForMemoryConsolidation && memoryConsolidator != null)
            {
                while (memoryConsolidator.IsDayEndSettlementRunning)
                {
                    yield return null;
                }
            }

            if (scheduleSystem == null)
            {
                Debug.LogWarning("[AI Daily Plan] Cannot generate plans: ScheduleSystem is missing.", this);
                pendingPlanningRoutines = Mathf.Max(0, pendingPlanningRoutines - 1);
                yield break;
            }

            RefreshLocationCache();
            NpcScheduleAgent[] agents = FindObjectsByType<NpcScheduleAgent>(FindObjectsSortMode.None);
            if (logPlans)
            {
                Debug.Log($"[AI Daily Plan] Generating plans for {agents.Length} NPCs. Memory date={memoryDate}, target date={targetDate}.", this);
            }

            int remainingJobs = 0;
            for (int i = 0; i < agents.Length; i++)
            {
                if (agents[i] != null)
                {
                    remainingJobs++;
                    StartCoroutine(GeneratePlanForAgentTracked(agents[i], memoryDate, targetDate, () => remainingJobs--));
                }
            }

            while (remainingJobs > 0)
            {
                yield return null;
            }

            pendingPlanningRoutines = Mathf.Max(0, pendingPlanningRoutines - 1);
        }

        private IEnumerator GeneratePlanForAgentTracked(NpcScheduleAgent agent, GameDate memoryDate, GameDate targetDate, Action onComplete)
        {
            activePlanningJobs++;
            yield return GeneratePlanForAgent(agent, memoryDate, targetDate);
            activePlanningJobs = Mathf.Max(0, activePlanningJobs - 1);
            onComplete?.Invoke();
        }

        private IEnumerator GeneratePlanForAgent(NpcScheduleAgent agent, GameDate memoryDate, GameDate targetDate)
        {
            NpcRuntimeState runtimeState = agent != null ? agent.RuntimeState : null;
            NpcProfile profile = runtimeState != null ? runtimeState.Profile : null;
            if (profile == null)
            {
                yield break;
            }

            string npcId = profile.NpcId;
            string memorySummary = memorySystem != null
                ? BuildPlanningMemorySummary(npcId, memoryDate)
                : string.Empty;
            if (logPlans)
            {
                Debug.Log($"[AI Daily Plan] {profile.DisplayName}: memory lines for {memoryDate}: {(string.IsNullOrWhiteSpace(memorySummary) ? "none" : memorySummary)}", this);
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                ApplyFallbackPlan(agent, targetDate, "Missing API key; copied base schedule.");
                yield break;
            }

            string body = BuildRequestBody(agent, profile, memoryDate, targetDate, memorySummary);
            string rawResponse = string.Empty;
            string requestError = string.Empty;
            int attempts = Mathf.Max(1, requestRetryCount + 1);
            for (int attempt = 1; attempt <= attempts; attempt++)
            {
                using UnityWebRequest webRequest = new UnityWebRequest(ApiUrl, UnityWebRequest.kHttpVerbPOST);
                byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
                webRequest.uploadHandler = new UploadHandlerRaw(bodyBytes);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.timeout = timeoutSeconds;
                webRequest.SetRequestHeader("Content-Type", "application/json");
                webRequest.SetRequestHeader("Authorization", $"Bearer {apiKey.Trim()}");

                yield return webRequest.SendWebRequest();

                if (webRequest.result == UnityWebRequest.Result.Success)
                {
                    rawResponse = webRequest.downloadHandler.text;
                    break;
                }

                requestError = $"AI daily plan request failed: {webRequest.responseCode} {webRequest.error}";
                if (attempt < attempts)
                {
                    if (logPlans)
                    {
                        Debug.LogWarning($"[AI Daily Plan] {profile.DisplayName}: {requestError}. Retry {attempt}/{requestRetryCount}.", this);
                    }

                    if (retryDelaySeconds > 0f)
                    {
                        yield return new WaitForSecondsRealtime(retryDelaySeconds);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                if (string.IsNullOrWhiteSpace(requestError))
                {
                    requestError = "AI daily plan returned an empty response body.";
                }

                ApplyFallbackPlan(agent, targetDate, requestError);
                yield break;
            }

            if (logRawResponse)
            {
                Debug.Log($"[AI Daily Plan] Raw response for {profile.DisplayName}: {rawResponse}", this);
            }

            if (!TryParsePlan(rawResponse, out NpcDailyPlanAiResponse aiPlan, out string error))
            {
                ApplyFallbackPlan(agent, targetDate, error);
                yield break;
            }

            NpcDailyPlan plan = BuildRuntimePlan(profile, targetDate, aiPlan, agent.Schedule);
            scheduleSystem.SetDailyPlan(npcId, targetDate, plan);
            if (clock == null || clock.CurrentDate.Equals(targetDate))
            {
                scheduleSystem.Resolve(agent);
            }

            if (logPlans)
            {
                Debug.Log($"[AI Daily Plan] {profile.DisplayName}: generated {plan.Intents.Count} intents and {plan.Entries.Count} fallback entries for {targetDate}. {plan.Summary}", this);
            }
        }

        private string BuildPlanningMemorySummary(string npcId, GameDate memoryDate)
        {
            if (memorySystem == null)
            {
                return string.Empty;
            }

            string dateSummary = memorySystem.BuildSummaryForDate(npcId, memoryDate, maxMemoryLines);
            string longTermSummary = memorySystem.BuildLongTermSummary(npcId, maxMemoryLines);
            string factSummary = memorySystem.BuildFactSummaryForDate(npcId, memoryDate, maxMemoryLines);
            StringBuilder builder = new StringBuilder();

            if (!string.IsNullOrWhiteSpace(dateSummary))
            {
                builder.AppendLine("Today's retained/important memories:");
                builder.AppendLine(dateSummary);
            }

            if (!string.IsNullOrWhiteSpace(longTermSummary))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine("Long-term retained memories:");
                builder.AppendLine(longTermSummary);
            }

            if (!string.IsNullOrWhiteSpace(factSummary))
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.AppendLine("Evidence fact records:");
                builder.AppendLine(factSummary);
            }

            return builder.ToString().Trim();
        }

        private string BuildRequestBody(NpcScheduleAgent agent, NpcProfile profile, GameDate memoryDate, GameDate targetDate, string memorySummary)
        {
            string systemPrompt = EscapeJson(
                "You are a daily intent planner for a 2D city-state life simulation. " +
                "Return only valid JSON matching the schema. " +
                "Use dialogue memories and external events as reasons to create tomorrow's fuzzy intent plan. " +
                "Fact records are evidence entries, including direct observations and quoted claims; do not treat a claim as verified unless its evidence says it was directly observed. " +
                "Think like a real person planning tomorrow at the end of the day. " +
                "Do not create intentions merely because something happened first. Rank memories by urgency, emotional weight, relationship importance, practical consequences, promises, and personal values. " +
                "Relationship matters: trusted friends, family-like bonds, debts, romance, rivalry, suspicion, and fear should change the threshold for follow-up. " +
                "A close person's urgent request can displace routine; a stranger's casual comment usually cannot. " +
                "The base schedule is only the NPC's fallback routine. Do not copy it. " +
                "Daily intents are soft priorities, watch items, promises, avoidances, and social goals, not minute-by-minute schedules. " +
                "Use the target date's weekday, month, and season when deciding whether a plan should differ from ordinary routine. " +
                "For appointments, shared meals, meetings, or planned group actions, represent only the final shared venue as a shared activity with activityKind, participantActorIds, requiredActorIds, optionalActorIds, and patienceMinutes. " +
                "Do not mark prep steps such as finding, fetching, inviting, escorting, checking on, or picking up a person as shared activities. Those should be ordinary actor-targeted intents. " +
                "Only use exact allowed locationId and actorId values. " +
                "Never invent locations, actors, action names, activity kinds, task templates, or location capabilities. " +
                "Every location-targeted activity must be selected from the chosen location's listed task templates. " +
                "Do not infer activity support from a location id, name, description, or common sense.");

            string userPrompt = EscapeJson(BuildUserPrompt(agent, profile, memoryDate, targetDate, memorySummary));
            string schema = BuildSchema();
            string escapedModel = EscapeJson(AiModelDefaults.ResolveRuntimeModel(model));

            return
                "{" +
                $"\"model\":\"{escapedModel}\"," +
                "\"input\":[" +
                "{\"role\":\"system\",\"content\":[{\"type\":\"input_text\",\"text\":\"" + systemPrompt + "\"}]}," +
                "{\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"" + userPrompt + "\"}]}" +
                "]," +
                "\"text\":{" +
                "\"format\":{" +
                "\"type\":\"json_schema\"," +
                "\"name\":\"npc_daily_plan\"," +
                "\"strict\":true," +
                "\"schema\":" + schema +
                "}" +
                "}" +
                "}";
        }

        private string BuildUserPrompt(NpcScheduleAgent agent, NpcProfile profile, GameDate memoryDate, GameDate targetDate, string memorySummary)
        {
            return
                "NPC:\n" +
                $"- id: {profile.NpcId}\n" +
                $"- name: {profile.DisplayName}\n" +
                $"- role: {profile.Role}\n" +
                $"- personality: {profile.PersonalitySummary}\n" +
                $"- personal interaction templates: {profile.BuildInteractionTemplateSummary()}\n" +
                $"- memory date: {memoryDate}\n" +
                $"- memory date details: {memoryDate.ToLongString()}\n" +
                $"- target date: {targetDate}\n\n" +
                $"- target date details: {targetDate.ToLongString()}\n\n" +
                "Today's memories, external stimuli, and evidence records:\n" +
                (string.IsNullOrWhiteSpace(memorySummary) ? "(none)" : memorySummary) +
                "\n\nBase schedule:\n" +
                BuildBaseScheduleText(agent.Schedule) +
                "\n\nAllowed locations:\n" +
                BuildLocationListText() +
                "\nLocation task templates by location:\n" +
                BuildLocationTaskCatalogText() +
                "\nAllowed actors:\n" +
                BuildActorListText(profile.NpcId) +
                "\n\nIntent planning policy:\n" +
                "Location/task rules:\n" +
                NpcAiPromptBuilder.LocationTaskRules +
                "\n" +
                "- Choose by weight, not order. Later memories are not automatically more important than earlier ones.\n" +
                "- Treat dialogue as real external stimuli: invitations, warnings, confessions, requests, arguments, promises, secrets, and emotional moments may justify tomorrow's intentions.\n" +
                "- Ask whether this NPC would actually care tomorrow: personality, job, energy, relationship, risk, and obligations all matter.\n" +
                "- Keep the base schedule as fallback routine. Do not copy base schedule blocks into intents.\n" +
                "- Use weekday, weekend, month, and season context for recurring preferences when it matters, but keep one-off promises tied to their actual agreed time.\n" +
                "- Treat the personal interaction templates as a menu of likely human follow-ups and social responses for this NPC.\n" +
                "- Treat each location's task templates as the legal menu of what can actually happen there. capabilityTags only explain template availability; they are not activity kinds.\n" +
                "- Use priority 0-30 for weak reminders, 40-60 for meaningful follow-up, 70-100 for urgent or high-stakes goals.\n" +
                "- Use time windows as soft availability windows, not exact schedules.\n" +
                "- targetLocationId must be empty or an exact id from Allowed locations. If the intent is person-first, use targetActorId and leave targetLocationId empty unless there is a known final venue.\n" +
                "- activityKind should be empty for simple solo intentions and for prep steps such as finding, fetching, inviting, checking on, or picking up a person before another plan.\n" +
                "- For AttendActivity-style intents, activityKind must be copied from the selected final location's task templates. Leave it empty for non-activity prep steps.\n" +
                "- If a plan needs someone first and a venue later, create the person step as an actor-targeted prep intent with activityKind empty, then create the shared activity only at a location whose templates support it.\n" +
                "- participantActorIds should include every known actor involved in the shared activity, including this NPC when appropriate. Use exact ids from Allowed actors, plus this NPC's own id.\n" +
                "- requiredActorIds should include people whose absence changes whether the activity can really start. optionalActorIds are welcome but not required.\n" +
                "- If the player is part of the plan, include player as optional unless the memory explicitly says everyone will wait for the player.\n" +
                "- patienceMinutes is how long a realistic person would wait after arriving before deciding what to do next. Use 0 for no waiting, 10-30 for ordinary social plans, higher only for important promises.\n" +
                "- allowedBehaviors should name broad executable options drawn from people, obligations, and the selected location's task templates.\n" +
                "- completionCondition should explain when the intent is satisfied in terms of observable runtime results.\n" +
                "- Do not reduce a shared meal or meeting into a one-on-one TalkToNpc target. The runtime will gather participants and start dialogue when conditions make sense.\n" +
                "- Do not expand a prep step into a shared activity just because it mentions a later meal, meeting, or companion. Waiting belongs only to the final shared activity intent.\n" +
                "- If the requested final venue does not expose a matching template, do not force the plan there. Use a person-first clarification/check/invite intent or choose a different explicitly supported venue.\n" +
                "- The reason field must explain the human reason for the intent, including relationship/urgency when relevant.\n\n" +
                "Return tomorrow's fuzzy intent plan. Use 24-hour integer time. " +
                $"Return at most {maxIntents} intents.";
        }

        private string BuildBaseScheduleText(NpcSchedule schedule)
        {
            if (schedule == null || schedule.Entries == null || schedule.Entries.Length == 0)
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < schedule.Entries.Length; i++)
            {
                ScheduleEntry entry = schedule.Entries[i];
                if (entry == null)
                {
                    continue;
                }

                string locationId = entry.TargetLocation != null ? entry.TargetLocation.LocationId : "";
                builder.AppendLine($"- {entry.StartTime}-{entry.EndTime}: {entry.Label}, locationId={locationId}, action={entry.GetActionName()}, priority={entry.Priority}, dateRule={entry.GetDateRuleSummary()}");
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private string BuildLocationListText()
        {
            if (locationsById.Count == 0)
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            foreach (LocationDefinition location in locationsById.Values)
            {
                builder.Append("- id=");
                builder.Append(location.LocationId);
                builder.Append(", name=");
                builder.Append(location.DisplayName);
                builder.Append(", type=");
                builder.Append(location.Type);

                if (!string.IsNullOrWhiteSpace(location.AreaId))
                {
                    builder.Append(", areaId=");
                    builder.Append(location.AreaId);
                }

                builder.Append(", open=");
                builder.Append(location.AlwaysOpen ? "always" : $"{location.OpenHour:00}:00-{location.CloseHour:00}:00");

                string[] capabilities = location.CapabilityTags;
                if (capabilities.Length > 0)
                {
                    builder.Append(", capabilities=");
                    builder.Append(string.Join("|", capabilities));
                }

                if (!string.IsNullOrWhiteSpace(location.Description))
                {
                    builder.Append(", description=");
                    builder.Append(location.Description.Replace('\n', ' ').Replace('\r', ' '));
                }

                builder.AppendLine();
            }

            return builder.ToString();
        }

        private static string BuildSchema()
        {
            return
                "{" +
                "\"type\":\"object\"," +
                "\"additionalProperties\":false," +
                "\"properties\":{" +
                "\"summary\":{\"type\":\"string\"}," +
                "\"intents\":{\"type\":\"array\",\"items\":{" +
                "\"type\":\"object\"," +
                "\"additionalProperties\":false," +
                "\"properties\":{" +
                "\"label\":{\"type\":\"string\"}," +
                "\"earliestStartHour\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":23}," +
                "\"earliestStartMinute\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":59}," +
                "\"latestEndHour\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":23}," +
                "\"latestEndMinute\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":59}," +
                "\"targetLocationId\":{\"type\":\"string\",\"description\":\"Empty string, or one exact id from Allowed locations. Location-based activities must be selected from that location's listed task templates.\"}," +
                "\"targetActorId\":{\"type\":\"string\"}," +
                "\"desiredOutcome\":{\"type\":\"string\"}," +
                "\"allowedBehaviors\":{\"type\":\"string\"}," +
                "\"completionCondition\":{\"type\":\"string\"}," +
                "\"activityKind\":{\"type\":\"string\",\"description\":\"Empty for prep or person-first intents. For shared/location-defined activities, copy the activityKind from the selected location's listed task template.\"}," +
                "\"participantActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
                "\"requiredActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
                "\"optionalActorIds\":{\"type\":\"array\",\"items\":{\"type\":\"string\"}}," +
                "\"patienceMinutes\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":240}," +
                "\"priority\":{\"type\":\"integer\",\"minimum\":0,\"maximum\":100}," +
                "\"canInterruptRoutine\":{\"type\":\"boolean\"}," +
                "\"reason\":{\"type\":\"string\"}" +
                "}," +
                "\"required\":[\"label\",\"earliestStartHour\",\"earliestStartMinute\",\"latestEndHour\",\"latestEndMinute\",\"targetLocationId\",\"targetActorId\",\"desiredOutcome\",\"allowedBehaviors\",\"completionCondition\",\"activityKind\",\"participantActorIds\",\"requiredActorIds\",\"optionalActorIds\",\"patienceMinutes\",\"priority\",\"canInterruptRoutine\",\"reason\"]" +
                "}}" +
                "}," +
                "\"required\":[\"summary\",\"intents\"]" +
                "}";
        }

        private NpcDailyPlan BuildRuntimePlan(NpcProfile profile, GameDate targetDate, NpcDailyPlanAiResponse aiPlan, NpcSchedule baseSchedule)
        {
            NpcDailyPlan plan = new NpcDailyPlan(profile.NpcId, targetDate, aiPlan.summary);
            NpcDailyIntentAiEntry[] intents = aiPlan.intents ?? Array.Empty<NpcDailyIntentAiEntry>();
            int intentCount = Mathf.Min(intents.Length, maxIntents);
            for (int i = 0; i < intentCount; i++)
            {
                NpcDailyIntent intent = ConvertIntent(intents[i]);
                if (intent != null)
                {
                    plan.AddIntent(intent);
                }
            }

            NpcDailyPlanAiEntry[] entries = aiPlan.entries ?? Array.Empty<NpcDailyPlanAiEntry>();
            int count = Mathf.Min(entries.Length, maxIntents);
            for (int i = 0; i < count; i++)
            {
                RuntimeScheduleEntry entry = ConvertEntry(entries[i]);
                if (entry != null)
                {
                    plan.AddEntry(entry);
                }
            }

            if (plan.Intents.Count == 0 && plan.Entries.Count == 0 && copyBaseScheduleWhenAiFails)
            {
                CopyBaseSchedule(baseSchedule, plan, "AI returned no usable entries; copied base schedule.");
            }

            return plan;
        }

        private NpcDailyIntent ConvertIntent(NpcDailyIntentAiEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            string locationId = string.IsNullOrWhiteSpace(entry.targetLocationId) ? string.Empty : entry.targetLocationId.Trim();
            if (!string.IsNullOrWhiteSpace(locationId) && !locationsById.ContainsKey(locationId))
            {
                if (logPlans)
                {
                    Debug.LogWarning($"[AI Daily Plan] Dropped intent '{entry.label}' because targetLocationId '{entry.targetLocationId}' is not registered.", this);
                }

                return null;
            }

            return new NpcDailyIntent(
                string.IsNullOrWhiteSpace(entry.label) ? entry.desiredOutcome : entry.label,
                new GameTime(entry.earliestStartHour, entry.earliestStartMinute),
                new GameTime(entry.latestEndHour, entry.latestEndMinute),
                locationId,
                entry.targetActorId,
                entry.desiredOutcome,
                entry.allowedBehaviors,
                entry.completionCondition,
                entry.activityKind,
                CleanActorIds(entry.participantActorIds),
                CleanActorIds(entry.requiredActorIds),
                CleanActorIds(entry.optionalActorIds),
                Mathf.Clamp(entry.patienceMinutes, 0, 240),
                entry.priority,
                entry.canInterruptRoutine,
                entry.reason);
        }

        private string BuildActorListText(string selfNpcId)
        {
            StringBuilder builder = new StringBuilder();
            builder.AppendLine("- id=player, name=Player, type=Player");

            NpcRuntimeState[] npcs = FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcProfile profile = npcs[i] != null ? npcs[i].Profile : null;
                if (profile == null || string.IsNullOrWhiteSpace(profile.NpcId))
                {
                    continue;
                }

                builder.Append("- id=");
                builder.Append(profile.NpcId);
                builder.Append(", name=");
                builder.Append(profile.DisplayName);
                builder.Append(", role=");
                builder.Append(profile.Role);
                if (string.Equals(profile.NpcId, selfNpcId, StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append(", self=true");
                }

                builder.AppendLine();
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private string BuildLocationTaskCatalogText()
        {
            if (locationsById.Count == 0)
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            foreach (LocationDefinition location in locationsById.Values)
            {
                if (location == null)
                {
                    continue;
                }

                builder.Append("- id=");
                builder.Append(location.LocationId);
                builder.Append(", name=");
                builder.Append(location.DisplayName);
                builder.AppendLine();

                string taskSummary = location.BuildTaskTemplateSummary();
                if (string.IsNullOrWhiteSpace(taskSummary) || string.Equals(taskSummary.Trim(), "(none)", StringComparison.OrdinalIgnoreCase))
                {
                    builder.AppendLine("  - tasks: (none)");
                    continue;
                }

                string[] lines = taskSummary.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < lines.Length; i++)
                {
                    builder.Append("  ");
                    builder.AppendLine(lines[i]);
                }
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private static string[] CleanActorIds(string[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<string>();
            }

            List<string> cleaned = new List<string>();
            for (int i = 0; i < ids.Length; i++)
            {
                string id = string.IsNullOrWhiteSpace(ids[i]) ? string.Empty : ids[i].Trim();
                if (string.IsNullOrWhiteSpace(id) || ContainsActorId(cleaned, id))
                {
                    continue;
                }

                cleaned.Add(id);
            }

            return cleaned.ToArray();
        }

        private static bool ContainsActorId(List<string> ids, string id)
        {
            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private RuntimeScheduleEntry ConvertEntry(NpcDailyPlanAiEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.locationId))
            {
                return null;
            }

            if (!locationsById.TryGetValue(entry.locationId.Trim(), out LocationDefinition location))
            {
                if (logPlans)
                {
                    Debug.LogWarning($"[AI Daily Plan] Dropped entry '{entry.label}' because locationId '{entry.locationId}' is not registered by any LocationMarker.", this);
                }

                return null;
            }

            GameTime start = new GameTime(entry.startHour, entry.startMinute);
            GameTime end = new GameTime(entry.endHour, entry.endMinute);
            if (start.Equals(end))
            {
                if (logPlans)
                {
                    Debug.LogWarning($"[AI Daily Plan] Dropped entry '{entry.label}' because start and end time are the same.", this);
                }

                return null;
            }

            return new RuntimeScheduleEntry(
                string.IsNullOrWhiteSpace(entry.label) ? entry.actionName : entry.label,
                start,
                end,
                location,
                entry.actionName,
                Mathf.Clamp(entry.priority, 0, 100),
                entry.interruptible,
                ScheduleSource.DailyPlan,
                entry.reason);
        }

        private void ApplyFallbackPlan(NpcScheduleAgent agent, GameDate targetDate, string reason)
        {
            NpcProfile profile = agent != null && agent.RuntimeState != null ? agent.RuntimeState.Profile : null;
            if (profile == null || scheduleSystem == null)
            {
                return;
            }

            if (!copyBaseScheduleWhenAiFails)
            {
                Debug.LogWarning($"[AI Daily Plan] {profile.DisplayName}: {reason}", this);
                return;
            }

            NpcDailyPlan plan = new NpcDailyPlan(profile.NpcId, targetDate, reason);
            CopyBaseSchedule(agent.Schedule, plan, reason);
            scheduleSystem.SetDailyPlan(profile.NpcId, targetDate, plan);
            Debug.LogWarning($"[AI Daily Plan] {profile.DisplayName}: {reason}", this);
        }

        private static void CopyBaseSchedule(NpcSchedule baseSchedule, NpcDailyPlan plan, string reason)
        {
            if (baseSchedule == null || baseSchedule.Entries == null)
            {
                return;
            }

            for (int i = 0; i < baseSchedule.Entries.Length; i++)
            {
                RuntimeScheduleEntry entry = RuntimeScheduleEntry.FromBaseEntry(baseSchedule.Entries[i]);
                if (entry == null)
                {
                    continue;
                }

                plan.AddEntry(new RuntimeScheduleEntry(
                    entry.Label,
                    entry.StartTime,
                    entry.EndTime,
                    entry.TargetLocation,
                    entry.ActionName,
                    entry.Priority,
                    entry.Interruptible,
                    ScheduleSource.DailyPlan,
                    reason));
            }
        }

        private static bool TryParsePlan(string rawResponse, out NpcDailyPlanAiResponse plan, out string error)
        {
            plan = null;
            error = string.Empty;

            OpenAiResponsesApiResponse response = JsonUtility.FromJson<OpenAiResponsesApiResponse>(rawResponse);
            if (response == null)
            {
                error = "AI daily plan response could not be parsed.";
                return false;
            }

            if (response.error != null && !string.IsNullOrWhiteSpace(response.error.message))
            {
                error = response.error.message;
                return false;
            }

            string outputText = ExtractOutputText(response);
            if (string.IsNullOrWhiteSpace(outputText))
            {
                error = "AI daily plan response did not include output text.";
                return false;
            }

            plan = JsonUtility.FromJson<NpcDailyPlanAiResponse>(outputText);
            if (plan == null)
            {
                error = $"AI daily plan JSON could not be parsed: {outputText}";
                return false;
            }

            return true;
        }

        private static string ExtractOutputText(OpenAiResponsesApiResponse response)
        {
            if (response.output == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < response.output.Length; i++)
            {
                OpenAiOutputContent[] content = response.output[i].content;
                if (content == null)
                {
                    continue;
                }

                for (int j = 0; j < content.Length; j++)
                {
                    if (!string.IsNullOrWhiteSpace(content[j].text))
                    {
                        return content[j].text;
                    }
                }
            }

            return string.Empty;
        }

        private void RefreshLocationCache()
        {
            locationsById.Clear();
            LocationMarker[] markers = FindObjectsByType<LocationMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                LocationDefinition definition = markers[i] != null ? markers[i].Definition : null;
                if (definition == null || string.IsNullOrWhiteSpace(definition.LocationId))
                {
                    continue;
                }

                locationsById[definition.LocationId] = definition;
            }

            if (logPlans)
            {
                Debug.Log($"[AI Daily Plan] Found {locationsById.Count} allowed locations.", this);
            }
        }

        private void HandleDayEnding(GameDate date)
        {
            if (!generateOnDayEnding || clock == null)
            {
                return;
            }

            StartCoroutine(GeneratePlansForDateRoutine(date, clock.GetNextDate(date), true));
        }

        private static string EscapeJson(string value)
        {
            return NpcAiPromptBuilder.EscapeJson(value);
        }
    }
}
