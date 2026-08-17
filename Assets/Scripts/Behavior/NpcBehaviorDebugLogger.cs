using System;
using CityStateSim.AI;
using CityStateSim.Tasks;
using UnityEngine;

namespace CityStateSim.Behavior
{
    [RequireComponent(typeof(NpcBehaviorController))]
    [RequireComponent(typeof(NpcBehaviorState))]
    public sealed class NpcBehaviorDebugLogger : MonoBehaviour
    {
        [SerializeField] private bool logRequests = true;
        [SerializeField] private bool logDecisions = true;
        [SerializeField] private bool logFailures = true;
        [SerializeField] private bool emitUiLogEvents = true;

        private NpcBehaviorController controller;
        private NpcBehaviorState state;
        private NpcTaskController taskController;

        public static event Action<string, string> UiLogEmitted;

        private void Awake()
        {
            controller = GetComponent<NpcBehaviorController>();
            state = GetComponent<NpcBehaviorState>();
            taskController = GetComponent<NpcTaskController>();
        }

        private void OnEnable()
        {
            controller.DecisionRequested += HandleDecisionRequested;
            controller.DecisionReceived += HandleDecisionReceived;
            controller.DecisionFailed += HandleDecisionFailed;
            state.DecisionApplied += HandleDecisionApplied;
        }

        private void OnDisable()
        {
            controller.DecisionRequested -= HandleDecisionRequested;
            controller.DecisionReceived -= HandleDecisionReceived;
            controller.DecisionFailed -= HandleDecisionFailed;
            state.DecisionApplied -= HandleDecisionApplied;
        }

        private void HandleDecisionRequested(NpcAiRequest request)
        {
            if (logRequests)
            {
                EmitInfo(
                    $"Request {request.requestId} for {request.npcName} at {request.time} intent={state.CurrentIntent} " +
                    $"task={BuildTaskSummary()} secondaryLookup={BuildSecondaryLookupSummary(request)} " +
                    $"pendingEncounters={BuildCompact(request.pendingEncounterSummary)} " +
                    $"socialPlans={BuildCompact(request.socialPlanSummary)} perception={BuildCompact(request.perceptionSummary)}");
            }
        }

        private void HandleDecisionReceived(NpcAiDecision decision)
        {
            if (logDecisions)
            {
                EmitInfo(
                    $"Decision: intent={decision.intent} eventKind={decision.eventKind} behavior={decision.behaviorMode} " +
                    $"targetLocationId={decision.targetLocationId} targetActorId={decision.targetActorId} " +
                    $"timingMode={decision.timingMode} delayMinutes={decision.delayMinutes} scheduledStart={FormatScheduledStart(decision)} " +
                    $"socialPlanChanges={BuildSocialPlanChangeSummary(decision)} " +
                    $"pendingEncounterChanges={BuildPendingEncounterChangeSummary(decision)} " +
                    $"secondaryEventQuery={BuildCompact(decision.secondaryEventQuery)} " +
                    $"confidence={decision.confidence:0.00} task={BuildTaskSummary()}");
            }
        }

        private void HandleDecisionApplied(NpcBehaviorState behaviorState, NpcAiDecision decision)
        {
            if (logDecisions && !string.IsNullOrWhiteSpace(decision.dialogue))
            {
                EmitInfo(
                    $"Dialogue: intent={decision.intent} text={decision.dialogue}",
                    $"Dialogue: {decision.dialogue}");
            }
        }

        private void HandleDecisionFailed(string error)
        {
            if (logFailures)
            {
                string message = $"Failed: intent={state.CurrentIntent} task={BuildTaskSummary()} error={error}";
                Debug.LogWarning($"[NPC AI] {message}", this);
                EmitUiLog(message);
            }
        }

        private void EmitInfo(string message, string uiMessage = null)
        {
            Debug.Log($"[NPC AI] {message}", this);
            EmitUiLog(uiMessage ?? message);
        }

        private void EmitUiLog(string message)
        {
            if (!emitUiLogEvents)
            {
                return;
            }

            UiLogEmitted?.Invoke("NPC AI", message);
        }

        private string BuildTaskSummary()
        {
            NpcTask task = taskController != null ? taskController.CurrentTask : null;
            if (task == null)
            {
                return "none";
            }

            string locationId = task.TargetLocation != null ? task.TargetLocation.LocationId : "";
            string activity = string.IsNullOrWhiteSpace(task.ActivityKey) ? "" : $"/activity={task.ActivityKind}:{task.ActivityKey}";
            return $"{task.Kind}/actor={task.TargetActorId}/location={locationId}/oneShot={task.OneShot}{activity}";
        }

        private static string BuildCompact(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "none";
            }

            text = text.Replace('\n', ' ').Replace('\r', ' ').Trim();
            return text.Length <= 160 ? text : text.Substring(0, 160) + "...";
        }

        private static string BuildSecondaryLookupSummary(NpcAiRequest request)
        {
            if (request == null || !request.secondaryEventLookupAvailable)
            {
                return "unavailable";
            }

            if (!request.secondaryEventLookupAlreadyResolved)
            {
                return "available";
            }

            return string.IsNullOrWhiteSpace(request.secondaryEventLookupQuery)
                ? "resolved"
                : "resolved:" + BuildCompact(request.secondaryEventLookupQuery);
        }

        private static string FormatScheduledStart(NpcAiDecision decision)
        {
            if (decision == null || decision.scheduledStartHour < 0 || decision.scheduledStartMinute < 0)
            {
                return "none";
            }

            return $"{decision.scheduledStartHour:00}:{decision.scheduledStartMinute:00}";
        }

        private static string BuildPendingEncounterChangeSummary(NpcAiDecision decision)
        {
            if (decision == null || decision.pendingEncounterChanges == null || decision.pendingEncounterChanges.Length == 0)
            {
                return "none";
            }

            int count = decision.pendingEncounterChanges.Length;
            NpcPendingEncounterChange first = decision.pendingEncounterChanges[0];
            if (first == null)
            {
                return $"{count} change(s)";
            }

            return $"{count} change(s), first={first.operation}:{first.targetActorId}:{first.actionKind}";
        }

        private static string BuildSocialPlanChangeSummary(NpcAiDecision decision)
        {
            if (decision == null || decision.socialPlanChanges == null || decision.socialPlanChanges.Length == 0)
            {
                return "none";
            }

            int count = decision.socialPlanChanges.Length;
            NpcSocialPlanChange first = decision.socialPlanChanges[0];
            if (first == null)
            {
                return $"{count} change(s)";
            }

            return $"{count} change(s), first={first.operation}:{first.activityKind}:{first.targetLocationId}";
        }
    }
}
