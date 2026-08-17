using CityStateSim.NPC;
using System;
using System.Text;
using UnityEngine;

namespace CityStateSim.Dialogue
{
    public sealed class DialogueProposal
    {
        public DialogueProposal(
            DialogueProposalKind kind,
            NpcRuntimeState initiator,
            NpcRuntimeState target,
            string topic,
            string reason,
            int priority,
            float audibleRadius = 4f,
            bool allowMovementInterruption = true,
            bool requiresPlayerWitness = true,
            string activityKey = "",
            bool allowLateJoin = false,
            DialogueContextInfo context = null)
        {
            Kind = kind;
            Initiator = initiator;
            Target = target;
            Topic = topic;
            Reason = reason;
            Priority = Mathf.Clamp(priority, 0, 100);
            AudibleRadius = Mathf.Max(0f, audibleRadius);
            AllowMovementInterruption = allowMovementInterruption;
            RequiresPlayerWitness = requiresPlayerWitness;
            ActivityKey = activityKey;
            AllowLateJoin = allowLateJoin;
            Context = context;
            CreatedRealtime = Time.realtimeSinceStartup;
        }

        public DialogueProposal(
            NpcRuntimeState[] participants,
            string topic,
            string reason,
            int priority,
            bool requiresPlayerWitness = true,
            string activityKey = "",
            bool allowLateJoin = false,
            DialogueContextInfo context = null)
            : this(
                DialogueProposalKind.Group,
                participants != null && participants.Length > 0 ? participants[0] : null,
                null,
                topic,
                reason,
                priority,
                0f,
                true,
                requiresPlayerWitness,
                activityKey,
                allowLateJoin,
                context)
        {
            Participants = participants;
        }

        public DialogueProposalKind Kind { get; }
        public NpcRuntimeState Initiator { get; }
        public NpcRuntimeState Target { get; }
        public NpcRuntimeState[] Participants { get; }
        public string Topic { get; }
        public string Reason { get; }
        public int Priority { get; }
        public float AudibleRadius { get; }
        public bool AllowMovementInterruption { get; }
        public bool RequiresPlayerWitness { get; }
        public string ActivityKey { get; }
        public bool AllowLateJoin { get; }
        public DialogueContextInfo Context { get; }
        public float CreatedRealtime { get; }
    }

    [Serializable]
    public sealed class DialogueContextInfo
    {
        [SerializeField] private string contextKind;
        [SerializeField] private string sourceActorId;
        [SerializeField] private string subjectActorId;
        [SerializeField] private string subjectLocationId;
        [SerializeField] private string sourceText;
        [SerializeField] private string taskReason;
        [SerializeField] private string activityKey;

        public DialogueContextInfo(
            string contextKind = "",
            string sourceActorId = "",
            string subjectActorId = "",
            string subjectLocationId = "",
            string sourceText = "",
            string taskReason = "",
            string activityKey = "")
        {
            this.contextKind = Clean(contextKind);
            this.sourceActorId = Clean(sourceActorId);
            this.subjectActorId = Clean(subjectActorId);
            this.subjectLocationId = Clean(subjectLocationId);
            this.sourceText = Clean(sourceText);
            this.taskReason = Clean(taskReason);
            this.activityKey = Clean(activityKey);
        }

        public string ContextKind => contextKind;
        public string SourceActorId => sourceActorId;
        public string SubjectActorId => subjectActorId;
        public string SubjectLocationId => subjectLocationId;
        public string SourceText => sourceText;
        public string TaskReason => taskReason;
        public string ActivityKey => activityKey;

        public bool HasAnyData =>
            !string.IsNullOrWhiteSpace(contextKind)
            || !string.IsNullOrWhiteSpace(sourceActorId)
            || !string.IsNullOrWhiteSpace(subjectActorId)
            || !string.IsNullOrWhiteSpace(subjectLocationId)
            || !string.IsNullOrWhiteSpace(sourceText)
            || !string.IsNullOrWhiteSpace(taskReason)
            || !string.IsNullOrWhiteSpace(activityKey);

        public string BuildPromptSummary(NpcRuntimeState speaker, NpcRuntimeState listener, DialogueProposal proposal)
        {
            string speakerId = GetActorId(speaker);
            string listenerId = listener != null ? GetActorId(listener) : BuildAudienceId(proposal);
            string effectiveSourceActorId = string.IsNullOrWhiteSpace(sourceActorId) ? speakerId : sourceActorId;
            string effectiveSubjectActorId = string.IsNullOrWhiteSpace(subjectActorId)
                ? listener != null ? listenerId : string.Empty
                : subjectActorId;
            string effectiveTaskReason = string.IsNullOrWhiteSpace(taskReason) && proposal != null
                ? proposal.Reason
                : taskReason;

            StringBuilder builder = new StringBuilder();
            builder.Append("Dialogue roles: ");
            builder.Append("currentSpeakerId=");
            builder.Append(EmptyToNone(speakerId));
            builder.Append(", currentListenerId=");
            builder.Append(EmptyToNone(listenerId));
            builder.Append(", informationSourceActorId=");
            builder.Append(EmptyToNone(effectiveSourceActorId));
            builder.Append(", subjectActorId=");
            builder.Append(EmptyToNone(effectiveSubjectActorId));
            builder.Append(", subjectLocationId=");
            builder.Append(EmptyToNone(subjectLocationId));
            builder.Append(", contextKind=");
            builder.Append(EmptyToNone(contextKind));
            builder.Append(", activityKey=");
            builder.Append(EmptyToNone(activityKey));
            builder.Append(". ");

            if (!string.IsNullOrWhiteSpace(effectiveTaskReason))
            {
                builder.Append("Task reason: ");
                builder.Append(effectiveTaskReason);
                builder.Append(". ");
            }

            if (!string.IsNullOrWhiteSpace(sourceText))
            {
                builder.Append("Source text or transcript: ");
                builder.Append(sourceText);
                builder.Append(". ");
            }

            builder.Append("Role semantics: the current line is addressed to currentListenerId/currentAudience. ");
            builder.Append("informationSourceActorId is the reporter or origin of the information and may differ from the listener. ");
            builder.Append("subjectActorId/subjectLocationId is what the conversation is about. ");
            if (!string.IsNullOrWhiteSpace(effectiveSourceActorId)
                && !string.IsNullOrWhiteSpace(listenerId)
                && !string.Equals(effectiveSourceActorId, listenerId, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append("When referring to the information source, name that source instead of using second person. ");
            }

            if (!string.IsNullOrWhiteSpace(effectiveSubjectActorId)
                && !string.IsNullOrWhiteSpace(listenerId)
                && string.Equals(effectiveSubjectActorId, listenerId, StringComparison.OrdinalIgnoreCase))
            {
                builder.Append("The listener is also the subject, so checks or questions about the subject can be addressed directly to the listener. ");
            }

            return builder.ToString();
        }

        private static string GetActorId(NpcRuntimeState npc)
        {
            return npc != null && npc.Profile != null ? npc.Profile.NpcId : string.Empty;
        }

        private static string BuildAudienceId(DialogueProposal proposal)
        {
            if (proposal == null)
            {
                return string.Empty;
            }

            return proposal.Kind == DialogueProposalKind.Group
                ? "group"
                : proposal.Kind == DialogueProposalKind.Broadcast
                    ? "nearby_audience"
                    : string.Empty;
        }

        private static string EmptyToNone(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "(none)" : value.Trim();
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }
}
