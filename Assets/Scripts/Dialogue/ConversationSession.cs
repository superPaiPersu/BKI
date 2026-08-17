using System;
using System.Collections;
using System.Collections.Generic;
using CityStateSim.AI;
using CityStateSim.Behavior;
using CityStateSim.Core;
using CityStateSim.Encounters;
using CityStateSim.Locations;
using CityStateSim.Memory;
using CityStateSim.Movement;
using CityStateSim.NPC;
using CityStateSim.Perception;
using CityStateSim.Relationships;
using CityStateSim.SecondaryEvents;
using CityStateSim.SocialPlans;
using UnityEngine;

namespace CityStateSim.Dialogue
{
    public sealed class ConversationSession
    {
        private readonly MonoBehaviour coroutineHost;
        private readonly NpcBrainProviderBehaviour brainProvider;
        private readonly GameClock clock;
        private readonly RelationshipSystem relationshipSystem;
        private readonly MemorySystem memorySystem;
        private readonly SecondaryEventSystem secondaryEventSystem;
        private readonly PendingEncounterSystem pendingEncounterSystem;
        private readonly SocialPlanSystem socialPlanSystem;
        private readonly Action<ConversationSession> endedCallback;
        private readonly int maxSafetyTurns;
        private readonly float secondsBetweenTurns;
        private readonly int maxSecondaryEventLookupResults;
        private readonly bool writeMemories;

        private readonly List<DialogueLine> lines = new List<DialogueLine>();
        private readonly List<ConversationParticipant> groupParticipants = new List<ConversationParticipant>();
        private readonly Dictionary<NpcRuntimeState, NpcAiDecision> postConversationDecisionsByNpc = new Dictionary<NpcRuntimeState, NpcAiDecision>();
        private Coroutine routine;
        private bool endRequested;
        private NpcRuntimeState endRequester;

        public ConversationSession(
            MonoBehaviour coroutineHost,
            NpcBrainProviderBehaviour brainProvider,
            GameClock clock,
            RelationshipSystem relationshipSystem,
            MemorySystem memorySystem,
            SecondaryEventSystem secondaryEventSystem,
            SocialPlanSystem socialPlanSystem,
            MessageDisplayer messageDisplayer,
            DialogueHistorySystem dialogueHistorySystem,
            DialogueProposal proposal,
            int maxSafetyTurns,
            float secondsBetweenTurns,
            int maxSecondaryEventLookupResults,
            bool writeMemories,
            Action<ConversationSession> endedCallback)
        {
            this.coroutineHost = coroutineHost;
            this.brainProvider = brainProvider;
            this.clock = clock;
            this.relationshipSystem = relationshipSystem;
            this.memorySystem = memorySystem;
            this.secondaryEventSystem = secondaryEventSystem;
            pendingEncounterSystem = PendingEncounterSystem.GetOrCreate();
            this.socialPlanSystem = socialPlanSystem != null
                ? socialPlanSystem
                : UnityEngine.Object.FindFirstObjectByType<SocialPlanSystem>();
            this.maxSafetyTurns = Mathf.Max(2, maxSafetyTurns);
            this.secondsBetweenTurns = Mathf.Max(0f, secondsBetweenTurns);
            this.maxSecondaryEventLookupResults = Mathf.Max(1, maxSecondaryEventLookupResults);
            this.writeMemories = writeMemories;
            this.endedCallback = endedCallback;
            Proposal = proposal;
            Initiator = new ConversationParticipant(proposal.Initiator);
            Target = new ConversationParticipant(proposal.Target);
            if (proposal.Participants != null)
            {
                for (int i = 0; i < proposal.Participants.Length; i++)
                {
                    AddGroupParticipant(proposal.Participants[i]);
                }
            }
        }

        public DialogueProposal Proposal { get; }
        public ConversationParticipant Initiator { get; }
        public ConversationParticipant Target { get; }
        public bool IsActive { get; private set; }
        public bool RequiresPlayerWitness => Proposal.RequiresPlayerWitness;
        public string TranscriptSummary => BuildLineSummary();
        public bool HasPostConversationAction => postConversationDecisionsByNpc.Count > 0;
        public bool HasSocialPlanChanges { get; private set; }
        public bool HasPendingEncounterChanges { get; private set; }
        public bool HasStructuredOutcome => HasPostConversationAction || HasSocialPlanChanges || HasPendingEncounterChanges;
        public int ParticipantCount
        {
            get
            {
                if (Proposal.Kind == DialogueProposalKind.Group)
                {
                    return groupParticipants.Count;
                }

                if (Target != null && Target.Npc != null)
                {
                    return 2;
                }

                return Initiator != null && Initiator.Npc != null ? 1 : 0;
            }
        }

        public event Action<ConversationSession, DialogueLine> LineAdded;
        public event Action<ConversationSession> Ended;

        public void Start()
        {
            if (coroutineHost == null || routine != null)
            {
                return;
            }

            IsActive = true;
            routine = coroutineHost.StartCoroutine(Run());
        }

        public void RequestEnd(NpcRuntimeState requester)
        {
            if (!IsActive || requester == null)
            {
                return;
            }

            endRequested = true;
            endRequester = requester;
        }

        public bool TryAddGroupParticipant(NpcRuntimeState npc)
        {
            if (!IsActive
                || Proposal.Kind != DialogueProposalKind.Group
                || !Proposal.AllowLateJoin
                || npc == null
                || ContainsGroupParticipant(npc))
            {
                return false;
            }

            AddGroupParticipant(npc);
            StopGroupAndFaceCenter();
            return true;
        }

        public bool TryGetPostConversationDecision(NpcRuntimeState npc, out NpcAiDecision decision)
        {
            decision = null;
            return npc != null && postConversationDecisionsByNpc.TryGetValue(npc, out decision) && decision != null;
        }

        public void ForceEnd()
        {
            if (!IsActive)
            {
                return;
            }

            if (routine != null && coroutineHost != null)
            {
                coroutineHost.StopCoroutine(routine);
                routine = null;
            }

            EndSession();
        }

