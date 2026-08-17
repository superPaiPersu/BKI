using UnityEngine;

namespace CityStateSim.Locations
{
    public sealed class LocationMarker : MonoBehaviour
    {
        [SerializeField] private LocationDefinition definition;
        [SerializeField] private Transform entryPoint;
        [SerializeField] private Transform[] activityPoints;

        public LocationDefinition Definition => definition;
        public Transform EntryPoint => entryPoint != null ? entryPoint : transform;
        public Transform[] ActivityPoints => activityPoints;

        public Vector3 GetEntryPosition()
        {
            return EntryPoint.position;
        }

        private void OnEnable()
        {
            LocationSystem locationSystem = FindFirstObjectByType<LocationSystem>();
            locationSystem?.Register(this);
        }
    }
}
