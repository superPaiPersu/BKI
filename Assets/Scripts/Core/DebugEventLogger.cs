using CityStateSim.Locations;
using CityStateSim.NPC;
using CityStateSim.Schedule;
using UnityEngine;

namespace CityStateSim.Core
{
    public sealed class DebugEventLogger : MonoBehaviour
    {
        [SerializeField] private GameClock clock;
        [SerializeField] private LocationSystem locationSystem;
        [SerializeField] private ScheduleSystem scheduleSystem;
        [SerializeField] private bool logClock;
        [SerializeField] private bool logLocation = true;
        [SerializeField] private bool logSchedule = true;

        private void Awake()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (locationSystem == null)
            {
                locationSystem = FindFirstObjectByType<LocationSystem>();
            }

            if (scheduleSystem == null)
            {
                scheduleSystem = FindFirstObjectByType<ScheduleSystem>();
            }
        }

        private void OnEnable()
        {
            if (clock != null)
            {
                clock.MinuteChanged += HandleMinuteChanged;
                clock.DayChanged += HandleDayChanged;
            }

            if (locationSystem != null)
            {
                locationSystem.CurrentLocationChanged += HandleCurrentLocationChanged;
            }

            if (scheduleSystem != null)
            {
                scheduleSystem.ScheduleResolved += HandleScheduleResolved;
            }
        }

        private void OnDisable()
        {
            if (clock != null)
            {
                clock.MinuteChanged -= HandleMinuteChanged;
                clock.DayChanged -= HandleDayChanged;
            }

            if (locationSystem != null)
            {
                locationSystem.CurrentLocationChanged -= HandleCurrentLocationChanged;
            }

            if (scheduleSystem != null)
            {
                scheduleSystem.ScheduleResolved -= HandleScheduleResolved;
            }
        }

        private void HandleMinuteChanged(GameDate date, GameTime time)
        {
            if (logClock)
            {
                Debug.Log($"[Clock] {date} {time}", this);
            }
        }

        private void HandleDayChanged(GameDate date)
        {
            Debug.Log($"[Clock] New day: {date}", this);
        }

        private void HandleCurrentLocationChanged(LocationDefinition location)
        {
            if (logLocation)
            {
                string locationName = location != null ? location.DisplayName : "None";
                Debug.Log($"[Location] Player current location: {locationName}", this);
            }
        }

        private void HandleScheduleResolved(NpcRuntimeState npc, ScheduleEntry entry)
        {
            if (!logSchedule || npc == null || entry == null)
            {
                return;
            }

            string npcName = npc.Profile != null ? npc.Profile.DisplayName : npc.name;
            string locationName = entry.TargetLocation != null ? entry.TargetLocation.DisplayName : "None";
            Debug.Log($"[Schedule] {npcName}: {entry.GetActionName()} at {locationName}", npc);
        }
    }
}
