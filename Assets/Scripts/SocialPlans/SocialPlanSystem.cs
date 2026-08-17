using System;
using System.Collections.Generic;
using System.Text;
using CityStateSim.AI;
using CityStateSim.Core;
using CityStateSim.Locations;
using CityStateSim.Memory;
using CityStateSim.NPC;
using CityStateSim.Tasks;
using UnityEngine;

namespace CityStateSim.SocialPlans
{
    public sealed class SocialPlanSystem : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameClock clock;
        [SerializeField] private LocationSystem locationSystem;
        [SerializeField] private MemorySystem memorySystem;

        [Header("Policy")]
        [SerializeField, Min(0)] private int defaultPatienceMinutes = 20;
        [SerializeField, Range(0, 100)] private int defaultPriority = 70;
        [SerializeField, Min(0)] private int duplicateCompletionSuppressionMinutes = 90;
        [SerializeField] private bool logDebug = true;

        private readonly Dictionary<string, SocialPlan> plansById = new Dictionary<string, SocialPlan>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> completedPlanIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> completedActivitySignatureMinutes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private void Awake()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (locationSystem == null)
            {
                locationSystem = FindFirstObjectByType<LocationSystem>();
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
                clock.DayChanged += HandleDayChanged;
            }
        }

        private void OnDisable()
        {
            if (clock != null)
            {
                clock.DayChanged -= HandleDayChanged;
            }
        }

        public void ApplyDecision(
            NpcRuntimeState sourceNpc,
            NpcAiDecision decision,
            string context,
            IReadOnlyList<NpcRuntimeState> relatedNpcs)
        {
            if (decision == null)
            {
                return;
            }

            NpcSocialPlanChange[] changes = decision.socialPlanChanges ?? Array.Empty<NpcSocialPlanChange>();
            for (int i = 0; i < changes.Length; i++)
            {
                ApplyChange(sourceNpc, changes[i], context, relatedNpcs);
            }
        }

        public bool TryRegisterFromDecision(
            NpcRuntimeState sourceNpc,
            NpcAiDecision decision,
            string context,
            IReadOnlyList<NpcRuntimeState> relatedNpcs,
            bool allowNonAttendActivityIntent,
            out SocialPlan plan)
        {
            plan = null;
            if (decision == null || clock == null)
            {
                return false;
            }

            NpcSocialPlanChange[] changes = decision.socialPlanChanges ?? Array.Empty<NpcSocialPlanChange>();
            bool changed = false;
            for (int i = 0; i < changes.Length; i++)
            {
                if (ApplyChange(sourceNpc, changes[i], context, relatedNpcs, out SocialPlan changedPlan))
                {
                    plan = changedPlan;
                    changed = true;
                }
            }

            if (changed)
            {
                return true;
            }

            bool attendActivity = decision.ParsedIntent == NpcIntentType.AttendActivity;
            if (!attendActivity)
            {
                return false;
            }

            NpcSocialPlanChange fallback = new NpcSocialPlanChange
            {
                operation = "add_or_update",
                label = string.IsNullOrWhiteSpace(decision.nextActionPreference) ? "Shared activity" : decision.nextActionPreference,
                activityKind = string.IsNullOrWhiteSpace(decision.activityKind) ? string.Empty : decision.activityKind.Trim(),
                targetLocationId = !string.IsNullOrWhiteSpace(decision.plannedTargetLocationId)
                    ? decision.plannedTargetLocationId
                    : decision.targetLocationId,
                participantActorIds = decision.participantActorIds ?? Array.Empty<string>(),
                requiredActorIds = decision.requiredActorIds ?? Array.Empty<string>(),
                optionalActorIds = decision.optionalActorIds ?? Array.Empty<string>(),
                acceptedActorIds = BuildRelatedNpcIds(sourceNpc, relatedNpcs),
                patienceMinutes = decision.patienceMinutes,
                priority = decision.confidence > 0f
                    ? Mathf.Clamp(Mathf.RoundToInt(decision.confidence * 100f), defaultPriority, 100)
                    : defaultPriority,
                timingMode = decision.timingMode,
                delayMinutes = decision.delayMinutes,
                scheduledStartHour = decision.scheduledStartHour,
                scheduledStartMinute = decision.scheduledStartMinute,
                reason = string.IsNullOrWhiteSpace(decision.nextActionPreference)
                    ? context ?? string.Empty
                    : decision.nextActionPreference.Trim()
            };

            return ApplyChange(sourceNpc, fallback, context, relatedNpcs, out plan);
        }

        public bool TryCreateDueTaskForNpc(
            NpcRuntimeState npc,
            HashSet<string> locallyCompletedActivityKeys,
            out NpcTask task,
            out SocialPlan plan,
            out string reason)
        {
            task = null;
            plan = null;
            reason = string.Empty;
            if (npc == null || npc.Profile == null || clock == null)
            {
                reason = "missing npc or clock";
                return false;
            }

            string npcId = npc.Profile.NpcId;
            foreach (SocialPlan candidate in plansById.Values)
            {
                if (candidate == null
                    || candidate.IsClosed
                    || completedPlanIds.Contains(candidate.PlanId)
                    || candidate.TargetLocation == null
                    || !candidate.ContainsActor(npcId)
                    || !candidate.IsDue(clock.CurrentDate, clock.CurrentTime))
                {
                    continue;
                }

                if (locallyCompletedActivityKeys != null && locallyCompletedActivityKeys.Contains(candidate.ActivityKey))
                {
                    candidate.MarkActivityAssigned(npcId);
                    continue;
                }

                if (candidate.HasRequiredUnavailable())
                {
                    if (candidate.IsCoordinator(npcId))
                    {
                        candidate.MarkFailed("required participant declined or was unavailable");
                        WritePlanMemory(candidate, $"Social plan failed: {candidate.ToSummaryLine()}", "social_plan_failed", 7);
                    }

                    continue;
                }

                if (candidate.TryGetNextPendingRequiredActor(out string pendingActorId))
                {
                    if (string.Equals(pendingActorId, npcId, StringComparison.OrdinalIgnoreCase))
                    {
                        candidate.MarkParticipantAccepted(npcId, "self participant is already committed");
                        continue;
                    }

                    if (!candidate.IsCoordinator(npcId))
                    {
                        continue;
                    }

                    task = BuildConfirmationTask(candidate, pendingActorId);
                    plan = candidate;
                    reason = "social plan needs participant confirmation";
                    candidate.MarkGathering();
                    return true;
                }

                if (!candidate.IsAcceptedOrArrived(npcId))
                {
                    continue;
                }

                if (candidate.IsActivityAssignedTo(npcId))
                {
                    continue;
                }

                task = BuildActivityTask(candidate);
                plan = candidate;
                reason = "social plan due and required participants accepted";
                candidate.MarkGathering();
                return true;
            }

            return false;
        }

        public void MarkPlanTaskStarted(NpcRuntimeState npc, NpcTask task)
        {
            if (npc == null || npc.Profile == null || task == null || string.IsNullOrWhiteSpace(task.ActivityKey))
            {
                return;
            }

            if (!plansById.TryGetValue(task.ActivityKey, out SocialPlan plan) || plan == null)
            {
                return;
            }

            if (task.Kind == NpcTaskKind.AttendActivity)
            {
                plan.MarkActivityAssigned(npc.Profile.NpcId);
                plan.MarkParticipantAccepted(npc.Profile.NpcId, "assigned to attend activity");
                plan.MarkGathering();
            }
            else if (task.Kind == NpcTaskKind.TalkToActor || task.Kind == NpcTaskKind.FindActor)
            {
                plan.MarkGathering();
            }
        }

        public void MarkPlanCompleted(string activityKey)
        {
            if (string.IsNullOrWhiteSpace(activityKey))
            {
                return;
            }

            completedPlanIds.Add(activityKey);
            if (plansById.TryGetValue(activityKey, out SocialPlan plan) && plan != null)
            {
                plan.MarkCompleted("activity conversation completed");
                string signature = BuildActivitySignature(
                    clock != null ? clock.CurrentDate : plan.Date,
                    plan.TargetLocationId,
                    plan.ActivityKind,
                    plan.ParticipantActorIds);
                int minute = clock != null ? clock.CurrentTime.TotalMinutes : plan.StartTime.TotalMinutes;
                completedActivitySignatureMinutes[signature] = minute;
                WritePlanMemory(plan, $"Social plan completed: {plan.ToSummaryLine()}", "social_plan_completed", 7);
            }
        }

        public void MarkPlanFailed(string activityKey, string reason)
        {
            if (string.IsNullOrWhiteSpace(activityKey))
            {
                return;
            }

            if (plansById.TryGetValue(activityKey, out SocialPlan plan) && plan != null)
            {
                plan.MarkFailed(reason);
                completedPlanIds.Add(activityKey);
                WritePlanMemory(plan, $"Social plan failed: {plan.ToSummaryLine()}", "social_plan_failed", 7);
            }
        }

        public void MarkParticipantAccepted(string planId, string actorId, string reason = "")
        {
            if (TryGetPlan(planId, out SocialPlan plan))
            {
                plan.MarkParticipantAccepted(actorId, reason);
                WritePlanMemory(plan, $"Social plan participant accepted: actorId={actorId}, plan={plan.ToSummaryLine()}", "social_plan_acceptance", 6);
            }
        }

        public void MarkParticipantDeclined(string planId, string actorId, string reason = "")
        {
            if (TryGetPlan(planId, out SocialPlan plan))
            {
                plan.MarkParticipantDeclined(actorId, reason);
                WritePlanMemory(plan, $"Social plan participant declined: actorId={actorId}, plan={plan.ToSummaryLine()}", "social_plan_decline", 6);
            }
        }

        public bool TryGetPlan(string planId, out SocialPlan plan)
        {
            plan = null;
            return !string.IsNullOrWhiteSpace(planId) && plansById.TryGetValue(planId.Trim(), out plan) && plan != null;
        }

        public string BuildPlanSummaryForNpc(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId) || plansById.Count == 0)
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            foreach (SocialPlan plan in plansById.Values)
            {
                if (plan != null && !plan.IsClosed && plan.ContainsActor(npcId))
                {
                    builder.AppendLine("- " + plan.ToSummaryLine());
                }
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private void ApplyChange(
            NpcRuntimeState sourceNpc,
            NpcSocialPlanChange change,
            string context,
            IReadOnlyList<NpcRuntimeState> relatedNpcs)
        {
            ApplyChange(sourceNpc, change, context, relatedNpcs, out _);
        }

        private bool ApplyChange(
            NpcRuntimeState sourceNpc,
            NpcSocialPlanChange change,
            string context,
            IReadOnlyList<NpcRuntimeState> relatedNpcs,
            out SocialPlan plan)
        {
            plan = null;
            if (change == null || clock == null)
            {
                return false;
            }

            change.Clamp();
            string operation = string.IsNullOrWhiteSpace(change.operation) ? "add_or_update" : change.operation.Trim();
            if (string.Equals(operation, "remove", StringComparison.OrdinalIgnoreCase)
                || string.Equals(operation, "cancel", StringComparison.OrdinalIgnoreCase))
            {
                return CancelOrRemovePlan(change, out plan);
            }

            if (string.Equals(operation, "complete", StringComparison.OrdinalIgnoreCase))
            {
                if (TryGetPlan(change.planId, out plan))
                {
                    MarkPlanCompleted(plan.PlanId);
                    return true;
                }

                return false;
            }

            if (!string.Equals(operation, "add_or_update", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return AddOrUpdatePlan(sourceNpc, change, context, relatedNpcs, out plan);
        }

        private bool CancelOrRemovePlan(NpcSocialPlanChange change, out SocialPlan plan)
        {
            plan = null;
            if (change == null)
            {
                return false;
            }

            if (!TryFindPlanForChange(change, out plan))
            {
                return false;
            }

            plan.MarkCancelled(change.reason);
            plansById.Remove(plan.PlanId);
            if (logDebug)
            {
                Debug.Log($"[SocialPlan] Cancelled {plan.ToSummaryLine()}", this);
            }

            return true;
        }

        private bool AddOrUpdatePlan(
            NpcRuntimeState sourceNpc,
            NpcSocialPlanChange change,
            string context,
            IReadOnlyList<NpcRuntimeState> relatedNpcs,
            out SocialPlan plan)
        {
            plan = null;
            bool existingPlan = TryFindPlanForChange(change, out plan);
            string finalLocationId = string.IsNullOrWhiteSpace(change.targetLocationId) ? string.Empty : change.targetLocationId.Trim();
            LocationDefinition finalLocation = null;
            if (string.IsNullOrWhiteSpace(finalLocationId) && existingPlan && plan != null)
            {
                finalLocationId = plan.TargetLocationId;
                finalLocation = plan.TargetLocation;
            }

            if (finalLocation == null
                && !string.IsNullOrWhiteSpace(finalLocationId)
                && TryResolveFinalLocationMarker(finalLocationId, out LocationMarker marker)
                && marker != null
                && marker.Definition != null)
            {
                finalLocation = marker.Definition;
                finalLocationId = finalLocation.LocationId;
            }

            if (string.IsNullOrWhiteSpace(finalLocationId) || finalLocation == null)
            {
                if (logDebug)
                {
                    Debug.Log($"[SocialPlan] Ignored plan change without valid targetLocationId={finalLocationId}. reason={change.reason}", this);
                }

                return false;
            }

            string organizerActorId = !string.IsNullOrWhiteSpace(change.organizerActorId)
                ? change.organizerActorId.Trim()
                : GetNpcId(sourceNpc);
            List<string> participants = BuildParticipantIds(sourceNpc, change, context, relatedNpcs, true);
            if (existingPlan && plan != null)
            {
                AddActorIds(participants, plan.ParticipantActorIds, true);
            }

            if (participants.Count < 2)
            {
                if (logDebug)
                {
                    Debug.Log($"[SocialPlan] Ignored plan change with fewer than two participants. reason={change.reason}", this);
                }

                return false;
            }

            string[] participantIds = participants.ToArray();
            string[] requiredIds = CleanActorIds(change.requiredActorIds, true);
            if (requiredIds.Length == 0 && existingPlan && plan != null)
            {
                requiredIds = plan.RequiredActorIds;
            }

            if (requiredIds.Length == 0)
            {
                requiredIds = RemoveOptionalActors(participantIds, change.optionalActorIds);
                if (requiredIds.Length == 0)
                {
                    requiredIds = participantIds;
                }
            }

            string[] optionalIds = CleanActorIds(change.optionalActorIds, true);
            if (optionalIds.Length == 0 && existingPlan && plan != null)
            {
                optionalIds = plan.OptionalActorIds;
            }

            string[] acceptedIds = BuildAcceptedActorIds(sourceNpc, change, context, relatedNpcs);
            string[] declinedIds = CleanActorIds(change.declinedActorIds, true);
            string[] pendingIds = CleanActorIds(change.pendingActorIds, true);
            if (!existingPlan && pendingIds.Length == 0)
            {
                pendingIds = BuildDefaultPendingIds(participantIds, acceptedIds, declinedIds);
            }

            int patience = change.patienceMinutes > 0 ? change.patienceMinutes : defaultPatienceMinutes;
            int priority = change.priority > 0 ? change.priority : defaultPriority;
            GameDate date;
            GameTime startTime;
            ResolvePlanTime(
                change.timingMode,
                change.delayMinutes,
                change.scheduledStartHour,
                change.scheduledStartMinute,
                out date,
                out startTime);
            if (!existingPlan && IsInvalidNewPlanTime(change.ParsedTimingMode, date, startTime))
            {
                if (logDebug)
                {
                    Debug.Log(
                        $"[SocialPlan] Ignored new social plan with invalid non-immediate time. " +
                        $"timingMode={change.timingMode}, scheduledStart={change.scheduledStartHour:00}:{change.scheduledStartMinute:00}, " +
                        $"resolved={date} {startTime}, now={(clock != null ? clock.CurrentDate.ToString() : "(no date)")} {(clock != null ? clock.CurrentTime.ToString() : "(no time)")}, " +
                        $"targetLocationId={finalLocation.LocationId}, activityKind={change.activityKind}, reason={change.reason}",
                        this);
                }

                return false;
            }

            string activityKind = string.IsNullOrWhiteSpace(change.activityKind) ? string.Empty : change.activityKind.Trim();
            if (string.IsNullOrWhiteSpace(activityKind))
            {
                if (logDebug)
                {
                    Debug.Log(
                        $"[SocialPlan] Ignored plan change without activityKind. " +
                        $"targetLocationId={finalLocation.LocationId}, allowed AttendActivity templates={finalLocation.BuildAvailableTaskTemplateList(NpcTaskKind.AttendActivity.ToString())}. " +
                        $"reason={change.reason}",
                        this);
                }

                return false;
            }

            if (!finalLocation.TryGetAvailableTaskTemplate(activityKind, NpcTaskKind.AttendActivity.ToString(), out _))
            {
                if (logDebug)
                {
                    Debug.Log(
                        $"[SocialPlan] Ignored plan change with unsupported activityKind='{activityKind}' at locationId={finalLocation.LocationId}. " +
                        $"Allowed AttendActivity templates={finalLocation.BuildAvailableTaskTemplateList(NpcTaskKind.AttendActivity.ToString())}. " +
                        $"reason={change.reason}",
                        this);
                }

                return false;
            }

            string label = string.IsNullOrWhiteSpace(change.label) ? "Shared activity" : change.label.Trim();
            string reason = string.IsNullOrWhiteSpace(change.reason) ? context ?? string.Empty : change.reason.Trim();
            string planId = existingPlan && plan != null
                ? plan.PlanId
                : !string.IsNullOrWhiteSpace(change.planId)
                    ? change.planId.Trim()
                    : BuildPlanId(date, startTime, finalLocation.LocationId, activityKind, participantIds, organizerActorId);
            string activitySignature = BuildActivitySignature(date, finalLocation.LocationId, activityKind, participantIds);
            if (!existingPlan && IsRecentlyCompletedDuplicate(activitySignature, date, startTime))
            {
                if (logDebug)
                {
                    Debug.Log($"[SocialPlan] Ignored recently completed duplicate activity. signature={activitySignature}", this);
                }

                return false;
            }

            if (!plansById.TryGetValue(planId, out plan))
            {
                plan = new SocialPlan(
                    planId,
                    label,
                    activityKind,
                    finalLocation,
                    date,
                    startTime,
                    organizerActorId,
                    participantIds,
                    requiredIds,
                    optionalIds,
                    acceptedIds,
                    pendingIds,
                    declinedIds,
                    patience,
                    priority,
                    reason);
                plansById.Add(planId, plan);
                WritePlanMemory(plan, "Shared social plan registered: " + plan.ToSummaryLine(), "social_plan", 7);

                if (logDebug)
                {
                    Debug.Log($"[SocialPlan] Registered {plan.ToSummaryLine()}", this);
                }
            }
            else
            {
                plan.ApplyUpdate(
                    label,
                    activityKind,
                    finalLocation,
                    date,
                    startTime,
                    organizerActorId,
                    participantIds,
                    requiredIds,
                    optionalIds,
                    acceptedIds,
                    pendingIds,
                    declinedIds,
                    patience,
                    priority,
                    reason);
                WritePlanMemory(plan, "Shared social plan updated: " + plan.ToSummaryLine(), "social_plan_update", 7);

                if (logDebug)
                {
                    Debug.Log($"[SocialPlan] Updated {plan.ToSummaryLine()}", this);
                }
            }

            return true;
        }

        private NpcTask BuildConfirmationTask(SocialPlan plan, string targetActorId)
        {
            string cleanedTarget = string.IsNullOrWhiteSpace(targetActorId) ? string.Empty : targetActorId.Trim();
            bool targetIsPlayer = string.Equals(cleanedTarget, "player", StringComparison.OrdinalIgnoreCase);
            string intent = targetIsPlayer ? NpcIntentType.TalkToPlayer.ToString() : NpcIntentType.TalkToNpc.ToString();
            string label = "Confirm social plan with " + cleanedTarget;
            string reason =
                $"SocialPlan due but participant targetActorId={cleanedTarget} has not accepted yet. " +
                $"Ask whether they will join. Plan: {plan.ToSummaryLine()}";

            return new NpcTask(
                label,
                NpcTaskKind.TalkToActor,
                null,
                cleanedTarget,
                plan.Priority,
                true,
                false,
                reason,
                -1f,
                intent,
                NpcEventKind.ScheduleOverride.ToString(),
                reason,
                plan.ActivityKind,
                plan.ActivityKey,
                plan.ParticipantActorIds,
                plan.RequiredActorIds,
                plan.OptionalActorIds,
                plan.PatienceMinutes,
                plan.TargetLocationId,
                "social_plan_invitation",
                plan.OrganizerActorId,
                cleanedTarget,
                plan.TargetLocationId,
                plan.ToSummaryLine());
        }

        private NpcTask BuildActivityTask(SocialPlan plan)
        {
            string[] acceptedNpcParticipants = plan.BuildAcceptedNpcParticipantIds();
            return new NpcTask(
                plan.Label,
                NpcTaskKind.AttendActivity,
                plan.TargetLocation,
                string.Empty,
                plan.Priority,
                true,
                false,
                plan.Reason,
                -1f,
                NpcIntentType.AttendActivity.ToString(),
                NpcEventKind.ScheduleOverride.ToString(),
                plan.Reason,
                plan.ActivityKind,
                plan.ActivityKey,
                acceptedNpcParticipants,
                plan.RequiredActorIds,
                plan.OptionalActorIds,
                plan.PatienceMinutes,
                plan.TargetLocationId,
                "social_plan_activity",
                plan.OrganizerActorId,
                string.Empty,
                plan.TargetLocationId,
                plan.ToSummaryLine());
        }

        private string[] BuildAcceptedActorIds(
            NpcRuntimeState sourceNpc,
            NpcSocialPlanChange change,
            string context,
            IReadOnlyList<NpcRuntimeState> relatedNpcs)
        {
            List<string> ids = new List<string>();
            AddActorIds(ids, change.acceptedActorIds, true);
            AddActorId(ids, GetNpcId(sourceNpc), true);

            if (LooksLikePlayerAgreementContext(context) && (!HasStructuredActorIds(change) || ChangeMentionsPlayer(change)))
            {
                AddActorId(ids, "player", true);
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids.ToArray();
        }

        private static string[] BuildRelatedNpcIds(NpcRuntimeState sourceNpc, IReadOnlyList<NpcRuntimeState> relatedNpcs)
        {
            List<string> ids = new List<string>();
            AddActorId(ids, GetNpcId(sourceNpc), true);
            if (relatedNpcs != null)
            {
                for (int i = 0; i < relatedNpcs.Count; i++)
                {
                    AddActorId(ids, GetNpcId(relatedNpcs[i]), true);
                }
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids.ToArray();
        }

        private bool TryFindPlanForChange(NpcSocialPlanChange change, out SocialPlan plan)
        {
            plan = null;
            if (change == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(change.planId) && plansById.TryGetValue(change.planId.Trim(), out plan))
            {
                return plan != null;
            }

            string locationId = string.IsNullOrWhiteSpace(change.targetLocationId) ? string.Empty : change.targetLocationId.Trim();
            string activityKind = string.IsNullOrWhiteSpace(change.activityKind) ? string.Empty : change.activityKind.Trim();
            string[] actorIds = BuildActorIdsForPlanMatch(change);
            foreach (SocialPlan candidate in plansById.Values)
            {
                if (candidate == null || candidate.IsClosed)
                {
                    continue;
                }

                bool sameLocation = string.IsNullOrWhiteSpace(locationId)
                    || string.Equals(candidate.TargetLocationId, locationId, StringComparison.OrdinalIgnoreCase);
                bool sameActivity = string.IsNullOrWhiteSpace(activityKind)
                    || string.Equals(candidate.ActivityKind, activityKind, StringComparison.OrdinalIgnoreCase);
                if (sameLocation && sameActivity && HasAnyActorOverlap(candidate.ParticipantActorIds, actorIds))
                {
                    plan = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveFinalLocationMarker(string requestedLocationId, out LocationMarker marker)
        {
            marker = null;
            if (locationSystem == null || string.IsNullOrWhiteSpace(requestedLocationId))
            {
                return false;
            }

            if (locationSystem.TryGetMarker(requestedLocationId.Trim(), out LocationMarker requestedMarker)
                && requestedMarker != null
                && requestedMarker.Definition != null)
            {
                marker = requestedMarker;
                return true;
            }

            return false;
        }

        private List<string> BuildParticipantIds(
            NpcRuntimeState sourceNpc,
            NpcSocialPlanChange change,
            string context,
            IReadOnlyList<NpcRuntimeState> relatedNpcs,
            bool allowPlayer)
        {
            List<string> ids = new List<string>();
            AddActorId(ids, GetNpcId(sourceNpc), allowPlayer);
            bool hasStructuredActorIds = HasStructuredActorIds(change);
            if (!hasStructuredActorIds && relatedNpcs != null)
            {
                for (int i = 0; i < relatedNpcs.Count; i++)
                {
                    AddActorId(ids, GetNpcId(relatedNpcs[i]), allowPlayer);
                }
            }

            AddActorIds(ids, change.participantActorIds, allowPlayer);
            AddActorIds(ids, change.requiredActorIds, allowPlayer);
            AddActorIds(ids, change.optionalActorIds, allowPlayer);
            AddActorIds(ids, change.acceptedActorIds, allowPlayer);
            AddActorIds(ids, change.pendingActorIds, allowPlayer);
            AddActorIds(ids, change.declinedActorIds, allowPlayer);
            AddActorId(ids, change.organizerActorId, allowPlayer);
            if (!hasStructuredActorIds)
            {
                AddMentionedActorIds(ids, context, allowPlayer);
                if (LooksLikePlayerAgreementContext(context))
                {
                    AddActorId(ids, "player", allowPlayer);
                }
            }
            else if (ChangeMentionsPlayer(change))
            {
                AddActorId(ids, "player", allowPlayer);
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids;
        }

        private static string[] BuildActorIdsForPlanMatch(NpcSocialPlanChange change)
        {
            List<string> ids = new List<string>();
            if (change == null)
            {
                return Array.Empty<string>();
            }

            AddActorIds(ids, change.participantActorIds, true);
            AddActorIds(ids, change.requiredActorIds, true);
            AddActorIds(ids, change.optionalActorIds, true);
            AddActorIds(ids, change.acceptedActorIds, true);
            AddActorIds(ids, change.pendingActorIds, true);
            AddActorIds(ids, change.declinedActorIds, true);
            AddActorId(ids, change.organizerActorId, true);
            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids.ToArray();
        }

        private static bool HasStructuredActorIds(NpcSocialPlanChange change)
        {
            return change != null
                && (HasAnyActorId(change.participantActorIds)
                    || HasAnyActorId(change.requiredActorIds)
                    || HasAnyActorId(change.optionalActorIds)
                    || HasAnyActorId(change.acceptedActorIds)
                    || HasAnyActorId(change.pendingActorIds)
                    || HasAnyActorId(change.declinedActorIds));
        }

        private static bool ChangeMentionsPlayer(NpcSocialPlanChange change)
        {
            return change != null
                && (IsPlayerId(change.organizerActorId)
                    || ContainsId(change.participantActorIds, "player")
                    || ContainsId(change.requiredActorIds, "player")
                    || ContainsId(change.optionalActorIds, "player")
                    || ContainsId(change.acceptedActorIds, "player")
                    || ContainsId(change.pendingActorIds, "player")
                    || ContainsId(change.declinedActorIds, "player"));
        }

        private static bool HasAnyActorId(string[] actorIds)
        {
            if (actorIds == null)
            {
                return false;
            }

            for (int i = 0; i < actorIds.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(actorIds[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private void ResolvePlanTime(
            string timingMode,
            int delayMinutes,
            int scheduledStartHour,
            int scheduledStartMinute,
            out GameDate date,
            out GameTime time)
        {
            date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            if (clock == null)
            {
                return;
            }

            switch (NpcTimingModeUtility.Parse(timingMode))
            {
                case NpcTimingMode.DelayMinutes:
                    if (delayMinutes <= 0)
                    {
                        return;
                    }

                    int totalMinutes = clock.CurrentTime.TotalMinutes + delayMinutes;
                    while (totalMinutes >= 1440)
                    {
                        totalMinutes -= 1440;
                        date = clock.GetNextDate(date);
                    }

                    time = GameTime.FromTotalMinutes(totalMinutes);
                    return;
                case NpcTimingMode.TodayAtTime:
                    if (scheduledStartHour >= 0 && scheduledStartMinute >= 0)
                    {
                        time = new GameTime(scheduledStartHour, scheduledStartMinute);
                    }

                    return;
                case NpcTimingMode.NextDayAtTime:
                    date = clock.GetNextDate(date);
                    if (scheduledStartHour >= 0 && scheduledStartMinute >= 0)
                    {
                        time = new GameTime(scheduledStartHour, scheduledStartMinute);
                    }

                    return;
                case NpcTimingMode.Immediate:
                default:
                    return;
            }
        }

        private void WritePlanMemory(SocialPlan plan, string text, string tag, int importance)
        {
            if (memorySystem == null || plan == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string[] actorIds = plan.ParticipantActorIds;
            for (int i = 0; i < actorIds.Length; i++)
            {
                string npcId = actorIds[i];
                if (string.IsNullOrWhiteSpace(npcId) || IsPlayerId(npcId) || FindNpcById(npcId) == null)
                {
                    continue;
                }

                memorySystem.AddMemory(npcId, text, tag, importance);
            }
        }

        private void HandleDayChanged(GameDate date)
        {
            List<string> oldPlanIds = new List<string>();
            foreach (KeyValuePair<string, SocialPlan> pair in plansById)
            {
                if (pair.Value == null || CompareDates(pair.Value.Date, date) < 0)
                {
                    oldPlanIds.Add(pair.Key);
                }
            }

            for (int i = 0; i < oldPlanIds.Count; i++)
            {
                plansById.Remove(oldPlanIds[i]);
            }

            completedPlanIds.Clear();
            completedActivitySignatureMinutes.Clear();
        }

        private bool IsInvalidNewPlanTime(NpcTimingMode timingMode, GameDate date, GameTime time)
        {
            if (clock == null || timingMode == NpcTimingMode.Immediate)
            {
                return false;
            }

            int dateComparison = CompareDates(date, clock.CurrentDate);
            if (dateComparison < 0)
            {
                return true;
            }

            return dateComparison == 0 && time.TotalMinutes <= clock.CurrentTime.TotalMinutes;
        }

        private bool IsRecentlyCompletedDuplicate(string activitySignature, GameDate candidateDate, GameTime candidateStartTime)
        {
            if (duplicateCompletionSuppressionMinutes <= 0
                || string.IsNullOrWhiteSpace(activitySignature)
                || clock == null
                || !candidateDate.Equals(clock.CurrentDate)
                || !completedActivitySignatureMinutes.TryGetValue(activitySignature, out int completedMinute))
            {
                return false;
            }

            int delta = candidateStartTime.TotalMinutes - completedMinute;
            if (delta < 0)
            {
                return true;
            }

            return delta <= duplicateCompletionSuppressionMinutes;
        }

        private static bool LooksLikePlayerAgreementContext(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            return ContainsAnyLoose(text, "Player dialogue", "Player said", "player", "\u73a9\u5bb6", "\u6211\u4eec", "\u4e00\u8d77");
        }

        private static string BuildPlanId(
            GameDate date,
            GameTime startTime,
            string locationId,
            string activityKind,
            string[] participantIds,
            string organizerActorId)
        {
            return
                $"{date.Key}:{startTime.TotalMinutes}:" +
                $"{NormalizeLoose(locationId)}:{NormalizeLoose(activityKind)}:" +
                $"{NormalizeLoose(organizerActorId)}:{string.Join(",", CleanActorIds(participantIds, true))}";
        }

        private static string BuildActivitySignature(
            GameDate date,
            string locationId,
            string activityKind,
            string[] participantIds)
        {
            return
                $"{date.Key}:" +
                $"{NormalizeLoose(locationId)}:{NormalizeLoose(activityKind)}:" +
                $"{string.Join(",", CleanActorIds(participantIds, true))}";
        }

        private static string[] BuildDefaultPendingIds(string[] participantIds, string[] acceptedIds, string[] declinedIds)
        {
            List<string> pending = new List<string>();
            for (int i = 0; i < participantIds.Length; i++)
            {
                string id = participantIds[i];
                if (ContainsId(acceptedIds, id) || ContainsId(declinedIds, id))
                {
                    continue;
                }

                AddActorId(pending, id, true);
            }

            pending.Sort(StringComparer.OrdinalIgnoreCase);
            return pending.ToArray();
        }

        private static string[] RemoveOptionalActors(string[] participantIds, string[] optionalActorIds)
        {
            List<string> required = new List<string>();
            for (int i = 0; i < participantIds.Length; i++)
            {
                string id = participantIds[i];
                if (!ContainsId(optionalActorIds, id))
                {
                    AddActorId(required, id, true);
                }
            }

            required.Sort(StringComparer.OrdinalIgnoreCase);
            return required.ToArray();
        }

        private static bool HasAnyActorOverlap(string[] left, string[] right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            for (int i = 0; i < left.Length; i++)
            {
                if (ContainsId(right, left[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static string[] CleanActorIds(string[] ids, bool allowPlayer)
        {
            if (ids == null || ids.Length == 0)
            {
                return Array.Empty<string>();
            }

            List<string> cleaned = new List<string>();
            AddActorIds(cleaned, ids, allowPlayer);
            cleaned.Sort(StringComparer.OrdinalIgnoreCase);
            return cleaned.ToArray();
        }

        private static void AddActorIds(List<string> ids, string[] values, bool allowPlayer)
        {
            if (values == null)
            {
                return;
            }

            for (int i = 0; i < values.Length; i++)
            {
                AddActorId(ids, values[i], allowPlayer);
            }
        }

        private static void AddActorId(List<string> ids, string id, bool allowPlayer)
        {
            id = Clean(id);
            if (string.IsNullOrWhiteSpace(id) || (!allowPlayer && IsPlayerId(id)))
            {
                return;
            }

            for (int i = 0; i < ids.Count; i++)
            {
                if (string.Equals(ids[i], id, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
            }

            ids.Add(id);
        }

        private static void AddMentionedActorIds(List<string> ids, string text, bool allowPlayer)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            if (allowPlayer && ContainsAnyLoose(text, "player", "\u73a9\u5bb6"))
            {
                AddActorId(ids, "player", true);
            }

            NpcRuntimeState[] npcs = FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcRuntimeState npc = npcs[i];
                if (npc == null || npc.Profile == null)
                {
                    continue;
                }

                if (ContainsLoose(text, npc.Profile.NpcId) || ContainsLoose(text, npc.Profile.DisplayName))
                {
                    AddActorId(ids, npc.Profile.NpcId, allowPlayer);
                }
            }
        }

        private static bool ContainsId(string[] ids, string actorId)
        {
            if (ids == null || string.IsNullOrWhiteSpace(actorId))
            {
                return false;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                if (string.Equals(ids[i], actorId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetNpcId(NpcRuntimeState npc)
        {
            return npc != null && npc.Profile != null ? npc.Profile.NpcId : string.Empty;
        }

        private static NpcRuntimeState FindNpcById(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                return null;
            }

            NpcRuntimeState[] npcs = FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcRuntimeState npc = npcs[i];
                if (npc != null && npc.Profile != null && string.Equals(npc.Profile.NpcId, npcId, StringComparison.OrdinalIgnoreCase))
                {
                    return npc;
                }
            }

            return null;
        }

        private static bool IsPlayerId(string id)
        {
            return string.Equals(id, "player", StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareDates(GameDate left, GameDate right)
        {
            return left.CompareTo(right);
        }

        private static bool ContainsAnyLoose(string text, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(text) || needles == null)
            {
                return false;
            }

            for (int i = 0; i < needles.Length; i++)
            {
                if (ContainsLoose(text, needles[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsLoose(string text, string needle)
        {
            if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(needle))
            {
                return false;
            }

            return NormalizeLoose(text).Contains(NormalizeLoose(needle));
        }

        private static string NormalizeLoose(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.ToLowerInvariant()
                    .Replace("'", string.Empty)
                    .Replace(" ", string.Empty)
                    .Replace("_", string.Empty)
                    .Replace("-", string.Empty);
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }
}
