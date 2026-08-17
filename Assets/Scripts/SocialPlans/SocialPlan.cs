using System;
using System.Collections.Generic;
using CityStateSim.Core;
using CityStateSim.Locations;
using UnityEngine;

namespace CityStateSim.SocialPlans
{
    public enum SocialPlanState
    {
        Planned = 0,
        Gathering = 1,
        Active = 2,
        Completed = 3,
        Failed = 4,
        Cancelled = 5
    }

    public enum SocialPlanParticipantStatus
    {
        Proposed = 0,
        Accepted = 1,
        Declined = 2,
        Unavailable = 3,
        Arrived = 4
    }

    [Serializable]
    public sealed class SocialPlanParticipant
    {
        [SerializeField] private string actorId;
        [SerializeField] private SocialPlanParticipantStatus status;
        [SerializeField] private string note;

        public SocialPlanParticipant(string actorId, SocialPlanParticipantStatus status, string note = "")
        {
            this.actorId = Clean(actorId);
            this.status = status;
            this.note = Clean(note);
        }

        public string ActorId => actorId;
        public SocialPlanParticipantStatus Status => status;
        public string Note => note;

        public void SetStatus(SocialPlanParticipantStatus value, string reason = "")
        {
            status = value;
            if (!string.IsNullOrWhiteSpace(reason))
            {
                note = Clean(reason);
            }
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }

    [Serializable]
    public sealed class SocialPlan
    {
        [SerializeField] private string planId;
        [SerializeField] private string label;
        [SerializeField] private string activityKind;
        [SerializeField] private LocationDefinition targetLocation;
        [SerializeField] private string targetLocationId;
        [SerializeField] private GameDate date;
        [SerializeField] private GameTime startTime;
        [SerializeField] private string organizerActorId;
        [SerializeField] private string[] participantActorIds;
        [SerializeField] private string[] requiredActorIds;
        [SerializeField] private string[] optionalActorIds;
        [SerializeField] private int patienceMinutes;
        [SerializeField] private int priority;
        [SerializeField] private string reason;
        [SerializeField] private SocialPlanState state;
        [SerializeField] private string terminalReason;
        [SerializeField] private List<SocialPlanParticipant> participants = new List<SocialPlanParticipant>();

        private readonly HashSet<string> assignedActivityActorIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public SocialPlan(
            string planId,
            string label,
            string activityKind,
            LocationDefinition targetLocation,
            GameDate date,
            GameTime startTime,
            string[] participantActorIds,
            string[] requiredActorIds,
            string[] optionalActorIds,
            int patienceMinutes,
            int priority,
            string reason)
            : this(
                planId,
                label,
                activityKind,
                targetLocation,
                date,
                startTime,
                string.Empty,
                participantActorIds,
                requiredActorIds,
                optionalActorIds,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                patienceMinutes,
                priority,
                reason)
        {
        }

        public SocialPlan(
            string planId,
            string label,
            string activityKind,
            LocationDefinition targetLocation,
            GameDate date,
            GameTime startTime,
            string organizerActorId,
            string[] participantActorIds,
            string[] requiredActorIds,
            string[] optionalActorIds,
            string[] acceptedActorIds,
            string[] pendingActorIds,
            string[] declinedActorIds,
            int patienceMinutes,
            int priority,
            string reason)
        {
            this.planId = string.IsNullOrWhiteSpace(planId) ? Guid.NewGuid().ToString("N") : Clean(planId);
            this.label = string.IsNullOrWhiteSpace(label) ? "Shared activity" : Clean(label);
            this.activityKind = string.IsNullOrWhiteSpace(activityKind) ? string.Empty : Clean(activityKind);
            this.targetLocation = targetLocation;
            targetLocationId = targetLocation != null ? targetLocation.LocationId : string.Empty;
            this.date = date;
            this.startTime = startTime;
            this.organizerActorId = Clean(organizerActorId);
            this.patienceMinutes = Mathf.Clamp(patienceMinutes, 0, 240);
            this.priority = Mathf.Clamp(priority, 0, 100);
            this.reason = Clean(reason);
            state = SocialPlanState.Planned;

            ApplyParticipantSets(participantActorIds, requiredActorIds, optionalActorIds, acceptedActorIds, pendingActorIds, declinedActorIds, this.reason);
        }

