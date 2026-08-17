using System;
using UnityEngine;

namespace CityStateSim.AI
{
    public sealed class MockNpcBrainProvider : NpcBrainProviderBehaviour
    {
        [SerializeField] private float responseDelaySeconds = 0.1f;

        public override void RequestDecision(NpcAiRequest request, Action<NpcAiDecision> onSuccess, Action<string> onError)
        {
            RequestToken token = BeginTrackedRequest(request);
            StartCoroutine(RespondLater(token, request, onSuccess));
        }

        private System.Collections.IEnumerator RespondLater(RequestToken token, NpcAiRequest request, Action<NpcAiDecision> onSuccess)
        {
            if (responseDelaySeconds > 0f)
            {
                yield return new WaitForSeconds(responseDelaySeconds);
            }

            bool hasEvent = !string.IsNullOrWhiteSpace(request.observedEventSummary);
            bool isOppositeDay = !string.IsNullOrWhiteSpace(request.festivalRuleSummary)
                && request.festivalRuleSummary.ToLowerInvariant().Contains("opposite");
            string portraitName = NpcPortraitCatalog.GetFallbackPortraitName(request.npcId, request.npcName, hasEvent ? "curious" : "neutral");

            NpcAiDecision decision = new NpcAiDecision
            {
                intent = IsPlayerDialogue(request)
                    ? NpcIntentType.TalkToPlayer.ToString()
                    : hasEvent ? NpcIntentType.ReactToEvent.ToString() : NpcIntentType.ContinueCurrentAction.ToString(),
                behaviorMode = isOppositeDay ? NpcBehaviorMode.OppositeDay.ToString() : NpcBehaviorMode.FollowSchedule.ToString(),
                tone = isOppositeDay ? "playfully_inverted" : "in_character",
                dialogue = BuildDialogue(request, hasEvent, isOppositeDay),
                emotion = portraitName,
                nextActionPreference = hasEvent ? "pause_and_observe" : "continue_schedule",
                originalGoal = hasEvent ? "Mock response to the observed event." : string.Empty,
                currentGoal = hasEvent ? "Pause and observe the event." : string.Empty,
                goalStatus = hasEvent ? "active" : "none",
                goalStatusReason = string.Empty,
                nextSpeakerId = string.Empty,
                secondaryEventQuery = string.Empty,
                eventKind = hasEvent ? NpcEventKind.OneShot.ToString() : NpcEventKind.None.ToString(),
                plannedTargetLocationId = string.Empty,
                activityKind = string.Empty,
                participantActorIds = Array.Empty<string>(),
                requiredActorIds = Array.Empty<string>(),
                optionalActorIds = Array.Empty<string>(),
                patienceMinutes = 0,
                timingMode = NpcTimingMode.Immediate.ToString(),
                socialPlanChanges = Array.Empty<NpcSocialPlanChange>(),
                postConversationAction = new NpcPostConversationAction
                {
                    hasAction = false,
                    intent = NpcIntentType.ContinueCurrentAction.ToString(),
                    eventKind = NpcEventKind.None.ToString(),
                    targetLocationId = string.Empty,
                    targetActorId = string.Empty,
                    plannedTargetLocationId = string.Empty,
                    activityKind = string.Empty,
                    participantActorIds = Array.Empty<string>(),
                    requiredActorIds = Array.Empty<string>(),
                    optionalActorIds = Array.Empty<string>(),
                    patienceMinutes = 0,
                    timingMode = NpcTimingMode.Immediate.ToString(),
                    delayMinutes = 0,
                    scheduledStartHour = -1,
                    scheduledStartMinute = -1,
                    reason = string.Empty
                },
                relationshipDeltaHint = 0,
                confidence = 0.7f
            };

            FinishTrackedRequest(token, true, decision, null);
            onSuccess?.Invoke(decision);
        }

        private static string BuildDialogue(NpcAiRequest request, bool hasEvent, bool isOppositeDay)
        {
            if (isOppositeDay)
            {
                return $"{request.npcName} seems unusually unlike themself today.";
            }

            if (IsPlayerDialogue(request))
            {
                return $"[MOCK] {request.npcName} has no real AI provider assigned for player dialogue.";
            }

            if (hasEvent)
            {
                return $"{request.npcName} noticed something happening and is deciding how to respond.";
            }

            return $"{request.npcName} continues with {request.currentAction}.";
        }

        private static bool IsPlayerDialogue(NpcAiRequest request)
        {
            return request != null
                && !string.IsNullOrWhiteSpace(request.observedEventSummary)
                && (request.observedEventSummary.StartsWith("Player said:", StringComparison.OrdinalIgnoreCase)
                    || request.observedEventSummary.StartsWith("Player started conversation:", StringComparison.OrdinalIgnoreCase)
                    || request.observedEventSummary.StartsWith("The player approached", StringComparison.OrdinalIgnoreCase));
        }

    }
}
