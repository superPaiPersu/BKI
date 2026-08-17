using System;
using CityStateSim.Pathfinding;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

namespace CityStateSim.Movement
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class PlayerMovementController : MonoBehaviour
    {
        private static readonly int DirectionXHash = Animator.StringToHash("dirx");
        private static readonly int DirectionYHash = Animator.StringToHash("diry");
        private static readonly int IsWalkingHash = Animator.StringToHash("iswalking");
        private static readonly int DigHash = Animator.StringToHash("dig");
        private static readonly int CutHash = Animator.StringToHash("cut");

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 4f;
        [SerializeField] private bool canMove = true;
        [SerializeField] private bool useRigidbodyInterpolation = true;

        [Header("Actions")]
        [SerializeField] private bool stopMovementDuringAction = true;
        [SerializeField, Min(0f)] private float actionTransitionLockSeconds = 0.05f;
        [SerializeField] private bool allowActionInterrupts;

        [Header("Collision")]
        [SerializeField] private bool ignoreDecorativeTilemapCollisions = true;
        [SerializeField] private string[] ignoredDecorativeTilemapNameTokens =
            { "display", "decoration", "decor", "visual", "render", "walkable" };

        [Header("Optional References")]
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerLayeredAppearanceController appearanceController;

        private Rigidbody2D body;
        private Collider2D bodyCollider;
        private Vector2 moveInput;
        private Direction8 facingDirection = Direction8.South;
        private float actionLockedUntilTime;

        public Vector2 MoveInput => moveInput;
        public Direction8 FacingDirection => facingDirection;
        public bool CanMove => canMove && !global::DayOverCheck.IsUserInputLocked;
        public bool IsMovementLockedByAction => stopMovementDuringAction && IsActionLockActive();

        public event Action<Direction8> FacingDirectionChanged;
        public event Action<Vector2> MoveInputChanged;
        public event Action DigTriggered;
        public event Action CutTriggered;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            bodyCollider = FindBodyCollider();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            if (useRigidbodyInterpolation)
            {
                body.interpolation = RigidbodyInterpolation2D.Interpolate;
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (appearanceController == null)
            {
                appearanceController = GetComponent<PlayerLayeredAppearanceController>();
                if (appearanceController == null)
                {
                    appearanceController = GetComponentInChildren<PlayerLayeredAppearanceController>(true);
                }

                if (appearanceController == null)
                {
                    appearanceController = gameObject.AddComponent<PlayerLayeredAppearanceController>();
                }
            }

            appearanceController?.ApplyDirection(facingDirection);
            IgnoreDecorativeTilemapCollisions();
        }

        private Collider2D FindBodyCollider()
        {
            Collider2D rootCollider = GetComponent<Collider2D>();
            if (rootCollider != null && !rootCollider.isTrigger)
            {
                return rootCollider;
            }

            Collider2D[] childColliders = GetComponentsInChildren<Collider2D>();
            for (int i = 0; i < childColliders.Length; i++)
            {
                Collider2D childCollider = childColliders[i];
                if (childCollider != null && !childCollider.isTrigger)
                {
                    return childCollider;
                }
            }

            return rootCollider;
        }

        private void FixedUpdate()
        {
            Vector2 velocity = CanMove && !IsMovementLockedByAction
                ? moveInput.normalized * moveSpeed
                : Vector2.zero;
            body.linearVelocity = velocity;
            UpdateAnimator(velocity);
        }

        public void SetMoveInput(Vector2 input)
        {
            moveInput = CanMove ? Vector2.ClampMagnitude(input, 1f) : Vector2.zero;
            MoveInputChanged?.Invoke(moveInput);

            if (moveInput.sqrMagnitude > 0.0001f)
            {
                SetFacingDirection(Direction8Utility.FromVector(moveInput, facingDirection));
            }
        }

        public void SetCanMove(bool value)
        {
            canMove = value;
            if (!canMove)
            {
                SetMoveInput(Vector2.zero);
            }
        }

        public bool TriggerDig()
        {
            bool triggered = TriggerAction(DigHash);
            if (triggered)
            {
                DigTriggered?.Invoke();
            }

            return triggered;
        }

        public bool TriggerCut()
        {
            bool triggered = TriggerAction(CutHash);
            if (triggered)
            {
                CutTriggered?.Invoke();
            }

            return triggered;
        }

        public bool TriggerDig(Direction8 direction)
        {
            SetFacingDirection(direction);
            return TriggerDig();
        }

        public bool TriggerCut(Direction8 direction)
        {
            SetFacingDirection(direction);
            return TriggerCut();
        }

        public bool TriggerDig(Vector2 direction)
        {
            FaceDirection(direction);
            return TriggerDig();
        }

        public bool TriggerCut(Vector2 direction)
        {
            FaceDirection(direction);
            return TriggerCut();
        }

        public void OnDig(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                TriggerDig();
            }
        }

        public void OnCut(InputAction.CallbackContext context)
        {
            if (context.performed)
            {
                TriggerCut();
            }
        }

        public void OnDig(InputValue value)
        {
            if (value.isPressed)
            {
                TriggerDig();
            }
        }

        public void OnCut(InputValue value)
        {
            if (value.isPressed)
            {
                TriggerCut();
            }
        }

        private void IgnoreDecorativeTilemapCollisions()
        {
            if (!ignoreDecorativeTilemapCollisions || bodyCollider == null)
            {
                return;
            }

            TilemapCollider2D[] tilemapColliders = FindObjectsByType<TilemapCollider2D>(FindObjectsSortMode.None);
            for (int i = 0; i < tilemapColliders.Length; i++)
            {
                TilemapCollider2D tilemapCollider = tilemapColliders[i];
                if (NavigationColliderFilter.ShouldIgnoreDecorativeTilemap(
                        tilemapCollider,
                        true,
                        ignoredDecorativeTilemapNameTokens))
                {
                    Physics2D.IgnoreCollision(bodyCollider, tilemapCollider, true);
                }
            }
        }

        public void OnMove(InputAction.CallbackContext context)
        {
            SetMoveInput(context.ReadValue<Vector2>());
        }

        public void OnMove(InputValue value)
        {
            SetMoveInput(value.Get<Vector2>());
        }

        public void FaceDirection(Vector2 direction)
        {
            if (direction.sqrMagnitude > 0.0001f)
            {
                SetFacingDirection(Direction8Utility.FromVector(direction, facingDirection));
            }
        }

        private void SetFacingDirection(Direction8 direction)
        {
            if (facingDirection == direction)
            {
                return;
            }

            facingDirection = direction;
            appearanceController?.ApplyDirection(facingDirection);
            FacingDirectionChanged?.Invoke(facingDirection);
        }

        private void UpdateAnimator(Vector2 velocity)
        {
            if (animator == null)
            {
                return;
            }

            Vector2 facingVector = Direction8Utility.ToVector(facingDirection);
            animator.SetFloat(DirectionXHash, facingVector.x);
            animator.SetFloat(DirectionYHash, facingVector.y);
            animator.SetBool(IsWalkingHash, velocity.sqrMagnitude > 0.0001f);
        }

        private bool TriggerAction(int actionHash)
        {
            if (animator == null || !CanMove)
            {
                return false;
            }

            if (!allowActionInterrupts && IsActionLockActive())
            {
                return false;
            }

            Vector2 facingVector = Direction8Utility.ToVector(facingDirection);
            animator.SetFloat(DirectionXHash, facingVector.x);
            animator.SetFloat(DirectionYHash, facingVector.y);
            animator.SetBool(IsWalkingHash, false);
            animator.ResetTrigger(DigHash);
            animator.ResetTrigger(CutHash);
            animator.SetTrigger(actionHash);
            actionLockedUntilTime = Mathf.Max(actionLockedUntilTime, Time.time + actionTransitionLockSeconds);
            return true;
        }

        private bool IsActionLockActive()
        {
            return Time.time < actionLockedUntilTime || IsActionStateActive();
        }

        private bool IsActionStateActive()
        {
            if (animator == null || animator.layerCount <= 0)
            {
                return false;
            }

            AnimatorStateInfo currentState = animator.GetCurrentAnimatorStateInfo(0);
            if (IsActionState(currentState))
            {
                return true;
            }

            return animator.IsInTransition(0) && IsActionState(animator.GetNextAnimatorStateInfo(0));
        }

        private static bool IsActionState(AnimatorStateInfo state)
        {
            return state.shortNameHash == DigHash || state.shortNameHash == CutHash;
        }
    }
}
