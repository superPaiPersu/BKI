using System;
using System.Collections.Generic;
using CityStateSim.Movement;
using CityStateSim.Pathfinding;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace CityStateSim.NPC
{
    [RequireComponent(typeof(Rigidbody2D))]
    public sealed class NpcMovementAgent : MonoBehaviour
    {
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int DirectionHash = Animator.StringToHash("Direction");
        private static readonly int DirectionXHash = Animator.StringToHash("direction_x");
        private static readonly int DirectionYHash = Animator.StringToHash("direction_y");

        [Header("Movement")]
        [SerializeField, Min(0f)] private float moveSpeed = 2.5f;
        [SerializeField, Min(0.01f)] private float stoppingDistance = 0.08f;
        [SerializeField] private bool canMove = true;
        [SerializeField] private bool useRigidbodyInterpolation = true;

        [Header("Pathfinding")]
        [SerializeField] private WorldGridPathfinder pathfinder;
        [SerializeField] private bool usePathfinding = true;
        [SerializeField] private bool fallBackToDirectMovement = true;
        [SerializeField] private bool logPathFailures = true;
        [SerializeField, Min(0.01f)] private float navigationRadius = 0.2f;
        [SerializeField] private bool useColliderSizeForPathfindingRadius;
        [SerializeField, Min(0.01f)] private float waypointReachDistance = 0.18f;
        [SerializeField, Min(0.05f)] private float stuckCheckInterval = 0.5f;
        [SerializeField, Min(0.001f)] private float stuckDistance = 0.03f;
        [SerializeField, Min(0f)] private float colliderClearancePadding = 0.03f;
        [SerializeField] private bool ignoreDecorativeTilemapCollisions = true;
        [SerializeField] private string[] ignoredDecorativeTilemapNameTokens =
            { "display", "decoration", "decor", "visual", "render", "walkable" };

        [Header("NPC Collision Unsticking")]
        [SerializeField] private bool temporarilyIgnoreNpcCollisionWhenBlocked = true;
        [SerializeField, Min(0.05f)] private float npcCollisionHoldSeconds = 1.25f;
        [SerializeField, Min(0.05f)] private float npcCollisionIgnoreSeconds = 1.5f;
        [SerializeField] private bool logNpcCollisionUnsticking;

        [Header("Optional References")]
        [SerializeField] private Animator animator;

        private const float WaypointReachRadiusFactor = 0.45f;
        private const float CornerSlowdownRadiusFactor = 1.5f;
        private const float MinimumCornerSpeedFactor = 0.35f;

        private static readonly Dictionary<CollisionPairKey, TemporaryNpcCollisionIgnore> activeNpcCollisionIgnores =
            new Dictionary<CollisionPairKey, TemporaryNpcCollisionIgnore>();
        private static readonly List<CollisionPairKey> expiredNpcCollisionIgnoreKeys = new List<CollisionPairKey>();

        private Rigidbody2D body;
        private Collider2D bodyCollider;
        private Vector2 targetPosition;
        private readonly List<Vector2> path = new List<Vector2>();
        private readonly HashSet<string> pauseReasons = new HashSet<string>();
        private readonly Dictionary<Collider2D, float> npcCollisionStartedRealtimeByCollider = new Dictionary<Collider2D, float>();
        private readonly Dictionary<int, AnimatorControllerParameterType> animatorParameterTypes =
            new Dictionary<int, AnimatorControllerParameterType>();
        private int pathIndex;
        private Vector2 lastStuckCheckPosition;
        private float nextStuckCheckTime;
        private bool hasTarget;
        private Direction8 facingDirection = Direction8.South;
        private RuntimeAnimatorController cachedAnimatorController;
        private Vector2 frozenAnimatorDirection;
        private float animatorPlaybackSpeed = 1f;
        private bool animatorFrozenOnStillFrame;

        public bool HasTarget => hasTarget;
        public Vector2 TargetPosition => targetPosition;
        public bool CanMove => canMove;
        public Direction8 FacingDirection => facingDirection;
        public Collider2D BodyCollider => bodyCollider;

        public event Action<NpcMovementAgent> TargetReached;
        public event Action<Direction8> FacingDirectionChanged;

        private void Reset()
        {
            Rigidbody2D resetBody = GetComponent<Rigidbody2D>();
            resetBody.gravityScale = 0f;
            resetBody.freezeRotation = true;
        }

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

            CacheAnimatorParameters(true);

            if (pathfinder == null)
            {
                pathfinder = FindFirstObjectByType<WorldGridPathfinder>();
            }

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
            UpdateTemporaryNpcCollisionIgnores();

            if (!canMove || !hasTarget)
            {
                body.linearVelocity = Vector2.zero;
                UpdateAnimator(Vector2.zero, canMove && !hasTarget);
                return;
            }

            Vector2 currentNavigationPosition = GetNavigationPosition();
            TrySkipReachableWaypoints(currentNavigationPosition);

            Vector2 currentWaypoint = GetCurrentWaypoint();
            Vector2 desiredBodyPosition = NavigationToBodyPosition(currentWaypoint);
            Vector2 toTarget = desiredBodyPosition - body.position;
            float distanceToWaypoint = Vector2.Distance(currentWaypoint, currentNavigationPosition);
            float reachDistance = GetCurrentReachDistance();
            if (distanceToWaypoint <= reachDistance)
            {
                if (TryAdvanceWaypoint())
                {
                    return;
                }

                CompleteMovement();
                return;
            }

            CheckForStuck(currentNavigationPosition);
            float speedScale = GetWaypointSpeedScale(distanceToWaypoint, reachDistance);
            Vector2 moveDirection = GetCardinalMoveDirection(toTarget);
            Vector2 velocity = moveDirection * (moveSpeed * speedScale);
            body.linearVelocity = velocity;
            SetFacingDirection(Direction8Utility.FromVector(velocity, facingDirection));
            UpdateAnimator(velocity);
        }

        private void OnDisable()
        {
            npcCollisionStartedRealtimeByCollider.Clear();
            EndTemporaryNpcCollisionIgnoresFor(bodyCollider);
        }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            TrackNpcCollision(collision);
        }

        private void OnCollisionStay2D(Collision2D collision)
        {
            TrackNpcCollision(collision);
        }

        private void OnCollisionExit2D(Collision2D collision)
        {
            if (TryGetNpcBodyCollision(collision, out _, out Collider2D otherBodyCollider))
            {
                npcCollisionStartedRealtimeByCollider.Remove(otherBodyCollider);
            }
        }

        public void MoveTo(Vector2 position)
        {
            targetPosition = position;
            hasTarget = true;
            path.Clear();
            pathIndex = 0;
            ResetStuckCheck();

            if (!usePathfinding || pathfinder == null)
            {
                return;
            }

            if (pathfinder.TryFindPath(GetNavigationPosition(), position, bodyCollider, GetPathfindingRadius(), path))
            {
                pathIndex = Mathf.Min(1, path.Count - 1);
                return;
            }

            path.Clear();
            if (!fallBackToDirectMovement)
            {
                Stop();
            }

            if (logPathFailures)
            {
                Debug.LogWarning($"[NPC Movement] {name}: pathfinding failed, moving directly to {position}.", this);
            }
        }

        public void Stop()
        {
            hasTarget = false;
            path.Clear();
            pathIndex = 0;
            ResetStuckCheck();
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
            }
            UpdateAnimator(Vector2.zero, canMove);
        }

        public void Face(Vector2 worldPosition)
        {
            Vector2 direction = worldPosition - (Vector2)transform.position;
            if (direction.sqrMagnitude > 0.0001f)
            {
                SetFacingDirection(Direction8Utility.FromVector(direction, facingDirection));
                UpdateAnimator(Vector2.zero, false);
            }
        }

        public void SetCanMove(bool value)
        {
            pauseReasons.Clear();
            canMove = value;
            if (!canMove)
            {
                Stop();
            }
        }

        public void PauseMovement(bool paused)
        {
            SetPause("default", paused);
        }

        public void SetPause(string reason, bool paused)
        {
            string key = string.IsNullOrWhiteSpace(reason) ? "default" : reason;
            if (paused)
            {
                pauseReasons.Add(key);
            }
            else
            {
                pauseReasons.Remove(key);
            }

            canMove = pauseReasons.Count == 0;
            if (!canMove && body != null)
            {
                body.linearVelocity = Vector2.zero;
                UpdateAnimator(Vector2.zero, false);
            }
        }

        private void TrackNpcCollision(Collision2D collision)
        {
            if (!temporarilyIgnoreNpcCollisionWhenBlocked
                || npcCollisionHoldSeconds <= 0f
                || npcCollisionIgnoreSeconds <= 0f
                || !TryGetNpcBodyCollision(collision, out NpcMovementAgent otherAgent, out Collider2D otherBodyCollider))
            {
                return;
            }

            if (IsNpcCollisionTemporarilyIgnored(bodyCollider, otherBodyCollider))
            {
                npcCollisionStartedRealtimeByCollider.Remove(otherBodyCollider);
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (!npcCollisionStartedRealtimeByCollider.TryGetValue(otherBodyCollider, out float startedRealtime))
            {
                npcCollisionStartedRealtimeByCollider[otherBodyCollider] = now;
                return;
            }

            if (now - startedRealtime < npcCollisionHoldSeconds)
            {
                return;
            }

            npcCollisionStartedRealtimeByCollider.Remove(otherBodyCollider);
            otherAgent.npcCollisionStartedRealtimeByCollider.Remove(bodyCollider);
            BeginTemporaryNpcCollisionIgnore(bodyCollider, otherBodyCollider, npcCollisionIgnoreSeconds);

            if (logNpcCollisionUnsticking)
            {
                Debug.Log(
                    $"[NPC Movement] {name}: temporarily ignoring collision with {otherAgent.name} for {npcCollisionIgnoreSeconds:0.0}s after {npcCollisionHoldSeconds:0.0}s contact.",
                    this);
            }
        }

        private bool TryGetNpcBodyCollision(Collision2D collision, out NpcMovementAgent otherAgent, out Collider2D otherBodyCollider)
        {
            otherAgent = null;
            otherBodyCollider = null;

            if (collision == null || bodyCollider == null || bodyCollider.isTrigger)
            {
                return false;
            }

            Collider2D first = collision.collider;
            Collider2D second = collision.otherCollider;
            Collider2D candidate = first == bodyCollider ? second : second == bodyCollider ? first : null;
            if (candidate == null || candidate.isTrigger)
            {
                return false;
            }

            otherAgent = candidate.GetComponentInParent<NpcMovementAgent>();
            if (otherAgent == null || otherAgent == this || otherAgent.bodyCollider == null || otherAgent.bodyCollider.isTrigger)
            {
                otherAgent = null;
                return false;
            }

            if (candidate != otherAgent.bodyCollider)
            {
                otherAgent = null;
                return false;
            }

            otherBodyCollider = otherAgent.bodyCollider;
            return true;
        }

        private static void BeginTemporaryNpcCollisionIgnore(Collider2D first, Collider2D second, float seconds)
        {
            if (first == null || second == null || first == second)
            {
                return;
            }

            CollisionPairKey key = new CollisionPairKey(first, second);
            float restoreAtRealtime = Time.realtimeSinceStartup + Mathf.Max(0.05f, seconds);
            if (activeNpcCollisionIgnores.TryGetValue(key, out TemporaryNpcCollisionIgnore activeIgnore))
            {
                activeIgnore.RestoreAtRealtime = Mathf.Max(activeIgnore.RestoreAtRealtime, restoreAtRealtime);
                return;
            }

            Physics2D.IgnoreCollision(first, second, true);
            activeNpcCollisionIgnores[key] = new TemporaryNpcCollisionIgnore(first, second, restoreAtRealtime);
        }

        private static bool IsNpcCollisionTemporarilyIgnored(Collider2D first, Collider2D second)
        {
            return first != null
                && second != null
                && activeNpcCollisionIgnores.ContainsKey(new CollisionPairKey(first, second));
        }

        private static void UpdateTemporaryNpcCollisionIgnores()
        {
            if (activeNpcCollisionIgnores.Count == 0)
            {
                return;
            }

            expiredNpcCollisionIgnoreKeys.Clear();
            float now = Time.realtimeSinceStartup;
            foreach (KeyValuePair<CollisionPairKey, TemporaryNpcCollisionIgnore> pair in activeNpcCollisionIgnores)
            {
                TemporaryNpcCollisionIgnore activeIgnore = pair.Value;
                if (activeIgnore == null
                    || activeIgnore.First == null
                    || activeIgnore.Second == null
                    || now >= activeIgnore.RestoreAtRealtime)
                {
                    expiredNpcCollisionIgnoreKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < expiredNpcCollisionIgnoreKeys.Count; i++)
            {
                EndTemporaryNpcCollisionIgnore(expiredNpcCollisionIgnoreKeys[i]);
            }
        }

        private static void EndTemporaryNpcCollisionIgnoresFor(Collider2D collider)
        {
            if (collider == null || activeNpcCollisionIgnores.Count == 0)
            {
                return;
            }

            expiredNpcCollisionIgnoreKeys.Clear();
            foreach (KeyValuePair<CollisionPairKey, TemporaryNpcCollisionIgnore> pair in activeNpcCollisionIgnores)
            {
                TemporaryNpcCollisionIgnore activeIgnore = pair.Value;
                if (activeIgnore == null || activeIgnore.First == collider || activeIgnore.Second == collider)
                {
                    expiredNpcCollisionIgnoreKeys.Add(pair.Key);
                }
            }

            for (int i = 0; i < expiredNpcCollisionIgnoreKeys.Count; i++)
            {
                EndTemporaryNpcCollisionIgnore(expiredNpcCollisionIgnoreKeys[i]);
            }
        }

        private static void EndTemporaryNpcCollisionIgnore(CollisionPairKey key)
        {
            if (!activeNpcCollisionIgnores.TryGetValue(key, out TemporaryNpcCollisionIgnore activeIgnore))
            {
                return;
            }

            activeNpcCollisionIgnores.Remove(key);
            if (activeIgnore != null && activeIgnore.First != null && activeIgnore.Second != null)
            {
                Physics2D.IgnoreCollision(activeIgnore.First, activeIgnore.Second, false);
            }
        }

        private Vector2 GetCurrentWaypoint()
        {
            if (path.Count == 0)
            {
                return targetPosition;
            }

            if (pathIndex < 0 || pathIndex >= path.Count)
            {
                return targetPosition;
            }

            return path[pathIndex];
        }

        private Vector2 GetNavigationPosition()
        {
            if (bodyCollider == null)
            {
                return body.position;
            }

            return bodyCollider.bounds.center;
        }

        private Vector2 GetBodyToNavigationOffset()
        {
            return GetNavigationPosition() - body.position;
        }

        private Vector2 NavigationToBodyPosition(Vector2 navigationPosition)
        {
            return navigationPosition - GetBodyToNavigationOffset();
        }

        private float GetPathfindingRadius()
        {
            if (!useColliderSizeForPathfindingRadius)
            {
                return Mathf.Max(0.01f, navigationRadius) + colliderClearancePadding;
            }

            if (bodyCollider == null)
            {
                return pathfinder != null ? pathfinder.AgentRadius + colliderClearancePadding : colliderClearancePadding;
            }

            Bounds bounds = bodyCollider.bounds;
            float halfWidth = bounds.extents.x;
            float halfHeight = bounds.extents.y;
            return Mathf.Max(halfWidth, halfHeight, 0.01f) + colliderClearancePadding;
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

        private float GetCurrentReachDistance()
        {
            float colliderCompensation = GetPathfindingRadius() * WaypointReachRadiusFactor;
            return Mathf.Max(Mathf.Max(stoppingDistance, waypointReachDistance), colliderCompensation);
        }

        private float GetWaypointSpeedScale(float distanceToWaypoint, float reachDistance)
        {
            float slowdownRadius = Mathf.Max(reachDistance * CornerSlowdownRadiusFactor, GetPathfindingRadius());
            if (distanceToWaypoint >= slowdownRadius)
            {
                return 1f;
            }

            float normalizedDistance = Mathf.Clamp01(distanceToWaypoint / Mathf.Max(slowdownRadius, 0.01f));
            return Mathf.Lerp(MinimumCornerSpeedFactor, 1f, normalizedDistance);
        }

        private void TrySkipReachableWaypoints(Vector2 currentPosition)
        {
            if (!usePathfinding || pathfinder == null || path.Count <= 2 || pathIndex >= path.Count - 1)
            {
                return;
            }

            for (int i = path.Count - 1; i > pathIndex; i--)
            {
                if (pathfinder.HasClearSegment(currentPosition, path[i], bodyCollider, GetPathfindingRadius()))
                {
                    pathIndex = i;
                    return;
                }
            }
        }

        private bool TryAdvanceWaypoint()
        {
            if (path.Count == 0 || pathIndex >= path.Count - 1)
            {
                return false;
            }

            pathIndex++;
            return true;
        }

        private static Vector2 GetCardinalMoveDirection(Vector2 delta)
        {
            if (delta.sqrMagnitude <= 0.0001f)
            {
                return Vector2.zero;
            }

            return Mathf.Abs(delta.x) >= Mathf.Abs(delta.y)
                ? new Vector2(Mathf.Sign(delta.x), 0f)
                : new Vector2(0f, Mathf.Sign(delta.y));
        }

        private void CheckForStuck(Vector2 currentPosition)
        {
            if (!usePathfinding || pathfinder == null || path.Count == 0 || Time.time < nextStuckCheckTime)
            {
                return;
            }

            float movedDistance = Vector2.Distance(currentPosition, lastStuckCheckPosition);
            lastStuckCheckPosition = currentPosition;
            nextStuckCheckTime = Time.time + stuckCheckInterval;

            if (movedDistance >= stuckDistance)
            {
                return;
            }

            if (pathfinder.TryFindPath(currentPosition, targetPosition, bodyCollider, GetPathfindingRadius(), path))
            {
                pathIndex = Mathf.Min(1, path.Count - 1);
                ResetStuckCheck();
                return;
            }

            if (logPathFailures)
            {
                Debug.LogWarning($"[NPC Movement] {name}: stuck near {currentPosition}, could not rebuild path to {targetPosition}.", this);
            }
        }

        private void ResetStuckCheck()
        {
            if (body == null)
            {
                return;
            }

            lastStuckCheckPosition = GetNavigationPosition();
            nextStuckCheckTime = Time.time + stuckCheckInterval;
        }

        private void CompleteMovement()
        {
            body.linearVelocity = Vector2.zero;
            hasTarget = false;
            path.Clear();
            pathIndex = 0;
            ResetStuckCheck();
            UpdateAnimator(Vector2.zero, true);
            TargetReached?.Invoke(this);
        }

        private void SetFacingDirection(Direction8 direction)
        {
            if (facingDirection == direction)
            {
                return;
            }

            facingDirection = direction;
            FacingDirectionChanged?.Invoke(facingDirection);
        }

        private void UpdateAnimator(Vector2 velocity, bool useDefaultIdleFacing = false)
        {
            if (animator == null)
            {
                return;
            }

            CacheAnimatorParameters(false);

            bool isMoving = velocity.sqrMagnitude > 0.0001f;
            Direction8 animationDirection = useDefaultIdleFacing ? Direction8.South : facingDirection;
            Vector2 facingVector = Direction8Utility.ToVector(animationDirection);
            float speed = velocity.magnitude;

            SetAnimatorFloat(MoveXHash, facingVector.x);
            SetAnimatorFloat(MoveYHash, facingVector.y);
            SetAnimatorFloat(DirectionXHash, facingVector.x);
            SetAnimatorFloat(DirectionYHash, facingVector.y);
            SetAnimatorFloat(SpeedHash, speed);
            SetAnimatorInteger(DirectionHash, (int)animationDirection);

            if (isMoving)
            {
                ResumeAnimatorPlayback();
            }
            else
            {
                FreezeAnimatorAtStillFrame(facingVector);
            }
        }

        private void CacheAnimatorParameters(bool force)
        {
            if (animator == null)
            {
                return;
            }

            RuntimeAnimatorController controller = animator.runtimeAnimatorController;
            if (!force && cachedAnimatorController == controller)
            {
                return;
            }

            cachedAnimatorController = controller;
            animatorParameterTypes.Clear();

            if (controller == null)
            {
                return;
            }

            if (!animatorFrozenOnStillFrame && animator.speed > 0f)
            {
                animatorPlaybackSpeed = animator.speed;
            }

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                if (parameter == null || animatorParameterTypes.ContainsKey(parameter.nameHash))
                {
                    continue;
                }

                animatorParameterTypes.Add(parameter.nameHash, parameter.type);
            }
        }

        private void ResumeAnimatorPlayback()
        {
            if (!animatorFrozenOnStillFrame)
            {
                return;
            }

            animator.speed = animatorPlaybackSpeed > 0f ? animatorPlaybackSpeed : 1f;
            animatorFrozenOnStillFrame = false;
        }

        private void FreezeAnimatorAtStillFrame(Vector2 facingVector)
        {
            bool shouldResample = !animatorFrozenOnStillFrame
                || (frozenAnimatorDirection - facingVector).sqrMagnitude > 0.0001f;

            if (!shouldResample)
            {
                return;
            }

            if (!animatorFrozenOnStillFrame && animator.speed > 0f)
            {
                animatorPlaybackSpeed = animator.speed;
            }

            if (animatorFrozenOnStillFrame)
            {
                animator.speed = animatorPlaybackSpeed > 0f ? animatorPlaybackSpeed : 1f;
            }

            if (animator.layerCount > 0)
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                if (stateInfo.fullPathHash != 0)
                {
                    animator.Play(stateInfo.fullPathHash, 0, 0f);
                    animator.Update(0f);
                }
            }

            frozenAnimatorDirection = facingVector;
            animator.speed = 0f;
            animatorFrozenOnStillFrame = true;
        }

        private void SetAnimatorFloat(int parameterHash, float value)
        {
            if (TryGetAnimatorParameter(parameterHash, out AnimatorControllerParameterType type)
                && type == AnimatorControllerParameterType.Float)
            {
                animator.SetFloat(parameterHash, value);
            }
        }

        private void SetAnimatorInteger(int parameterHash, int value)
        {
            if (TryGetAnimatorParameter(parameterHash, out AnimatorControllerParameterType type)
                && type == AnimatorControllerParameterType.Int)
            {
                animator.SetInteger(parameterHash, value);
            }
        }

        private bool TryGetAnimatorParameter(int parameterHash, out AnimatorControllerParameterType type)
        {
            CacheAnimatorParameters(false);
            return animatorParameterTypes.TryGetValue(parameterHash, out type);
        }

        private readonly struct CollisionPairKey : IEquatable<CollisionPairKey>
        {
            private readonly int firstId;
            private readonly int secondId;

            public CollisionPairKey(Collider2D first, Collider2D second)
            {
                int a = first != null ? first.GetInstanceID() : 0;
                int b = second != null ? second.GetInstanceID() : 0;
                if (a <= b)
                {
                    firstId = a;
                    secondId = b;
                }
                else
                {
                    firstId = b;
                    secondId = a;
                }
            }

            public bool Equals(CollisionPairKey other)
            {
                return firstId == other.firstId && secondId == other.secondId;
            }

            public override bool Equals(object obj)
            {
                return obj is CollisionPairKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (firstId * 397) ^ secondId;
                }
            }
        }

        private sealed class TemporaryNpcCollisionIgnore
        {
            public TemporaryNpcCollisionIgnore(Collider2D first, Collider2D second, float restoreAtRealtime)
            {
                First = first;
                Second = second;
                RestoreAtRealtime = restoreAtRealtime;
            }

            public Collider2D First { get; }
            public Collider2D Second { get; }
            public float RestoreAtRealtime { get; set; }
        }
    }
}
