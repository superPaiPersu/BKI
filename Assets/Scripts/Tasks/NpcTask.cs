using System;
using System.Text;
using CityStateSim.Locations;
using UnityEngine;

namespace CityStateSim.Tasks
{
    [Serializable]
    public sealed class NpcTask
    {
        [SerializeField] private string label;
        [SerializeField] private NpcTaskKind kind;
        [SerializeField] private LocationDefinition targetLocation;
        [SerializeField] private string targetActorId;
        [SerializeField] private int priority;
        [SerializeField] private bool interruptible = true;
        [SerializeField] private bool oneShot;
        [SerializeField] private string reason;
        [SerializeField] private float expiresAtRealtime = -1f;
        [SerializeField] private string sourceIntent;
        [SerializeField] private string sourceEventKind;
        [SerializeField] private string dialogue;
        [SerializeField] private string activityKind;
        [SerializeField] private string activityKey;
        [SerializeField] private string[] participantActorIds;
        [SerializeField] private string[] requiredActorIds;
        [SerializeField] private string[] optionalActorIds;
        [SerializeField] private int patienceMinutes;
        [SerializeField] private string plannedTargetLocationId;
        [SerializeField] private string dialogueContextKind;
        [SerializeField] private string dialogueSourceActorId;
        [SerializeField] private string dialogueSubjectActorId;
        [SerializeField] private string dialogueSubjectLocationId;
        [SerializeField] private string dialogueSourceText;
        [SerializeField] private string rollingPlanId;
        [SerializeField] private string originalGoal;
        [SerializeField] private string currentGoal;
        [SerializeField] private string completedGoalResultsBeforeTask;

        public string Label => label;
        public NpcTaskKind Kind => kind;
        public LocationDefinition TargetLocation => targetLocation;
        public string TargetActorId => targetActorId;
        public int Priority => priority;
        public bool Interruptible => interruptible;
        public bool OneShot => oneShot;
        public string Reason => reason;
        public bool HasRealtimeExpiry => expiresAtRealtime > 0f;
        public string SourceIntent => sourceIntent;
        public string SourceEventKind => sourceEventKind;
        public string Dialogue => dialogue;
        public string ActivityKind => activityKind;
        public string ActivityKey => activityKey;
        public string[] ParticipantActorIds => participantActorIds;
        public string[] RequiredActorIds => requiredActorIds;
        public string[] OptionalActorIds => optionalActorIds;
        public int PatienceMinutes => patienceMinutes;
        public string PlannedTargetLocationId => plannedTargetLocationId;
        public string DialogueContextKind => dialogueContextKind;
        public string DialogueSourceActorId => dialogueSourceActorId;
        public string DialogueSubjectActorId => dialogueSubjectActorId;
        public string DialogueSubjectLocationId => dialogueSubjectLocationId;
        public string DialogueSourceText => dialogueSourceText;
        public string RollingPlanId => rollingPlanId;
        public string OriginalGoal => originalGoal;
        public string CurrentGoal => currentGoal;
        public string CompletedGoalResultsBeforeTask => completedGoalResultsBeforeTask;

        public NpcTask(
            string label,
            NpcTaskKind kind,
            LocationDefinition targetLocation,
            string targetActorId,
            int priority,
            bool interruptible,
            bool oneShot,
            string reason,
            float durationSeconds = -1f,
            string sourceIntent = "",
            string sourceEventKind = "",
            string dialogue = "",
            string activityKind = "",
            string activityKey = "",
            string[] participantActorIds = null,
            string[] requiredActorIds = null,
            string[] optionalActorIds = null,
            int patienceMinutes = 0,
            string plannedTargetLocationId = "",
            string dialogueContextKind = "",
            string dialogueSourceActorId = "",
            string dialogueSubjectActorId = "",
            string dialogueSubjectLocationId = "",
            string dialogueSourceText = "",
            string rollingPlanId = "",
            string originalGoal = "",
            string currentGoal = "",
            string completedGoalResultsBeforeTask = "")
        {
            this.label = label;
            this.kind = kind;
            this.targetLocation = targetLocation;
            this.targetActorId = targetActorId;
            this.priority = Mathf.Clamp(priority, 0, 100);
            this.interruptible = interruptible;
            this.oneShot = oneShot;
            this.reason = reason;
            expiresAtRealtime = durationSeconds > 0f ? Time.realtimeSinceStartup + durationSeconds : -1f;
            this.sourceIntent = sourceIntent;
            this.sourceEventKind = sourceEventKind;
            this.dialogue = dialogue;
            this.activityKind = activityKind;
            this.activityKey = activityKey;
            this.participantActorIds = participantActorIds ?? Array.Empty<string>();
            this.requiredActorIds = requiredActorIds ?? Array.Empty<string>();
            this.optionalActorIds = optionalActorIds ?? Array.Empty<string>();
            this.patienceMinutes = Mathf.Max(0, patienceMinutes);
            this.plannedTargetLocationId = plannedTargetLocationId ?? string.Empty;
            this.dialogueContextKind = Clean(dialogueContextKind);
            this.dialogueSourceActorId = Clean(dialogueSourceActorId);
            this.dialogueSubjectActorId = Clean(dialogueSubjectActorId);
            this.dialogueSubjectLocationId = Clean(dialogueSubjectLocationId);
            this.dialogueSourceText = Clean(dialogueSourceText);
            this.rollingPlanId = Clean(rollingPlanId);
            this.originalGoal = Clean(originalGoal);
            this.currentGoal = Clean(currentGoal);
            this.completedGoalResultsBeforeTask = Clean(completedGoalResultsBeforeTask);
        }