        private IEnumerator Run()
        {
            if (Proposal.Kind == DialogueProposalKind.SelfTalk)
            {
                yield return RunSelfTalk();
                EndSession();
                yield break;
            }

            if (Proposal.Kind == DialogueProposalKind.Broadcast)
            {
                yield return RunBroadcast();
                EndSession();
                yield break;
            }

            if (Proposal.Kind == DialogueProposalKind.Group)
            {
                yield return RunGroupConversation();
                EndSession();
                yield break;
            }

            if (Proposal.Target == null)
            {
                yield return RunSelfTalk("No one is close enough to hear.");
                EndSession();
                yield break;
            }

            Initiator.StopAndFace(Target.Npc != null ? Target.Npc.transform : null);
            Target.StopAndFace(Initiator.Npc != null ? Initiator.Npc.transform : null);

            yield return RunInvitation();
        }

        private IEnumerator RunInvitation()
        {
            DialogueTurnResult opener = null;
            yield return RequestTurn(
                Initiator.Npc,
                Target.Npc,
                "One-on-one opener: speak directly to currentListenerId. The line itself is the invitation. Use the role context to separate reporter/source from listener/subject. If the listener is the subject, ask or check on the listener directly. Avoid repeating lines already spoken to the player or already visible in recent memory.",
                result => opener = result);

            if (!IsActive)
            {
                yield break;
            }

            if (opener == null || string.IsNullOrWhiteSpace(opener.Text))
            {
                EndSession();
                yield break;
            }

            AddLine(Initiator.Npc, opener.Text);

            DialogueTurnResult response = null;
            string invitationResponseInstruction =
                "One-on-one invitation response: answer with one short spoken line. " +
                "Accept naturally, or refuse with nextActionPreference=reject_conversation. " +
                "When the practical matter is already settled, nextActionPreference=end_request lets the conversation close cleanly.";
            yield return RequestTurn(
                Target.Npc,
                Initiator.Npc,
                invitationResponseInstruction,
                result => response = result);

            if (!IsActive)
            {
                yield break;
            }

            if (response == null || string.IsNullOrWhiteSpace(response.Text))
            {
                yield return RequestTurn(
                    Target.Npc,
                    Initiator.Npc,
                    invitationResponseInstruction + " The previous reply was empty; give one short spoken response so the invitation can resolve.",
                    result => response = result);
            }

            if (!IsActive)
            {
                yield break;
            }

            if (response == null || string.IsNullOrWhiteSpace(response.Text))
            {
                response = CreateFallbackInvitationResponse();
            }

            bool accepted = response.AcceptedInvitation && !IsReject(response);
            AddLine(Target.Npc, response.Text);
            ApplySocialPlanInvitationResult(accepted, response.Text);

            if (!accepted)
            {
                yield return RunSelfTalk("Your conversation invitation was rejected. You may briefly mutter to yourself without interrupting movement.");
                EndSession();
                yield break;
            }

            if (ShouldEndSettledOneOnOneTurn(response))
            {
                EndSession();
                yield break;
            }

            NpcRuntimeState speaker = Initiator.Npc;
            NpcRuntimeState listener = Target.Npc;
            int turn = 0;
            while (IsActive && turn < maxSafetyTurns)
            {
                if (endRequested)
                {
                    bool agreed = false;
                    NpcRuntimeState receiver = endRequester == speaker ? listener : speaker;
                    yield return RequestEndReply(receiver, endRequester, value => agreed = value);
                    if (!IsActive)
                    {
                        yield break;
                    }

                    if (agreed)
                    {
                        EndSession();
                        yield break;
                    }

                    endRequested = false;
                    endRequester = null;
                }

                DialogueTurnResult result = null;
                yield return RequestTurn(
                    speaker,
                    listener,
                    "One-on-one continuation: speak when you add new information, a question, a decision, or a changed emotion. When the practical matter is settled, nextActionPreference=end_request is the natural close. Avoid filler acknowledgements.",
                    value => result = value);

                if (!IsActive)
                {
                    yield break;
                }

                if (result == null || string.IsNullOrWhiteSpace(result.Text))
                {
                    EndSession();
                    yield break;
                }

                AddLine(speaker, result.Text);
                if (result.WantsToEnd)
                {
                    endRequested = true;
                    endRequester = speaker;
                }

                if (ShouldEndSettledOneOnOneTurn(result))
                {
                    EndSession();
                    yield break;
                }

                NpcRuntimeState nextSpeaker = listener;
                listener = speaker;
                speaker = nextSpeaker;
                turn++;
                if (secondsBetweenTurns > 0f)
                {
                    yield return new WaitForSeconds(secondsBetweenTurns);
                }
            }

            EndSession();
        }

        private IEnumerator RunGroupConversation()
        {
            if (groupParticipants.Count < 2)
            {
                yield return RunSelfTalk("No one else is ready to discuss this.");
                yield break;
            }

            StopGroupAndFaceCenter();

            int maxTurns = maxSafetyTurns;
            int spokenLines = 0;
            int silentStreak = 0;
            int currentSpeakerIndex = 0;
            List<int> speakingOpportunities = new List<int>();
            List<int> spokenLineCounts = new List<int>();
            for (int turn = 0; IsActive && turn < maxTurns; turn++)
            {
                EnsureTurnCountListSize(speakingOpportunities);
                EnsureTurnCountListSize(spokenLineCounts);
                currentSpeakerIndex = Mathf.Clamp(currentSpeakerIndex, 0, groupParticipants.Count - 1);
                ConversationParticipant participant = groupParticipants[currentSpeakerIndex];
                NpcRuntimeState speaker = participant.Npc;
                NpcRuntimeState listener = FindPrimaryGroupListener(speaker);
                speakingOpportunities[currentSpeakerIndex]++;
                string instruction = turn == 0
                    ? "Group opener: this is a shared event and everyone present is the audience. Speak if you have useful content; empty dialogue with nextActionPreference=listen is a valid choice. nextSpeakerId can point to the natural next speaker."
                    : "Group turn: you may speak or listen. Speak for new information, a question, a reaction, a decision, or to guide the group. Empty dialogue with listen/silent/no_comment is valid when listening is more human. nextSpeakerId can point to the addressed or most relevant participant. When the group has enough information or agreement, nextActionPreference=end_request closes the exchange.";

                DialogueTurnResult result = null;
                yield return RequestTurn(speaker, listener, instruction, value => result = value);
                if (!IsActive)
                {
                    yield break;
                }

                if (result == null)
                {
                    continue;
                }

                SanitizeGroupTurnResult(result, speaker);

                if (GroupTurnIsSilent(result))
                {
                    silentStreak++;
                    if (result.WantsToEnd || (spokenLines > 0 && silentStreak >= groupParticipants.Count))
                    {
                        yield break;
                    }
                }
                else
                {
                    AddLine(speaker, result.Text);
                    spokenLines++;
                    silentStreak = 0;
                    spokenLineCounts[currentSpeakerIndex]++;
                    if (result.WantsToEnd && spokenLines >= 1)
                    {
                        yield break;
                    }

                    if (ShouldNaturallyEndGroupConversation(result, spokenLineCounts))
                    {
                        yield break;
                    }
                }

                currentSpeakerIndex = ResolveNextGroupSpeakerIndex(
                    currentSpeakerIndex,
                    result.NextSpeakerId,
                    speakingOpportunities,
                    spokenLineCounts);

                if (secondsBetweenTurns > 0f)
                {
                    yield return new WaitForSeconds(secondsBetweenTurns);
                }
            }
        }

