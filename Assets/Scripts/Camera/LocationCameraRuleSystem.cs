using CityStateSim.Locations;
using UnityEngine;

namespace CityStateSim.Camera
{
    public sealed class LocationCameraRuleSystem : MonoBehaviour
    {
        [SerializeField] private LocationSystem locationSystem;
        [SerializeField] private CameraController2D cameraController;

        private void Awake()
        {
            if (locationSystem == null)
            {
                locationSystem = FindFirstObjectByType<LocationSystem>();
            }

            if (cameraController == null)
            {
                cameraController = FindFirstObjectByType<CameraController2D>();
            }
        }

        private void OnEnable()
        {
            if (locationSystem != null)
            {
                locationSystem.CurrentLocationChanged += HandleCurrentLocationChanged;
            }
        }

        private void OnDisable()
        {
            if (locationSystem != null)
            {
                locationSystem.CurrentLocationChanged -= HandleCurrentLocationChanged;
            }
        }

        private void HandleCurrentLocationChanged(LocationDefinition location)
        {
            if (location == null || cameraController == null)
            {
                return;
            }

            LocationCameraRule[] rules = FindObjectsByType<LocationCameraRule>(FindObjectsSortMode.None);
            for (int i = 0; i < rules.Length; i++)
            {
                if (rules[i].Location == location)
                {
                    rules[i].Apply(cameraController);
                    return;
                }
            }
        }
    }
}
