using System;
using CityStateSim.AI;
using CityStateSim.Locations;
using CityStateSim.Movement;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Dialogue
{
    public static class PostConversationActionResolver
    {
        public static bool TryBuildDecision(
            NpcRuntimeState npc,
            NpcAiDecision sourceDecision,
            NpcPostConversationAction action,
            string dialogueContextKind,
            string dialogueSourceActorId,
            string dialogueSubjectActorId,
            string dialogueSubjectLocationId,
            string dialogueSourceText,
            out NpcAiDecision executableDecision,
            out string reason)
        {
            executableDecision = null;
            reason = string.Empty;
            if (npc == null || npc.Profile == null)
            {
                reason = "missing npc.";
                return false;
            }

            if (action == null || !action.hasAction)
            {
                reason = "hasAction was false.";
                return false;
            }

            action.Clamp();
            NpcIntentType intent = action.ParsedIntent;
            if (!IsAllowedIntent(intent))
            {
                reason = $"intent {action.intent} is not allowed as a post-conversation action.";
                return false;
            }

            string targetActorId = Clean(action.targetActorId);
            string targetLocationId = Clean(action.targetLocationId);
            string plannedTargetLocationId = Clean(action.plannedTargetLocationId);

            if (!string.IsNullOrWhiteSpace(targetActorId) && !IsKnownActorId(npc, targetActorId))
            {
                reason = $"unknown targetActorId '{targetActorId}'.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(targetLocationId) && !IsKnownLocationId(targetLocationId))
            {
                reason = $"unknown targetLocationId '{targetLocationId}'.";
                return false;
            }

            if (!string.IsNullOrWhiteSpace(plannedTargetLocationId) && !IsKnownLocationId(plannedTargetLocationId))
            {
                reason = $"unknown plannedTargetLocationId '{plannedTargetLocationId}'.";
                return false;
            }

            if (RequiresActorTarget(intent) && string.IsNullOrWhiteSpace(targetActorId))
            {
                reason = $"intent {intent} requires targetActorId.";
                return false;
            }

            if (RequiresLocationTarget(intent) && string.IsNullOrWhiteSpace(targetLocationId))
            {
                reason = $"intent {intent} requires targetLocationId.";
                return false;
            }

            if (intent == NpcIntentType.ReactToEvent
                && string.IsNullOrWhiteSpace(targetActorId)
                && string.IsNullOrWhiteSpace(targetLocationId))
            {
                reason = "ReactToEvent requires targetActorId or targetLocationId.";
                return false;
            }

            string subjectActorId = !string.IsNullOrWhiteSpace(dialogueSubjectActorId)
                ? Clean(dialogueSubjectActorId)
                : DetermineSubjectActor(npc, targetActorId, action);
            string subjectLocationId = !string.IsNullOrWhiteSpace(dialogueSubjectLocationId)
                ? Clean(dialogueSubjectLocationId)
                : !string.IsNullOrWhiteSpace(targetLocationId)
                    ? targetLocationId
                    : plannedTargetLocationId;

            executableDecision = new NpcAiDecision
            {
                intent = intent.ToString(),
                behaviorMode = GuessBehaviorMode(intent),
                tone = sourceDecision != null ? sourceDecision.tone : "neutral",
                dialogue = string.Empty,
                emotion = sourceDecision != null ? sourceDecision.emotion : "neutral",
                nextActionPreference = string.IsNullOrWhiteSpace(action.reason)
                    ? sourceDecision != null ? sourceDecision.nextActionPreference : string.Empty
                    : action.reason,
                originalGoal = sourceDecision != null && !string.IsNullOrWhiteSpace(sourceDecision.originalGoal)
                    ? sourceDecision.originalGoal
                    : !string.IsNullOrWhiteSpace(action.reason)
                        ? action.reason
                        : dialogueSourceText,
                currentGoal = sourceDecision != null && !string.IsNullOrWhiteSpace(sourceDecision.currentGoal)
                    ? sourceDecision.currentGoal
                    : !string.IsNullOrWhiteSpace(action.reason)
                        ? action.reason
                        : string.Empty,
                goalStatus = "active",
                goalStatusReason = string.Empty,
                nextSpeakerId = string.Empty,
                secondaryEventQuery = string.Empty,
                eventKind = string.IsNullOrWhiteSpace(action.eventKind) ? NpcEventKind.OneShot.ToString() : action.eventKind,
                targetLocationId = targetLocationId,
                targetActorId = targetActorId,
                plannedTargetLocationId = plannedTargetLocationId,
                activityKind = action.activityKind,
                participantActorIds = action.participantActorIds ?? Array.Empty<string>(),
                requiredActorIds = action.requiredActorIds ?? Array.Empty<string>(),
                optionalActorIds = action.optionalActorIds ?? Array.Empty<string>(),
                patienceMinutes = action.patienceMinutes,
                timingMode = action.timingMode,
                delayMinutes = action.delayMinutes,
                scheduledStartHour = action.scheduledStartHour,
                scheduledStartMinute = action.scheduledStartMinute,
                dialogueContextKind = Clean(dialogueContextKind),
                dialogueSourceActorId = Clean(dialogueSourceActorId),
                dialogueSubjectActorId = subjectActorId,
                dialogueSubjectLocationId = subjectLocationId,
                dialogueSourceText = Clean(dialogueSourceText),
                pendingEncounterChanges = Array.Empty<NpcPendingEncounterChange>(),
                socialPlanChanges = Array.Empty<NpcSocialPlanChange>(),
                relationshipDeltaHint = sourceDecision != null ? sourceDecision.relationshipDeltaHint : 0,
                confidence = sourceDecision != null ? sourceDecision.confidence : 0.5f
            };
            executableDecision.ClampHints();
            return true;
        }

        public static bool IsAllowedIntent(NpcIntentType intent)
        {
            return intent == NpcIntentType.FindActor
                || intent == NpcIntentType.FollowActor
                || intent == NpcIntentType.MoveToLocation
                || intent == NpcIntentType.ReactToEvent
                || intent == NpcIntentType.TalkToPlayer
                || intent == NpcIntentType.TalkToNpc
                || intent == NpcIntentType.AvoidActor
                || intent == NpcIntentType.WorkAtLocation
                || intent == NpcIntentType.RestAtLocation
                || intent == NpcIntentType.JoinFestival;
        }

        private static bool RequiresActorTarget(NpcIntentType intent)
        {
            return intent == NpcIntentType.FindActor
                || intent == NpcIntentType.FollowActor
                || intent == NpcIntentType.TalkToNpc
                || intent == NpcIntentType.AvoidActor;
        }

        private static bool RequiresLocationTarget(NpcIntentType intent)
        {
            return intent == NpcIntentType.MoveToLocation
                || intent == NpcIntentType.WorkAtLocation
                || intent == NpcIntentType.RestAtLocation
                || intent == NpcIntentType.JoinFestival;
        }

        private static bool IsKnownActorId(NpcRuntimeState self, string actorId)
        {
            if (string.IsNullOrWhiteSpace(actorId))
            {
                return false;
            }

            if (string.Equals(actorId, "player", StringComparison.OrdinalIgnoreCase))
            {
                return UnityEngine.Object.FindFirstObjectByType<PlayerMovementController>() != null;
            }

            if (self != null
                && self.Profile != null
                && string.Equals(self.Profile.NpcId, actorId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            NpcRuntimeState[] npcs = UnityEngine.Object.FindObjectsByType<NpcRuntimeState>(FindObjectsSortMode.None);
            for (int i = 0; i < npcs.Length; i++)
            {
                NpcProfile profile = npcs[i] != null ? npcs[i].Profile : null;
                if (profile != null && string.Equals(profile.NpcId, actorId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsKnownLocationId(string locationId)
        {
            if (string.IsNullOrWhiteSpace(locationId))
            {
                return false;
            }

            LocationMarker[] markers = UnityEngine.Object.FindObjectsByType<LocationMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                LocationDefinition definition = markers[i] != null ? markers[i].Definition : null;
                if (definition != null && string.Equals(definition.LocationId, locationId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private static string DetermineSubjectActor(NpcRuntimeState npc, string targetActorId, NpcPostConversationAction action)
        {
            if (!string.IsNullOrWhiteSpace(targetActorId)
                && !string.Equals(targetActorId, "player", StringComparison.OrdinalIgnoreCase))
            {
                return targetActorId.Trim();
            }

            string selfId = npc != null && npc.Profile != null ? npc.Profile.NpcId : string.Empty;
            string actorId = FirstOtherNpcId(selfId, action != null ? action.requiredActorIds : null);
            if (!string.IsNullOrWhiteSpace(actorId))
            {
                return actorId;
            }

            actorId = FirstOtherNpcId(selfId, action != null ? action.participantActorIds : null);
            if (!string.IsNullOrWhiteSpace(actorId))
            {
                return actorId;
            }

            return FirstOtherNpcId(selfId, action != null ? action.optionalActorIds : null);
        }

        private static string FirstOtherNpcId(string selfId, string[] actorIds)
        {
            if (actorIds == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < actorIds.Length; i++)
            {
                string actorId = Clean(actorIds[i]);
                if (string.IsNullOrWhiteSpace(actorId)
                    || string.Equals(actorId, "player", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(actorId, selfId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                return actorId;
            }

            return string.Empty;
        }

        private static string GuessBehaviorMode(NpcIntentType intent)
        {
            switch (intent)
            {
                case NpcIntentType.TalkToPlayer:
                case NpcIntentType.TalkToNpc:
                    return NpcBehaviorMode.Socialize.ToString();
                case NpcIntentType.WorkAtLocation:
                    return NpcBehaviorMode.Work.ToString();
                case NpcIntentType.RestAtLocation:
                    return NpcBehaviorMode.Rest.ToString();
                case NpcIntentType.AvoidActor:
                    return NpcBehaviorMode.Avoid.ToString();
                case NpcIntentType.JoinFestival:
                    return NpcBehaviorMode.Celebrate.ToString();
                case NpcIntentType.FindActor:
                case NpcIntentType.FollowActor:
                case NpcIntentType.ReactToEvent:
                case NpcIntentType.MoveToLocation:
                default:
                    return NpcBehaviorMode.Investigate.ToString();
            }
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }
}