        public bool IsExpired()
        {
            return HasRealtimeExpiry && Time.realtimeSinceStartup >= expiresAtRealtime;
        }

        public static NpcTask FollowSchedule(LocationDefinition location, string actionName)
        {
            return new NpcTask(
                string.IsNullOrWhiteSpace(actionName) ? "Follow schedule" : actionName,
                NpcTaskKind.FollowSchedule,
                location,
                string.Empty,
                0,
                true,
                false,
                "base schedule");
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }

    public static class NpcTaskConstraintValidator
    {
        public static bool ValidateAtExecutionPoint(NpcTask task, out string reason)
        {
            reason = string.Empty;
            if (task == null)
            {
                reason = "task constraint violation: task is missing.";
                return false;
            }

            switch (task.Kind)
            {
                case NpcTaskKind.AttendActivity:
                    return ValidateLocationTemplateTask(task, NpcTaskKind.AttendActivity.ToString(), true, out reason);
                case NpcTaskKind.WorkAtLocation:
                    return ValidateLocationTemplateTask(task, NpcTaskKind.WorkAtLocation.ToString(), false, out reason);
                case NpcTaskKind.RestAtLocation:
                    return ValidateLocationTemplateTask(task, NpcTaskKind.RestAtLocation.ToString(), false, out reason);
                default:
                    return true;
            }
        }

        public static string BuildConstraintSummary()
        {
            return
                "Runtime task constraints:\n" +
                "- AttendActivity requires targetLocationId and a non-empty activityKind. That activityKind must exactly match activityKind or templateId from the selected location's available task templates with primaryAction=AttendActivity.\n" +
                "- WorkAtLocation and RestAtLocation require the selected location to expose an available task template with the matching primaryAction. If activityKind is provided, it must match that template's activityKind or templateId.\n" +
                "- Location capabilityTags only unlock templates; location id, display name, and description are not runtime proof that an activity is supported.\n" +
                "- MoveToLocation is a pure place task. If targetActorId is present, the runtime treats it as FindActor because the executable goal is actor-targeted.\n" +
                "- FindActor with targetActorId follows the actor's live position; targetLocationId is only background context for why the NPC expected that actor there.\n" +
                "- FollowActor can be open-ended when targetActorId=player. For NPC targets, it needs a destination or an active leader movement goal; otherwise the runtime fails it and asks for a corrected decision.\n" +
                "- When a task violates its constraints, the runtime fails it and sends the exact failure reason back as the next observed event. Treat that feedback as real and choose a corrected legal action.\n";
        }

        private static bool ValidateLocationTemplateTask(
            NpcTask task,
            string primaryAction,
            bool requireActivityKind,
            out string reason)
        {
            reason = string.Empty;
            if (task.TargetLocation == null)
            {
                reason = $"task constraint violation: {primaryAction} has no target location.";
                return false;
            }

            string activityKind = string.IsNullOrWhiteSpace(task.ActivityKind) ? string.Empty : task.ActivityKind.Trim();
            string allowedTemplates = task.TargetLocation.BuildAvailableTaskTemplateList(primaryAction);
            if (requireActivityKind && string.IsNullOrWhiteSpace(activityKind))
            {
                reason =
                    $"task constraint violation: {primaryAction} requires activityKind selected from the target location's task templates. " +
                    DescribeLocation(task.TargetLocation) +
                    $". Available {primaryAction} templates: {allowedTemplates}.";
                return false;
            }

            if (!task.TargetLocation.TryGetAvailableTaskTemplate(activityKind, primaryAction, out _))
            {
                string chosen = string.IsNullOrWhiteSpace(activityKind) ? "(empty)" : activityKind;
                reason =
                    $"task constraint violation: {primaryAction} activityKind/templateId '{chosen}' is not available at this location. " +
                    DescribeLocation(task.TargetLocation) +
                    $". Available {primaryAction} templates: {allowedTemplates}.";
                return false;
            }

            return true;
        }

        private static string DescribeLocation(LocationDefinition location)
        {
            if (location == null)
            {
                return "(no location)";
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("location id=");
            builder.Append(location.LocationId);
            builder.Append(", name=");
            builder.Append(location.DisplayName);
            builder.Append(", type=");
            builder.Append(location.Type);
            if (!string.IsNullOrWhiteSpace(location.Description))
            {
                builder.Append(", description=");
                builder.Append(location.Description);
            }

            return builder.ToString();
        }
    }
}