        private IEnumerator RequestEndReply(NpcRuntimeState receiver, NpcRuntimeState requester, Action<bool> onComplete)
        {
            DialogueTurnResult result = null;
            yield return RequestTurn(
                receiver,
                requester,
                "End request received: agree with nextActionPreference=end_accept, or continue with a brief reason.",
                value => result = value);

            if (!IsActive)
            {
                onComplete?.Invoke(false);
                yield break;
            }

            if (result != null && !string.IsNullOrWhiteSpace(result.Text))
            {
                AddLine(receiver, result.Text);
            }

            bool agreed = result == null
                || string.Equals(result.NextActionPreference, "end_accept", StringComparison.OrdinalIgnoreCase)
                || result.WantsToEnd;
            onComplete?.Invoke(agreed);
        }

        private IEnumerator RunBroadcast()
        {
            DialogueTurnResult result = null;
            yield return RequestTurn(
                Initiator.Npc,
                null,
                "Broadcast: say one concise line loudly enough for nearby people to hear. Hearing it does not force anyone to answer.",
                value => result = value);

            if (!IsActive)
            {
                yield break;
            }

            if (result != null && !string.IsNullOrWhiteSpace(result.Text))
            {
                AddLine(Initiator.Npc, result.Text);
            }
        }

        private IEnumerator RunSelfTalk(string overrideReason = null)
        {
            DialogueTurnResult result = null;
            yield return RequestTurn(
                Initiator.Npc,
                null,
                overrideReason ?? "Self-talk bubble: one short line; it does not change the current movement or task.",
                value => result = value);

            if (!IsActive)
            {
                yield break;
            }

            if (result != null && !string.IsNullOrWhiteSpace(result.Text))
            {
                AddLine(Initiator.Npc, result.Text);
            }
        }

        private IEnumerator RequestTurn(
            NpcRuntimeState speaker,
            NpcRuntimeState listener,
            string instruction,
            Action<DialogueTurnResult> onComplete)
        {
            if (speaker == null || brainProvider == null || clock == null)
            {
                onComplete?.Invoke(null);
                yield break;
            }

            NpcAiRequest request = BuildRequest(speaker, listener, instruction);
            bool finished = false;
            NpcAiDecision decision = null;
            NpcAiSecondaryEventResolver.RequestDecision(
                brainProvider,
                request,
                secondaryEventSystem,
                maxSecondaryEventLookupResults,
                value =>
                {
                    decision = value;
                    ApplyPendingEncounterChanges(speaker, decision);
                    ApplySocialPlanChanges(speaker, decision);
                    CapturePostConversationOutcome(speaker, decision);
                    finished = true;
                },
                _ => finished = true);

            while (!finished)
            {
                yield return null;
            }

            onComplete?.Invoke(ToTurnResult(decision));
        }

        private static DialogueTurnResult CreateFallbackInvitationResponse()
        {
            return new DialogueTurnResult
            {
                Text = "\u6211\u542C\u5230\u4E86\u3002\u60C5\u51B5\u8FD8\u4E0D\u786E\u5B9A\uFF0C\u6211\u5148\u6309\u73B0\u5728\u77E5\u9053\u7684\u6765\u5224\u65AD\u3002",
                AcceptedInvitation = true,
                NextActionPreference = "end_request",
                WantsToEnd = true
            };
        }

