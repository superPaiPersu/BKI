using CityStateSim.Interactions;
using UnityEngine;

namespace CityStateSim.Locations
{
    public sealed class LocationTrigger : MonoBehaviour, IInteractable
    {
        [SerializeField] private LocationDefinition location;
        [SerializeField] private bool enterOnTrigger = true;
        [SerializeField] private bool requireInteraction;
        [SerializeField] private string interactionLabel = "Enter";
        [SerializeField] private LocationSystem locationSystem;

        public string InteractionLabel => interactionLabel;
        public LocationDefinition Location => location;

        private void Reset()
        {
            Collider2D trigger = GetComponent<Collider2D>();
            if (trigger != null)
            {
                trigger.isTrigger = true;
            }
        }

        private void Awake()
        {
            if (locationSystem == null)
            {
                locationSystem = FindFirstObjectByType<LocationSystem>();
            }
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            HandleTriggerEnter(other);
        }

        private void OnTriggerExit2D(Collider2D other)
        {
            HandleTriggerExit(other);
        }

        public void HandleTriggerEnter(Collider2D other)
        {
            if (!enterOnTrigger || requireInteraction)
            {
                return;
            }

            locationSystem?.NotifyActorEntered(location, other.gameObject);
        }

        public void HandleTriggerExit(Collider2D other)
        {
            if (!enterOnTrigger || requireInteraction)
            {
                return;
            }

            locationSystem?.NotifyActorExited(location, other.gameObject);
        }

        public bool CanInteract(GameObject interactor)
        {
            return requireInteraction && location != null;
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            locationSystem?.NotifyActorEntered(location, interactor);
        }
    }
}
