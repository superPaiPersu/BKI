using CityStateSim.Items;
using UnityEngine;

namespace CityStateSim.Combat
{
    public sealed class LootDropper : MonoBehaviour
    {
        [SerializeField] private CombatantHealth health;
        [SerializeField] private ItemDrop[] drops;
        [SerializeField] private bool dropOnDeath = true;
        [SerializeField] private GameObject pickupPrefab;

        private void Awake()
        {
            if (health == null)
            {
                health = GetComponent<CombatantHealth>();
            }

            if (health != null)
            {
                health.Died += HandleDeath;
            }
        }

        private void OnDestroy()
        {
            if (health != null)
            {
                health.Died -= HandleDeath;
            }
        }

        private void HandleDeath(CombatantHealth dead)
        {
            if (!dropOnDeath || drops == null || drops.Length == 0)
            {
                return;
            }

            for (int i = 0; i < drops.Length; i++)
            {
                ItemDrop drop = drops[i];
                if (drop == null || !drop.TryRoll(out ItemDefinition item, out int amount))
                {
                    continue;
                }

                SpawnPickup(item, amount);
            }
        }

        private void SpawnPickup(ItemDefinition item, int amount)
        {
            if (item == null || amount <= 0)
            {
                return;
            }

            if (pickupPrefab != null)
            {
                GameObject pickupObject = Instantiate(pickupPrefab, transform.position, Quaternion.identity);
                WorldItemPickup pickup = pickupObject.GetComponent<WorldItemPickup>();
                if (pickup == null)
                {
                    EnsurePickupCollider(pickupObject);
                    pickup = pickupObject.AddComponent<WorldItemPickup>();
                }

                pickup.Initialize(item, amount);

                return;
            }

            GameObject fallback = new GameObject($"{item.DisplayName} Pickup");
            fallback.transform.position = transform.position;
            EnsurePickupCollider(fallback);
            WorldItemPickup worldItemPickup = fallback.AddComponent<WorldItemPickup>();
            worldItemPickup.Initialize(item, amount);
        }

        private static void EnsurePickupCollider(GameObject target)
        {
            if (target == null || target.GetComponent<Collider2D>() != null)
            {
                return;
            }

            BoxCollider2D collider = target.AddComponent<BoxCollider2D>();
            collider.isTrigger = true;
            collider.size = Vector2.one * 0.5f;
        }
    }
}