        private NpcAiRequest BuildRequest(NpcRuntimeState speaker, NpcRuntimeState listener, string instruction)
        {
            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            GameTime time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            NpcAiRequest request = NpcAiRequest.FromRuntimeState(speaker, date, time);
            string speakerId = GetNpcId(speaker);
            string listenerId = GetNpcId(listener);
            string relationship = listener != null && relationshipSystem != null
                ? relationshipSystem.GetSummary(speakerId, listenerId)
                : "(none)";
            string roleContext = BuildDialogueRoleContext(speaker, listener);
            string recent = memorySystem != null
                ? memorySystem.BuildRecentSummaryWithoutDialogueChatter(speakerId, 6)
                : string.Empty;
            string facts = memorySystem != null
                ? memorySystem.BuildRecentFactSummary(speakerId, 12)
                : string.Empty;
            NpcPerceptionSensor speakerSensor = speaker != null ? speaker.GetComponent<NpcPerceptionSensor>() : null;

            request.currentEmotion = GetEmotion(speaker);
            request.currentLocationTaskSummary = speaker != null && speaker.ActualLocation != null
                ? speaker.ActualLocation.BuildTaskTemplateSummary()
                : "(none)";
            request.currentNpcInteractionTemplateSummary = speaker != null && speaker.Profile != null
                ? speaker.Profile.BuildInteractionTemplateSummary()
                : "(none)";
            request.currentWorldEventTemplateSummary = "(none)";
            request.recentMemorySummary = recent;
            request.factSummary = facts;
            request.perceptionSummary = speakerSensor != null ? speakerSensor.BuildObservationSummary() : string.Empty;
            NpcActionExecutor speakerExecutor = speaker != null ? speaker.GetComponent<NpcActionExecutor>() : null;
            request.rollingGoalSummary = speakerExecutor != null
                ? speakerExecutor.BuildRollingGoalSummary()
                : "(none)";
            request.pendingEncounterSummary = pendingEncounterSystem != null
                ? pendingEncounterSystem.BuildSummaryForNpc(speakerId)
                : "(none)";
            request.socialPlanSummary = socialPlanSystem != null
                ? socialPlanSystem.BuildPlanSummaryForNpc(speakerId)
                : "(none)";
            request.playerRelationshipSummary = speaker != null && speaker.Profile != null && relationshipSystem != null
                ? relationshipSystem.GetPlayerSummary(speaker.Profile)
                : string.Empty;
            request.secondaryEventLookupAvailable = secondaryEventSystem != null;
            request.secondaryEventAccessSummary = secondaryEventSystem != null
                ? secondaryEventSystem.BuildAccessSummaryForNpc(speakerId)
                : string.Empty;
            request.allowedActorSummary = BuildAllActorSummary(speaker);

            request.allowedLocationSummary = BuildAllowedLocationSummary();
            request.observedEventSummary =
                $"Dialogue mode={Proposal.Kind}. Topic={Proposal.Topic}. Reason={Proposal.Reason}. " +
                $"Role context:\n{roleContext}\n" +
                $"Speaker={GetDisplayName(speaker)}. Listener={(listener != null ? GetDisplayName(listener) : "(none)")}. " +
                $"Participants={BuildGroupParticipantSummary()}. " +
                $"Relationship={relationship}. Recent visible dialogue:\n{BuildLineSummary()}\n" +
                instruction + " " +
                "Perception facts belong to the exact listed entity ids; listener health/energy/appearance is about the listener. " +
                "Third-party condition claims need current perception or direct memory; otherwise express uncertainty or the need to verify. " +
                "Dialogue turn fields: top-level TalkToNpc for interpersonal dialogue or SelfTalk for self-talk, eventKind=None, no top-level movement target, timingMode=Immediate, delayMinutes=0, scheduled time=-1. " +
                "If this spoken turn creates an immediate concrete action after the conversation closes, put it in postConversationAction; otherwise set postConversationAction.hasAction=false. " +
                "Future or delayed shared commitments belong in socialPlanChanges, not postConversationAction. " +
                "nextActionPreference options include continue_conversation, listen, silent, no_comment, reject_conversation, end_request, end_accept, and self_talk_done. " +
                "When this dialogue creates, accepts, refuses, cancels, or completes a shared appointment, meal, visit, or gathering, update socialPlanChanges with the exact planId when activityKey is present. " +
                "Group dialogue allows quiet listening with empty dialogue. nextSpeakerId names a current participant who was addressed, has key information, is responsible, or is emotionally likely to respond.";
            return request;
        }

        private void ApplySocialPlanInvitationResult(bool accepted, string responseText)
        {
            string planId = Proposal.Context != null ? Proposal.Context.ActivityKey : string.Empty;
            if (string.IsNullOrWhiteSpace(planId) || socialPlanSystem == null)
            {
                return;
            }

            string initiatorId = GetNpcId(Initiator.Npc);
            string targetId = GetNpcId(Target.Npc);
            if (!string.IsNullOrWhiteSpace(initiatorId))
            {
                socialPlanSystem.MarkParticipantAccepted(planId, initiatorId, "social plan invitation was delivered");
            }

            if (string.IsNullOrWhiteSpace(targetId))
            {
                return;
            }

            if (accepted)
            {
                socialPlanSystem.MarkParticipantAccepted(planId, targetId, responseText);
            }
            else
            {
                socialPlanSystem.MarkParticipantDeclined(planId, targetId, responseText);
            }
        }

        private string BuildDialogueRoleContext(NpcRuntimeState speaker, NpcRuntimeState listener)
        {
            DialogueContextInfo context = Proposal.Context;
            if (context == null || !context.HasAnyData)
            {
                context = new DialogueContextInfo(
                    Proposal.Kind.ToString(),
                    GetNpcId(speaker),
                    GetNpcId(listener),
                    string.Empty,
                    string.Empty,
                    Proposal.Reason);
            }

            return context.BuildPromptSummary(speaker, listener, Proposal);
        }

        private void ApplyPendingEncounterChanges(NpcRuntimeState speaker, NpcAiDecision decision)
        {
            if (pendingEncounterSystem == null || speaker == null || speaker.Profile == null || decision == null)
            {
                return;
            }

            if (HasAnyPendingEncounterChange(decision))
            {
                HasPendingEncounterChanges = true;
            }

            pendingEncounterSystem.ApplyDecision(speaker.Profile, decision);
        }

        private void ApplySocialPlanChanges(NpcRuntimeState speaker, NpcAiDecision decision)
        {
            if (socialPlanSystem == null || speaker == null || speaker.Profile == null || decision == null)
            {
                return;
            }

            if (HasAnySocialPlanChange(decision))
            {
                HasSocialPlanChanges = true;
            }

            socialPlanSystem.ApplyDecision(
                speaker,
                decision,
                $"{Proposal.Topic} {Proposal.Reason} {TranscriptSummary}",
                CollectCurrentParticipants());
        }

