using System;
using CityStateSim.AI;
using CityStateSim.Core;
using CityStateSim.Dialogue;
using CityStateSim.Encounters;
using CityStateSim.Locations;
using CityStateSim.Memory;
using CityStateSim.Movement;
using CityStateSim.NPC;
using CityStateSim.Perception;
using CityStateSim.Relationships;
using CityStateSim.Quests;
using CityStateSim.Schedule;
using CityStateSim.SecondaryEvents;
using CityStateSim.SocialPlans;
using CityStateSim.Tasks;
using UnityEngine;

namespace CityStateSim.Behavior
{
    [RequireComponent(typeof(NpcRuntimeState))]
    [RequireComponent(typeof(NpcBehaviorState))]
    public sealed class NpcBehaviorController : MonoBehaviour
    {
        private const string AiThinkingPauseReason = "ai_thinking";

        [Header("References")]
        [SerializeField] private NpcBrainProviderBehaviour brainProvider;
        [SerializeField] private GameClock clock;
        [SerializeField] private RelationshipSystem relationshipSystem;
        [SerializeField] private MemorySystem memorySystem;
        [SerializeField] private NpcTaskController taskController;
        [SerializeField] private NpcActionExecutor actionExecutor;
        [SerializeField] private NpcPerceptionSensor perceptionSensor;
        [SerializeField] private ScheduleSystem scheduleSystem;
        [SerializeField] private ConversationArbiter conversationArbiter;
        [SerializeField] private SecondaryEventSystem secondaryEventSystem;
        [SerializeField] private SocialPlanSystem socialPlanSystem;
        [SerializeField] private PendingEncounterSystem pendingEncounterSystem;
        [SerializeField] private DialogueController dialogueController;
        [SerializeField] private QuestSystem questSystem;

        [Header("Context")]
        [SerializeField] private string currentEmotion = "neutral";
        [SerializeField, TextArea] private string playerRelationshipSummary;
        [SerializeField, TextArea] private string recentMemorySummary;
        [SerializeField, TextArea] private string factSummary;
        [SerializeField, TextArea] private string perceptionSummary;
        [SerializeField, TextArea] private string observedEventSummary;
        [SerializeField, TextArea] private string worldEventTemplateSummary;
        [SerializeField, TextArea] private string festivalRuleSummary;

        [Header("Request Policy")]
        [SerializeField] private bool requestOnStart;
        [SerializeField] private bool requestImmediateBehaviorOnDayEnding;
        [SerializeField] private bool allowPerceptionDrivenDecisionRequests;
        [SerializeField] private bool allowPendingEncounterDecisionRequests = true;
        [SerializeField, Min(0f)] private float minSecondsBetweenRequests = 10f;
        [SerializeField, Min(1)] private int maxSecondaryEventLookupResults = 8;

        private NpcRuntimeState runtimeState;
        private NpcBehaviorState behaviorState;
        private NpcMovementAgent movementAgent;
        private float lastRequestRealtime = -999f;
        private bool requestInFlight;
        private string pendingTriggeredEncounterId = string.Empty;

        public bool RequestInFlight => requestInFlight;
        public NpcBrainProviderBehaviour BrainProvider => brainProvider;

        public event Action<NpcAiRequest> DecisionRequested;
        public event Action<NpcAiDecision> DecisionReceived;
        public event Action<string> DecisionFailed;

        private void Awake()
        {
            runtimeState = GetComponent<NpcRuntimeState>();
            behaviorState = GetComponent<NpcBehaviorState>();
            movementAgent = GetComponent<NpcMovementAgent>();

            if (brainProvider == null)
            {
                brainProvider = NpcBrainProviderBehaviour.FindPreferredProvider();
            }

            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (relationshipSystem == null)
            {
                relationshipSystem = FindFirstObjectByType<RelationshipSystem>();
            }

            if (memorySystem == null)
            {
                memorySystem = FindFirstObjectByType<MemorySystem>();
            }

            if (taskController == null)
            {
                taskController = GetComponent<NpcTaskController>();
            }

            if (actionExecutor == null)
            {
                actionExecutor = GetComponent<NpcActionExecutor>();
            }

            if (perceptionSensor == null)
            {
                perceptionSensor = GetComponent<NpcPerceptionSensor>();
            }

            if (scheduleSystem == null)
            {
                scheduleSystem = FindFirstObjectByType<ScheduleSystem>();
            }

            if (conversationArbiter == null)
            {
                conversationArbiter = FindFirstObjectByType<ConversationArbiter>();
            }

            if (secondaryEventSystem == null)
            {
                secondaryEventSystem = FindFirstObjectByType<SecondaryEventSystem>();
            }

            if (socialPlanSystem == null)
            {
                socialPlanSystem = FindFirstObjectByType<SocialPlanSystem>();
            }

            if (pendingEncounterSystem == null)
            {
                pendingEncounterSystem = PendingEncounterSystem.GetOrCreate();
            }

            if (dialogueController == null)
            {
                dialogueController = FindFirstObjectByType<DialogueController>();
            }

            if (questSystem == null)
            {
                questSystem = FindFirstObjectByType<QuestSystem>();
            }
        }

