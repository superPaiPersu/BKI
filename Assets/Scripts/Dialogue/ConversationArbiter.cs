using System.Collections.Generic;
using System;
using System.Collections;
using CityStateSim.AI;
using CityStateSim.Behavior;
using CityStateSim.Core;
using CityStateSim.Memory;
using CityStateSim.Movement;
using CityStateSim.NPC;
using CityStateSim.Relationships;
using CityStateSim.SecondaryEvents;
using CityStateSim.SocialPlans;
using CityStateSim.Tasks;
using UnityEngine;
using UnityEngine.Serialization;

namespace CityStateSim.Dialogue
{
    public sealed class ConversationArbiter : MonoBehaviour
    {
        public enum ConversationStartFailureReason
        {
            None = 0,
            InvalidProposal = 1,
            InitiatorAlreadyInConversation = 2,
            TargetAlreadyInConversation = 3,
            BrainBusy = 4,
            PlayerDialogueActive = 5,
            PendingPlayerReply = 6,
            TargetMissing = 7,
            TooFar = 8
        }

        public enum SharedEventConversationResult
        {
            Rejected = 0,
            WaitingForMoreParticipants = 1,
            Started = 2
        }

        [Header("References")]
        [SerializeField] private NpcBrainProviderBehaviour brainProvider;
        [SerializeField] private GameClock clock;
        [SerializeField] private RelationshipSystem relationshipSystem;
        [SerializeField] private MemorySystem memorySystem;
        [SerializeField] private SecondaryEventSystem secondaryEventSystem;
        [SerializeField] private MessageDisplayer messageDisplayer;
        [SerializeField] private DialogueHistorySystem dialogueHistorySystem;
        [SerializeField] private DialogueController dialogueController;
        [SerializeField] private SocialPlanSystem socialPlanSystem;
        [SerializeField] private Transform player;

        [Header("Policy")]
        [SerializeField, Min(1)] private int maxSafetyTurns = 12;
        [SerializeField, Min(0f)] private float secondsBetweenTurns = 0.75f;
        [SerializeField, Min(1)] private int maxSecondaryEventLookupResults = 8;
        [SerializeField, Min(0f)] private float oneOnOneStartDistance = 1.8f;
        [SerializeField, Min(0.1f)] private float groupConversationStartDistance = 2.6f;
        [SerializeField, Min(0f)] private float sharedEventGatherSeconds = 3f;
        [SerializeField, Min(0f)] private float broadcastHearRadius = 5f;
        [SerializeField, FormerlySerializedAs("requirePlayerWitnessForNpcFreeTalk")]
        private bool showNpcConversationBubblesOnlyWhenPlayerWitnessing = true;
        [SerializeField, Min(0f)] private float playerWitnessDistance = 8f;
        [SerializeField, Range(0, 100)] private int broadcastReplyPriorityThreshold = 40;
        [SerializeField] private bool writeMemories = true;
        [SerializeField] private bool logDebug;

        private readonly Dictionary<NpcRuntimeState, ConversationSession> sessionsByNpc = new Dictionary<NpcRuntimeState, ConversationSession>();
        private readonly List<ConversationSession> activeSessions = new List<ConversationSession>();
        private readonly List<DialogueProposal> pendingProposals = new List<DialogueProposal>();
        private readonly HashSet<ConversationSession> witnessedOneOnOneSessions = new HashSet<ConversationSession>();
        private readonly Dictionary<string, PendingGroupEvent> pendingGroupEventsByKey = new Dictionary<string, PendingGroupEvent>();
        private readonly Dictionary<NpcRuntimeState, PendingGroupEvent> pendingGroupEventsByNpc = new Dictionary<NpcRuntimeState, PendingGroupEvent>();
        private readonly Dictionary<string, ConversationSession> sessionsByActivityKey = new Dictionary<string, ConversationSession>(StringComparer.OrdinalIgnoreCase);

        private const string SharedEventPauseReason = "shared_event_conversation";
        public float OneOnOneStartDistance => oneOnOneStartDistance;
        public float GroupConversationStartDistance => groupConversationStartDistance;

        public static ConversationArbiter GetOrCreate()
        {
            ConversationArbiter existing = FindFirstObjectByType<ConversationArbiter>();
            if (existing != null)
            {
                return existing;
            }

            GameObject instance = new GameObject("ConversationArbiter");
            return instance.AddComponent<ConversationArbiter>();
        }

        public bool AreCloseEnoughForOneOnOne(NpcRuntimeState initiator, NpcRuntimeState target)
        {
            return IsCloseEnough(initiator, target);
        }

        private void Awake()
        {
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

            if (secondaryEventSystem == null)
            {
                secondaryEventSystem = FindFirstObjectByType<SecondaryEventSystem>();
            }

            if (messageDisplayer == null)
            {
                messageDisplayer = FindFirstObjectByType<MessageDisplayer>();
            }

            if (dialogueHistorySystem == null)
            {
                dialogueHistorySystem = FindFirstObjectByType<DialogueHistorySystem>();
            }

            if (dialogueController == null)
            {
                dialogueController = FindFirstObjectByType<DialogueController>();
            }

            if (socialPlanSystem == null)
            {
                socialPlanSystem = FindFirstObjectByType<SocialPlanSystem>();
            }

            if (player == null)
            {
                PlayerMovementController playerMovement = FindFirstObjectByType<PlayerMovementController>();
                player = playerMovement != null ? playerMovement.transform : null;
            }
        }

        private void Update()
        {
            EndUnwitnessedSessions();

            if (pendingProposals.Count == 0)
            {
                return;
            }

            DialogueProposal selected = SelectBestProposal();
            pendingProposals.Clear();
            TryStartProposal(selected);
        }

        public bool IsNpcInConversation(NpcRuntimeState npc)
        {
            return npc != null && (sessionsByNpc.ContainsKey(npc) || pendingGroupEventsByNpc.ContainsKey(npc));
        }

        public bool IsNpcInActiveConversation(NpcRuntimeState npc)
        {
            return npc != null && sessionsByNpc.ContainsKey(npc);
        }

