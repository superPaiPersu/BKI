using System;
using CityStateSim.Core;
using UnityEngine;

namespace CityStateSim.Schedule
{
    [Serializable]
    public sealed class NpcDailyIntent
    {
        [SerializeField] private string label;
        [SerializeField] private GameTime earliestStart;
        [SerializeField] private GameTime latestEnd;
        [SerializeField] private string targetLocationId;
        [SerializeField] private string targetActorId;
        [SerializeField] private string desiredOutcome;
        [SerializeField] private string allowedBehaviors;
        [SerializeField] private string completionCondition;
        [SerializeField] private string activityKind;
        [SerializeField] private string[] participantActorIds;
        [SerializeField] private string[] requiredActorIds;
        [SerializeField] private string[] optionalActorIds;
        [SerializeField] private int patienceMinutes;
        [SerializeField] private int priority;
        [SerializeField] private bool canInterruptRoutine = true;
        [SerializeField] private string reason;

        public string Label => label;
        public GameTime EarliestStart => earliestStart;
        public GameTime LatestEnd => latestEnd;
        public string TargetLocationId => targetLocationId;
        public string TargetActorId => targetActorId;
        public string DesiredOutcome => desiredOutcome;
        public string AllowedBehaviors => allowedBehaviors;
        public string CompletionCondition => completionCondition;
        public string ActivityKind => activityKind;
        public string[] ParticipantActorIds => participantActorIds;
        public string[] RequiredActorIds => requiredActorIds;
        public string[] OptionalActorIds => optionalActorIds;
        public int PatienceMinutes => patienceMinutes;
        public int Priority => priority;
        public bool CanInterruptRoutine => canInterruptRoutine;
        public string Reason => reason;

        public NpcDailyIntent(
            string label,
            GameTime earliestStart,
            GameTime latestEnd,
            string targetLocationId,
            string targetActorId,
            string desiredOutcome,
            string allowedBehaviors,
            string completionCondition,
            int priority,
            bool canInterruptRoutine,
            string reason)
            : this(
                label,
                earliestStart,
                latestEnd,
                targetLocationId,
                targetActorId,
                desiredOutcome,
                allowedBehaviors,
                completionCondition,
                string.Empty,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                0,
                priority,
                canInterruptRoutine,
                reason)
        {
        }

        public NpcDailyIntent(
            string label,
            GameTime earliestStart,
            GameTime latestEnd,
            string targetLocationId,
            string targetActorId,
            string desiredOutcome,
            string allowedBehaviors,
            string completionCondition,
            string activityKind,
            string[] participantActorIds,
            string[] requiredActorIds,
            string[] optionalActorIds,
            int patienceMinutes,
            int priority,
            bool canInterruptRoutine,
            string reason)
        {
            this.label = label;
            this.earliestStart = earliestStart;
            this.latestEnd = latestEnd;
            this.targetLocationId = targetLocationId;
            this.targetActorId = targetActorId;
            this.desiredOutcome = desiredOutcome;
            this.allowedBehaviors = allowedBehaviors;
            this.completionCondition = completionCondition;
            this.activityKind = activityKind;
            this.participantActorIds = participantActorIds ?? Array.Empty<string>();
            this.requiredActorIds = requiredActorIds ?? Array.Empty<string>();
            this.optionalActorIds = optionalActorIds ?? Array.Empty<string>();
            this.patienceMinutes = Mathf.Max(0, patienceMinutes);
            this.priority = Mathf.Clamp(priority, 0, 100);
            this.canInterruptRoutine = canInterruptRoutine;
            this.reason = reason;
        }

        public bool IsActiveAt(GameTime time)
        {
            int start = earliestStart.TotalMinutes;
            int end = latestEnd.TotalMinutes;
            int current = time.TotalMinutes;
            if (start == end)
            {
                return true;
            }

            if (start < end)
            {
                return current >= start && current < end;
            }

            return current >= start || current < end;
        }

        public string ToSummaryLine()
        {
            return $"{earliestStart}-{latestEnd} {label}, activity={activityKind}, participants={JoinIds(participantActorIds)}, required={JoinIds(requiredActorIds)}, optional={JoinIds(optionalActorIds)}, patience={patienceMinutes}m, actor={targetActorId}, location={targetLocationId}, priority={priority}, outcome={desiredOutcome}, completion={completionCondition}, reason={reason}";
        }

        private static string JoinIds(string[] ids)
        {
            return ids == null || ids.Length == 0 ? "" : string.Join(",", ids);
        }
    }
}
