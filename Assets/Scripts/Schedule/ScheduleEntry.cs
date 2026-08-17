using System;
using CityStateSim.Core;
using CityStateSim.Locations;
using UnityEngine;

namespace CityStateSim.Schedule
{
    [Serializable]
    public sealed class ScheduleEntry
    {
        [SerializeField] private string label;
        [SerializeField] private GameTime startTime = new(6, 0);
        [SerializeField] private GameTime endTime = new(8, 0);
        [SerializeField] private ScheduleDateRule dateRule = new ScheduleDateRule();
        [SerializeField] private LocationDefinition targetLocation;
        [SerializeField] private ScheduleActionType actionType = ScheduleActionType.Custom;
        [SerializeField] private string customAction;
        [SerializeField] private int priority;
        [SerializeField] private bool interruptible = true;

        public string Label => label;
        public GameTime StartTime => startTime;
        public GameTime EndTime => endTime;
        public ScheduleDateRule DateRule => dateRule;
        public LocationDefinition TargetLocation => targetLocation;
        public ScheduleActionType ActionType => actionType;
        public string CustomAction => customAction;
        public int Priority => priority;
        public bool Interruptible => interruptible;

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

        public bool Matches(GameDate date, GameTime time)
        {
            return AppliesToDate(date) && Contains(time);
        }

        public bool AppliesToDate(GameDate date)
        {
            return dateRule == null || dateRule.Matches(date);
        }

        public string GetDateRuleSummary()
        {
            return dateRule != null ? dateRule.ToSummaryText() : "every day";
        }

        public string GetActionName()
        {
            if (actionType == ScheduleActionType.Custom && !string.IsNullOrWhiteSpace(customAction))
            {
                return customAction;
            }

            return actionType.ToString();
        }
    }

    [Serializable]
    public sealed class ScheduleDateRule
    {
        [SerializeField] private bool limitToWeekdays;
        [SerializeField] private GameWeekday[] weekdays;
        [SerializeField] private bool limitToMonths;
        [SerializeField] private int[] months;
        [SerializeField] private bool limitToSeasons;
        [SerializeField] private GameSeason[] seasons;
        [SerializeField] private bool limitToDaysOfMonth;
        [SerializeField] private int[] daysOfMonth;
        [SerializeField] private bool limitToDaysOfYear;
        [SerializeField] private int[] daysOfYear;

        public bool LimitToWeekdays => limitToWeekdays;
        public GameWeekday[] Weekdays => weekdays ?? Array.Empty<GameWeekday>();
        public bool LimitToMonths => limitToMonths;
        public int[] Months => months ?? Array.Empty<int>();
        public bool LimitToSeasons => limitToSeasons;
        public GameSeason[] Seasons => seasons ?? Array.Empty<GameSeason>();
        public bool LimitToDaysOfMonth => limitToDaysOfMonth;
        public int[] DaysOfMonth => daysOfMonth ?? Array.Empty<int>();
        public bool LimitToDaysOfYear => limitToDaysOfYear;
        public int[] DaysOfYear => daysOfYear ?? Array.Empty<int>();

        public bool Matches(GameDate date)
        {
            if (limitToWeekdays && !ContainsWeekday(Weekdays, date.Weekday))
            {
                return false;
            }

            if (limitToMonths && !ContainsClamped(Months, date.Month, 1, GameCalendar.MonthsPerYear))
            {
                return false;
            }

            if (limitToSeasons && !ContainsSeason(Seasons, date.SeasonType))
            {
                return false;
            }

            if (limitToDaysOfMonth && !ContainsClamped(DaysOfMonth, date.DayOfMonth, 1, GameCalendar.GetDaysInMonth(date.Month)))
            {
                return false;
            }

            if (limitToDaysOfYear && !ContainsClamped(DaysOfYear, date.DayOfYear, 1, GameCalendar.DaysPerYear))
            {
                return false;
            }

            return true;
        }

        public string ToSummaryText()
        {
            if (!HasAnyLimit())
            {
                return "every day";
            }

            System.Text.StringBuilder builder = new System.Text.StringBuilder();
            AppendPart(builder, limitToWeekdays, "weekdays", JoinEnums(Weekdays));
            AppendPart(builder, limitToMonths, "months", JoinInts(Months));
            AppendPart(builder, limitToSeasons, "seasons", JoinEnums(Seasons));
            AppendPart(builder, limitToDaysOfMonth, "monthDays", JoinInts(DaysOfMonth));
            AppendPart(builder, limitToDaysOfYear, "yearDays", JoinInts(DaysOfYear));
            return builder.Length > 0 ? builder.ToString() : "every day";
        }

        private bool HasAnyLimit()
        {
            return limitToWeekdays || limitToMonths || limitToSeasons || limitToDaysOfMonth || limitToDaysOfYear;
        }

        private static bool ContainsWeekday(GameWeekday[] values, GameWeekday target)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == target)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsSeason(GameSeason[] values, GameSeason target)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == target)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsClamped(int[] values, int target, int min, int max)
        {
            if (values == null || values.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < values.Length; i++)
            {
                if (Mathf.Clamp(values[i], min, max) == target)
                {
                    return true;
                }
            }

            return false;
        }

        private static void AppendPart(System.Text.StringBuilder builder, bool enabled, string label, string value)
        {
            if (!enabled)
            {
                return;
            }

            if (builder.Length > 0)
            {
                builder.Append("; ");
            }

            builder.Append(label);
            builder.Append("=");
            builder.Append(string.IsNullOrWhiteSpace(value) ? "(none)" : value);
        }

        private static string JoinInts(int[] values)
        {
            return values == null || values.Length == 0 ? "" : string.Join("|", values);
        }

        private static string JoinEnums<T>(T[] values)
        {
            return values == null || values.Length == 0 ? "" : string.Join("|", values);
        }
    }
}