        private void CapturePostConversationOutcome(NpcRuntimeState speaker, NpcAiDecision decision)
        {
            if (speaker == null || decision == null || decision.postConversationAction == null || !decision.postConversationAction.hasAction)
            {
                return;
            }

            DialogueContextInfo context = Proposal.Context;
            string contextKind = FirstNonEmpty(decision.dialogueContextKind, context != null ? context.ContextKind : string.Empty, "npc_dialogue");
            string sourceActorId = FirstNonEmpty(decision.dialogueSourceActorId, context != null ? context.SourceActorId : string.Empty, GetNpcId(speaker));
            string subjectActorId = FirstNonEmpty(decision.dialogueSubjectActorId, context != null ? context.SubjectActorId : string.Empty);
            string subjectLocationId = FirstNonEmpty(decision.dialogueSubjectLocationId, context != null ? context.SubjectLocationId : string.Empty);
            string sourceText = FirstNonEmpty(decision.dialogueSourceText, BuildTranscriptWithCurrentLine(speaker, decision.GetPrimaryDialogue()));

            if (PostConversationActionResolver.TryBuildDecision(
                    speaker,
                    decision,
                    decision.postConversationAction,
                    contextKind,
                    sourceActorId,
                    subjectActorId,
                    subjectLocationId,
                    sourceText,
                    out NpcAiDecision executableDecision,
                    out _))
            {
                postConversationDecisionsByNpc[speaker] = executableDecision;
            }
        }

