using CityStateSim.Interactions;
using CityStateSim.Jobs;
using UnityEngine;

namespace CityStateSim.Economy
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class ShopInteraction : MonoBehaviour, IInteractable
    {
        [SerializeField] private ShopDefinition definition;
        [SerializeField] private ShopSession shopSession;
        [SerializeField] private string interactionLabel = "Trade";
        [SerializeField, Min(0f)] private float maxInteractionDistance = 2f;

        public ShopDefinition Definition => definition;
        public string InteractionLabel => interactionLabel;

        private void Awake()
        {
            if (shopSession == null)
            {
                shopSession = FindFirstObjectByType<ShopSession>();
            }
        }

        private void Reset()
        {
            Collider2D collider = GetComponent<Collider2D>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }
        }

        public bool CanInteract(GameObject interactor)
        {
            if (definition == null || interactor == null || global::DayOverCheck.IsUserInputLocked)
            {
                return false;
            }

            if (maxInteractionDistance <= 0f)
            {
                return true;
            }

            return ((Vector2)transform.position - (Vector2)interactor.transform.position).sqrMagnitude
                <= maxInteractionDistance * maxInteractionDistance;
        }

        public void Interact(GameObject interactor)
        {
            if (!CanInteract(interactor))
            {
                return;
            }

            if (shopSession == null)
            {
                shopSession = FindFirstObjectByType<ShopSession>();
            }

            if (shopSession == null)
            {
                Debug.LogWarning("[ShopInteraction] No ShopSession is present in the scene.", this);
                return;
            }

            shopSession.Open(this, interactor);
        }
    }
}
