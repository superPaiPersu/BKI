using CityStateSim.Movement;
using UnityEngine;

namespace CityStateSim.WorldResources
{
    [DisallowMultipleComponent]
    public sealed class PlayerResourceActionController : MonoBehaviour
    {
        [SerializeField] private PlayerMovementController movementController;
        [SerializeField] private LayerMask resourceLayerMask = ~0;
        [SerializeField, Min(0f)] private float actionReach = 0.75f;
        [SerializeField, Min(0.01f)] private float actionRadius = 0.35f;
        [SerializeField] private bool logMisses;

        private readonly Collider2D[] overlapResults = new Collider2D[16];

        private void Awake()
        {
            if (movementController == null)
            {
                movementController = GetComponent<PlayerMovementController>();
            }
        }

        private void OnEnable()
        {
            if (movementController != null)
            {
                movementController.DigTriggered += HandleDigTriggered;
                movementController.CutTriggered += HandleCutTriggered;
            }
        }

        private void OnDisable()
        {
            if (movementController != null)
            {
                movementController.DigTriggered -= HandleDigTriggered;
                movementController.CutTriggered -= HandleCutTriggered;
            }
        }

        private void HandleDigTriggered()
        {
            TryHarvest(WorldResourceNode.HarvestMode.Dig);
        }

        private void HandleCutTriggered()
        {
            TryHarvest(WorldResourceNode.HarvestMode.Cut);
        }

        private bool TryHarvest(WorldResourceNode.HarvestMode mode)
        {
            Vector2 center = GetActionCenter();
            int count = Physics2D.OverlapCircleNonAlloc(center, actionRadius, overlapResults, resourceLayerMask);
            WorldResourceNode best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = overlapResults[i];
                WorldResourceNode node = hit != null ? hit.GetComponentInParent<WorldResourceNode>() : null;
                if (node == null || node.Mode != mode || !node.CanInteract(gameObject))
                {
                    continue;
                }

                float distance = ((Vector2)node.transform.position - center).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = node;
                }
            }

            if (best == null)
            {
                if (logMisses)
                {
                    Debug.Log($"[Resource] No {mode} resource in range.", this);
                }

                return false;
            }

            return best.TryHarvest(gameObject);
        }

        private Vector2 GetActionCenter()
        {
            Vector2 facing = movementController != null
                ? Direction8Utility.ToVector(movementController.FacingDirection)
                : Vector2.down;
            if (facing.sqrMagnitude <= 0.0001f)
            {
                facing = Vector2.down;
            }

            return (Vector2)transform.position + facing.normalized * Mathf.Max(0f, actionReach);
        }

        private void OnDrawGizmosSelected()
        {
            if (movementController == null)
            {
                movementController = GetComponent<PlayerMovementController>();
            }

            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(GetActionCenter(), actionRadius);
        }
    }
}
