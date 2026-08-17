using CityStateSim.Movement;
using UnityEngine;

namespace CityStateSim.Camera
{
    [RequireComponent(typeof(UnityEngine.Camera))]
    public sealed class CameraController2D : MonoBehaviour
    {
        [Header("Target")]
        [SerializeField] private Transform target;
        [SerializeField] private CameraMode2D mode = CameraMode2D.FollowTarget;

        [Header("Follow")]
        [SerializeField, Min(0f)] private float smoothTime;
        [SerializeField] private Vector2 followOffset;
        [SerializeField] private bool snapToPixelGrid;
        [SerializeField, Min(1f)] private float pixelsPerUnit = 16f;

        [Header("Fixed")]
        [SerializeField] private Transform fixedPoint;
        [SerializeField] private Vector2 fixedPosition;

        [Header("Bounds")]
        [SerializeField] private bool useBounds;
        [SerializeField] private Vector2 minBounds = new Vector2(-20f, -20f);
        [SerializeField] private Vector2 maxBounds = new Vector2(20f, 20f);

        [Header("Zoom")]
        [SerializeField] private bool initializeOrthographicSizeFromCamera = true;
        [SerializeField] private bool controlOrthographicSize = true;
        [SerializeField, Min(0.1f)] private float orthographicSize = 5f;
        [SerializeField, Min(0.1f)] private float minOrthographicSize = 1f;
        [SerializeField, Min(0.1f)] private float maxOrthographicSize = 30f;

        private UnityEngine.Camera controlledCamera;
        private Vector3 velocity;

        public Transform Target => target;
        public CameraMode2D Mode => mode;
        public bool UseBounds => useBounds;
        public float OrthographicSize => controlledCamera != null ? controlledCamera.orthographicSize : orthographicSize;

        private void OnValidate()
        {
            ClampZoomSettings();
        }

        private void Awake()
        {
            controlledCamera = GetComponent<UnityEngine.Camera>();
            controlledCamera.orthographic = true;
            if (initializeOrthographicSizeFromCamera)
            {
                orthographicSize = controlledCamera.orthographicSize;
            }

            ClampZoomSettings();
            ApplyOrthographicSize();

            if (target == null)
            {
                PlayerMovementController player = FindFirstObjectByType<PlayerMovementController>();
                if (player != null)
                {
                    target = player.transform;
                }
            }
        }

        private void LateUpdate()
        {
            if (controlOrthographicSize)
            {
                ApplyOrthographicSize();
            }
            else if (controlledCamera != null)
            {
                orthographicSize = controlledCamera.orthographicSize;
            }

            Vector3 desired = GetDesiredPosition();
            desired.z = transform.position.z;
            desired = ClampToBounds(desired);
            desired = SnapToPixelGrid(desired);

            if (smoothTime <= 0f)
            {
                transform.position = desired;
            }
            else
            {
                transform.position = Vector3.SmoothDamp(transform.position, desired, ref velocity, smoothTime);
            }
        }

        public void SetTarget(Transform newTarget)
        {
            target = newTarget;
        }

        public void SetMode(CameraMode2D newMode)
        {
            mode = newMode;
        }

        public void SetFollowOffset(Vector2 offset)
        {
            followOffset = offset;
        }

        public void SetFixedPoint(Transform point)
        {
            fixedPoint = point;
            mode = CameraMode2D.FixedPosition;
        }

        public void SetFixedPosition(Vector2 position)
        {
            fixedPosition = position;
            fixedPoint = null;
            mode = CameraMode2D.FixedPosition;
        }

        public void SetBounds(bool enabled, Vector2 min, Vector2 max)
        {
            useBounds = enabled;
            minBounds = Vector2.Min(min, max);
            maxBounds = Vector2.Max(min, max);
        }

        public void SetBoundsFromPoints(bool enabled, params Transform[] points)
        {
            if (!enabled)
            {
                useBounds = false;
                return;
            }

            if (!TryGetBoundsFromPoints(points, out Vector2 min, out Vector2 max))
            {
                return;
            }

            SetBounds(true, min, max);
        }

        public void SetOrthographicSize(float size)
        {
            orthographicSize = Mathf.Clamp(size, minOrthographicSize, maxOrthographicSize);
            ApplyOrthographicSize();
        }

        public void AddOrthographicSize(float delta)
        {
            SetOrthographicSize(orthographicSize + delta);
        }

        public void SetControlOrthographicSize(bool enabled)
        {
            controlOrthographicSize = enabled;
            if (controlOrthographicSize)
            {
                ApplyOrthographicSize();
            }
            else if (controlledCamera != null)
            {
                orthographicSize = controlledCamera.orthographicSize;
            }
        }

        private Vector3 GetDesiredPosition()
        {
            if (mode == CameraMode2D.FixedPosition)
            {
                if (fixedPoint != null)
                {
                    return fixedPoint.position;
                }

                return new Vector3(fixedPosition.x, fixedPosition.y, transform.position.z);
            }

            if (target == null)
            {
                return transform.position;
            }

            Vector3 targetPosition = target.position;
            return new Vector3(targetPosition.x + followOffset.x, targetPosition.y + followOffset.y, transform.position.z);
        }

        private Vector3 ClampToBounds(Vector3 position)
        {
            if (!useBounds)
            {
                return position;
            }

            float verticalExtent = controlledCamera.orthographicSize;
            float horizontalExtent = verticalExtent * controlledCamera.aspect;

            float minX = minBounds.x + horizontalExtent;
            float maxX = maxBounds.x - horizontalExtent;
            float minY = minBounds.y + verticalExtent;
            float maxY = maxBounds.y - verticalExtent;

            if (minX > maxX)
            {
                float centerX = (minBounds.x + maxBounds.x) * 0.5f;
                minX = centerX;
                maxX = centerX;
            }

            if (minY > maxY)
            {
                float centerY = (minBounds.y + maxBounds.y) * 0.5f;
                minY = centerY;
                maxY = centerY;
            }

            position.x = Mathf.Clamp(position.x, minX, maxX);
            position.y = Mathf.Clamp(position.y, minY, maxY);
            return position;
        }

        private void ApplyOrthographicSize()
        {
            if (controlledCamera == null)
            {
                return;
            }

            ClampZoomSettings();
            controlledCamera.orthographicSize = orthographicSize;
        }

        private void ClampZoomSettings()
        {
            minOrthographicSize = Mathf.Max(0.1f, minOrthographicSize);
            maxOrthographicSize = Mathf.Max(minOrthographicSize, maxOrthographicSize);
            orthographicSize = Mathf.Clamp(orthographicSize, minOrthographicSize, maxOrthographicSize);
        }

        private Vector3 SnapToPixelGrid(Vector3 position)
        {
            if (!snapToPixelGrid)
            {
                return position;
            }

            float unit = 1f / pixelsPerUnit;
            position.x = Mathf.Round(position.x / unit) * unit;
            position.y = Mathf.Round(position.y / unit) * unit;
            return position;
        }

        private static bool TryGetBoundsFromPoints(Transform[] points, out Vector2 min, out Vector2 max)
        {
            min = Vector2.zero;
            max = Vector2.zero;
            if (points == null || points.Length == 0)
            {
                return false;
            }

            bool found = false;
            for (int i = 0; i < points.Length; i++)
            {
                Transform point = points[i];
                if (point == null)
                {
                    continue;
                }

                Vector2 position = point.position;
                if (!found)
                {
                    min = position;
                    max = position;
                    found = true;
                    continue;
                }

                min = Vector2.Min(min, position);
                max = Vector2.Max(max, position);
            }

            return found;
        }
    }
}