        private static bool HasAnyPendingEncounterChange(NpcAiDecision decision)
        {
            if (decision == null || decision.pendingEncounterChanges == null)
            {
                return false;
            }

            for (int i = 0; i < decision.pendingEncounterChanges.Length; i++)
            {
                NpcPendingEncounterChange change = decision.pendingEncounterChanges[i];
                if (change != null && !string.IsNullOrWhiteSpace(change.operation))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasAnySocialPlanChange(NpcAiDecision decision)
        {
            if (decision == null || decision.socialPlanChanges == null)
            {
                return false;
            }

            for (int i = 0; i < decision.socialPlanChanges.Length; i++)
            {
                NpcSocialPlanChange change = decision.socialPlanChanges[i];
                if (change != null && !string.IsNullOrWhiteSpace(change.operation))
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildTranscriptWithCurrentLine(NpcRuntimeState speaker, string currentLine)
        {
            string transcript = BuildLineSummary();
            if (speaker == null || speaker.Profile == null || string.IsNullOrWhiteSpace(currentLine))
            {
                return transcript;
            }

            if (string.Equals(transcript, "(none)", StringComparison.OrdinalIgnoreCase))
            {
                transcript = string.Empty;
            }

            if (!string.IsNullOrWhiteSpace(transcript))
            {
                transcript += "\n";
            }

            return transcript + speaker.Profile.DisplayName + ": " + currentLine;
        }

        private static string FirstNonEmpty(params string[] values)
        {
            if (values == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value.Trim();
                }
            }

            return string.Empty;
        }

        private IReadOnlyList<NpcRuntimeState> CollectCurrentParticipants()
        {
            List<NpcRuntimeState> npcs = new List<NpcRuntimeState>();
            AddParticipant(npcs, Initiator != null ? Initiator.Npc : null);
            AddParticipant(npcs, Target != null ? Target.Npc : null);
            for (int i = 0; i < groupParticipants.Count; i++)
            {
                AddParticipant(npcs, groupParticipants[i] != null ? groupParticipants[i].Npc : null);
            }

            return npcs;
        }

        private static void AddParticipant(List<NpcRuntimeState> npcs, NpcRuntimeState npc)
        {
            if (npc == null || npcs.Contains(npc))
            {
                return;
            }

            npcs.Add(npc);
        }

        private void AddLine(NpcRuntimeState speaker, string text)
        {
            if (speaker == null || speaker.Profile == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            text = SanitizeUnseenThirdPartyConditionClaim(speaker, text);
            DialogueLine line = new DialogueLine(speaker.Profile.NpcId, speaker.Profile.DisplayName, text);
            lines.Add(line);
            LineAdded?.Invoke(this, line);

            if (writeMemories && memorySystem != null)
            {
                WriteLineMemories(speaker, text);
            }
        }

        private string SanitizeUnseenThirdPartyConditionClaim(NpcRuntimeState speaker, string text)
        {
            if (speaker == null || string.IsNullOrWhiteSpace(text) || !LooksLikeDirectConditionClaim(text))
            {
                return text;
            }

            string actorName = FindUnseenThirdPartyMentionedInConversation(speaker, text);
            if (string.IsNullOrWhiteSpace(actorName))
            {
                return text;
            }

            return $"\u6211\u8FD8\u6CA1\u6709\u4EB2\u773C\u770B\u5230{actorName}\uFF0C\u4E0D\u80FD\u786E\u5B9A\u60C5\u51B5\u3002\u5148\u522B\u4E0B\u7ED3\u8BBA\uFF0C\u6211\u4EEC\u5148\u53BB\u786E\u8BA4\u3002";
        }

        private string FindUnseenThirdPartyMentionedInConversation(NpcRuntimeState speaker, string currentText)
        {
            NpcPerceptionSensor sensor = speaker != null ? speaker.GetComponent<NpcPerceptionSensor>() : null;
            if (speaker == null || sensor == null)
            {
                return string.Empty;
            }

            string context = $"{Proposal.Topic} {Proposal.Reason} {BuildLineSummary()} {currentText}";
            NpcRuntimeState[] npcs = UnityEngine.Object.FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcRuntimeState candidate = npcs[i];
                if (candidate == null
                    || candidate == speaker
                    || ContainsParticipant(candidate)
                    || candidate.Profile == null)
                {
                    continue;
                }

                string actorId = candidate.Profile.NpcId;
                string displayName = candidate.Profile.DisplayName;
                if (!ContainsIgnoreCase(context, actorId) && !ContainsIgnoreCase(context, displayName))
                {
                    continue;
                }

                if (sensor.CanCurrentlyPerceive(actorId))
                {
                    continue;
                }

                return string.IsNullOrWhiteSpace(displayName) ? actorId : displayName;
            }

            return string.Empty;
        }

        private static bool LooksLikeDirectConditionClaim(string text)
        {
            return ContainsAny(
                text,
                "looks",
                "appears",
                "seems fine",
                "seems okay",
                "healthy",
                "safe",
                "fine",
                "okay",
                "health",
                "energy",
                "\u770B\u8D77\u6765",
                "\u770B\u7740",
                "\u6C14\u8272",
                "\u72B6\u6001",
                "\u6CA1\u4E8B",
                "\u6CA1\u4EC0\u4E48\u4E8B",
                "\u6CA1\u95EE\u9898",
                "\u8FD8\u884C",
                "\u4E0D\u9519",
                "\u5065\u5EB7",
                "\u7CBE\u795E",
                "\u5B89\u5168");
        }

        private void WriteLineMemories(NpcRuntimeState speaker, string text)
        {
            if (speaker == null || speaker.Profile == null || memorySystem == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            memorySystem.AddMemory(speaker.Profile.NpcId, $"I said during conversation: {text}", "dialogue_said", 4);

            if (Proposal.Kind == DialogueProposalKind.Group)
            {
                memorySystem.AddFact(
                    speaker.Profile.NpcId,
                    speaker.Profile.NpcId,
                    speaker.Profile.NpcId,
                    speaker.Profile.DisplayName,
                    $"I said in group conversation: {text}",
                    "dialogue_said",
                    4);
                WriteGroupHeardMemories(speaker, text);
                return;
            }

            NpcRuntimeState listener = GetDirectListener(speaker);
            if (listener == null || listener.Profile == null)
            {
                return;
            }

            memorySystem.AddMemory(
                listener.Profile.NpcId,
                $"{speaker.Profile.DisplayName} said to me during conversation: {text}",
                "dialogue_heard",
                5);
            memorySystem.AddFact(
                speaker.Profile.NpcId,
                listener.Profile.NpcId,
                speaker.Profile.NpcId,
                speaker.Profile.DisplayName,
                $"I said to {listener.Profile.DisplayName}: {text}",
                "dialogue_said",
                4);
            memorySystem.AddFact(
                listener.Profile.NpcId,
                speaker.Profile.NpcId,
                speaker.Profile.NpcId,
                speaker.Profile.DisplayName,
                $"{speaker.Profile.DisplayName} said to me during conversation: {text}",
                "dialogue_heard",
                6);
        }

        private NpcRuntimeState GetDirectListener(NpcRuntimeState speaker)
        {
            if (speaker == null || Proposal.Kind != DialogueProposalKind.OneOnOne)
            {
                return null;
            }

            if (speaker == Initiator.Npc)
            {
                return Target.Npc;
            }

            if (speaker == Target.Npc)
            {
                return Initiator.Npc;
            }

            return null;
        }

        private void WriteGroupHeardMemories(NpcRuntimeState speaker, string text)
        {
            if (speaker == null || speaker.Profile == null || memorySystem == null)
            {
                return;
            }

            for (int i = 0; i < groupParticipants.Count; i++)
            {
                NpcRuntimeState listener = groupParticipants[i].Npc;
                if (listener == null || listener == speaker || listener.Profile == null)
                {
                    continue;
                }

                memorySystem.AddMemory(
                    listener.Profile.NpcId,
                    $"{speaker.Profile.DisplayName} said in group conversation: {text}",
                    "group_dialogue_heard",
                    5);
                memorySystem.AddFact(
                    listener.Profile.NpcId,
                    speaker.Profile.NpcId,
                    speaker.Profile.NpcId,
                    speaker.Profile.DisplayName,
                    $"{speaker.Profile.DisplayName} said in group conversation: {text}",
                    "group_dialogue_heard",
                    6);
            }
        }

        private void EndSession()
        {
            if (!IsActive)
            {
                return;
            }

            IsActive = false;
            routine = null;
            RestoreAllParticipants();

            Ended?.Invoke(this);
            endedCallback?.Invoke(this);
        }

        public bool Contains(NpcRuntimeState npc)
        {
            return ContainsParticipant(npc);
        }

        public ConversationParticipant GetParticipant(int index)
        {
            if (index < 0)
            {
                return null;
            }

            if (Proposal.Kind == DialogueProposalKind.Group)
            {
                return index < groupParticipants.Count ? groupParticipants[index] : null;
            }

            if (index == 0)
            {
                return Initiator;
            }

            if (index == 1 && Target != null && Target.Npc != null)
            {
                return Target;
            }

            return null;
        }

        public NpcRuntimeState FindParticipantById(string speakerId)
        {
            if (string.IsNullOrWhiteSpace(speakerId))
            {
                return null;
            }

            for (int i = 0; i < ParticipantCount; i++)
            {
                ConversationParticipant participant = GetParticipant(i);
                NpcRuntimeState npc = participant != null ? participant.Npc : null;
                if (npc != null
                    && npc.Profile != null
                    && string.Equals(npc.Profile.NpcId, speakerId, StringComparison.OrdinalIgnoreCase))
                {
                    return npc;
                }
            }

            return null;
        }

        public bool HasAnyParticipantId(string[] actorIds)
        {
            if (actorIds == null || actorIds.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < actorIds.Length; i++)
            {
                if (FindParticipantById(actorIds[i]) != null)
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildLineSummary()
        {
            if (lines.Count == 0)
            {
                return "(none)";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            int start = Mathf.Max(0, lines.Count - 6);
            for (int i = start; i < lines.Count; i++)
            {
                DialogueLine line = lines[i];
                builder.Append(line.SpeakerName);
                builder.Append(": ");
                builder.AppendLine(line.Text);
            }

            return builder.ToString();
        }

        private static DialogueTurnResult ToTurnResult(NpcAiDecision decision)
        {
            if (decision == null)
            {
                return null;
            }

            string preference = decision.GetPrimaryNextActionPreference() ?? string.Empty;
            return new DialogueTurnResult
            {
                Text = decision.GetPrimaryDialogue(),
                Emotion = decision.emotion,
                Tone = decision.tone,
                WantsToEnd = string.Equals(preference, "end_request", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(preference, "end_accept", StringComparison.OrdinalIgnoreCase),
                AcceptedInvitation = !string.Equals(preference, "reject_conversation", StringComparison.OrdinalIgnoreCase),
                RelationshipDeltaHint = decision.relationshipDeltaHint,
                NextActionPreference = preference,
                NextSpeakerId = decision.nextSpeakerId
            };
        }

        private DialogueTurnResult SanitizeGroupTurnResult(DialogueTurnResult result, NpcRuntimeState speaker)
        {
            if (result == null || Proposal.Kind != DialogueProposalKind.Group)
            {
                return result;
            }

            int nextIndex = FindGroupParticipantIndex(result.NextSpeakerId);
            if (nextIndex < 0 || groupParticipants[nextIndex].Npc == speaker)
            {
                result.NextSpeakerId = string.Empty;
            }

            return result;
        }

        private static bool IsReject(DialogueTurnResult result)
        {
            if (result == null)
            {
                return false;
            }

            if (string.Equals(result.NextActionPreference, "reject_conversation", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return ContainsAny(
                result.Text,
                "no",
                "not now",
                "can't",
                "cannot",
                "busy",
                "refuse",
                "decline",
                "\u4e0d\u53bb",
                "\u4e0d\u884c",
                "\u4e0d\u80fd",
                "\u6ca1\u7a7a",
                "\u5fd9",
                "\u62d2\u7edd",
                "\u7b97\u4e86");
        }

        private static bool ShouldEndSettledOneOnOneTurn(DialogueTurnResult result)
        {
            if (result == null)
            {
                return true;
            }

            if (result.WantsToEnd)
            {
                return true;
            }

            string preference = result.NextActionPreference ?? string.Empty;
            if (string.Equals(preference, "end_accept", StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "silent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "no_comment", StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "self_talk_done", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return LooksLikePracticalMatterSettled(result.Text);
        }

        private void EnsureTurnCountListSize(List<int> counts)
        {
            while (counts.Count < groupParticipants.Count)
            {
                counts.Add(0);
            }
        }

        private static bool GroupTurnIsSilent(DialogueTurnResult result)
        {
            if (result == null)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                return false;
            }

            string preference = result.NextActionPreference ?? string.Empty;
            return string.IsNullOrWhiteSpace(preference)
                || string.Equals(preference, "listen", StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "silent", StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "no_comment", StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "continue_conversation", StringComparison.OrdinalIgnoreCase)
                || string.Equals(preference, "end_request", StringComparison.OrdinalIgnoreCase);
        }

        private int ResolveNextGroupSpeakerIndex(
            int currentIndex,
            string requestedSpeakerId,
            List<int> speakingOpportunities,
            List<int> spokenLineCounts)
        {
            int requestedIndex = FindGroupParticipantIndex(requestedSpeakerId);
            if (requestedIndex >= 0 && requestedIndex != currentIndex)
            {
                return requestedIndex;
            }

            int neverOfferedIndex = FindLowestCountParticipantIndex(currentIndex, speakingOpportunities, true);
            if (neverOfferedIndex >= 0)
            {
                return neverOfferedIndex;
            }

            int leastSpokenIndex = FindLowestCountParticipantIndex(currentIndex, spokenLineCounts, false);
            if (leastSpokenIndex >= 0)
            {
                return leastSpokenIndex;
            }

            return groupParticipants.Count > 0 ? (currentIndex + 1) % groupParticipants.Count : 0;
        }

        private int FindGroupParticipantIndex(string npcId)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                return -1;
            }

            for (int i = 0; i < groupParticipants.Count; i++)
            {
                NpcRuntimeState npc = groupParticipants[i].Npc;
                if (npc != null
                    && npc.Profile != null
                    && string.Equals(npc.Profile.NpcId, npcId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        private int FindLowestCountParticipantIndex(int currentIndex, List<int> counts, bool requireZero)
        {
            if (counts == null || counts.Count == 0 || groupParticipants.Count == 0)
            {
                return -1;
            }

            int bestIndex = -1;
            int bestCount = int.MaxValue;
            for (int offset = 1; offset <= groupParticipants.Count; offset++)
            {
                int index = (currentIndex + offset) % groupParticipants.Count;
                if (index == currentIndex || groupParticipants[index].Npc == null)
                {
                    continue;
                }

                int count = index < counts.Count ? counts[index] : 0;
                if (requireZero && count != 0)
                {
                    continue;
                }

                if (count < bestCount)
                {
                    bestCount = count;
                    bestIndex = index;
                }
            }

            return bestIndex;
        }

        private bool ShouldNaturallyEndGroupConversation(DialogueTurnResult result, List<int> spokenLineCounts)
        {
            if (result == null || Proposal.Kind != DialogueProposalKind.Group || groupParticipants.Count < 2)
            {
                return false;
            }

            if (!EveryCurrentGroupParticipantHasSpoken(spokenLineCounts))
            {
                return false;
            }

            if (LooksLikeQuestion(result.Text))
            {
                return false;
            }

            bool activityConversation = !string.IsNullOrWhiteSpace(Proposal.ActivityKey);
            if (activityConversation)
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(result.NextSpeakerId))
            {
                return false;
            }

            return !string.Equals(result.NextActionPreference, "continue_conversation", StringComparison.OrdinalIgnoreCase);
        }

        private bool EveryCurrentGroupParticipantHasSpoken(List<int> spokenLineCounts)
        {
            if (spokenLineCounts == null || groupParticipants.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < groupParticipants.Count; i++)
            {
                if (groupParticipants[i].Npc == null)
                {
                    continue;
                }

                if (i >= spokenLineCounts.Count || spokenLineCounts[i] <= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool LooksLikeQuestion(string text)
        {
            return !string.IsNullOrWhiteSpace(text)
                && (text.Contains("?")
                    || text.Contains("\uFF1F")
                    || text.Contains("\u5417")
                    || text.Contains("\u5462")
                    || text.Contains("\u600E\u4E48")
                    || text.Contains("\u4EC0\u4E48")
                    || text.Contains("\u4E3A\u4EC0\u4E48"));
        }

        private static bool LooksLikePracticalMatterSettled(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || LooksLikeQuestion(text))
            {
                return false;
            }

            string value = text.ToLowerInvariant();
            return ContainsAny(
                value,
                "ok",
                "okay",
                "sure",
                "agreed",
                "sounds good",
                "no problem",
                "i will",
                "wait",
                "soon",
                "later",
                "ready",
                "coming",
                "let's",
                "see you",
                "\u597D",
                "\u884C",
                "\u53EF\u4EE5",
                "\u77E5\u9053",
                "\u660E\u767D",
                "\u6CA1\u95EE\u9898",
                "\u7B49",
                "\u4E00\u4F1A",
                "\u5F85\u4F1A",
                "\u9A6C\u4E0A",
                "\u8FC7\u53BB",
                "\u51C6\u5907",
                "\u6536\u62FE",
                "\u5FD9\u5B8C",
                "\u4E00\u8D77",
                "\u5403\u65E9\u996D",
                "\u5C31\u8FD9\u6837");
        }

        private static bool ContainsAny(string text, params string[] needles)
        {
            if (string.IsNullOrWhiteSpace(text) || needles == null)
            {
                return false;
            }

            for (int i = 0; i < needles.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(needles[i])
                    && text.Contains(needles[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsIgnoreCase(string text, string value)
        {
            return !string.IsNullOrWhiteSpace(text)
                && !string.IsNullOrWhiteSpace(value)
                && text.IndexOf(value, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetNpcId(NpcRuntimeState npc)
        {
            return npc != null && npc.Profile != null && !string.IsNullOrWhiteSpace(npc.Profile.NpcId)
                ? npc.Profile.NpcId
                : string.Empty;
        }

        private static string GetDisplayName(NpcRuntimeState npc)
        {
            return npc != null && npc.Profile != null ? npc.Profile.DisplayName : "Unknown NPC";
        }

        private static string GetEmotion(NpcRuntimeState npc)
        {
            Behavior.NpcBehaviorState state = npc != null ? npc.GetComponent<Behavior.NpcBehaviorState>() : null;
            return state != null ? state.Emotion : "neutral";
        }

        private bool ContainsParticipant(NpcRuntimeState npc)
        {
            if (npc == null)
            {
                return false;
            }

            if (Initiator != null && Initiator.Npc == npc)
            {
                return true;
            }

            if (Target != null && Target.Npc == npc)
            {
                return true;
            }

            for (int i = 0; i < groupParticipants.Count; i++)
            {
                if (groupParticipants[i].Npc == npc)
                {
                    return true;
                }
            }

            return false;
        }

        private void AddGroupParticipant(NpcRuntimeState npc)
        {
            if (npc == null || ContainsGroupParticipant(npc))
            {
                return;
            }

            groupParticipants.Add(new ConversationParticipant(npc));
        }

        private bool ContainsGroupParticipant(NpcRuntimeState npc)
        {
            if (npc == null)
            {
                return false;
            }

            for (int i = 0; i < groupParticipants.Count; i++)
            {
                if (groupParticipants[i].Npc == npc)
                {
                    return true;
                }
            }

            return false;
        }

        private void StopGroupAndFaceCenter()
        {
            Vector3 center = Vector3.zero;
            int count = 0;
            for (int i = 0; i < groupParticipants.Count; i++)
            {
                NpcRuntimeState npc = groupParticipants[i].Npc;
                if (npc == null)
                {
                    continue;
                }

                center += npc.transform.position;
                count++;
            }

            if (count > 0)
            {
                center /= count;
            }

            for (int i = 0; i < groupParticipants.Count; i++)
            {
                groupParticipants[i].StopAndFace(null);
                if (groupParticipants[i].Npc != null)
                {
                    groupParticipants[i].Movement?.Face(center);
                }
            }
        }

        private void RestoreAllParticipants()
        {
            if (Proposal.Kind != DialogueProposalKind.Group)
            {
                Initiator.RestoreMovement();
                Target.RestoreMovement();
                return;
            }

            for (int i = 0; i < groupParticipants.Count; i++)
            {
                groupParticipants[i].RestoreMovement();
            }
        }

        private NpcRuntimeState FindPrimaryGroupListener(NpcRuntimeState speaker)
        {
            for (int i = 0; i < groupParticipants.Count; i++)
            {
                NpcRuntimeState candidate = groupParticipants[i].Npc;
                if (candidate != null && candidate != speaker)
                {
                    return candidate;
                }
            }

            return null;
        }

        private string BuildGroupActorSummary(NpcRuntimeState speaker)
        {
            if (groupParticipants.Count == 0)
            {
                return "(none)";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < groupParticipants.Count; i++)
            {
                NpcRuntimeState npc = groupParticipants[i].Npc;
                if (npc == null || npc == speaker || npc.Profile == null)
                {
                    continue;
                }

                builder.Append("- id=");
                builder.Append(npc.Profile.NpcId);
                builder.Append(", name=");
                builder.Append(npc.Profile.DisplayName);
                builder.Append(", type=NPC");
                builder.AppendLine();
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private static string BuildAllActorSummary(NpcRuntimeState speaker)
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            PlayerMovementController player = UnityEngine.Object.FindFirstObjectByType<PlayerMovementController>();
            if (player != null)
            {
                builder.AppendLine("- id=player, name=Player, type=Player");
            }

            NpcRuntimeState[] npcs = UnityEngine.Object.FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcRuntimeState npc = npcs[i];
                if (npc == null || npc == speaker || npc.Profile == null || string.IsNullOrWhiteSpace(npc.Profile.NpcId))
                {
                    continue;
                }

                builder.Append("- id=");
                builder.Append(npc.Profile.NpcId);
                builder.Append(", name=");
                builder.Append(npc.Profile.DisplayName);
                builder.Append(", role=");
                builder.Append(npc.Profile.Role);
                builder.AppendLine();
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private static string BuildAllowedLocationSummary()
        {
            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            LocationMarker[] markers = UnityEngine.Object.FindObjectsByType<LocationMarker>(FindObjectsSortMode.None);
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

                if (!string.IsNullOrWhiteSpace(definition.AreaId))
                {
                    builder.Append(", areaId=");
                    builder.Append(definition.AreaId);
                }

                builder.Append(", open=");
                builder.Append(definition.AlwaysOpen ? "always" : $"{definition.OpenHour:00}:00-{definition.CloseHour:00}:00");

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

        private string BuildGroupParticipantSummary()
        {
            if (Proposal.Kind != DialogueProposalKind.Group || groupParticipants.Count == 0)
            {
                return "(none)";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < groupParticipants.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(groupParticipants[i].DisplayName);
            }

            return builder.ToString();
        }
    }
}
