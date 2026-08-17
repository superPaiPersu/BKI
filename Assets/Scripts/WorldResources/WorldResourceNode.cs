using CityStateSim.Interactions;
using CityStateSim.Items;
using CityStateSim.Movement;
using UnityEngine;

namespace CityStateSim.WorldResources
{
    [RequireComponent(typeof(Collider2D))]
    [RequireComponent(typeof(InteractableHoverHitbox))]
    public sealed class WorldResourceNode : MonoBehaviour, IInteractable
    {
        public enum HarvestMode
        {
            Interact = 0,
            Dig = 1,
            Cut = 2
        }

        [Header("Resource")]
        [SerializeField] private HarvestMode harvestMode = HarvestMode.Interact;
        [SerializeField, Min(1)] private int hitsRequired = 1;
        [SerializeField, Min(0f)] private float maxInteractionDistance = 1.5f;
        [SerializeField] private bool requireToolAction = true;
        [SerializeField] private string interactionLabel = "Gather";
        [SerializeField] private ItemDrop[] drops;
        [SerializeField] private bool destroyWhenEmpty = true;
        [SerializeField] private bool respawnAfterHarvest;
        [SerializeField, Min(0f)] private float respawnSeconds = 60f;

        [Header("References")]
        [SerializeField] private InventorySystem playerInventory;

        private int remainingHits;
        private float respawnAtRealtime = -1f;
        private bool hiddenForRespawn;

        public string InteractionLabel => interactionLabel;
        public HarvestMode Mode => harvestMode;
        public int RemainingHits => remainingHits > 0 ? remainingHits : Mathf.Max(1, hitsRequired);
        public bool IsDepleted => remainingHits <= 0;

        private void Awake()
        {
            if (playerInventory == null)
            {
                playerInventory = FindFirstObjectByType<InventorySystem>();
            }

            remainingHits = Mathf.Max(1, hitsRequired);
        }

        private void Reset()
        {
            Collider2D collider = GetComponent<Collider2D>();
            if (collider != null)
            {
                collider.isTrigger = true;
            }
        }

        private void Update()
        {
            if (respawnAtRealtime > 0f && Time.realtimeSinceStartup >= respawnAtRealtime)
            {
                respawnAtRealtime = -1f;
                remainingHits = Mathf.Max(1, hitsRequired);
                hiddenForRespawn = false;
                SetVisible(true);
            }
        }

        public bool CanInteract(GameObject interactor)
        {
            if (interactor == null || global::DayOverCheck.IsUserInputLocked)
            {
                return false;
            }

            if (hiddenForRespawn || remainingHits <= 0)
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

            if (requireToolAction && harvestMode == HarvestMode.Dig && interactor != null)
            {
                PlayerMovementController playerMovement = interactor.GetComponentInParent<PlayerMovementController>();
                if (playerMovement != null)
                {
                    playerMovement.FaceDirection((Vector2)transform.position - (Vector2)interactor.transform.position);
                    playerMovement.TriggerDig();
                    return;
                }
            }
            else if (requireToolAction && harvestMode == HarvestMode.Cut && interactor != null)
            {
                PlayerMovementController playerMovement = interactor.GetComponentInParent<PlayerMovementController>();
                if (playerMovement != null)
                {
                    playerMovement.FaceDirection((Vector2)transform.position - (Vector2)interactor.transform.position);
                    playerMovement.TriggerCut();
                    return;
                }
            }

            Harvest(interactor);
        }

        public bool TryHarvest(GameObject interactor)
        {
            if (!CanInteract(interactor))
            {
                return false;
            }

            Harvest(interactor);
            return true;
        }

        private void Harvest(GameObject interactor)
        {
            InventorySystem inventory = ResolveInventory(interactor);
            RuntimeDrop[] rolledDrops = RollDrops();
            if (rolledDrops.Length > 0 && inventory == null)
            {
                Debug.LogWarning($"[WorldResourceNode] No inventory to receive drops from {name}.", this);
                return;
            }

            if (inventory != null && rolledDrops.Length > 0 && !CanAddAllRolledDrops(inventory, rolledDrops))
            {
                Debug.LogWarning($"[WorldResourceNode] Inventory has no room for drops from {name}.", this);
                return;
            }

            if (!ConsumeHit())
            {
                return;
            }

            GiveDrops(inventory, rolledDrops);

            if (remainingHits <= 0)
            {
                if (respawnAfterHarvest && respawnSeconds > 0f)
                {
                    hiddenForRespawn = true;
                    respawnAtRealtime = Time.realtimeSinceStartup + respawnSeconds;
                    SetVisible(false);
                }
                else if (destroyWhenEmpty)
                {
                    Destroy(gameObject);
                }
            }
        }