        public bool IsNpcWaitingForSharedEvent(NpcRuntimeState npc)
        {
            return npc != null && pendingGroupEventsByNpc.ContainsKey(npc);
        }

        public bool TryStartGroupNow(
            NpcRuntimeState[] participants,
            string topic,
            string reason,
            int priority,
            bool requiresPlayerWitness,
            string activityKey,
            bool allowLateJoin,
            out ConversationStartFailureReason failureReason)
        {
            return TryStartProposal(new DialogueProposal(
                participants,
                topic,
                reason,
                priority,
                requiresPlayerWitness,
                activityKey,
                allowLateJoin),
                out failureReason);
        }

        public bool TryJoinActivityConversation(
            NpcRuntimeState participant,
            string activityKey,
            out ConversationStartFailureReason failureReason)
        {
            failureReason = ConversationStartFailureReason.InvalidProposal;
            if (participant == null || string.IsNullOrWhiteSpace(activityKey))
            {
                return false;
            }

            if (sessionsByNpc.ContainsKey(participant) || pendingGroupEventsByNpc.ContainsKey(participant))
            {
                failureReason = ConversationStartFailureReason.InitiatorAlreadyInConversation;
                return false;
            }

            if (!sessionsByActivityKey.TryGetValue(activityKey, out ConversationSession session)
                || session == null
                || !session.IsActive)
            {
                failureReason = ConversationStartFailureReason.TargetMissing;
                return false;
            }

            if (IsBrainBusy(participant) || IsInPlayerDialogue(participant) || HasPendingPlayerReply(participant))
            {
                failureReason = IsBrainBusy(participant)
                    ? ConversationStartFailureReason.BrainBusy
                    : IsInPlayerDialogue(participant)
                        ? ConversationStartFailureReason.PlayerDialogueActive
                        : ConversationStartFailureReason.PendingPlayerReply;
                return false;
            }

            if (!HasNearbySessionParticipant(participant, session))
            {
                failureReason = ConversationStartFailureReason.TooFar;
                return false;
            }

            if (!session.TryAddGroupParticipant(participant))
            {
                failureReason = ConversationStartFailureReason.TargetAlreadyInConversation;
                return false;
            }

            sessionsByNpc[participant] = session;
            failureReason = ConversationStartFailureReason.None;
            if (logDebug)
            {
                Debug.Log($"[Conversation] Late-joined {GetName(participant)} into activity conversation key={activityKey}.", this);
            }

            return true;
        }

        public bool TryJoinCompatibleActivityConversation(
            NpcRuntimeState participant,
            string[] candidateActorIds,
            out ConversationStartFailureReason failureReason)
        {
            failureReason = ConversationStartFailureReason.InvalidProposal;
            if (participant == null || candidateActorIds == null || candidateActorIds.Length == 0)
            {
                return false;
            }

            List<ConversationSession> sessions = new List<ConversationSession>(sessionsByActivityKey.Values);
            for (int i = 0; i < sessions.Count; i++)
            {
                ConversationSession session = sessions[i];
                if (session == null
                    || !session.IsActive
                    || !session.Proposal.AllowLateJoin
                    || !session.HasAnyParticipantId(candidateActorIds))
                {
                    continue;
                }

                if (TryJoinActivityConversation(participant, session.Proposal.ActivityKey, out failureReason))
                {
                    return true;
                }
            }

            return false;
        }

        public SharedEventConversationResult TryJoinSharedEventConversation(
            NpcRuntimeState participant,
            NpcRuntimeState target,
            string topic,
            string reason,
            int priority,
            out ConversationStartFailureReason failureReason)
        {
            return TryJoinSharedEventConversation(
                participant,
                target,
                topic,
                reason,
                priority,
                null,
                out failureReason);
        }

        public SharedEventConversationResult TryJoinSharedEventConversation(
            NpcRuntimeState participant,
            NpcRuntimeState target,
            string topic,
            string reason,
            int priority,
            DialogueContextInfo context,
            out ConversationStartFailureReason failureReason)
        {
            failureReason = ConversationStartFailureReason.InvalidProposal;
            if (participant == null || target == null || participant == target)
            {
                return SharedEventConversationResult.Rejected;
            }

            if (pendingGroupEventsByNpc.TryGetValue(participant, out PendingGroupEvent existingForParticipant))
            {
                failureReason = ConversationStartFailureReason.None;
                return existingForParticipant.Target == target
                    ? SharedEventConversationResult.WaitingForMoreParticipants
                    : SharedEventConversationResult.Rejected;
            }

            if (sessionsByNpc.ContainsKey(participant))
            {
                failureReason = ConversationStartFailureReason.InitiatorAlreadyInConversation;
                return SharedEventConversationResult.Rejected;
            }

            string key = BuildSharedEventKey(target);
            if (!pendingGroupEventsByKey.TryGetValue(key, out PendingGroupEvent pendingEvent))
            {
                if (sessionsByNpc.ContainsKey(target))
                {
                    failureReason = ConversationStartFailureReason.TargetAlreadyInConversation;
                    return SharedEventConversationResult.Rejected;
                }

                if (IsInPlayerDialogue(target) || HasPendingPlayerReply(target))
                {
                    failureReason = IsInPlayerDialogue(target)
                        ? ConversationStartFailureReason.PlayerDialogueActive
                        : ConversationStartFailureReason.PendingPlayerReply;
                    return SharedEventConversationResult.Rejected;
                }

                pendingEvent = new PendingGroupEvent(
                    key,
                    target,
                    topic,
                    reason,
                    priority,
                    context,
                    Time.realtimeSinceStartup + sharedEventGatherSeconds);
                pendingGroupEventsByKey[key] = pendingEvent;
                AddNpcToPendingGroupEvent(pendingEvent, target);
                pendingEvent.ResolveCoroutine = StartCoroutine(ResolvePendingGroupEvent(pendingEvent));
            }

            if (pendingEvent.IsResolving)
            {
                failureReason = ConversationStartFailureReason.TargetAlreadyInConversation;
                return SharedEventConversationResult.Rejected;
            }

            if (pendingEvent.Target != target)
            {
                failureReason = ConversationStartFailureReason.TargetAlreadyInConversation;
                return SharedEventConversationResult.Rejected;
            }

            AddNpcToPendingGroupEvent(pendingEvent, participant);
            failureReason = ConversationStartFailureReason.None;

            if (Time.realtimeSinceStartup >= pendingEvent.ResolveAtRealtime && pendingEvent.ParticipantCount >= 3)
            {
                return TryResolvePendingGroupEventNow(pendingEvent, out failureReason)
                    ? SharedEventConversationResult.Started
                    : SharedEventConversationResult.Rejected;
            }

            return SharedEventConversationResult.WaitingForMoreParticipants;
        }