        private void OnEnable()
        {
            if (clock != null)
            {
                clock.DayEnding += HandleDayEnding;
            }

            if (perceptionSensor != null)
            {
                perceptionSensor.SignificantChangeDetected += HandlePerceptionChanged;
                perceptionSensor.ObservationsRefreshed += HandlePerceptionRefreshed;
            }
        }

        private void Start()
        {
            if (requestOnStart)
            {
                RequestDecision();
            }
        }

        private void OnDisable()
        {
            if (clock != null)
            {
                clock.DayEnding -= HandleDayEnding;
            }

            if (perceptionSensor != null)
            {
                perceptionSensor.SignificantChangeDetected -= HandlePerceptionChanged;
                perceptionSensor.ObservationsRefreshed -= HandlePerceptionRefreshed;
            }
        }

        public void SetObservedEventSummary(string summary)
        {
            observedEventSummary = summary;
            worldEventTemplateSummary = string.Empty;
        }

        public void SetObservedWorldEventContext(string summary, string templateSummary)
        {
            observedEventSummary = summary;
            worldEventTemplateSummary = templateSummary;
        }

        public void SetFestivalRuleSummary(string summary)
        {
            festivalRuleSummary = summary;
        }

        public void SetRelationshipSummary(string summary)
        {
            playerRelationshipSummary = summary;
        }

        public void SetRecentMemorySummary(string summary)
        {
            recentMemorySummary = summary;
        }

        public void RefreshContextFromSystems()
        {
            NpcProfile profile = runtimeState != null ? runtimeState.Profile : null;
            if (profile == null)
            {
                return;
            }

            if (relationshipSystem != null)
            {
                playerRelationshipSummary = relationshipSystem.GetPlayerSummary(profile);
            }

            if (memorySystem != null)
            {
                string recent = memorySystem.BuildRecentSummaryWithoutDialogueChatter(profile.NpcId, 12);
                string longTerm = memorySystem.BuildLongTermSummary(profile.NpcId);
                string facts = memorySystem.BuildRecentFactSummary(profile.NpcId, 16);
                string values = profile.ValueProfile != null ? profile.ValueProfile.ToSummary() : "values: default/unknown";
                factSummary = facts;
                recentMemorySummary =
                    $"Recent short-term memories:\n{recent}\n\nLong-term retained memories:\n{longTerm}\n\n{values}";
            }

            if (perceptionSensor != null)
            {
                perceptionSummary = perceptionSensor.BuildObservationSummary();
            }
        }

        public void RequestDecision()
        {
            RequestDecisionInternal(false);
        }

        public void ForceRequestDecision()
        {
            RequestDecisionInternal(true);
        }

        public bool TryRequestPreviewDecision(string observedEventOverride, Action<NpcAiDecision> onSuccess, Action<string> onError)
        {
            return TryRequestPreviewDecision(observedEventOverride, onSuccess, onError, false);
        }

        public bool TryRequestPreviewDecisionIgnoringNpcConversation(string observedEventOverride, Action<NpcAiDecision> onSuccess, Action<string> onError)
        {
            return TryRequestPreviewDecision(observedEventOverride, onSuccess, onError, true);
        }

