using System;
using CityStateSim.SecondaryEvents;

namespace CityStateSim.AI
{
    public static class NpcAiSecondaryEventResolver
    {
        public static void RequestDecision(
            NpcBrainProviderBehaviour brainProvider,
            NpcAiRequest request,
            SecondaryEventSystem secondaryEventSystem,
            int maxLookupResults,
            Action<NpcAiDecision> onSuccess,
            Action<string> onError,
            Action<NpcAiRequest> onFollowupRequestIssued = null)
        {
            if (brainProvider == null)
            {
                onError?.Invoke("No NPC brain provider assigned.");
                return;
            }

            brainProvider.RequestDecision(
                request,
                firstDecision =>
                {
                    if (!ShouldRunLookup(request, firstDecision, secondaryEventSystem))
                    {
                        ClearResolvedLookupQuery(request, firstDecision);
                        onSuccess?.Invoke(firstDecision);
                        return;
                    }

                    string query = firstDecision.secondaryEventQuery.Trim();
                    string lookupResult = secondaryEventSystem.QueryForNpc(request.npcId, query, maxLookupResults);
                    NpcAiRequest followupRequest = request.CloneWithSecondaryEventLookupResult(query, lookupResult);
                    onFollowupRequestIssued?.Invoke(followupRequest);

                    brainProvider.RequestDecision(
                        followupRequest,
                        finalDecision =>
                        {
                            ClearResolvedLookupQuery(followupRequest, finalDecision);
                            onSuccess?.Invoke(finalDecision);
                        },
                        onError);
                },
                onError);
        }

        private static bool ShouldRunLookup(
            NpcAiRequest request,
            NpcAiDecision decision,
            SecondaryEventSystem secondaryEventSystem)
        {
            return request != null
                && decision != null
                && secondaryEventSystem != null
                && request.secondaryEventLookupAvailable
                && !request.secondaryEventLookupAlreadyResolved
                && !string.IsNullOrWhiteSpace(decision.secondaryEventQuery);
        }

        private static void ClearResolvedLookupQuery(NpcAiRequest request, NpcAiDecision decision)
        {
            if (request == null || decision == null || !request.secondaryEventLookupAlreadyResolved)
            {
                return;
            }

            decision.secondaryEventQuery = string.Empty;
        }
    }
}
