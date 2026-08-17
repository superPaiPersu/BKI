using UnityEngine;

namespace CityStateSim.Locations
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class LocationTriggerZone : MonoBehaviour
    {
        [SerializeField] private LocationTrigger locationTrigger;

        private void Reset()
        {
            Collider2D trigger = GetComponent<Collider2D>();
            trigger.isTrigger = true;
            locationTrigger = GetComponentInParent<LocationTrigger>();
        }

        private void Awake()
        {
            if (locationTrigger == null)
            {
                locationTrigger = GetComponentInParent<LocationTrigger>();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            locationTrigger?.HandleTriggerEnter(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            locationTrigger?.HandleTriggerExit(other);
        }
    }
}
