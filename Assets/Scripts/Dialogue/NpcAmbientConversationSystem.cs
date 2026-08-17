using System;
using System.Collections.Generic;
using CityStateSim.Activities;
using CityStateSim.Core;
using CityStateSim.Locations;
using CityStateSim.Movement;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Dialogue
{
    public sealed class NpcAmbientConversationSystem : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private ActivitySpotSystem activitySpotSystem;
        [SerializeField] private ConversationArbiter conversationArbiter;
        [SerializeField] private GameClock clock;

        [Header("Policy")]
        [SerializeField, Min(0.5f)] private float checkIntervalSeconds = 2f;
        [SerializeField, Range(0, 100)] private int freeTalkPriority = 35;
        [SerializeField] private bool logDebug;

        private readonly HashSet<string> consumedGroupsByDate = new HashSet<string>();
        private float nextCheckTime;

        private void Awake()
        {
            if (activitySpotSystem == null)
            {
                activitySpotSystem = FindFirstObjectByType<ActivitySpotSystem>();
            }

            if (conversationArbiter == null)
            {
                conversationArbiter = FindFirstObjectByType<ConversationArbiter>();
            }

            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }
        }

        private void Update()
        {
            if (Time.time < nextCheckTime)
            {
                return;
            }

            nextCheckTime = Time.time + checkIntervalSeconds;
            TryProposeFreeTalk();
        }

        private void TryProposeFreeTalk()
        {
            if (activitySpotSystem == null || conversationArbiter == null)
            {
                return;
            }

            IReadOnlyList<ActivitySpot> spots = activitySpotSystem.Spots;
            for (int i = 0; i < spots.Count; i++)
            {
                ActivitySpot firstSpot = spots[i];
                if (!IsEligibleSpot(firstSpot))
                {
                    continue;
                }

                for (int j = i + 1; j < spots.Count; j++)
                {
                    ActivitySpot secondSpot = spots[j];
                    if (!IsEligiblePair(firstSpot, secondSpot))
                    {
                        continue;
                    }

                    string key = BuildDailyGroupKey(firstSpot);
                    if (consumedGroupsByDate.Contains(key))
                    {
                        continue;
                    }

                    consumedGroupsByDate.Add(key);
                    bool proposed = conversationArbiter.TryProposeWitnessedOneOnOne(
                        firstSpot.Occupant,
                        secondSpot.Occupant,
                        $"free talk at {firstSpot.LocationMarker.Definition.DisplayName}",
                        $"Both NPCs are at activity spots in socialGroup={firstSpot.SocialGroupId}. This can happen naturally even when the player is not watching.",
                        freeTalkPriority);

                    if (logDebug)
                    {
                        Debug.Log($"[Ambient Conversation] Proposed free talk key={key}, acceptedByArbiter={proposed}", this);
                    }

                    return;
                }
            }
        }

        private bool IsEligibleSpot(ActivitySpot spot)
        {
            if (spot == null || !spot.AllowFreeTalk || !spot.IsOccupied || spot.LocationMarker == null || spot.LocationMarker.Definition == null)
            {
                return false;
            }

            NpcMovementAgent movement = spot.Occupant.GetComponent<NpcMovementAgent>();
            return movement == null || (!movement.HasTarget && movement.CanMove);
        }

        private static bool IsEligiblePair(ActivitySpot first, ActivitySpot second)
        {
            return second != null
                && second.AllowFreeTalk
                && second.IsOccupied
                && second.LocationMarker == first.LocationMarker
                && string.Equals(second.SocialGroupId, first.SocialGroupId, StringComparison.OrdinalIgnoreCase)
                && second.Occupant != first.Occupant;
        }

        private string BuildDailyGroupKey(ActivitySpot spot)
        {
            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            string locationId = spot.LocationMarker != null && spot.LocationMarker.Definition != null
                ? spot.LocationMarker.Definition.LocationId
                : "unknown_location";
            return $"{date.Key}:{locationId}:{spot.SocialGroupId}";
        }
    }
}
