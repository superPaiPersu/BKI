using System;
using CityStateSim.Locations;
using UnityEngine;
using UnityEngine.Serialization;

namespace CityStateSim.NPC
{
    public enum NpcPresenceMode
    {
        World = 0,
        InsideActivity = 1
    }

    public sealed class NpcRuntimeState : MonoBehaviour
    {
        [SerializeField] private NpcProfile profile;
        [FormerlySerializedAs("currentLocation")]
        [SerializeField] private LocationDefinition plannedLocation;
        [SerializeField] private LocationDefinition actualLocation;
        [SerializeField] private string currentAction;
        [SerializeField] private NpcPresenceMode presenceMode = NpcPresenceMode.World;
        [SerializeField] private LocationDefinition insideActivityLocation;
        [SerializeField] private string insideActivityKind;

        public NpcProfile Profile => profile;
        public LocationDefinition CurrentLocation => actualLocation != null ? actualLocation : plannedLocation;
        public LocationDefinition PlannedLocation => plannedLocation;
        public LocationDefinition ActualLocation => actualLocation;
        public string CurrentAction => currentAction;
        public NpcPresenceMode PresenceMode => presenceMode;
        public bool IsInsideActivity => presenceMode == NpcPresenceMode.InsideActivity;
        public LocationDefinition InsideActivityLocation => insideActivityLocation;
        public string InsideActivityKind => insideActivityKind;

        public event Action<NpcRuntimeState> StateChanged;
        public event Action<NpcRuntimeState> ScheduleTargetChanged;
        public event Action<NpcRuntimeState> ActualLocationChanged;
        public event Action<NpcRuntimeState> PresenceChanged;

        private void Awake()
        {
            if (GetComponent<NpcWorldPresencePresenter>() == null)
            {
                gameObject.AddComponent<NpcWorldPresencePresenter>();
            }
        }

        public void ApplyScheduleTarget(LocationDefinition location, string action)
        {
            bool changed = plannedLocation != location || currentAction != action;
            plannedLocation = location;
            currentAction = action;

            if (changed)
            {
                ScheduleTargetChanged?.Invoke(this);
                StateChanged?.Invoke(this);
            }
        }

        public void SetActualLocation(LocationDefinition location)
        {
            if (actualLocation == location)
            {
                return;
            }

            actualLocation = location;
            ActualLocationChanged?.Invoke(this);
            StateChanged?.Invoke(this);
        }

        public void ClearActualLocation(LocationDefinition location = null)
        {
            if (actualLocation == null)
            {
                return;
            }

            if (location != null && actualLocation != location)
            {
                return;
            }

            actualLocation = null;
            ActualLocationChanged?.Invoke(this);
            StateChanged?.Invoke(this);
        }

        public void EnterInsideActivity(LocationDefinition location, string activityKind)
        {
            string cleanedActivityKind = Clean(activityKind);
            bool changed = presenceMode != NpcPresenceMode.InsideActivity
                || insideActivityLocation != location
                || !string.Equals(insideActivityKind, cleanedActivityKind, StringComparison.Ordinal);

            presenceMode = NpcPresenceMode.InsideActivity;
            insideActivityLocation = location;
            insideActivityKind = cleanedActivityKind;

            if (location != null)
            {
                SetActualLocation(location);
            }

            if (!changed)
            {
                return;
            }

            PresenceChanged?.Invoke(this);
            StateChanged?.Invoke(this);
        }

        public void ExitInsideActivity()
        {
            if (presenceMode == NpcPresenceMode.World
                && insideActivityLocation == null
                && string.IsNullOrWhiteSpace(insideActivityKind))
            {
                return;
            }

            presenceMode = NpcPresenceMode.World;
            insideActivityLocation = null;
            insideActivityKind = string.Empty;
            PresenceChanged?.Invoke(this);
            StateChanged?.Invoke(this);
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }
}