        public string PlanId => planId;
        public string ActivityKey => planId;
        public string Label => label;
        public string ActivityKind => activityKind;
        public LocationDefinition TargetLocation => targetLocation;
        public string TargetLocationId => targetLocationId;
        public GameDate Date => date;
        public GameTime StartTime => startTime;
        public string OrganizerActorId => organizerActorId;
        public string[] ParticipantActorIds => participantActorIds;
        public string[] RequiredActorIds => requiredActorIds;
        public string[] OptionalActorIds => optionalActorIds;
        public int PatienceMinutes => patienceMinutes;
        public int Priority => priority;
        public string Reason => reason;
        public SocialPlanState State => state;
        public string TerminalReason => terminalReason;
        public IReadOnlyList<SocialPlanParticipant> ParticipantStates => participants;
        public bool IsClosed => state == SocialPlanState.Completed || state == SocialPlanState.Failed || state == SocialPlanState.Cancelled;

        public void ApplyUpdate(
            string label,
            string activityKind,
            LocationDefinition targetLocation,
            GameDate date,
            GameTime startTime,
            string organizerActorId,
            string[] participantActorIds,
            string[] requiredActorIds,
            string[] optionalActorIds,
            string[] acceptedActorIds,
            string[] pendingActorIds,
            string[] declinedActorIds,
            int patienceMinutes,
            int priority,
            string reason)
        {
            if (!string.IsNullOrWhiteSpace(label))
            {
                this.label = Clean(label);
            }

            if (!string.IsNullOrWhiteSpace(activityKind))
            {
                this.activityKind = Clean(activityKind);
            }

            if (targetLocation != null)
            {
                this.targetLocation = targetLocation;
                targetLocationId = targetLocation.LocationId;
            }

            this.date = date;
            this.startTime = startTime;

            if (!string.IsNullOrWhiteSpace(organizerActorId))
            {
                this.organizerActorId = Clean(organizerActorId);
            }

            if (patienceMinutes > 0)
            {
                this.patienceMinutes = Mathf.Clamp(patienceMinutes, 0, 240);
            }

            if (priority > 0)
            {
                this.priority = Mathf.Clamp(priority, 0, 100);
            }

            if (!string.IsNullOrWhiteSpace(reason))
            {
                this.reason = Clean(reason);
            }

            ApplyParticipantSets(participantActorIds, requiredActorIds, optionalActorIds, acceptedActorIds, pendingActorIds, declinedActorIds, this.reason);
            if (IsClosed)
            {
                state = SocialPlanState.Planned;
                terminalReason = string.Empty;
            }
        }

        public bool ContainsActor(string actorId)
        {
            return ContainsId(participantActorIds, actorId)
                || ContainsId(requiredActorIds, actorId)
                || ContainsId(optionalActorIds, actorId)
                || FindParticipant(actorId) != null;
        }

        public bool IsCoordinator(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(organizerActorId))
            {
                return SameId(actorId, organizerActorId);
            }

            string fallback = FirstAcceptedNpcId();
            return string.IsNullOrWhiteSpace(fallback) || SameId(actorId, fallback);
        }

        public bool IsActivityAssignedTo(string actorId)
        {
            return !string.IsNullOrWhiteSpace(actorId) && assignedActivityActorIds.Contains(actorId.Trim());
        }

        public void MarkActivityAssigned(string actorId)
        {
            if (!string.IsNullOrWhiteSpace(actorId))
            {
                assignedActivityActorIds.Add(actorId.Trim());
            }
        }

        public void MarkGathering()
        {
            if (!IsClosed)
            {
                state = SocialPlanState.Gathering;
            }
        }

        public void MarkActive()
        {
            if (!IsClosed)
            {
                state = SocialPlanState.Active;
            }
        }

        public void MarkCompleted(string reason)
        {
            state = SocialPlanState.Completed;
            terminalReason = Clean(reason);
        }

        public void MarkFailed(string reason)
        {
            state = SocialPlanState.Failed;
            terminalReason = Clean(reason);
        }

        public void MarkCancelled(string reason)
        {
            state = SocialPlanState.Cancelled;
            terminalReason = Clean(reason);
        }

        public void MarkParticipantAccepted(string actorId, string note = "")
        {
            SetParticipantStatus(actorId, SocialPlanParticipantStatus.Accepted, note);
        }

        public void MarkParticipantDeclined(string actorId, string note = "")
        {
            SetParticipantStatus(actorId, SocialPlanParticipantStatus.Declined, note);
        }

        public void MarkParticipantUnavailable(string actorId, string note = "")
        {
            SetParticipantStatus(actorId, SocialPlanParticipantStatus.Unavailable, note);
        }

        public void MarkParticipantArrived(string actorId, string note = "")
        {
            SetParticipantStatus(actorId, SocialPlanParticipantStatus.Arrived, note);
        }