        public bool TryProposeOneOnOne(NpcRuntimeState initiator, NpcRuntimeState target, string topic, string reason, int priority)
        {
            return TryProposeOneOnOne(initiator, target, topic, reason, priority, true);
        }

        public bool TryProposeWitnessedOneOnOne(NpcRuntimeState initiator, NpcRuntimeState target, string topic, string reason, int priority)
        {
            return TryProposeOneOnOne(initiator, target, topic, reason, priority, true);
        }

        public bool TryStartOneOnOneNow(NpcRuntimeState initiator, NpcRuntimeState target, string topic, string reason, int priority)
        {
            return TryStartOneOnOneNow(initiator, target, topic, reason, priority, out _);
        }

        public bool TryStartOneOnOneNow(
            NpcRuntimeState initiator,
            NpcRuntimeState target,
            string topic,
            string reason,
            int priority,
            out ConversationStartFailureReason failureReason)
        {
            return TryStartOneOnOneNow(
                initiator,
                target,
                topic,
                reason,
                priority,
                true,
                out failureReason);
        }

        public bool TryStartOneOnOneNow(
            NpcRuntimeState initiator,
            NpcRuntimeState target,
            string topic,
            string reason,
            int priority,
            bool requiresPlayerWitness,
            DialogueContextInfo context,
            out ConversationStartFailureReason failureReason)
        {
            return TryStartProposal(new DialogueProposal(
                DialogueProposalKind.OneOnOne,
                initiator,
                target,
                topic,
                reason,
                priority,
                broadcastHearRadius,
                true,
                requiresPlayerWitness,
                string.Empty,
                false,
                context),
                out failureReason);
        }

        public bool TryStartOneOnOneNow(
            NpcRuntimeState initiator,
            NpcRuntimeState target,
            string topic,
            string reason,
            int priority,
            bool requiresPlayerWitness,
            out ConversationStartFailureReason failureReason)
        {
            return TryStartProposal(new DialogueProposal(
                DialogueProposalKind.OneOnOne,
                initiator,
                target,
                topic,
                reason,
                priority,
                broadcastHearRadius,
                true,
                requiresPlayerWitness),
                out failureReason);
        }

        private bool TryProposeOneOnOne(
            NpcRuntimeState initiator,
            NpcRuntimeState target,
            string topic,
            string reason,
            int priority,
            bool requiresPlayerWitness,
            DialogueContextInfo context = null)
        {
            return EnqueueProposal(new DialogueProposal(
                DialogueProposalKind.OneOnOne,
                initiator,
                target,
                topic,
                reason,
                priority,
                broadcastHearRadius,
                true,
                requiresPlayerWitness,
                string.Empty,
                false,
                context));
        }

        public bool TryProposeSelfTalk(NpcRuntimeState speaker, string topic, string reason, int priority)
        {
            return EnqueueProposal(new DialogueProposal(
                DialogueProposalKind.SelfTalk,
                speaker,
                null,
                topic,
                reason,
                priority,
                0f,
                false,
                false));
        }

        public bool TryProposeBroadcast(NpcRuntimeState speaker, string topic, string reason, int priority, float audibleRadius = -1f)
        {
            return EnqueueProposal(new DialogueProposal(
                DialogueProposalKind.Broadcast,
                speaker,
                null,
                topic,
                reason,
                priority,
                audibleRadius > 0f ? audibleRadius : broadcastHearRadius,
                false,
                false));
        }

        public bool TryRequestEnd(NpcRuntimeState requester)
        {
            if (requester == null || !sessionsByNpc.TryGetValue(requester, out ConversationSession session))
            {
                return false;
            }

            session.RequestEnd(requester);
            return true;
        }

        private bool EnqueueProposal(DialogueProposal proposal)
        {
            if (!IsValidProposal(proposal))
            {
                return false;
            }

            pendingProposals.Add(proposal);
            return true;
        }

        private bool TryStartProposal(DialogueProposal proposal)
        {
            return TryStartProposal(proposal, out _);
        }

        private bool TryStartProposal(DialogueProposal proposal, out ConversationStartFailureReason failureReason)
        {
            if (!IsValidProposal(proposal, out failureReason))
            {
                if (logDebug)
                {
                    Debug.Log($"[Conversation] Rejected proposal: reason={failureReason}, {DescribeProposal(proposal)}", this);
                }

                return false;
            }

            if (proposal.Kind == DialogueProposalKind.OneOnOne)
            {
                if (proposal.Target == null)
                {
                    failureReason = ConversationStartFailureReason.TargetMissing;
                    if (logDebug)
                    {
                        Debug.Log($"[Conversation] Rejected one-on-one target missing: {DescribeProposal(proposal)}", this);
                    }

                    return false;
                }

                if (IsNpcInConversation(proposal.Target))
                {
                    failureReason = ConversationStartFailureReason.TargetAlreadyInConversation;
                    if (logDebug)
                    {
                        Debug.Log($"[Conversation] Rejected one-on-one target busy: {DescribeProposal(proposal)}", this);
                    }

                    return false;
                }

                if (!IsCloseEnough(proposal.Initiator, proposal.Target))
                {
                    failureReason = ConversationStartFailureReason.TooFar;
                    if (logDebug)
                    {
                        Debug.Log($"[Conversation] Rejected one-on-one distance: {DescribeProposal(proposal)}", this);
                    }

                    return false;
                }
            }

            if (proposal.Kind == DialogueProposalKind.Group)
            {
                if (!ValidateGroupProposal(proposal, out failureReason))
                {
                    if (logDebug)
                    {
                        Debug.Log($"[Conversation] Rejected group proposal: reason={failureReason}, {DescribeProposal(proposal)}", this);
                    }

                    return false;
                }
            }

            NpcBrainProviderBehaviour provider = ResolveBrainProviderForProposal(proposal);
            if (provider == null)
            {
                failureReason = ConversationStartFailureReason.InvalidProposal;
                if (logDebug)
                {
                    Debug.Log($"[Conversation] Rejected proposal because no NPC brain provider was available: {DescribeProposal(proposal)}", this);
                }

                return false;
            }

            ConversationSession session = new ConversationSession(
                this,
                provider,
                clock,
                relationshipSystem,
                memorySystem,
                secondaryEventSystem,
                socialPlanSystem,
                messageDisplayer,
                dialogueHistorySystem,
                proposal,
                maxSafetyTurns,
                secondsBetweenTurns,
                maxSecondaryEventLookupResults,
                writeMemories,
                HandleSessionEnded);

            RegisterSession(session);
            session.LineAdded += HandleSessionLineAdded;
            session.Start();
            if (logDebug)
            {
                Debug.Log($"[Conversation] Started {proposal.Kind}: {GetName(proposal.Initiator)} -> {GetName(proposal.Target)} topic={proposal.Topic}", this);
            }

            failureReason = ConversationStartFailureReason.None;
            return true;
        }