        private bool TryRequestPreviewDecision(string observedEventOverride, Action<NpcAiDecision> onSuccess, Action<string> onError, bool ignoreNpcConversation)
        {
            if (brainProvider == null)
            {
                onError?.Invoke("No NPC brain provider assigned.");
                return false;
            }

            if (clock == null)
            {
                onError?.Invoke("No GameClock found for NPC AI context.");
                return false;
            }

            if (requestInFlight)
            {
                return false;
            }

            if (!ignoreNpcConversation && IsBlockedByNpcConversation())
            {
                return false;
            }

            NpcAiRequest request = BuildRequest(observedEventOverride);
            requestInFlight = true;
            SetThinkingPaused(true);
            lastRequestRealtime = Time.realtimeSinceStartup;
            DecisionRequested?.Invoke(request);
            NpcAiSecondaryEventResolver.RequestDecision(
                brainProvider,
                request,
                secondaryEventSystem,
                maxSecondaryEventLookupResults,
                decision =>
                {
                    requestInFlight = false;
                    SetThinkingPaused(false);
                    SanitizePortraitName(decision, runtimeState != null ? runtimeState.Profile : null);
                    ApplyPendingEncounterChanges(decision);
                    ApplySocialPlanChanges(decision, observedEventOverride);
                    onSuccess?.Invoke(decision);
                },
                error =>
                {
                    requestInFlight = false;
                    SetThinkingPaused(false);
                    onError?.Invoke(error);
                    Debug.LogWarning($"[NPC AI] {name}: {error}", this);
                },
                DecisionRequested);

            return true;
        }

        public bool TryRequestDecision()
        {
            if (requestInFlight)
            {
                return false;
            }

            if (Time.realtimeSinceStartup - lastRequestRealtime < minSecondsBetweenRequests)
            {
                return false;
            }

            RequestDecisionInternal(false);
            return true;
        }

        private void RequestDecisionInternal(bool ignoreCooldown)
        {
            if (brainProvider == null)
            {
                ReportFailure("No NPC brain provider assigned.");
                return;
            }

            if (clock == null)
            {
                ReportFailure("No GameClock found for NPC AI context.");
                return;
            }

            if (requestInFlight)
            {
                return;
            }

            if (IsBlockedByNpcConversation())
            {
                return;
            }

            if (!ignoreCooldown && Time.realtimeSinceStartup - lastRequestRealtime < minSecondsBetweenRequests)
            {
                return;
            }

            NpcAiRequest request = BuildRequest(null);
            requestInFlight = true;
            SetThinkingPaused(true);
            lastRequestRealtime = Time.realtimeSinceStartup;
            DecisionRequested?.Invoke(request);

            NpcAiSecondaryEventResolver.RequestDecision(
                brainProvider,
                request,
                secondaryEventSystem,
                maxSecondaryEventLookupResults,
                HandleDecisionSuccess,
                ReportFailure,
                DecisionRequested);
        }

        private NpcAiRequest BuildRequest(string observedEventOverride)
        {
            RefreshContextFromSystems();
            NpcProfile profile = runtimeState != null ? runtimeState.Profile : null;
            NpcAiRequest request = NpcAiRequest.FromRuntimeState(runtimeState, clock.CurrentDate, clock.CurrentTime);
            request.currentEmotion = currentEmotion;
            LocationDefinition actualLocation = runtimeState != null ? runtimeState.ActualLocation : null;
            request.currentLocationTaskSummary = actualLocation != null
                ? actualLocation.BuildTaskTemplateSummary()
                : "(none)";
            request.currentNpcInteractionTemplateSummary = profile != null
                ? profile.BuildInteractionTemplateSummary()
                : "(none)";
            request.currentWorldEventTemplateSummary = string.IsNullOrWhiteSpace(worldEventTemplateSummary)
                ? "(none)"
                : worldEventTemplateSummary;
            request.playerRelationshipSummary = playerRelationshipSummary;
            request.recentMemorySummary = recentMemorySummary;
            request.sameDayPlayerDialogueTranscript = memorySystem != null && profile != null
                ? memorySystem.BuildPlayerDialogueTranscriptForDate(profile.NpcId, clock.CurrentDate)
                : string.Empty;
            request.factSummary = factSummary;
            request.perceptionSummary = perceptionSummary;
            request.observedEventSummary = observedEventOverride ?? observedEventSummary;
            request.rollingGoalSummary = actionExecutor != null
                ? actionExecutor.BuildRollingGoalSummary()
                : "(none)";
            request.festivalRuleSummary = festivalRuleSummary;
            request.pendingEncounterSummary = pendingEncounterSystem != null && profile != null
                ? pendingEncounterSystem.BuildSummaryForNpc(profile.NpcId)
                : "(none)";
            request.socialPlanSummary = socialPlanSystem != null && profile != null
                ? socialPlanSystem.BuildPlanSummaryForNpc(profile.NpcId)
                : "(none)";
            request.playerQuestSummary = questSystem != null && profile != null
                ? questSystem.BuildQuestSummaryForNpc(profile.NpcId)
                : "(none)";
            request.allowedLocationSummary = BuildAllowedLocationSummary();
            request.allowedActorSummary = BuildAllowedActorSummary(profile != null ? profile.NpcId : string.Empty);
            request.secondaryEventLookupAvailable = secondaryEventSystem != null;
            request.secondaryEventAccessSummary = secondaryEventSystem != null && profile != null
                ? secondaryEventSystem.BuildAccessSummaryForNpc(profile.NpcId)
                : string.Empty;
            request.currentAction = BuildCurrentActionSummary(request.currentAction);
            return request;
        }

