using System;
using System.Collections.Generic;
using CityStateSim.Movement;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Locations
{
    public sealed class LocationSystem : MonoBehaviour
    {
        private readonly Dictionary<string, LocationMarker> markersById = new Dictionary<string, LocationMarker>();
        private readonly Dictionary<string, int> actorLocationOverlapCounts = new Dictionary<string, int>();

        [SerializeField] private LocationDefinition startingLocation;

        public LocationDefinition CurrentLocation { get; private set; }

        public event Action<LocationDefinition> CurrentLocationChanged;
        public event Action<LocationDefinition, GameObject> ActorEnteredLocation;
        public event Action<LocationDefinition, GameObject> ActorExitedLocation;

        private void Awake()
        {
            RegisterSceneMarkers();
            if (startingLocation != null)
            {
                SetCurrentLocation(startingLocation);
            }
        }

        public void Register(LocationMarker marker)
        {
            if (marker == null || marker.Definition == null || string.IsNullOrWhiteSpace(marker.Definition.LocationId))
            {
                return;
            }

            markersById[marker.Definition.LocationId] = marker;
        }

        public bool TryGetMarker(string locationId, out LocationMarker marker)
        {
            if (markersById.TryGetValue(locationId, out marker))
            {
                return true;
            }

            RegisterSceneMarkers();
            return markersById.TryGetValue(locationId, out marker);
        }

        public bool TryGetMarker(LocationDefinition location, out LocationMarker marker)
        {
            marker = null;
            if (location == null)
            {
                return false;
            }

            return TryGetMarker(location.LocationId, out marker);
        }

        public void NotifyActorEntered(LocationDefinition location, GameObject actor)
        {
            if (location == null)
            {
                return;
            }

            if (actor != null && !TryRegisterActorLocationOverlap(location, actor, true))
            {
                return;
            }

            ActorEnteredLocation?.Invoke(location, actor);

            if (IsPlayerActor(actor))
            {
                SetCurrentLocation(location);
            }
        }

        public void NotifyActorExited(LocationDefinition location, GameObject actor)
        {
            if (location == null)
            {
                return;
            }

            if (actor != null && !TryRegisterActorLocationOverlap(location, actor, false))
            {
                return;
            }

            ClearActorActualLocation(location, actor);
            ActorExitedLocation?.Invoke(location, actor);
        }

        public void SetCurrentLocation(LocationDefinition location)
        {
            if (CurrentLocation == location)
            {
                return;
            }

            CurrentLocation = location;
            CurrentLocationChanged?.Invoke(CurrentLocation);
        }

        private void RegisterSceneMarkers()
        {
            LocationMarker[] markers = FindObjectsByType<LocationMarker>(FindObjectsSortMode.None);
            for (int i = 0; i < markers.Length; i++)
            {
                Register(markers[i]);
            }
        }

        private bool TryRegisterActorLocationOverlap(LocationDefinition location, GameObject actor, bool entered)
        {
            GameObject rootActor = ResolveActorRoot(actor);
            if (rootActor == null || location == null)
            {
                return true;
            }

            string key = BuildActorLocationOverlapKey(location, rootActor);
            actorLocationOverlapCounts.TryGetValue(key, out int count);
            if (entered)
            {
                actorLocationOverlapCounts[key] = count + 1;
                if (count == 0)
                {
                    UpdateActorActualLocation(location, rootActor);
                    return true;
                }

                return false;
            }

            count--;
            if (count > 0)
            {
                actorLocationOverlapCounts[key] = count;
                return false;
            }

            actorLocationOverlapCounts.Remove(key);
            return true;
        }

        private static string BuildActorLocationOverlapKey(LocationDefinition location, GameObject actor)
        {
            return $"{location.GetInstanceID()}:{actor.GetInstanceID()}";
        }

        private static GameObject ResolveActorRoot(GameObject actor)
        {
            if (actor == null)
            {
                return null;
            }

            NpcRuntimeState npc = actor.GetComponentInParent<NpcRuntimeState>();
            if (npc != null)
            {
                return npc.gameObject;
            }

            PlayerMovementController player = actor.GetComponentInParent<PlayerMovementController>();
            if (player != null)
            {
                return player.gameObject;
            }

            return actor;
        }

        private static bool IsPlayerActor(GameObject actor)
        {
            if (actor == null)
            {
                return false;
            }

            GameObject rootActor = ResolveActorRoot(actor);
            return rootActor != null
                && (rootActor.CompareTag("Player") || rootActor.GetComponent<PlayerMovementController>() != null);
        }

        private static void UpdateActorActualLocation(LocationDefinition location, GameObject actor)
        {
            if (actor == null || location == null)
            {
                return;
            }

            NpcRuntimeState npc = actor.GetComponentInParent<NpcRuntimeState>();
            if (npc != null)
            {
                npc.SetActualLocation(location);
            }
        }

        private static void ClearActorActualLocation(LocationDefinition location, GameObject actor)
        {
            if (actor == null || location == null)
            {
                return;
            }

            NpcRuntimeState npc = actor.GetComponentInParent<NpcRuntimeState>();
            if (npc != null)
            {
                npc.ClearActualLocation(location);
            }
        }
    }
}