        private DialogueProposal SelectBestProposal()
        {
            DialogueProposal best = null;
            for (int i = 0; i < pendingProposals.Count; i++)
            {
                DialogueProposal candidate = pendingProposals[i];
                if (!IsValidProposal(candidate, out _))
                {
                    continue;
                }

                if (best == null || ComparePriority(candidate, best) > 0)
                {
                    best = candidate;
                }
            }

            return best;
        }

        private int ComparePriority(DialogueProposal left, DialogueProposal right)
        {
            int priority = left.Priority.CompareTo(right.Priority);
            if (priority != 0)
            {
                return priority;
            }

            return right.CreatedRealtime.CompareTo(left.CreatedRealtime);
        }

        private bool IsValidProposal(DialogueProposal proposal)
        {
            return IsValidProposal(proposal, out _);
        }

        private bool IsValidProposal(DialogueProposal proposal, out ConversationStartFailureReason failureReason)
        {
            if (proposal == null || proposal.Initiator == null || clock == null)
            {
                failureReason = ConversationStartFailureReason.InvalidProposal;
                return false;
            }

            if (ResolveBrainProviderForProposal(proposal) == null)
            {
                failureReason = ConversationStartFailureReason.InvalidProposal;
                return false;
            }

            if (sessionsByNpc.ContainsKey(proposal.Initiator) || pendingGroupEventsByNpc.ContainsKey(proposal.Initiator))
            {
                failureReason = ConversationStartFailureReason.InitiatorAlreadyInConversation;
                return false;
            }

            if (IsBrainBusy(proposal.Initiator) || IsBrainBusy(proposal.Target) || IsAnyGroupParticipantBrainBusy(proposal))
            {
                failureReason = ConversationStartFailureReason.BrainBusy;
                return false;
            }

            if (IsInPlayerDialogue(proposal.Initiator) || IsInPlayerDialogue(proposal.Target) || IsAnyGroupParticipantInPlayerDialogue(proposal))
            {
                failureReason = ConversationStartFailureReason.PlayerDialogueActive;
                return false;
            }

            if (HasPendingPlayerReply(proposal.Initiator) || HasPendingPlayerReply(proposal.Target) || HasAnyGroupParticipantPendingPlayerReply(proposal))
            {
                failureReason = ConversationStartFailureReason.PendingPlayerReply;
                return false;
            }

            failureReason = ConversationStartFailureReason.None;
            return true;
        }

        private NpcBrainProviderBehaviour ResolveBrainProviderForProposal(DialogueProposal proposal)
        {
            if (brainProvider != null)
            {
                return brainProvider;
            }

            brainProvider = NpcBrainProviderBehaviour.FindPreferredProvider();
            if (brainProvider != null)
            {
                return brainProvider;
            }

            brainProvider = FindBrainProviderOnNpc(proposal != null ? proposal.Initiator : null);
            if (brainProvider != null)
            {
                return brainProvider;
            }

            brainProvider = FindBrainProviderOnNpc(proposal != null ? proposal.Target : null);
            if (brainProvider != null)
            {
                return brainProvider;
            }

            if (proposal != null && proposal.Participants != null)
            {
                for (int i = 0; i < proposal.Participants.Length; i++)
                {
                    brainProvider = FindBrainProviderOnNpc(proposal.Participants[i]);
                    if (brainProvider != null)
                    {
                        return brainProvider;
                    }
                }
            }

            return null;
        }

        private static NpcBrainProviderBehaviour FindBrainProviderOnNpc(NpcRuntimeState npc)
        {
            NpcBehaviorController controller = npc != null ? npc.GetComponent<NpcBehaviorController>() : null;
            return controller != null ? controller.BrainProvider : null;
        }

        private static bool IsBrainBusy(NpcRuntimeState npc)
        {
            Behavior.NpcBehaviorController controller = npc != null ? npc.GetComponent<Behavior.NpcBehaviorController>() : null;
            return controller != null && controller.RequestInFlight;
        }