        private string BuildCurrentActionSummary(string scheduleAction)
        {
            string intentSummary = BuildCurrentIntentSummary();
            if (taskController == null || taskController.CurrentTask == null)
            {
                string plannedLocation = runtimeState != null && runtimeState.PlannedLocation != null
                    ? $"{runtimeState.PlannedLocation.LocationId} ({runtimeState.PlannedLocation.DisplayName})"
                    : "(none)";
                return $"{scheduleAction}; plannedScheduleLocation={plannedLocation}; currentDailyIntent={intentSummary}";
            }

            NpcTask task = taskController.CurrentTask;
            string locationId = task.TargetLocation != null ? task.TargetLocation.LocationId : string.Empty;
            return
                $"{scheduleAction}; activeTask={task.Kind}; taskLabel={task.Label}; " +
                $"targetLocationId={locationId}; targetActorId={task.TargetActorId}; " +
                $"activityKind={task.ActivityKind}; activityKey={task.ActivityKey}; " +
                $"participants={JoinIds(task.ParticipantActorIds)}; required={JoinIds(task.RequiredActorIds)}; " +
                $"priority={task.Priority}; oneShot={task.OneShot}; reason={task.Reason}; " +
                $"currentDailyIntent={intentSummary}";
        }

        private static string JoinIds(string[] ids)
        {
            return ids == null || ids.Length == 0 ? "" : string.Join(",", ids);
        }

        private string BuildCurrentIntentSummary()
        {
            if (scheduleSystem == null)
            {
                return "(none)";
            }

            NpcScheduleAgent agent = GetComponent<NpcScheduleAgent>();
            NpcDailyIntent intent = scheduleSystem.GetCurrentIntent(agent);
            return intent != null ? intent.ToSummaryLine() : "(none)";
        }

