using UnityEngine;

namespace CityStateSim.Camera
{
    public sealed class CameraBounds2D : MonoBehaviour
    {
        [SerializeField] private CameraController2D cameraController;
        [SerializeField] private bool applyOnStart = true;
        [SerializeField] private bool applyEveryFrame;

        [Header("Boundary Points")]
        [SerializeField] private Transform[] boundaryPoints = new Transform[4];

        private void Awake()
        {
            if (cameraController == null)
            {
                cameraController = FindFirstObjectByType<CameraController2D>();
            }
        }

        private void Start()
        {
            if (applyOnStart)
            {
                Apply();
            }
        }

        private void LateUpdate()
        {
            if (applyEveryFrame)
            {
                Apply();
            }
        }

        public void Apply()
        {
            if (cameraController == null)
            {
                return;
            }

            cameraController.SetBoundsFromPoints(true, boundaryPoints);
        }
    }
}
