using System;
using System.Collections.Generic;
using CityStateSim.AI;
using CityStateSim.Behavior;
using CityStateSim.Locations;
using CityStateSim.Memory;
using CityStateSim.Movement;
using CityStateSim.NPC;
using CityStateSim.Relationships;
using CityStateSim.Tasks;
using UnityEngine;

namespace CityStateSim.Dialogue
{
    public sealed class DialogueController : MonoBehaviour
    {
        private const string PlayerDialoguePauseReason = "player_dialogue";
        private const string QueuedReplyPauseReason = "queued_dialogue_reply";

        [SerializeField] private RelationshipSystem relationshipSystem;
        [SerializeField] private MemorySystem memorySystem;
        [SerializeField] private string playerActorId = "player";
        [SerializeField] private bool applyRelationshipHints = true;
        [SerializeField] private bool writeMemories = true;
        [SerializeField] private bool addNpcLineWhenDecisionReceived = true;
        [SerializeField] private bool stopActorsWhileTalking = true;
        [SerializeField] private bool showLateRepliesAsBubble = true;
        [SerializeField] private bool logPostConversationDecisions = true;

        private NpcRuntimeState currentNpc;
        private NpcBehaviorController currentBehaviorController;
        private NpcMovementAgent currentNpcMovement;
        private PlayerMovementController currentPlayerMovement;
        private MessageDisplayer messageDisplayer;
        private DialogueHistorySystem dialogueHistorySystem;
        private PlayerDialogueRequestSystem playerDialogueRequestSystem;
        private readonly List<DialogueLine> activeConversationTranscript = new List<DialogueLine>();
        private string lastPlayerLine;
        private string lastNpcLine;
        private bool waitingForConversationReply;
        private bool replyRequestQueued;
        private Coroutine pendingReplyRetryCoroutine;
        private NpcRuntimeState pendingLateReplyNpc;
        private NpcBehaviorController pendingLateReplyController;
        private NpcBehaviorController pendingPostConversationActionController;
        private NpcAiDecision pendingPostConversationAction;
        private bool warnedMockBrainProvider;

        public NpcRuntimeState CurrentNpc => currentNpc;
        public bool IsConversationActive => currentNpc != null;
        public bool IsWaitingForConversationReply => waitingForConversationReply || replyRequestQueued;

        public event Action<NpcRuntimeState> ConversationStarted;
        public event Action<DialogueLine> LineAdded;
        public event Action<NpcRuntimeState> ConversationEnded;

        private void Awake()
        {
            if (relationshipSystem == null)
            {
                relationshipSystem = FindFirstObjectByType<RelationshipSystem>();
            }

            if (memorySystem == null)
            {
                memorySystem = FindFirstObjectByType<MemorySystem>();
            }

            if (messageDisplayer == null)
            {
                messageDisplayer = FindFirstObjectByType<MessageDisplayer>();
            }

            dialogueHistorySystem = FindFirstObjectByType<DialogueHistorySystem>();
            playerDialogueRequestSystem = FindFirstObjectByType<PlayerDialogueRequestSystem>();
        }

        public void StartConversation(NpcRuntimeState npc)
        {
            TryStartConversation(npc, null);
        }

        public void StartConversation(NpcRuntimeState npc, GameObject playerActor)
        {
            TryStartConversation(npc, playerActor);
        }

        public bool TryStartConversation(NpcRuntimeState npc, GameObject playerActor)
        {
            if (global::DayOverCheck.IsUserInputLocked)
            {
                return false;
            }

            if (npc == null || currentNpc != null)
            {
                return false;
            }

            currentNpc = npc;
            currentBehaviorController = npc != null ? npc.GetComponent<NpcBehaviorController>() : null;
            currentNpcMovement = npc != null ? npc.GetComponent<NpcMovementAgent>() : null;
            currentPlayerMovement = playerActor != null
                ? playerActor.GetComponent<PlayerMovementController>()
                : FindFirstObjectByType<PlayerMovementController>();
            lastPlayerLine = string.Empty;
            lastNpcLine = string.Empty;
            ClearActiveConversationTranscript();
            ClearPendingPostConversationAction();
            bool alreadyWaitingForThisNpc = pendingLateReplyController != null && pendingLateReplyController == currentBehaviorController;
            if (alreadyWaitingForThisNpc)
            {
                pendingLateReplyController = null;
                pendingLateReplyNpc = null;
            }

            StopConversationActors();
            currentBehaviorController?.RefreshContextFromSystems();
            ConversationStarted?.Invoke(npc);
            return true;
        }

