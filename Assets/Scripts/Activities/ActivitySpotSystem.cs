using System.Collections.Generic;
using CityStateSim.Locations;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Activities
{
    public sealed class ActivitySpotSystem : MonoBehaviour
    {
        private readonly List<ActivitySpot> spots = new List<ActivitySpot>();
        private readonly Dictionary<NpcRuntimeState, ActivitySpot> spotsByNpc = new Dictionary<NpcRuntimeState, ActivitySpot>();

        public IReadOnlyList<ActivitySpot> Spots => spots;

        private void Awake()
        {
            RegisterSceneSpots();
        }

        public void Register(ActivitySpot spot)
        {
            if (spot == null || spots.Contains(spot))
            {
                return;
            }

            spots.Add(spot);
        }

        public void Unregister(ActivitySpot spot)
        {
            if (spot == null)
            {
                return;
            }

            spots.Remove(spot);
            if (spot.Occupant != null)
            {
                spotsByNpc.Remove(spot.Occupant);
            }
        }

        public bool TryAssignSpot(NpcRuntimeState npc, LocationDefinition location, out ActivitySpot spot)
        {
            spot = null;
            if (npc == null || location == null)
            {
                return false;
            }

            ReleaseSpot(npc);
            for (int i = 0; i < spots.Count; i++)
            {
                ActivitySpot candidate = spots[i];
                if (candidate == null || candidate.IsOccupied || candidate.LocationMarker == null || candidate.LocationMarker.Definition != location)
                {
                    continue;
                }

                if (!candidate.TryOccupy(npc))
                {
                    continue;
                }

                spotsByNpc[npc] = candidate;
                spot = candidate;
                return true;
            }

            return false;
        }

        public bool TryAssignSpotNear(
            NpcRuntimeState npc,
            LocationDefinition location,
            Vector3 anchor,
            float maxDistance,
            out ActivitySpot spot)
        {
            spot = null;
            if (npc == null || location == null)
            {
                return false;
            }

            ActivitySpot currentSpot = null;
            if (spotsByNpc.TryGetValue(npc, out currentSpot)
                && currentSpot != null
                && IsSpotForLocation(currentSpot, location)
                && Vector2.Distance(currentSpot.GetUsePosition(), anchor) <= maxDistance)
            {
                spot = currentSpot;
                return true;
            }

            ReleaseSpot(npc);

            ActivitySpot best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < spots.Count; i++)
            {
                ActivitySpot candidate = spots[i];
                if (candidate == null || candidate.IsOccupied || !IsSpotForLocation(candidate, location))
                {
                    continue;
                }

                float distance = Vector2.Distance(candidate.GetUsePosition(), anchor);
                if (distance > maxDistance || distance >= bestDistance)
                {
                    continue;
                }

                best = candidate;
                bestDistance = distance;
            }

            if (best == null || !best.TryOccupy(npc))
            {
                return false;
            }

            spotsByNpc[npc] = best;
            spot = best;
            return true;
        }

        public void ReleaseSpot(NpcRuntimeState npc)
        {
            if (npc == null || !spotsByNpc.TryGetValue(npc, out ActivitySpot spot))
            {
                return;
            }

            spot?.Release(npc);
            spotsByNpc.Remove(npc);
        }

        public bool TryGetSpot(NpcRuntimeState npc, out ActivitySpot spot)
        {
            spot = null;
            return npc != null && spotsByNpc.TryGetValue(npc, out spot) && spot != null;
        }

        private static bool IsSpotForLocation(ActivitySpot spot, LocationDefinition location)
        {
            return spot != null
                && location != null
                && spot.LocationMarker != null
                && spot.LocationMarker.Definition == location;
        }

        private void RegisterSceneSpots()
        {
            ActivitySpot[] sceneSpots = FindObjectsByType<ActivitySpot>(FindObjectsSortMode.None);
            for (int i = 0; i < sceneSpots.Length; i++)
            {
                Register(sceneSpots[i]);
            }
        }
    }
}
