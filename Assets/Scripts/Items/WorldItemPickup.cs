using CityStateSim.Interactions;
using UnityEngine;

namespace CityStateSim.Items
{
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(InteractableHoverHitbox))]
    public sealed class WorldItemPickup : MonoBehaviour, IInteractable
    {
        [SerializeField] private InventorySystem playerInventory;
        [SerializeField] private ItemDefinition item;
        [SerializeField, Min(1)] private int amount = 1;
        [SerializeField] private string interactionLabel = "Pick Up";
        [SerializeField, Min(0f)] private float maxInteractionDistance = 1.5f;
        [SerializeField] private bool destroyWhenCollected = true;

        public string InteractionLabel => interactionLabel;
        public ItemDefinition Item => item;
        public int Amount => Mathf.Max(1, amount);

        public void Initialize(ItemDefinition newItem, int newAmount)
        {
            item = newItem;
            amount = Mathf.Max(1, newAmount);
        }

        private void Awake()
        {
            if (playerInventory == null)
            {
                playerInventory = FindFirstObjectByType<InventorySystem>();
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
            if (item == null || interactor == null || global::DayOverCheck.IsUserInputLocked)
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

            InventorySystem inventory = playerInventory;
            if (inventory == null && interactor != null)
            {
                inventory = interactor.GetComponentInParent<InventorySystem>();
            }

            if (inventory == null)
            {
                Debug.LogWarning("[WorldItemPickup] Player inventory is missing.", this);
                return;
            }

            if (!inventory.TryAdd(item, Amount))
            {
                return;
            }

            if (destroyWhenCollected)
            {
                Destroy(gameObject);
            }
        }
    }
}