        private bool IsAnyGroupParticipantBrainBusy(DialogueProposal proposal)
        {
            if (proposal == null || proposal.Kind != DialogueProposalKind.Group || proposal.Participants == null)
            {
                return false;
            }

            for (int i = 0; i < proposal.Participants.Length; i++)
            {
                if (proposal.Participants[i] != proposal.Initiator && IsBrainBusy(proposal.Participants[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsInPlayerDialogue(NpcRuntimeState npc)
        {
            return npc != null && dialogueController != null && dialogueController.IsConversationWith(npc);
        }

        private bool IsAnyGroupParticipantInPlayerDialogue(DialogueProposal proposal)
        {
            if (proposal == null || proposal.Kind != DialogueProposalKind.Group || proposal.Participants == null)
            {
                return false;
            }

            for (int i = 0; i < proposal.Participants.Length; i++)
            {
                if (proposal.Participants[i] != proposal.Initiator && IsInPlayerDialogue(proposal.Participants[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasPendingPlayerReply(NpcRuntimeState npc)
        {
            return npc != null && dialogueController != null && dialogueController.HasPendingReplyFor(npc);
        }

        private bool HasAnyGroupParticipantPendingPlayerReply(DialogueProposal proposal)
        {
            if (proposal == null || proposal.Kind != DialogueProposalKind.Group || proposal.Participants == null)
            {
                return false;
            }

            for (int i = 0; i < proposal.Participants.Length; i++)
            {
                if (proposal.Participants[i] != proposal.Initiator && HasPendingPlayerReply(proposal.Participants[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ValidateGroupProposal(DialogueProposal proposal, out ConversationStartFailureReason failureReason)
        {
            failureReason = ConversationStartFailureReason.InvalidProposal;
            if (proposal == null || proposal.Participants == null || proposal.Participants.Length < 2)
            {
                return false;
            }

            List<NpcRuntimeState> participants = new List<NpcRuntimeState>();
            for (int i = 0; i < proposal.Participants.Length; i++)
            {
                NpcRuntimeState participant = proposal.Participants[i];
                if (participant == null || participants.Contains(participant))
                {
                    continue;
                }

                if (sessionsByNpc.ContainsKey(participant) || pendingGroupEventsByNpc.ContainsKey(participant))
                {
                    failureReason = participant == proposal.Initiator
                        ? ConversationStartFailureReason.InitiatorAlreadyInConversation
                        : ConversationStartFailureReason.TargetAlreadyInConversation;
                    return false;
                }

                participants.Add(participant);
            }

            if (participants.Count < 2)
            {
                failureReason = ConversationStartFailureReason.TargetMissing;
                return false;
            }

            for (int i = 0; i < participants.Count; i++)
            {
                if (!HasNearbyGroupParticipant(participants[i], participants))
                {
                    failureReason = ConversationStartFailureReason.TooFar;
                    return false;
                }
            }

            failureReason = ConversationStartFailureReason.None;
            return true;
        }

        private bool HasNearbyGroupParticipant(NpcRuntimeState npc, List<NpcRuntimeState> participants)
        {
            if (npc == null || participants == null || participants.Count < 2)
            {
                return false;
            }

            for (int i = 0; i < participants.Count; i++)
            {
                NpcRuntimeState other = participants[i];
                if (other != null
                    && other != npc
                    && Vector2.Distance(npc.transform.position, other.transform.position) <= groupConversationStartDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasNearbySessionParticipant(NpcRuntimeState npc, ConversationSession session)
        {
            if (npc == null || session == null)
            {
                return false;
            }

            for (int i = 0; i < session.ParticipantCount; i++)
            {
                ConversationParticipant participant = session.GetParticipant(i);
                NpcRuntimeState other = participant != null ? participant.Npc : null;
                if (other != null
                    && other != npc
                    && Vector2.Distance(npc.transform.position, other.transform.position) <= groupConversationStartDistance)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsCloseEnough(NpcRuntimeState initiator, NpcRuntimeState target)
        {
            if (initiator == null || target == null)
            {
                return false;
            }

            return Vector2.Distance(initiator.transform.position, target.transform.position) <= oneOnOneStartDistance;
        }

        private bool IsPlayerWitnessing(ConversationSession session, NpcRuntimeState speaker)
        {
            if (player == null || session == null)
            {
                return false;
            }

            if (speaker != null && Vector2.Distance(player.position, speaker.transform.position) <= playerWitnessDistance)
            {
                return true;
            }

            for (int i = 0; i < session.ParticipantCount; i++)
            {
                ConversationParticipant participant = session.GetParticipant(i);
                NpcRuntimeState npc = participant != null ? participant.Npc : null;
                if (npc != null && Vector2.Distance(player.position, npc.transform.position) <= playerWitnessDistance)
                {
                    return true;
                }
            }

            NpcRuntimeState initiator = session.Initiator.Npc;
            if (initiator != null && Vector2.Distance(player.position, initiator.transform.position) <= playerWitnessDistance)
            {
                return true;
            }

            NpcRuntimeState target = session.Target.Npc;
            return target != null && Vector2.Distance(player.position, target.transform.position) <= playerWitnessDistance;
        }

        private void RegisterSession(ConversationSession session)
        {
            if (session == null)
            {
                return;
            }

            for (int i = 0; i < session.ParticipantCount; i++)
            {
                ConversationParticipant participant = session.GetParticipant(i);
                if (participant != null && participant.Npc != null)
                {
                    sessionsByNpc[participant.Npc] = session;
                }
            }

            if (!activeSessions.Contains(session))
            {
                activeSessions.Add(session);
            }

            if (session.Proposal.AllowLateJoin && !string.IsNullOrWhiteSpace(session.Proposal.ActivityKey))
            {
                sessionsByActivityKey[session.Proposal.ActivityKey] = session;
            }
        }

        private void HandleSessionEnded(ConversationSession session)
        {
            if (session == null)
            {
                return;
            }

            bool wasActivityConversation = !string.IsNullOrWhiteSpace(session.Proposal.ActivityKey);
            if (wasActivityConversation)
            {
                socialPlanSystem?.MarkPlanCompleted(session.Proposal.ActivityKey);
            }

            for (int i = 0; i < session.ParticipantCount; i++)
            {
                ConversationParticipant participant = session.GetParticipant(i);
                if (participant != null && participant.Npc != null)
                {
                    sessionsByNpc.Remove(participant.Npc);
                }
            }

            activeSessions.Remove(session);
            witnessedOneOnOneSessions.Remove(session);
            if (!string.IsNullOrWhiteSpace(session.Proposal.ActivityKey)
                && sessionsByActivityKey.TryGetValue(session.Proposal.ActivityKey, out ConversationSession mapped)
                && mapped == session)
            {
                sessionsByActivityKey.Remove(session.Proposal.ActivityKey);
            }

            session.LineAdded -= HandleSessionLineAdded;

            SettlePostConversationOutcome(session, wasActivityConversation);
        }

        private void SettlePostConversationOutcome(ConversationSession session, bool wasActivityConversation)
        {
            if (session == null)
            {
                return;
            }

            if (session.Proposal.Kind != DialogueProposalKind.OneOnOne && session.Proposal.Kind != DialogueProposalKind.Group)
            {
                return;
            }

            List<NpcRuntimeState> participants = CollectSessionParticipants(session);
            for (int i = 0; i < participants.Count; i++)
            {
                ClearConversationTaskWithoutAi(participants[i], wasActivityConversation, session.Proposal.ActivityKey);
            }

            for (int i = 0; i < participants.Count; i++)
            {
                NpcRuntimeState npc = participants[i];
                if (npc == null)
                {
                    continue;
                }

                if (session.TryGetPostConversationDecision(npc, out NpcAiDecision decision))
                {
                    ApplyPostConversationDecision(npc, decision);
                    continue;
                }

                ResolveNpcAfterConversation(npc, session.HasStructuredOutcome
                    ? "conversation ended after structured outcome"
                    : "conversation ended without new action");
            }
        }

        private static void ClearConversationTaskWithoutAi(NpcRuntimeState npc, bool wasActivityConversation, string activityKey)
        {
            NpcTaskController taskController = npc != null ? npc.GetComponent<NpcTaskController>() : null;
            NpcTask task = taskController != null ? taskController.CurrentTask : null;
            if (task == null)
            {
                return;
            }

            if (task.Kind == NpcTaskKind.TalkToActor)
            {
                taskController.ClearCurrentTask("npc conversation ended");
                return;
            }

            if (wasActivityConversation
                && task.Kind == NpcTaskKind.AttendActivity
                && (string.IsNullOrWhiteSpace(activityKey)
                    || string.Equals(task.ActivityKey, activityKey, StringComparison.OrdinalIgnoreCase)))
            {
                taskController.ClearCurrentTask("activity conversation ended");
            }
        }

        private void ApplyPostConversationDecision(NpcRuntimeState npc, NpcAiDecision decision)
        {
            if (npc == null || decision == null)
            {
                return;
            }

            NpcBehaviorState state = npc.GetComponent<NpcBehaviorState>();
            if (state == null)
            {
                ResolveNpcAfterConversation(npc, "post-conversation action could not apply because NpcBehaviorState is missing");
                return;
            }

            if (logDebug)
            {
                Debug.Log(
                    $"[Conversation] Applying cached post-conversation action to {GetName(npc)}: " +
                    $"intent={decision.intent}, targetActor={decision.targetActorId}, targetLocation={decision.targetLocationId}, " +
                    $"eventKind={decision.eventKind}, timingMode={decision.timingMode}",
                    this);
            }

            state.ApplyDecision(decision);
        }

        private void ResolveNpcAfterConversation(NpcRuntimeState npc, string reason)
        {
            NpcActionExecutor executor = npc != null ? npc.GetComponent<NpcActionExecutor>() : null;
            if (executor != null)
            {
                executor.ResolveScheduleNow();
                return;
            }

            NpcTaskController taskController = npc != null ? npc.GetComponent<NpcTaskController>() : null;
            if (taskController != null && !taskController.HasTask)
            {
                taskController.SetScheduleTask(npc.PlannedLocation, npc.CurrentAction);
            }

            if (logDebug)
            {
                Debug.Log($"[Conversation] Resolved {GetName(npc)} after conversation. reason={reason}", this);
            }
        }

        private List<NpcRuntimeState> CollectSessionParticipants(ConversationSession session)
        {
            List<NpcRuntimeState> participants = new List<NpcRuntimeState>();
            if (session == null)
            {
                return participants;
            }

            for (int i = 0; i < session.ParticipantCount; i++)
            {
                ConversationParticipant participant = session.GetParticipant(i);
                NpcRuntimeState npc = participant != null ? participant.Npc : null;
                if (npc != null && !participants.Contains(npc))
                {
                    participants.Add(npc);
                }
            }

            return participants;
        }

        private void HandleSessionLineAdded(ConversationSession session, DialogueLine line)
        {
            if (session == null || line == null)
            {
                return;
            }

            ShowLineIfWitnessed(session, line);

            if (session.Proposal.Kind != DialogueProposalKind.Broadcast)
            {
                return;
            }

            NpcRuntimeState speaker = session.Initiator.Npc;
            if (speaker == null)
            {
                return;
            }

            NpcRuntimeState[] npcs = FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcRuntimeState listener = npcs[i];
                if (listener == null || listener == speaker || IsNpcInConversation(listener))
                {
                    continue;
                }

                if (Vector2.Distance(listener.transform.position, speaker.transform.position) > session.Proposal.AudibleRadius)
                {
                    continue;
                }

                int replyPriority = EstimateBroadcastReplyPriority(speaker, listener, line.Text);
                if (replyPriority < broadcastReplyPriorityThreshold)
                {
                    continue;
                }

                TryProposeOneOnOne(
                    listener,
                    speaker,
                    "reply to nearby broadcast",
                    $"I heard {GetName(speaker)} say: {line.Text}",
                    replyPriority,
                    true,
                    new DialogueContextInfo(
                        "broadcast_reply",
                        GetActorId(speaker),
                        GetActorId(speaker),
                        string.Empty,
                        line.Text,
                        $"I heard {GetName(speaker)} say: {line.Text}"));
            }
        }

        private int EstimateBroadcastReplyPriority(NpcRuntimeState speaker, NpcRuntimeState listener, string text)
        {
            int priority = 20;
            if (relationshipSystem != null && speaker != null && listener != null && speaker.Profile != null && listener.Profile != null)
            {
                RelationshipRecord record = relationshipSystem.GetOrCreate(listener.Profile.NpcId, speaker.Profile.NpcId);
                priority += Mathf.Clamp(record.Affinity + record.Trust - record.Suspicion, -20, 35);
            }

            if (!string.IsNullOrWhiteSpace(text))
            {
                string normalized = text.ToLowerInvariant();
                if (ContainsUrgentKeyword(normalized))
                {
                    priority += 30;
                }

                if (listener != null && listener.Profile != null && normalized.Contains(listener.Profile.DisplayName.ToLowerInvariant()))
                {
                    priority += 35;
                }
            }

            return Mathf.Clamp(priority, 0, 100);
        }

        private static bool ContainsUrgentKeyword(string normalizedText)
        {
            if (string.IsNullOrWhiteSpace(normalizedText))
            {
                return false;
            }

            return normalizedText.Contains("help")
                || normalizedText.Contains("danger")
                || normalizedText.Contains("sick")
                || normalizedText.Contains("ill")
                || normalizedText.Contains("collapse")
                || normalizedText.Contains("\u6551")
                || normalizedText.Contains("\u5371\u9669")
                || normalizedText.Contains("\u751f\u75c5")
                || normalizedText.Contains("\u91cd\u75c5")
                || normalizedText.Contains("\u660f\u5012");
        }

        private void EndUnwitnessedSessions()
        {
            for (int i = activeSessions.Count - 1; i >= 0; i--)
            {
                ConversationSession session = activeSessions[i];
                if (session == null || !session.IsActive)
                {
                    activeSessions.RemoveAt(i);
                }
            }
        }

        private static string GetName(NpcRuntimeState npc)
        {
            return npc != null && npc.Profile != null ? npc.Profile.DisplayName : "(none)";
        }

        private static string GetActorId(NpcRuntimeState npc)
        {
            return npc != null && npc.Profile != null ? npc.Profile.NpcId : string.Empty;
        }

        private static string DescribeProposal(DialogueProposal proposal)
        {
            if (proposal == null)
            {
                return "(null)";
            }

            return $"kind={proposal.Kind}, initiator={GetName(proposal.Initiator)}, target={GetName(proposal.Target)}, topic={proposal.Topic}, priority={proposal.Priority}";
        }

        private void ShowLineIfWitnessed(ConversationSession session, DialogueLine line)
        {
            if (session == null || line == null)
            {
                return;
            }

            EnsureLineDisplayReferences();
            dialogueHistorySystem?.AddDisplayedLine(line);

            NpcRuntimeState speaker = FindSessionSpeaker(session, line.SpeakerId);
            if (speaker == null)
            {
                return;
            }

            if (showNpcConversationBubblesOnlyWhenPlayerWitnessing && !ShouldShowLineToPlayer(session, speaker))
            {
                return;
            }

            messageDisplayer?.ShowMessageWithoutRecording(speaker, line.Text);
        }

        private void EnsureLineDisplayReferences()
        {
            if (messageDisplayer == null)
            {
                messageDisplayer = FindFirstObjectByType<MessageDisplayer>();
            }

            if (dialogueHistorySystem == null)
            {
                dialogueHistorySystem = FindFirstObjectByType<DialogueHistorySystem>();
            }
        }

        private bool ShouldShowLineToPlayer(ConversationSession session, NpcRuntimeState speaker)
        {
            if (session == null)
            {
                return false;
            }

            bool canPersistAfterWitness = session.Proposal.Kind == DialogueProposalKind.OneOnOne
                || session.Proposal.Kind == DialogueProposalKind.Group;
            if (canPersistAfterWitness && witnessedOneOnOneSessions.Contains(session))
            {
                return true;
            }

            bool witnessingNow = IsPlayerWitnessing(session, speaker);
            if (witnessingNow && canPersistAfterWitness)
            {
                witnessedOneOnOneSessions.Add(session);
            }

            return witnessingNow;
        }

        private static NpcRuntimeState FindSessionSpeaker(ConversationSession session, string speakerId)
        {
            return session != null ? session.FindParticipantById(speakerId) : null;
        }

        private IEnumerator ResolvePendingGroupEvent(PendingGroupEvent pendingEvent)
        {
            if (pendingEvent == null)
            {
                yield break;
            }

            while (Time.realtimeSinceStartup < pendingEvent.ResolveAtRealtime)
            {
                yield return null;
            }

            TryResolvePendingGroupEventNow(pendingEvent, out _);
        }

        private bool TryResolvePendingGroupEventNow(PendingGroupEvent pendingEvent, out ConversationStartFailureReason failureReason)
        {
            failureReason = ConversationStartFailureReason.InvalidProposal;
            if (pendingEvent == null || pendingEvent.IsResolving)
            {
                return false;
            }

            pendingEvent.IsResolving = true;
            List<NpcRuntimeState> participants = BuildReadyParticipants(pendingEvent);
            RemovePendingGroupEvent(pendingEvent, true);

            if (participants.Count < 2)
            {
                RestorePendingGroupParticipants(pendingEvent);
                failureReason = ConversationStartFailureReason.TargetMissing;
                return false;
            }

            if (participants.Count == 2)
            {
                bool startedOneOnOne = TryStartProposal(new DialogueProposal(
                    DialogueProposalKind.OneOnOne,
                    participants[0],
                    participants[1],
                    pendingEvent.Topic,
                    pendingEvent.Reason,
                    pendingEvent.Priority,
                    broadcastHearRadius,
                    true,
                    false,
                    string.Empty,
                    false,
                    pendingEvent.Context),
                    out failureReason);
                if (startedOneOnOne)
                {
                    return true;
                }

                if (failureReason == ConversationStartFailureReason.TooFar
                    && AreWithinGroupConversationDistance(participants[0], participants[1])
                    && TryStartProposal(new DialogueProposal(
                        participants.ToArray(),
                        pendingEvent.Topic,
                        pendingEvent.Reason,
                        pendingEvent.Priority,
                        false,
                        string.Empty,
                        false,
                        pendingEvent.Context),
                        out failureReason))
                {
                    return true;
                }

                RestorePendingGroupParticipants(pendingEvent);
                return false;
            }

            DialogueProposal proposal = new DialogueProposal(
                participants.ToArray(),
                pendingEvent.Topic,
                pendingEvent.Reason,
                pendingEvent.Priority,
                false,
                string.Empty,
                false,
                pendingEvent.Context);

            if (TryStartProposal(proposal, out failureReason))
            {
                if (logDebug)
                {
                    Debug.Log($"[Conversation] Started shared event group: target={GetName(pendingEvent.Target)}, participants={BuildNpcList(participants)}", this);
                }

                return true;
            }

            if (participants.Count > 2)
            {
                bool startedOneOnOne = TryStartProposal(new DialogueProposal(
                    DialogueProposalKind.OneOnOne,
                    participants[0],
                    participants[participants.Count - 1],
                    pendingEvent.Topic,
                    pendingEvent.Reason,
                    pendingEvent.Priority,
                    broadcastHearRadius,
                    true,
                    false,
                    string.Empty,
                    false,
                    pendingEvent.Context),
                    out failureReason);
                if (startedOneOnOne)
                {
                    return true;
                }
            }

            RestorePendingGroupParticipants(pendingEvent);
            return false;
        }

        private List<NpcRuntimeState> BuildReadyParticipants(PendingGroupEvent pendingEvent)
        {
            List<NpcRuntimeState> participants = new List<NpcRuntimeState>();
            if (pendingEvent == null)
            {
                return participants;
            }

            NpcRuntimeState target = pendingEvent.Target;
            if (target == null
                || sessionsByNpc.ContainsKey(target)
                || IsBrainBusy(target)
                || IsInPlayerDialogue(target)
                || HasPendingPlayerReply(target))
            {
                return participants;
            }

            Vector2 anchor = target.transform.position;
            for (int i = 0; i < pendingEvent.ParticipantCount; i++)
            {
                NpcRuntimeState npc = pendingEvent.GetParticipant(i);
                if (npc == null || npc == target || participants.Contains(npc))
                {
                    continue;
                }

                if (sessionsByNpc.ContainsKey(npc) || IsBrainBusy(npc) || IsInPlayerDialogue(npc) || HasPendingPlayerReply(npc))
                {
                    continue;
                }

                if (Vector2.Distance(npc.transform.position, anchor) <= groupConversationStartDistance)
                {
                    participants.Add(npc);
                }
            }

            if (participants.Count > 0)
            {
                participants.Add(target);
            }

            return participants;
        }

        private bool AreWithinGroupConversationDistance(NpcRuntimeState first, NpcRuntimeState second)
        {
            return first != null
                && second != null
                && Vector2.Distance(first.transform.position, second.transform.position) <= groupConversationStartDistance;
        }

        private void AddNpcToPendingGroupEvent(PendingGroupEvent pendingEvent, NpcRuntimeState npc)
        {
            if (pendingEvent == null || npc == null || pendingEvent.Contains(npc))
            {
                return;
            }

            pendingEvent.AddParticipant(npc);
            pendingGroupEventsByNpc[npc] = pendingEvent;
            NpcMovementAgent movement = npc.GetComponent<NpcMovementAgent>();
            movement?.SetPause(SharedEventPauseReason, true);
            if (pendingEvent.Target != null && pendingEvent.Target != npc)
            {
                movement?.Face(pendingEvent.Target.transform.position);
            }
        }

        private void RemovePendingGroupEvent(PendingGroupEvent pendingEvent, bool restoreMovement)
        {
            if (pendingEvent == null)
            {
                return;
            }

            if (pendingEvent.ResolveCoroutine != null)
            {
                StopCoroutine(pendingEvent.ResolveCoroutine);
                pendingEvent.ResolveCoroutine = null;
            }

            pendingGroupEventsByKey.Remove(pendingEvent.Key);
            for (int i = 0; i < pendingEvent.ParticipantCount; i++)
            {
                NpcRuntimeState participant = pendingEvent.GetParticipant(i);
                if (participant != null && pendingGroupEventsByNpc.TryGetValue(participant, out PendingGroupEvent mapped) && mapped == pendingEvent)
                {
                    pendingGroupEventsByNpc.Remove(participant);
                }

                if (restoreMovement)
                {
                    NpcMovementAgent movement = participant != null ? participant.GetComponent<NpcMovementAgent>() : null;
                    movement?.SetPause(SharedEventPauseReason, false);
                }
            }
        }

        private void RestorePendingGroupParticipants(PendingGroupEvent pendingEvent)
        {
            if (pendingEvent == null)
            {
                return;
            }

            for (int i = 0; i < pendingEvent.ParticipantCount; i++)
            {
                NpcRuntimeState participant = pendingEvent.GetParticipant(i);
                NpcMovementAgent movement = participant != null ? participant.GetComponent<NpcMovementAgent>() : null;
                movement?.SetPause(SharedEventPauseReason, false);
            }
        }

        private static string BuildSharedEventKey(NpcRuntimeState target)
        {
            string id = target != null && target.Profile != null && !string.IsNullOrWhiteSpace(target.Profile.NpcId)
                ? target.Profile.NpcId
                : target != null ? target.GetInstanceID().ToString() : "none";
            return "target:" + id;
        }

        private static string BuildNpcList(List<NpcRuntimeState> npcs)
        {
            if (npcs == null || npcs.Count == 0)
            {
                return "(none)";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < npcs.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(GetName(npcs[i]));
            }

            return builder.ToString();
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

        private sealed class PendingGroupEvent
        {
            private readonly List<NpcRuntimeState> participants = new List<NpcRuntimeState>();

            public PendingGroupEvent(
                string key,
                NpcRuntimeState target,
                string topic,
                string reason,
                int priority,
                DialogueContextInfo context,
                float resolveAtRealtime)
            {
                Key = key;
                Target = target;
                Topic = topic;
                Reason = reason;
                Priority = priority;
                Context = context;
                ResolveAtRealtime = resolveAtRealtime;
            }

            public string Key { get; }
            public NpcRuntimeState Target { get; }
            public string Topic { get; }
            public string Reason { get; }
            public int Priority { get; }
            public DialogueContextInfo Context { get; }
            public float ResolveAtRealtime { get; }
            public bool IsResolving { get; set; }
            public Coroutine ResolveCoroutine { get; set; }
            public int ParticipantCount => participants.Count;

            public bool Contains(NpcRuntimeState npc)
            {
                return npc != null && participants.Contains(npc);
            }

            public void AddParticipant(NpcRuntimeState npc)
            {
                if (npc != null && !participants.Contains(npc))
                {
                    participants.Add(npc);
                }
            }

            public NpcRuntimeState GetParticipant(int index)
            {
                return index >= 0 && index < participants.Count ? participants[index] : null;
            }
        }
    }
}
