using System;
using CityStateSim.Core;
using CityStateSim.NPC;

namespace CityStateSim.AI
{
    [Serializable]
    public sealed class NpcAiRequest
    {
        public string requestId;
        public string npcId;
        public string npcName;
        public string role;
        public string personalitySummary;
        public string currentLocation;
        public string currentLocationId;
        public string plannedLocation;
        public string plannedLocationId;
        public string currentLocationTaskSummary;
        public string currentNpcInteractionTemplateSummary;
        public string currentWorldEventTemplateSummary;
        public string currentAction;
        public string currentEmotion;
        public string playerRelationshipSummary;
        public string recentMemorySummary;
        public string sameDayPlayerDialogueTranscript;
        public string factSummary;
        public string perceptionSummary;
        public string observedEventSummary;
        public string rollingGoalSummary;
        public string festivalRuleSummary;
        public string pendingEncounterSummary;
        public string socialPlanSummary;
        public string playerQuestSummary;
        public string allowedLocationSummary;
        public string allowedActorSummary;
        public bool secondaryEventLookupAvailable;
        public bool secondaryEventLookupAlreadyResolved;
        public string secondaryEventAccessSummary;
        public string secondaryEventLookupQuery;
        public string secondaryEventLookupResultSummary;
        public GameDate date;
        public GameTime time;

        public static NpcAiRequest FromRuntimeState(NpcRuntimeState state, GameDate date, GameTime time)
        {
            NpcProfile profile = state != null ? state.Profile : null;
            return new NpcAiRequest
            {
                requestId = Guid.NewGuid().ToString("N"),
                npcId = profile != null ? profile.NpcId : string.Empty,
                npcName = profile != null ? profile.DisplayName : "Unknown NPC",
                role = profile != null ? profile.Role : string.Empty,
                personalitySummary = profile != null ? profile.PersonalitySummary : string.Empty,
                currentLocation = state != null && state.ActualLocation != null ? state.ActualLocation.DisplayName : string.Empty,
                currentLocationId = state != null && state.ActualLocation != null ? state.ActualLocation.LocationId : string.Empty,
                plannedLocation = state != null && state.PlannedLocation != null ? state.PlannedLocation.DisplayName : string.Empty,
                plannedLocationId = state != null && state.PlannedLocation != null ? state.PlannedLocation.LocationId : string.Empty,
                currentLocationTaskSummary = string.Empty,
                currentNpcInteractionTemplateSummary = string.Empty,
                currentWorldEventTemplateSummary = string.Empty,
                currentAction = state != null ? state.CurrentAction : string.Empty,
                date = date,
                time = time
            };
        }

        public NpcAiRequest CloneWithSecondaryEventLookupResult(string query, string resultSummary)
        {
            return new NpcAiRequest
            {
                requestId = Guid.NewGuid().ToString("N"),
                npcId = npcId,
                npcName = npcName,
                role = role,
                personalitySummary = personalitySummary,
                currentLocation = currentLocation,
                currentLocationId = currentLocationId,
                plannedLocation = plannedLocation,
                plannedLocationId = plannedLocationId,
                currentLocationTaskSummary = currentLocationTaskSummary,
                currentNpcInteractionTemplateSummary = currentNpcInteractionTemplateSummary,
                currentWorldEventTemplateSummary = currentWorldEventTemplateSummary,
                currentAction = currentAction,
                currentEmotion = currentEmotion,
                playerRelationshipSummary = playerRelationshipSummary,
                recentMemorySummary = recentMemorySummary,
                sameDayPlayerDialogueTranscript = sameDayPlayerDialogueTranscript,
                factSummary = factSummary,
                perceptionSummary = perceptionSummary,
                observedEventSummary = observedEventSummary,
                rollingGoalSummary = rollingGoalSummary,
                festivalRuleSummary = festivalRuleSummary,
                pendingEncounterSummary = pendingEncounterSummary,
                socialPlanSummary = socialPlanSummary,
                playerQuestSummary = playerQuestSummary,
                allowedLocationSummary = allowedLocationSummary,
                allowedActorSummary = allowedActorSummary,
                secondaryEventLookupAvailable = secondaryEventLookupAvailable,
                secondaryEventLookupAlreadyResolved = true,
                secondaryEventAccessSummary = secondaryEventAccessSummary,
                secondaryEventLookupQuery = query,
                secondaryEventLookupResultSummary = resultSummary,
                date = date,
                time = time
            };
        }
    }
}
