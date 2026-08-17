using CityStateSim.Locations;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Activities
{
    public sealed class ActivitySpot : MonoBehaviour
    {
        [SerializeField] private LocationMarker locationMarker;
        [SerializeField] private ActivitySpotType spotType = ActivitySpotType.Seat;
        [SerializeField] private bool allowFreeTalk = true;
        [SerializeField] private string socialGroupId = "default";
        [SerializeField] private Transform faceTarget;

        private NpcRuntimeState occupant;

        public LocationMarker LocationMarker => locationMarker;
        public ActivitySpotType SpotType => spotType;
        public bool AllowFreeTalk => allowFreeTalk;
        public string SocialGroupId => string.IsNullOrWhiteSpace(socialGroupId) ? name : socialGroupId;
        public Transform FaceTarget => faceTarget;
        public NpcRuntimeState Occupant => occupant;
        public bool IsOccupied => occupant != null;

        private void Awake()
        {
            if (locationMarker == null)
            {
                locationMarker = GetComponentInParent<LocationMarker>();
            }
        }

        private void OnEnable()
        {
            ActivitySpotSystem system = FindFirstObjectByType<ActivitySpotSystem>();
            system?.Register(this);
        }

        private void OnDisable()
        {
            ActivitySpotSystem system = FindFirstObjectByType<ActivitySpotSystem>();
            system?.Unregister(this);
        }

        public bool TryOccupy(NpcRuntimeState npc)
        {
            if (npc == null || occupant != null)
            {
                return false;
            }

            occupant = npc;
            return true;
        }

        public void Release(NpcRuntimeState npc)
        {
            if (occupant == npc)
            {
                occupant = null;
            }
        }

        public Vector3 GetUsePosition()
        {
            return transform.position;
        }

        public Vector3 GetFacePosition()
        {
            return faceTarget != null ? faceTarget.position : transform.position + transform.up;
        }
    }
}