        private static string BuildAllowedLocationSummary()
        {
            LocationMarker[] markers = FindObjectsByType<LocationMarker>(FindObjectsSortMode.None);
            if (markers == null || markers.Length == 0)
            {
                return "(none)";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < markers.Length; i++)
            {
                LocationDefinition definition = markers[i] != null ? markers[i].Definition : null;
                if (definition == null || string.IsNullOrWhiteSpace(definition.LocationId))
                {
                    continue;
                }

                builder.Append("- id=");
                builder.Append(definition.LocationId);
                builder.Append(", name=");
                builder.Append(definition.DisplayName);
                builder.Append(", type=");
                builder.Append(definition.Type);

                if (!string.IsNullOrWhiteSpace(definition.Description))
                {
                    builder.Append(", description=");
                    builder.Append(definition.Description.Replace('\n', ' ').Replace('\r', ' '));
                }

                string[] capabilityTags = definition.CapabilityTags;
                if (capabilityTags.Length > 0)
                {
                    builder.Append(", capabilities=");
                    builder.Append(string.Join("|", capabilityTags));
                }

                string taskSummary = definition.BuildTaskTemplateSummary();
                if (!string.IsNullOrWhiteSpace(taskSummary)
                    && !string.Equals(taskSummary.Trim(), "(none)", StringComparison.OrdinalIgnoreCase))
                {
                    builder.Append(", tasks=");
                    builder.Append(taskSummary.Replace('\n', ' ').Replace('\r', ' ').Trim());
                }

                builder.AppendLine();
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private static string BuildAllowedActorSummary(string selfNpcId)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();

            PlayerMovementController player = FindFirstObjectByType<PlayerMovementController>();
            if (player != null)
            {
                builder.AppendLine("- id=player, name=Player, type=Player");
            }

            NpcRuntimeState[] npcs = FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcRuntimeState npc = npcs[i];
                NpcProfile npcProfile = npc != null ? npc.Profile : null;
                if (npcProfile == null || string.IsNullOrWhiteSpace(npcProfile.NpcId))
                {
                    continue;
                }

                if (string.Equals(npcProfile.NpcId, selfNpcId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                builder.Append("- id=");
                builder.Append(npcProfile.NpcId);
                builder.Append(", name=");
                builder.Append(npcProfile.DisplayName);
                builder.Append(", role=");
                builder.Append(npcProfile.Role);
                builder.AppendLine();
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private void HandleDecisionSuccess(NpcAiDecision decision)
        {
            requestInFlight = false;
            SetThinkingPaused(false);
            SanitizePortraitName(decision, runtimeState != null ? runtimeState.Profile : null);
            NpcAiDecision executableDecision = SelectExecutableDecision(decision);
            ResolvePendingEncounterDecisionEffects(executableDecision);
            ApplySocialPlanChanges(executableDecision, observedEventSummary);
            SanitizePortraitName(executableDecision, runtimeState != null ? runtimeState.Profile : null);
            behaviorState.ApplyDecision(executableDecision);
            currentEmotion = executableDecision.emotion;
            DecisionReceived?.Invoke(executableDecision);
        }

        private static NpcAiDecision SelectExecutableDecision(NpcAiDecision decision)
        {
            return decision;
        }

        private static void SanitizePortraitName(NpcAiDecision decision, NpcProfile profile)
        {
            if (decision == null)
            {
                return;
            }

            string npcId = profile != null ? profile.NpcId : string.Empty;
            string npcName = profile != null ? profile.DisplayName : string.Empty;
            if (NpcPortraitCatalog.LoadPortrait(npcId, npcName, decision.emotion) == null)
            {
                decision.emotion = NpcPortraitCatalog.GetFallbackPortraitName(npcId, npcName);
            }
        }

        private void ReportFailure(string error)
        {
            requestInFlight = false;
            SetThinkingPaused(false);
            DecisionFailed?.Invoke(error);
            Debug.LogWarning($"[NPC AI] {name}: {error}", this);
        }

        private void SetThinkingPaused(bool paused)
        {
            movementAgent?.SetPause(AiThinkingPauseReason, paused);
        }

        private bool IsBlockedByNpcConversation()
        {
            return conversationArbiter != null && conversationArbiter.IsNpcInConversation(runtimeState);
        }

        private void HandleDayEnding(GameDate date)
        {
            if (!requestImmediateBehaviorOnDayEnding)
            {
                return;
            }

            SetObservedEventSummary($"The day is ending on {date}. Choose one immediate end-of-day behavior response if the situation calls for it.");
            ForceRequestDecision();
        }

        private void HandlePerceptionChanged(NpcPerceptionSensor sensor, string changeSummary)
        {
            if (string.IsNullOrWhiteSpace(changeSummary))
            {
                return;
            }

            if (IsOnlyPlayerPerceptionChange(changeSummary))
            {
                return;
            }

            if (!allowPerceptionDrivenDecisionRequests)
            {
                return;
            }

            SetObservedEventSummary(
                "New sensory information became noticeable. " +
                "This is not automatically urgent; judge like a real person whether it deserves attention. " +
                $"Perception change: {changeSummary}");
            TryRequestDecision();
        }

        private void HandlePerceptionRefreshed(NpcPerceptionSensor sensor)
        {
            if (!allowPendingEncounterDecisionRequests
                || sensor == null
                || pendingEncounterSystem == null
                || runtimeState == null
                || runtimeState.Profile == null
                || clock == null
                || brainProvider == null
                || requestInFlight
                || IsBlockedByNpcConversation()
                || IsBlockedByPlayerDialogue()
                || (clock != null && clock.IsCompletingDay))
            {
                return;
            }

            if (Time.realtimeSinceStartup - lastRequestRealtime < minSecondsBetweenRequests)
            {
                return;
            }

            if (!pendingEncounterSystem.TryGetBestTrigger(
                    runtimeState.Profile.NpcId,
                    sensor.Observations,
                    out PendingEncounterRecord encounter))
            {
                return;
            }

            if (!CanConsiderPendingEncounter(encounter))
            {
                return;
            }

            pendingEncounterSystem.MarkTriggered(encounter);
            pendingTriggeredEncounterId = encounter.EncounterId;
            SetObservedEventSummary(BuildPendingEncounterObservedEvent(encounter, sensor));
            RequestDecisionInternal(false);
        }

        private bool IsBlockedByPlayerDialogue()
        {
            return dialogueController != null
                && (dialogueController.IsConversationWith(runtimeState)
                    || dialogueController.HasPendingReplyFor(runtimeState));
        }

        private bool CanConsiderPendingEncounter(PendingEncounterRecord encounter)
        {
            if (encounter == null)
            {
                return false;
            }

            NpcTask task = taskController != null ? taskController.CurrentTask : null;
            if (task != null && task.Kind != NpcTaskKind.FollowSchedule)
            {
                return task.Interruptible
                    && encounter.Priority >= task.Priority
                    && string.Equals(encounter.InterruptPolicy, PendingEncounterSystem.InterruptAnything, StringComparison.OrdinalIgnoreCase);
            }

            if (string.Equals(encounter.InterruptPolicy, PendingEncounterSystem.InterruptAnything, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return !LooksLikeBusyScheduleAction(runtimeState.CurrentAction);
        }

        private string BuildPendingEncounterObservedEvent(PendingEncounterRecord encounter, NpcPerceptionSensor sensor)
        {
            return
                "PendingEncounterOpportunity: The NPC just perceived a target from their persistent pending encounter list. " +
                "This is an opportunity, not an automatic command. Decide whether to act now, postpone, update/remove this pending encounter, or continue the current routine. " +
                $"Pending encounter: {encounter.ToPromptLine()}. " +
                $"Current direct perception: {(sensor != null ? sensor.BuildObservationSummary() : "(no perception sensor)")}";
        }

        private void ApplyPendingEncounterChanges(NpcAiDecision decision)
        {
            if (pendingEncounterSystem == null || runtimeState == null || runtimeState.Profile == null || decision == null)
            {
                return;
            }

            pendingEncounterSystem.ApplyDecision(runtimeState.Profile, decision);
        }

        private void ApplySocialPlanChanges(NpcAiDecision decision, string context)
        {
            if (socialPlanSystem == null || runtimeState == null || runtimeState.Profile == null || decision == null)
            {
                return;
            }

            socialPlanSystem.ApplyDecision(
                runtimeState,
                decision,
                context ?? observedEventSummary,
                new[] { runtimeState });
        }

        private void ResolvePendingEncounterDecisionEffects(NpcAiDecision decision)
        {
            if (pendingEncounterSystem == null || runtimeState == null || runtimeState.Profile == null || decision == null)
            {
                pendingTriggeredEncounterId = string.Empty;
                return;
            }

            string triggeredId = pendingTriggeredEncounterId;
            pendingTriggeredEncounterId = string.Empty;
            ApplyPendingEncounterChanges(decision);
            pendingEncounterSystem.ResolveTriggeredEncounter(runtimeState.Profile.NpcId, triggeredId, decision);
        }

        private static bool LooksLikeBusyScheduleAction(string action)
        {
            if (string.IsNullOrWhiteSpace(action))
            {
                return false;
            }

            string normalized = action.ToLowerInvariant();
            return normalized.Contains("sleep")
                || normalized.Contains("work")
                || normalized.Contains("service")
                || normalized.Contains("rush")
                || normalized.Contains("clinic")
                || normalized.Contains("triage")
                || normalized.Contains("appointment")
                || normalized.Contains("on_call")
                || normalized.Contains("kitchen")
                || normalized.Contains("prepare")
                || normalized.Contains("prep")
                || normalized.Contains("inventory")
                || normalized.Contains("bake")
                || normalized.Contains("clean_tools");
        }

        private static bool ContainsPlayerPerception(string changeSummary)
        {
            return !string.IsNullOrWhiteSpace(changeSummary)
                && (changeSummary.IndexOf("(player)", StringComparison.OrdinalIgnoreCase) >= 0
                    || changeSummary.IndexOf("id=player", StringComparison.OrdinalIgnoreCase) >= 0
                    || changeSummary.IndexOf("targetActorId=player", StringComparison.OrdinalIgnoreCase) >= 0
                    || changeSummary.IndexOf("lost perception of player", StringComparison.OrdinalIgnoreCase) >= 0
                    || changeSummary.IndexOf("perceived change in Player:", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool IsOnlyPlayerPerceptionChange(string changeSummary)
        {
            if (string.IsNullOrWhiteSpace(changeSummary))
            {
                return false;
            }

            string[] lines = changeSummary.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                return ContainsPlayerPerception(changeSummary);
            }

            for (int i = 0; i < lines.Length; i++)
            {
                if (!ContainsPlayerPerception(lines[i]))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
