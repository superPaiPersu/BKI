using CityStateSim.Movement;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CityStateSim.Combat
{
    [DisallowMultipleComponent]
    public sealed class PlayerMeleeAttackController : MonoBehaviour
    {
        [SerializeField] private PlayerMovementController movementController;
        [SerializeField] private LayerMask targetLayerMask = ~0;
        [SerializeField, Min(1)] private int damage = 1;
        [SerializeField, Min(0f)] private float attackReach = 0.8f;
        [SerializeField, Min(0.01f)] private float attackRadius = 0.4f;
        [SerializeField, Min(0f)] private float cooldownSeconds = 0.25f;
        [SerializeField] private bool logMisses;

        private readonly Collider2D[] overlapResults = new Collider2D[16];
        private float nextAttackRealtime;

        private void Awake()
        {
            if (movementController == null)
            {
                movementController = GetComponent<PlayerMovementController>();
            }
        }

        public void OnAttack(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                TryAttack();
            }
        }

        public void OnAttack(InputValue value)
        {
            if (value.isPressed)
            {
                TryAttack();
            }
        }

        public bool TryAttack()
        {
            if (global::DayOverCheck.IsUserInputLocked || Time.realtimeSinceStartup < nextAttackRealtime)
            {
                return false;
            }

            nextAttackRealtime = Time.realtimeSinceStartup + cooldownSeconds;
            Vector2 center = GetAttackCenter();
            int count = Physics2D.OverlapCircleNonAlloc(center, attackRadius, overlapResults, targetLayerMask);
            CombatantHealth best = null;
            float bestDistance = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                Collider2D hit = overlapResults[i];
                CombatantHealth health = hit != null ? hit.GetComponentInParent<CombatantHealth>() : null;
                if (health == null || health.IsDead)
                {
                    continue;
                }

                float distance = ((Vector2)health.transform.position - center).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = health;
                }
            }

            if (best == null)
            {
                if (logMisses)
                {
                    Debug.Log("[Combat] Player attack missed.", this);
                }

                return false;
            }

            return best.TryApplyDamage(damage);
        }

        private Vector2 GetAttackCenter()
        {
            Vector2 facing = movementController != null
                ? Direction8Utility.ToVector(movementController.FacingDirection)
                : Vector2.down;
            if (facing.sqrMagnitude <= 0.0001f)
            {
                facing = Vector2.down;
            }

            return (Vector2)transform.position + facing.normalized * Mathf.Max(0f, attackReach);
        }

        private void OnDrawGizmosSelected()
        {
            if (movementController == null)
            {
                movementController = GetComponent<PlayerMovementController>();
            }

            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(GetAttackCenter(), attackRadius);
        }
    }
}