        public bool IsAcceptedOrArrived(string actorId)
        {
            SocialPlanParticipant participant = FindParticipant(actorId);
            return participant != null
                && (participant.Status == SocialPlanParticipantStatus.Accepted
                    || participant.Status == SocialPlanParticipantStatus.Arrived);
        }

        public bool HasRequiredUnavailable()
        {
            string[] required = RequiredActorIds;
            for (int i = 0; i < required.Length; i++)
            {
                SocialPlanParticipant participant = FindParticipant(required[i]);
                if (participant != null
                    && (participant.Status == SocialPlanParticipantStatus.Declined
                        || participant.Status == SocialPlanParticipantStatus.Unavailable))
                {
                    return true;
                }
            }

            return false;
        }

        public bool TryGetNextPendingRequiredActor(out string actorId)
        {
            actorId = FirstPendingActor(requiredActorIds);
            if (!string.IsNullOrWhiteSpace(actorId))
            {
                return true;
            }

            actorId = FirstPendingActor(participantActorIds);
            return !string.IsNullOrWhiteSpace(actorId);
        }

        public string[] BuildAcceptedNpcParticipantIds()
        {
            List<string> ids = new List<string>();
            for (int i = 0; i < participants.Count; i++)
            {
                SocialPlanParticipant participant = participants[i];
                if (participant == null
                    || IsPlayerId(participant.ActorId)
                    || (participant.Status != SocialPlanParticipantStatus.Accepted
                        && participant.Status != SocialPlanParticipantStatus.Arrived))
                {
                    continue;
                }

                AddActorId(ids, participant.ActorId, false);
            }

            if (ids.Count == 0)
            {
                AddActorIds(ids, participantActorIds, false);
            }

            ids.Sort(StringComparer.OrdinalIgnoreCase);
            return ids.ToArray();
        }

        public bool IsDue(GameDate currentDate, GameTime currentTime)
        {
            int dateComparison = CompareDates(currentDate, date);
            if (dateComparison < 0)
            {
                return false;
            }

            if (dateComparison > 0)
            {
                return true;
            }

            return currentTime.TotalMinutes >= startTime.TotalMinutes;
        }

        public bool IsPastPatience(GameDate currentDate, GameTime currentTime)
        {
            if (patienceMinutes <= 0)
            {
                return IsDue(currentDate, currentTime);
            }

            int dateComparison = CompareDates(currentDate, date);
            if (dateComparison > 0)
            {
                return true;
            }

            if (dateComparison < 0)
            {
                return false;
            }

            return currentTime.TotalMinutes - startTime.TotalMinutes >= patienceMinutes;
        }

        public string ToSummaryLine()
        {
            return $"{date} {startTime} {label}, state={state}, location={targetLocationId}, activity={activityKind}, organizer={organizerActorId}, participants={JoinIds(participantActorIds)}, required={JoinIds(requiredActorIds)}, optional={JoinIds(optionalActorIds)}, statuses={BuildParticipantStatusSummary()}, patience={patienceMinutes}m, priority={priority}, reason={reason}, terminalReason={terminalReason}";
        }

        private void ApplyParticipantSets(
            string[] participantActorIds,
            string[] requiredActorIds,
            string[] optionalActorIds,
            string[] acceptedActorIds,
            string[] pendingActorIds,
            string[] declinedActorIds,
            string note)
        {
            List<string> allParticipants = new List<string>();
            AddActorIds(allParticipants, participantActorIds, true);
            AddActorIds(allParticipants, requiredActorIds, true);
            AddActorIds(allParticipants, optionalActorIds, true);
            AddActorIds(allParticipants, acceptedActorIds, true);
            AddActorIds(allParticipants, pendingActorIds, true);
            AddActorIds(allParticipants, declinedActorIds, true);
            AddActorId(allParticipants, organizerActorId, true);

            this.participantActorIds = CleanActorIds(allParticipants.ToArray(), true);
            this.requiredActorIds = CleanActorIds(requiredActorIds, true);
            this.optionalActorIds = CleanActorIds(optionalActorIds, true);
            if (this.requiredActorIds.Length == 0)
            {
                this.requiredActorIds = CleanActorIds(this.participantActorIds, true);
            }

            for (int i = 0; i < this.participantActorIds.Length; i++)
            {
                EnsureParticipant(this.participantActorIds[i], SocialPlanParticipantStatus.Proposed, note);
            }

            SetParticipantStatuses(acceptedActorIds, SocialPlanParticipantStatus.Accepted, note);
            SetParticipantStatuses(pendingActorIds, SocialPlanParticipantStatus.Proposed, note);
            SetParticipantStatuses(declinedActorIds, SocialPlanParticipantStatus.Declined, note);
        }

