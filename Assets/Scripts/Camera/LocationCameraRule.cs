using CityStateSim.Locations;
using UnityEngine;

namespace CityStateSim.Camera
{
    public sealed class LocationCameraRule : MonoBehaviour
    {
        [SerializeField] private LocationDefinition location;
        [SerializeField] private CameraMode2D mode = CameraMode2D.FollowTarget;

        [Header("Fixed Mode")]
        [SerializeField] private Transform fixedPoint;
        [SerializeField] private Vector2 fixedPosition;

        [Header("Bounds")]
        [SerializeField] private bool overrideBounds;
        [SerializeField] private Transform[] boundaryPoints = new Transform[4];
        [SerializeField] private Vector2 minBounds;
        [SerializeField] private Vector2 maxBounds;

        [Header("Zoom")]
        [SerializeField] private bool overrideOrthographicSize;
        [SerializeField, Min(0.1f)] private float orthographicSize = 5f;

        public LocationDefinition Location => location;

        public void Apply(CameraController2D controller)
        {
            if (controller == null)
            {
                return;
            }

            controller.SetMode(mode);

            if (mode == CameraMode2D.FixedPosition)
            {
                if (fixedPoint != null)
                {
                    controller.SetFixedPoint(fixedPoint);
                }
                else
                {
                    controller.SetFixedPosition(fixedPosition);
                }
            }

            if (overrideBounds)
            {
                if (HasBoundaryPoints())
                {
                    controller.SetBoundsFromPoints(true, boundaryPoints);
                }
                else
                {
                    controller.SetBounds(true, minBounds, maxBounds);
                }
            }

            if (overrideOrthographicSize)
            {
                controller.SetOrthographicSize(orthographicSize);
            }
        }

        private bool HasBoundaryPoints()
        {
            if (boundaryPoints == null)
            {
                return false;
            }

            for (int i = 0; i < boundaryPoints.Length; i++)
            {
                if (boundaryPoints[i] != null)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
