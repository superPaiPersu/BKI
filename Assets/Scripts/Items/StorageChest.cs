using CityStateSim.Interactions;
using CityStateSim.UI;
using UnityEngine;

namespace CityStateSim.Items
{
    [RequireComponent(typeof(Collider2D))]
    public sealed class StorageChest : MonoBehaviour, IInteractable
    {
        [Header("Storage")]
        [SerializeField] private InventorySystem inventorySystem;
        [SerializeField] private string displayName = "Storage Chest";

        [Header("Interaction")]
        [SerializeField] private string interactionLabel = "Open Storage Chest";
        [SerializeField, Min(0f)] private float maxInteractionDistance = 2f;

        [Header("Debug")]
        [SerializeField] private string lastCanInteractResult;
        [SerializeField] private string lastInteractResult;
        [SerializeField] private float lastInteractTime;
        [SerializeField] private UnityEngine.Object lastInteractor;

        public InventorySystem Inventory => inventorySystem;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public string InteractionLabel => interactionLabel;

        private void Awake()
        {
            if (inventorySystem == null)
            {
                inventorySystem = GetComponent<InventorySystem>();
                if (inventorySystem == null)
                {
                    inventorySystem = GetComponentInParent<InventorySystem>();
                }

                if (inventorySystem == null)
                {
                    inventorySystem = GetComponentInChildren<InventorySystem>(true);
                }
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
            bool canInteract = EvaluateCanInteract(interactor, out string reason);
            lastCanInteractResult = reason;
            return canInteract;
        }

        public void Interact(GameObject interactor)
        {
            lastInteractTime = Time.unscaledTime;
            lastInteractor = interactor;

            if (!EvaluateCanInteract(interactor, out string reason))
            {
                lastInteractResult = $"failed: {reason}";
                lastCanInteractResult = reason;
                return;
            }

            StorageChestSession session = FindFirstObjectByType<StorageChestSession>();
            if (session == null)
            {
                lastInteractResult = "failed: no StorageChestSession in scene";
                Debug.LogWarning("[StorageChest] No StorageChestSession is present in the scene.", this);
                return;
            }

            bool opened = session.Open(this, interactor);
            lastInteractResult = opened ? "success: session opened" : "failed: StorageChestSession.Open returned false";
        }

        private bool EvaluateCanInteract(GameObject interactor, out string reason)
        {
            if (inventorySystem == null)
            {
                reason = "inventorySystem is null";
                return false;
            }

            if (interactor == null)
            {
                reason = "interactor is null";
                return false;
            }

            if (global::DayOverCheck.IsUserInputLocked)
            {
                reason = "user input is locked";
                return false;
            }

            if (maxInteractionDistance <= 0f)
            {
                reason = "ok";
                return true;
            }

            float sqrDistance = ((Vector2)transform.position - (Vector2)interactor.transform.position).sqrMagnitude;
            float maxSqrDistance = maxInteractionDistance * maxInteractionDistance;
            if (sqrDistance > maxSqrDistance)
            {
                reason = $"too far: {Mathf.Sqrt(sqrDistance):0.00}/{maxInteractionDistance:0.00}";
                return false;
            }

            reason = "ok";
            return true;
        }
    }
}