        private bool ConsumeHit()
        {
            remainingHits = Mathf.Max(0, remainingHits - 1);
            return true;
        }

        private RuntimeDrop[] RollDrops()
        {
            if (drops == null || drops.Length == 0)
            {
                return System.Array.Empty<RuntimeDrop>();
            }

            System.Collections.Generic.List<RuntimeDrop> rolled = new System.Collections.Generic.List<RuntimeDrop>();
            for (int i = 0; i < drops.Length; i++)
            {
                ItemDrop drop = drops[i];
                if (drop != null && drop.TryRoll(out ItemDefinition item, out int amount))
                {
                    AddRolledDrop(rolled, item, amount);
                }
            }

            return rolled.ToArray();
        }

        private static void AddRolledDrop(System.Collections.Generic.List<RuntimeDrop> rolled, ItemDefinition item, int amount)
        {
            if (rolled == null || item == null || amount <= 0)
            {
                return;
            }

            for (int i = 0; i < rolled.Count; i++)
            {
                RuntimeDrop existing = rolled[i];
                if (existing != null && SameItem(existing.Item, item))
                {
                    existing.Amount += amount;
                    return;
                }
            }

            rolled.Add(new RuntimeDrop(item, amount));
        }

        private static bool CanAddAllRolledDrops(InventorySystem inventory, RuntimeDrop[] rolledDrops)
        {
            if (inventory == null)
            {
                return rolledDrops == null || rolledDrops.Length == 0;
            }

            if (rolledDrops == null)
            {
                return true;
            }

            for (int i = 0; i < rolledDrops.Length; i++)
            {
                RuntimeDrop drop = rolledDrops[i];
                if (drop != null && drop.Item != null && drop.Amount > 0 && !inventory.CanAdd(drop.Item, drop.Amount))
                {
                    return false;
                }
            }

            return true;
        }

        private static void GiveDrops(InventorySystem inventory, RuntimeDrop[] rolledDrops)
        {
            if (inventory == null || rolledDrops == null)
            {
                return;
            }

            for (int i = 0; i < rolledDrops.Length; i++)
            {
                RuntimeDrop drop = rolledDrops[i];
                if (drop != null && drop.Item != null && drop.Amount > 0)
                {
                    inventory.TryAdd(drop.Item, drop.Amount);
                }
            }
        }

        private InventorySystem ResolveInventory(GameObject interactor)
        {
            InventorySystem inventory = playerInventory;
            if (inventory == null && interactor != null)
            {
                inventory = interactor.GetComponentInParent<InventorySystem>();
            }

            return inventory;
        }

        private static bool SameItem(ItemDefinition left, ItemDefinition right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return ReferenceEquals(left, right)
                || (!string.IsNullOrWhiteSpace(left.ItemId)
                    && string.Equals(left.ItemId, right.ItemId, System.StringComparison.OrdinalIgnoreCase));
        }

        private void SetVisible(bool visible)
        {
            Collider2D collider = GetComponent<Collider2D>();
            if (collider != null)
            {
                collider.enabled = visible;
            }

            SpriteRenderer renderer = GetComponent<SpriteRenderer>();
            if (renderer != null)
            {
                renderer.enabled = visible;
            }

            CanvasGroup canvasGroup = GetComponent<CanvasGroup>();
            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.blocksRaycasts = visible;
                canvasGroup.interactable = visible;
            }
        }

        private sealed class RuntimeDrop
        {
            public RuntimeDrop(ItemDefinition item, int amount)
            {
                Item = item;
                Amount = Mathf.Max(0, amount);
            }

            public ItemDefinition Item { get; }
            public int Amount { get; set; }
        }
    }
}