        public bool TryStartConversationWithOpeningLine(NpcRuntimeState npc, GameObject playerActor, string openingLine)
        {
            if (!TryStartConversation(npc, playerActor))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(openingLine))
            {
                AddNpcLine(npc, openingLine);
            }

            return true;
        }

        public void SubmitPlayerLine(string text)
        {
            if (global::DayOverCheck.IsUserInputLocked)
            {
                return;
            }

            if (currentNpc == null || currentNpc.Profile == null || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            string trimmedText = text.Trim();
            DialogueLine playerLine = new DialogueLine(playerActorId, "Player", trimmedText);
            RecordDisplayedLine(playerLine);
            LineAdded?.Invoke(playerLine);
            lastPlayerLine = trimmedText;
            StopConversationActors();
            if (writeMemories)
            {
                memorySystem?.AddMemory(currentNpc.Profile.NpcId, $"Player said: {trimmedText}", "dialogue", 5);
                memorySystem?.AddPlayerDialogueLine(currentNpc.Profile.NpcId, playerActorId, "Player", trimmedText);
                memorySystem?.AddFact(
                    currentNpc.Profile.NpcId,
                    playerActorId,
                    playerActorId,
                    "Player",
                    $"Player said to me: {trimmedText}",
                    "player_dialogue",
                    6);
            }

            RecordActiveConversationLine(playerActorId, "Player", trimmedText);

            if (currentBehaviorController != null)
            {
                TryRequestCurrentNpcReply(BuildPlayerDialogueObservedEvent($"Player said: {trimmedText}"));
            }
        }

        public bool TryRequestCurrentNpcReply(string observedEventSummary)
        {
            if (currentNpc == null || currentBehaviorController == null)
            {
                return false;
            }

            if (waitingForConversationReply || replyRequestQueued)
            {
                return false;
            }

            if (currentBehaviorController.RequestInFlight)
            {
                SetNpcQueuedReplyPaused(currentNpc, true);
                ScheduleReplyRequestWhenIdle(currentNpc, currentBehaviorController, observedEventSummary);
                return false;
            }

            NpcRuntimeState requestedNpc = currentNpc;
            NpcBehaviorController requestedController = currentBehaviorController;
            WarnIfUsingMockBrainProvider(requestedController);
            waitingForConversationReply = true;
            bool requested = requestedController.TryRequestPreviewDecisionIgnoringNpcConversation(
                observedEventSummary,
                decision => HandleConversationReplyDecision(requestedNpc, requestedController, decision),
                error => HandleConversationReplyFailed(requestedNpc, requestedController, error));

            if (!requested)
            {
                waitingForConversationReply = false;
            }

            return requested;
        }

        private void AddNpcLine(NpcRuntimeState npc, string line)
        {
            if (npc == null || npc.Profile == null || string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            DialogueLine dialogueLine = new DialogueLine(npc.Profile.NpcId, npc.Profile.DisplayName, line);
            RecordDisplayedLine(dialogueLine);
            LineAdded?.Invoke(dialogueLine);
            lastNpcLine = line;
            RecordActiveConversationLine(dialogueLine.SpeakerId, dialogueLine.SpeakerName, dialogueLine.Text);
            if (writeMemories)
            {
                memorySystem?.AddMemory(npc.Profile.NpcId, $"I replied to the player: {line}", "dialogue", 5);
                memorySystem?.AddPlayerDialogueLine(npc.Profile.NpcId, npc.Profile.NpcId, npc.Profile.DisplayName, line);
                memorySystem?.AddFact(
                    npc.Profile.NpcId,
                    playerActorId,
                    npc.Profile.NpcId,
                    npc.Profile.DisplayName,
                    $"I replied to the player: {line}",
                    "player_dialogue_reply",
                    4);
            }

            if (applyRelationshipHints)
            {
                ApplySmallTalkRelationshipDelta(npc.Profile);
            }
        }

        public void EndConversation()
        {
            EndConversation(true);
        }

        private void EndConversation(bool requestPostConversationDecision)
        {
            NpcRuntimeState npc = currentNpc;
            NpcBehaviorController behaviorController = currentBehaviorController;
            bool hasDisplayedConversationContent = !string.IsNullOrWhiteSpace(lastPlayerLine) || !string.IsNullOrWhiteSpace(lastNpcLine);
            string eventSummary = BuildConversationEndedEventSummary();
            bool keepWaitingForLateReply = showLateRepliesAsBubble
                && (waitingForConversationReply || replyRequestQueued)
                && behaviorController != null;
            if (!keepWaitingForLateReply)
            {
                waitingForConversationReply = false;
                replyRequestQueued = false;
                SetNpcQueuedReplyPaused(npc, false);
                pendingLateReplyController = null;
                pendingLateReplyNpc = null;
            }
            else
            {
                pendingLateReplyController = behaviorController;
                pendingLateReplyNpc = npc;
            }

            if (!keepWaitingForLateReply && pendingReplyRetryCoroutine != null)
            {
                StopCoroutine(pendingReplyRetryCoroutine);
                pendingReplyRetryCoroutine = null;
            }

            if (currentPlayerMovement != null)
            {
                currentPlayerMovement.SetCanMove(true);
            }

            if (currentNpcMovement != null)
            {
                currentNpcMovement.SetPause(PlayerDialoguePauseReason, false);
            }

            if (!keepWaitingForLateReply)
            {
                ClearPlayerConversationTask(npc);
            }

            currentNpc = null;
            currentBehaviorController = null;
            currentNpcMovement = null;
            currentPlayerMovement = null;
            ConversationEnded?.Invoke(npc);

            if (requestPostConversationDecision && !keepWaitingForLateReply && hasDisplayedConversationContent)
            {
                ApplyOrClearPostConversationAction(behaviorController, eventSummary);
            }

            if (!keepWaitingForLateReply)
            {
                ClearClosedConversationState();
            }
        }

        private void ClearPlayerConversationTask(NpcRuntimeState npc)
        {
            if (npc == null)
            {
                return;
            }

            NpcTaskController taskController = npc.GetComponent<NpcTaskController>();
            NpcTask task = taskController != null ? taskController.CurrentTask : null;
            if (task == null || task.Kind != NpcTaskKind.TalkToActor)
            {
                return;
            }

            if (string.Equals(task.TargetActorId, playerActorId, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(task.TargetActorId))
            {
                taskController.ClearCurrentTask("player conversation ended");
            }
        }

        private void ApplySmallTalkRelationshipDelta(NpcProfile profile)
        {
            relationshipSystem?.ApplyPlayerDelta(profile, 0, 1, 0);
        }

        private void HandleConversationReplyDecision(NpcRuntimeState requestedNpc, NpcBehaviorController requestedController, NpcAiDecision decision)
        {
            if (!waitingForConversationReply)
            {
                return;
            }

            waitingForConversationReply = false;
            NpcRuntimeState replyNpc = currentNpc == requestedNpc ? currentNpc : requestedNpc;
            bool conversationStillOpen = currentNpc == requestedNpc && currentBehaviorController == requestedController;
            bool shouldHandleLateReply = !conversationStillOpen && pendingLateReplyController == requestedController;
            NpcBehaviorState replyState = replyNpc != null ? replyNpc.GetComponent<NpcBehaviorState>() : null;
            replyState?.ApplyDialoguePreview(decision);

            string line = decision != null ? decision.GetPrimaryDialogue() : string.Empty;
            if (addNpcLineWhenDecisionReceived && conversationStillOpen)
            {
                AddNpcLine(replyNpc, line);
            }
            else if (showLateRepliesAsBubble && shouldHandleLateReply)
            {
                ShowLateReply(replyNpc, line);
            }

            CapturePostConversationAction(replyNpc, requestedController, decision);

            string nextActionPreference = decision != null ? decision.GetPrimaryNextActionPreference() : string.Empty;
            if (writeMemories && replyNpc != null && replyNpc.Profile != null && !string.IsNullOrWhiteSpace(nextActionPreference))
            {
                memorySystem?.AddMemory(replyNpc.Profile.NpcId, $"My follow-up preference after this conversation: {nextActionPreference}", "dialogue_plan_intent", 6);
            }

            if (!conversationStillOpen)
            {
                if (shouldHandleLateReply)
                {
                    string finalEventSummary = BuildConversationEndedEventSummary();
                    ClearPlayerConversationTask(replyNpc);
                    ApplyOrClearPostConversationAction(requestedController, finalEventSummary);
                    ClearClosedConversationState();
                }

                pendingLateReplyController = null;
                pendingLateReplyNpc = null;
            }
            else
            {
                pendingLateReplyController = null;
                pendingLateReplyNpc = null;
            }

            // Player dialogue replies are preview decisions, not behavior commands.
            // The player-facing dialogue stays open until an explicit UI action ends it.
        }

        private void HandleConversationReplyFailed(NpcRuntimeState requestedNpc, NpcBehaviorController requestedController, string error)
        {
            bool conversationStillOpen = currentNpc == requestedNpc && currentBehaviorController == requestedController;
            bool shouldHandleLateReply = pendingLateReplyController == requestedController;
            if (conversationStillOpen || shouldHandleLateReply)
            {
                waitingForConversationReply = false;
                replyRequestQueued = false;
                SetNpcQueuedReplyPaused(requestedNpc, false);

                if (shouldHandleLateReply)
                {
                    ClearPlayerConversationTask(requestedNpc);
                    ClearPendingPostConversationAction();
                    ClearClosedConversationState();
                }
                else
                {
                    ClearPendingPostConversationAction();
                }

                pendingLateReplyController = null;
                pendingLateReplyNpc = null;
            }
        }

        private void ShowLateReply(NpcRuntimeState npc, string line)
        {
            if (npc == null || npc.Profile == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            messageDisplayer ??= FindFirstObjectByType<MessageDisplayer>();
            messageDisplayer?.ShowMessage(npc, line);
            DialogueLine dialogueLine = new DialogueLine(npc.Profile.NpcId, npc.Profile.DisplayName, line);
            RecordDisplayedLine(dialogueLine);
            lastNpcLine = line;
            RecordActiveConversationLine(dialogueLine.SpeakerId, dialogueLine.SpeakerName, dialogueLine.Text);
            if (writeMemories)
            {
                memorySystem?.AddMemory(npc.Profile.NpcId, $"I replied after the player closed the conversation: {line}", "dialogue", 5);
                memorySystem?.AddPlayerDialogueLine(npc.Profile.NpcId, npc.Profile.NpcId, npc.Profile.DisplayName, line);
            }
        }

        private static NpcRuntimeState FindNpcForController(NpcBehaviorController controller)
        {
            return controller != null ? controller.GetComponent<NpcRuntimeState>() : null;
        }

        public bool IsConversationWith(NpcRuntimeState npc)
        {
            return currentNpc != null && currentNpc == npc;
        }

        public bool HasPendingReplyFor(NpcRuntimeState npc)
        {
            if (npc == null)
            {
                return false;
            }

            if (currentNpc == npc && (waitingForConversationReply || replyRequestQueued))
            {
                return true;
            }

            if (playerDialogueRequestSystem == null)
            {
                playerDialogueRequestSystem = FindFirstObjectByType<PlayerDialogueRequestSystem>();
            }

            return pendingLateReplyNpc == npc
                || (pendingLateReplyController != null && pendingLateReplyController == npc.GetComponent<NpcBehaviorController>())
                || (playerDialogueRequestSystem != null && playerDialogueRequestSystem.HasPendingRequestFor(npc));
        }

        private void RecordDisplayedLine(DialogueLine line)
        {
            dialogueHistorySystem?.AddDisplayedLine(line);
        }

        private void StopConversationActors()
        {
            if (!stopActorsWhileTalking)
            {
                return;
            }

            currentPlayerMovement?.SetCanMove(false);
            currentNpcMovement?.SetPause(PlayerDialoguePauseReason, true);
        }

        private static void SetNpcQueuedReplyPaused(NpcRuntimeState npc, bool paused)
        {
            NpcMovementAgent movement = npc != null ? npc.GetComponent<NpcMovementAgent>() : null;
            movement?.SetPause(QueuedReplyPauseReason, paused);
        }

        private string BuildPlayerDialogueObservedEvent(string eventText)
        {
            string currentTask = BuildCurrentTaskContext(currentNpc);
            string previousPlayerLine = string.IsNullOrWhiteSpace(lastPlayerLine) ? "(none)" : lastPlayerLine;
            string previousNpcLine = string.IsNullOrWhiteSpace(lastNpcLine) ? "(none)" : lastNpcLine;
            string transcript = BuildActiveConversationTranscriptSummary();
            return
                "PlayerDialogue: The current conversation partner is player. " +
                "The NPC is speaking to the player only, not to any active task target or remembered person. " +
                "If activeTask mentions another actor, treat that actor as background context, not as the listener. " +
                "The following transcript is the immediate conversation history, not just the latest line. " +
                $"Conversation transcript so far:\n{transcript}\n" +
                $"Previous player line: {previousPlayerLine}. " +
                $"Previous NPC line: {previousNpcLine}. " +
                "Gibberish, contradiction, vague requests, or unsupported actions are handled with a brief refusal or clarification. " +
                $"{eventText}. " +
                $"Background current task: {currentTask}";
        }

        private static string BuildCurrentTaskContext(NpcRuntimeState npc)
        {
            NpcTaskController taskController = npc != null ? npc.GetComponent<NpcTaskController>() : null;
            NpcTask task = taskController != null ? taskController.CurrentTask : null;
            if (task == null)
            {
                return "(none)";
            }

            return
                $"kind={task.Kind}, targetActorId={task.TargetActorId}, " +
                $"targetLocationId={(task.TargetLocation != null ? task.TargetLocation.LocationId : string.Empty)}, " +
                $"plannedTargetLocationId={task.PlannedTargetLocationId}, activityKind={task.ActivityKind}, participants={string.Join(",", task.ParticipantActorIds ?? Array.Empty<string>())}, " +
                $"reason={task.Reason}";
        }

        private string BuildConversationEndedEventSummary()
        {
            string playerLine = string.IsNullOrWhiteSpace(lastPlayerLine) ? "(none)" : lastPlayerLine;
            string npcLine = string.IsNullOrWhiteSpace(lastNpcLine) ? "(none)" : lastNpcLine;
            string transcript = BuildActiveConversationTranscriptSummary();
            return
                "Conversation with the player ended. " +
                $"Latest player line: {playerLine}. " +
                $"Latest NPC reply: {npcLine}. " +
                $"Conversation transcript so far:\n{transcript}\n" +
                "This conversation is an external event. Decide whether it is important enough to adjust the remaining schedule today. " +
                "Unimportant conversations can resolve by continuing the current schedule.";
        }

        private void RecordActiveConversationLine(string speakerId, string speakerName, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            activeConversationTranscript.Add(new DialogueLine(speakerId ?? string.Empty, speakerName ?? string.Empty, text));
        }

        private void ClearActiveConversationTranscript()
        {
            activeConversationTranscript.Clear();
        }

        private static string ShortenForSummary(string text, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return string.Empty;
            }

            string normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
            if (normalized.Length <= maxChars)
            {
                return normalized;
            }

            return normalized.Substring(0, Mathf.Max(1, maxChars - 3)) + "...";
        }

        private string BuildActiveConversationTranscriptSummary()
        {
            if (activeConversationTranscript.Count == 0)
            {
                return "(none)";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            for (int i = 0; i < activeConversationTranscript.Count; i++)
            {
                DialogueLine line = activeConversationTranscript[i];
                if (line == null)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(line.SpeakerName);
                builder.Append(": ");
                builder.Append(line.Text);
            }

            return builder.ToString();
        }

        private void CapturePostConversationAction(NpcRuntimeState npc, NpcBehaviorController behaviorController, NpcAiDecision sourceDecision)
        {
            if (npc == null || npc.Profile == null || behaviorController == null)
            {
                ClearPendingPostConversationAction();
                return;
            }

            NpcPostConversationAction action = sourceDecision?.postConversationAction;
            if (action == null || !action.hasAction)
            {
                if (sourceDecision != null && PostConversationActionResolver.IsAllowedIntent(sourceDecision.ParsedIntent))
                {
                    Debug.LogWarning(
                        $"[DialogueController] Ignored top-level executable intent during player dialogue for {npc.Profile.NpcId}: " +
                        $"intent={sourceDecision.intent}. Put after-dialogue behavior in postConversationAction instead.",
                        this);
                }

                ClearPendingPostConversationAction();
                return;
            }

            if (action.ParsedIntent == NpcIntentType.ContinueCurrentAction)
            {
                ClearPendingPostConversationAction();
                return;
            }

            if (action.ParsedIntent == NpcIntentType.AttendActivity)
            {
                ClearPendingPostConversationAction();
                if (logPostConversationDecisions)
                {
                    Debug.LogWarning(
                        $"[DialogueController] Ignored postConversationAction AttendActivity for {npc.Profile.NpcId}. " +
                        "Shared activities, meals, visits, and gatherings must be represented in socialPlanChanges.",
                        this);
                }

                return;
            }

            if (action.ParsedTimingMode != NpcTimingMode.Immediate)
            {
                ClearPendingPostConversationAction();
                if (logPostConversationDecisions)
                {
                    Debug.LogWarning(
                        $"[DialogueController] Ignored delayed postConversationAction for {npc.Profile.NpcId}: " +
                        $"intent={action.intent}, timingMode={action.timingMode}, delayMinutes={action.delayMinutes}, " +
                        $"scheduledStart={action.scheduledStartHour:00}:{action.scheduledStartMinute:00}. " +
                        "Future or delayed agreements must be represented in socialPlanChanges, not postConversationAction.",
                        this);
                }

                return;
            }

            if (!TryBuildPostConversationDecision(npc, sourceDecision, action, out NpcAiDecision executableDecision, out string reason))
            {
                ClearPendingPostConversationAction();
                Debug.LogWarning(
                    $"[DialogueController] Ignored invalid postConversationAction for {npc.Profile.NpcId}: {reason}",
                    this);
                return;
            }

            pendingPostConversationActionController = behaviorController;
            pendingPostConversationAction = executableDecision;
            if (logPostConversationDecisions)
            {
                Debug.Log(
                    $"[DialogueController] Cached postConversationAction for {npc.Profile.NpcId}: " +
                    $"intent={executableDecision.intent}, targetActor={executableDecision.targetActorId}, " +
                    $"targetLocation={executableDecision.targetLocationId}, eventKind={executableDecision.eventKind}, " +
                    $"timingMode={executableDecision.timingMode}, delayMinutes={executableDecision.delayMinutes}, " +
                    $"scheduledStart={FormatScheduledStart(executableDecision)}",
                    this);
            }
        }

        private bool TryBuildPostConversationDecision(
            NpcRuntimeState npc,
            NpcAiDecision sourceDecision,
            NpcPostConversationAction action,
            out NpcAiDecision executableDecision,
            out string reason)
        {
            return PostConversationActionResolver.TryBuildDecision(
                npc,
                sourceDecision,
                action,
                "player_dialogue",
                playerActorId,
                string.Empty,
                string.Empty,
                BuildActiveConversationTranscriptSummary(),
                out executableDecision,
                out reason);
        }

        private static string FormatScheduledStart(NpcAiDecision decision)
        {
            if (decision == null || decision.scheduledStartHour < 0 || decision.scheduledStartMinute < 0)
            {
                return "none";
            }

            return $"{decision.scheduledStartHour:00}:{decision.scheduledStartMinute:00}";
        }

        private void ApplyOrClearPostConversationAction(NpcBehaviorController behaviorController, string eventSummary)
        {
            if (behaviorController == null)
            {
                ClearPendingPostConversationAction();
                return;
            }

            if (pendingPostConversationAction != null && pendingPostConversationActionController == behaviorController)
            {
                ApplyPostConversationDecision(behaviorController, pendingPostConversationAction);
                ClearPendingPostConversationAction();
                return;
            }

            if (logPostConversationDecisions)
            {
                Debug.Log(
                    $"[DialogueController] No valid postConversationAction for {behaviorController.name}; continuing current behavior. event={ShortenForSummary(eventSummary, 96)}",
                    this);
            }

            ClearPendingPostConversationAction();
        }

        private void ClearPendingPostConversationAction()
        {
            pendingPostConversationActionController = null;
            pendingPostConversationAction = null;
        }

        private void ApplyPostConversationDecision(NpcBehaviorController behaviorController, NpcAiDecision decision)
        {
            if (behaviorController == null || decision == null)
            {
                return;
            }

            NpcBehaviorState state = behaviorController.GetComponent<NpcBehaviorState>();
            if (logPostConversationDecisions)
            {
                Debug.Log(
                    $"[DialogueController] Applying post-conversation decision to {behaviorController.name}: " +
                    $"intent={decision.intent}, targetActor={decision.targetActorId}, targetLocation={decision.targetLocationId}, " +
                    $"eventKind={decision.eventKind}, timingMode={decision.timingMode}",
                    this);
            }

            state?.ApplyDecision(decision);
        }

        private void ClearClosedConversationState()
        {
            lastPlayerLine = string.Empty;
            lastNpcLine = string.Empty;
            ClearActiveConversationTranscript();
            ClearPendingPostConversationAction();
        }

        private void ScheduleReplyRequestWhenIdle(NpcRuntimeState npc, NpcBehaviorController behaviorController, string observedEventSummary)
        {
            if (pendingReplyRetryCoroutine != null)
            {
                StopCoroutine(pendingReplyRetryCoroutine);
            }

            replyRequestQueued = true;
            pendingReplyRetryCoroutine = StartCoroutine(RequestReplyWhenIdle(npc, behaviorController, observedEventSummary));
        }

        private void WarnIfUsingMockBrainProvider(NpcBehaviorController controller)
        {
            if (warnedMockBrainProvider || controller == null)
            {
                return;
            }

            if (controller.BrainProvider is CityStateSim.AI.MockNpcBrainProvider)
            {
                warnedMockBrainProvider = true;
                Debug.LogWarning(
                    $"[DialogueController] {controller.name} is using MockNpcBrainProvider, so player dialogue will be placeholder text. " +
                    "Assign OpenAiNpcBrainProvider to this NPC's NpcBehaviorController.brainProvider, or clear the field so it auto-picks OpenAI.",
                    this);
            }
        }

        private System.Collections.IEnumerator RequestReplyWhenIdle(NpcRuntimeState npc, NpcBehaviorController behaviorController, string observedEventSummary)
        {
            const float timeoutSeconds = 15f;
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (behaviorController != null && behaviorController.RequestInFlight && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            pendingReplyRetryCoroutine = null;
            bool conversationStillOpen = currentNpc == npc && currentBehaviorController == behaviorController;
            bool shouldHandleLateReply = pendingLateReplyController == behaviorController;
            if (!conversationStillOpen && !shouldHandleLateReply)
            {
                replyRequestQueued = false;
                SetNpcQueuedReplyPaused(npc, false);
                yield break;
            }

            replyRequestQueued = false;
            WarnIfUsingMockBrainProvider(behaviorController);
            waitingForConversationReply = true;
            bool requested = behaviorController != null
                && behaviorController.TryRequestPreviewDecisionIgnoringNpcConversation(
                    observedEventSummary,
                    decision => HandleConversationReplyDecision(npc, behaviorController, decision),
                    error => HandleConversationReplyFailed(npc, behaviorController, error));

            if (requested)
            {
                SetNpcQueuedReplyPaused(npc, false);
                yield break;
            }

            waitingForConversationReply = false;
            SetNpcQueuedReplyPaused(npc, false);
            if (shouldHandleLateReply)
            {
                pendingLateReplyController = null;
                pendingLateReplyNpc = null;
            }
        }
    }

}