        private void SetParticipantStatuses(string[] actorIds, SocialPlanParticipantStatus status, string note)
        {
            if (actorIds == null)
            {
                return;
            }

            for (int i = 0; i < actorIds.Length; i++)
            {
                SetParticipantStatus(actorIds[i], status, note);
            }
        }

        private void SetParticipantStatus(string actorId, SocialPlanParticipantStatus status, string note)
        {
            actorId = Clean(actorId);
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return;
            }

            AddActorIdToArray(ref participantActorIds, actorId, true);
            SocialPlanParticipant participant = EnsureParticipant(actorId, status, note);
            participant.SetStatus(status, note);
        }

        private SocialPlanParticipant EnsureParticipant(string actorId, SocialPlanParticipantStatus defaultStatus, string note)
        {
            actorId = Clean(actorId);
            SocialPlanParticipant existing = FindParticipant(actorId);
            if (existing != null)
            {
                return existing;
            }

            SocialPlanParticipant participant = new SocialPlanParticipant(actorId, defaultStatus, note);
            participants.Add(participant);
            return participant;
        }

        private SocialPlanParticipant FindParticipant(string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return null;
            }

            string cleaned = actorId.Trim();
            for (int i = 0; i < participants.Count; i++)
            {
                SocialPlanParticipant participant = participants[i];
                if (participant != null && SameId(participant.ActorId, cleaned))
                {
                    return participant;
                }
            }

            return null;
        }

        private string FirstPendingActor(string[] actorIds)
        {
            if (actorIds == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < actorIds.Length; i++)
            {
                string actorId = Clean(actorIds[i]);
                if (string.IsNullOrWhiteSpace(actorId))
                {
                    continue;
                }

                SocialPlanParticipant participant = FindParticipant(actorId);
                if (participant == null || participant.Status == SocialPlanParticipantStatus.Proposed)
                {
                    return actorId;
                }
            }

            return string.Empty;
        }

        private string FirstAcceptedNpcId()
        {
            for (int i = 0; i < participants.Count; i++)
            {
                SocialPlanParticipant participant = participants[i];
                if (participant != null
                    && !IsPlayerId(participant.ActorId)
                    && (participant.Status == SocialPlanParticipantStatus.Accepted
                        || participant.Status == SocialPlanParticipantStatus.Arrived))
                {
                    return participant.ActorId;
                }
            }

            return string.Empty;
        }

        private string BuildParticipantStatusSummary()
        {
            if (participants == null || participants.Count == 0)
            {
                return "";
            }

            List<string> lines = new List<string>();
            for (int i = 0; i < participants.Count; i++)
            {
                SocialPlanParticipant participant = participants[i];
                if (participant == null || string.IsNullOrWhiteSpace(participant.ActorId))
                {
                    continue;
                }

                string text = $"{participant.ActorId}:{participant.Status}";
                if (!string.IsNullOrWhiteSpace(participant.Note))
                {
                    text += $"({participant.Note})";
                }

                lines.Add(text);
            }

            return string.Join("|", lines);
        }

        private static void AddActorIdToArray(ref string[] ids, string actorId, bool allowPlayer)
        {
            List<string> list = new List<string>();
            AddActorIds(list, ids, allowPlayer);
            AddActorId(list, actorId, allowPlayer);
            ids = CleanActorIds(list.ToArray(), allowPlayer);
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
                if (SameId(ids[i], id))
                {
                    return;
                }
            }

            ids.Add(id);
        }

        private static bool ContainsId(string[] ids, string actorId)
        {
            if (ids == null || string.IsNullOrWhiteSpace(actorId))
            {
                return false;
            }

            for (int i = 0; i < ids.Length; i++)
            {
                if (SameId(ids[i], actorId))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool SameId(string left, string right)
        {
            return !string.IsNullOrWhiteSpace(left)
                && !string.IsNullOrWhiteSpace(right)
                && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsPlayerId(string id)
        {
            return string.Equals(id, "player", StringComparison.OrdinalIgnoreCase);
        }

        private static int CompareDates(GameDate left, GameDate right)
        {
            return left.CompareTo(right);
        }

        private static string JoinIds(string[] ids)
        {
            return ids == null || ids.Length == 0 ? "" : string.Join(",", ids);
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }
}
