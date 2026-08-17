using System;
using CityStateSim.Core;
using CityStateSim.Locations;
using UnityEngine;

namespace CityStateSim.Schedule
{
    [Serializable]
    public sealed class RuntimeScheduleEntry
    {
        [SerializeField] private string label;
        [SerializeField] private GameTime startTime;
        [SerializeField] private GameTime endTime;
        [SerializeField] private LocationDefinition targetLocation;
        [SerializeField] private string actionName;
        [SerializeField] private int priority;
        [SerializeField] private bool interruptible = true;
        [SerializeField] private ScheduleSource source;
        [SerializeField] private string reason;

        public string Label => label;
        public GameTime StartTime => startTime;
        public GameTime EndTime => endTime;
        public LocationDefinition TargetLocation => targetLocation;
        public string ActionName => actionName;
        public int Priority => priority;
        public bool Interruptible => interruptible;
        public ScheduleSource Source => source;
        public string Reason => reason;

        public RuntimeScheduleEntry(
            string label,
            GameTime startTime,
            GameTime endTime,
            LocationDefinition targetLocation,
            string actionName,
            int priority,
            bool interruptible,
            ScheduleSource source,
            string reason)
        {
            this.label = label;
            this.startTime = startTime;
            this.endTime = endTime;
            this.targetLocation = targetLocation;
            this.actionName = actionName;
            this.priority = priority;
            this.interruptible = interruptible;
            this.source = source;
            this.reason = reason;
        }

        public static RuntimeScheduleEntry FromBaseEntry(ScheduleEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            return new RuntimeScheduleEntry(
                entry.Label,
                entry.StartTime,
                entry.EndTime,
                entry.TargetLocation,
                entry.GetActionName(),
                entry.Priority,
                entry.Interruptible,
                ScheduleSource.BaseSchedule,
                "base schedule");
        }

        public bool Contains(GameTime time)
        {
            int start = startTime.TotalMinutes;
            int end = endTime.TotalMinutes;
            int current = time.TotalMinutes;

            if (start == end)
            {
                return false;
            }

            if (start < end)
            {
                return current >= start && current < end;
            }

            return current >= start || current < end;
        }
    }
}
