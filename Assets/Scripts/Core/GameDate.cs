using System;
using UnityEngine;

namespace CityStateSim.Core
{
    public enum GameSeason
    {
        Spring = 1,
        Summer = 2,
        Autumn = 3,
        Winter = 4
    }

    public enum GameWeekday
    {
        Monday = 1,
        Tuesday = 2,
        Wednesday = 3,
        Thursday = 4,
        Friday = 5,
        Saturday = 6,
        Sunday = 7
    }

    public static class GameCalendar
    {
        public const int MonthsPerYear = 12;
        public const int DaysPerWeek = 7;
        public const int DaysPerYear = 365;

        private static readonly int[] MonthLengths =
        {
            31, 28, 31, 30, 31, 30,
            31, 31, 30, 31, 30, 31
        };

        private static readonly string[] MonthNames =
        {
            "January", "February", "March", "April", "May", "June",
            "July", "August", "September", "October", "November", "December"
        };

        public static int ClampMonth(int month)
        {
            return Mathf.Clamp(month, 1, MonthsPerYear);
        }

        public static int GetDaysInMonth(int month)
        {
            return MonthLengths[ClampMonth(month) - 1];
        }

        public static GameDate GetNextDate(GameDate date)
        {
            return AddDays(date, 1);
        }

        public static GameDate AddDays(GameDate date, int days)
        {
            int absoluteDay = ToAbsoluteDayIndex(date) + days;
            if (absoluteDay < 0)
            {
                absoluteDay = 0;
            }

            int year = absoluteDay / DaysPerYear + 1;
            int dayOfYear = absoluteDay % DaysPerYear + 1;
            return FromDayOfYear(year, dayOfYear);
        }

        public static int ToAbsoluteDayIndex(GameDate date)
        {
            return (Mathf.Max(1, date.Year) - 1) * DaysPerYear + Mathf.Clamp(date.DayOfYear, 1, DaysPerYear) - 1;
        }

        public static int DaysBetween(GameDate from, GameDate to)
        {
            return ToAbsoluteDayIndex(to) - ToAbsoluteDayIndex(from);
        }

        public static GameDate FromDayOfYear(int year, int dayOfYear)
        {
            int remaining = Mathf.Clamp(dayOfYear, 1, DaysPerYear);
            for (int month = 1; month <= MonthsPerYear; month++)
            {
                int daysInMonth = GetDaysInMonth(month);
                if (remaining <= daysInMonth)
                {
                    return new GameDate(year, month, remaining);
                }

                remaining -= daysInMonth;
            }

            return new GameDate(year, MonthsPerYear, GetDaysInMonth(MonthsPerYear));
        }

        public static int GetDayOfYear(int month, int dayOfMonth)
        {
            month = ClampMonth(month);
            int day = Mathf.Clamp(dayOfMonth, 1, GetDaysInMonth(month));
            int total = day;
            for (int i = 1; i < month; i++)
            {
                total += GetDaysInMonth(i);
            }

            return total;
        }

        public static int GetWeekOfYear(int dayOfYear)
        {
            return Mathf.Clamp((Mathf.Clamp(dayOfYear, 1, DaysPerYear) - 1) / DaysPerWeek + 1, 1, 53);
        }

        public static GameWeekday GetWeekday(int year, int dayOfYear)
        {
            int absoluteDayIndex = (Mathf.Max(1, year) - 1) * DaysPerYear + Mathf.Clamp(dayOfYear, 1, DaysPerYear) - 1;
            return (GameWeekday)(absoluteDayIndex % DaysPerWeek + 1);
        }

        public static GameSeason GetSeason(int month)
        {
            month = ClampMonth(month);
            if (month <= 3)
            {
                return GameSeason.Spring;
            }

            if (month <= 6)
            {
                return GameSeason.Summer;
            }

            if (month <= 9)
            {
                return GameSeason.Autumn;
            }

            return GameSeason.Winter;
        }

        public static int GetDayOfSeason(int month, int dayOfMonth)
        {
            month = ClampMonth(month);
            int seasonStartMonth = ((month - 1) / 3) * 3 + 1;
            int day = Mathf.Clamp(dayOfMonth, 1, GetDaysInMonth(month));
            for (int i = seasonStartMonth; i < month; i++)
            {
                day += GetDaysInMonth(i);
            }

            return day;
        }

        public static string GetMonthName(int month)
        {
            return MonthNames[ClampMonth(month) - 1];
        }
    }

    [Serializable]
    public struct GameDate : IComparable<GameDate>, IEquatable<GameDate>
    {
        [SerializeField] private int year;
        [SerializeField] private int month;
        [SerializeField] private int day;

        public int Year => Mathf.Max(1, year);
        public int Month => GameCalendar.ClampMonth(month);
        public int Day => DayOfMonth;
        public int DayOfMonth => Mathf.Clamp(day, 1, GameCalendar.GetDaysInMonth(Month));
        public int DayOfYear => GameCalendar.GetDayOfYear(Month, DayOfMonth);
        public int WeekOfYear => GameCalendar.GetWeekOfYear(DayOfYear);
        public GameWeekday Weekday => GameCalendar.GetWeekday(Year, DayOfYear);
        public int WeekdayNumber => (int)Weekday;
        public int Season => (int)SeasonType;
        public GameSeason SeasonType => GameCalendar.GetSeason(Month);
        public int DayOfSeason => GameCalendar.GetDayOfSeason(Month, DayOfMonth);
        public string MonthName => GameCalendar.GetMonthName(Month);
        public string SeasonName => SeasonType.ToString();
        public string WeekdayName => Weekday.ToString();
        public string Key => $"{Year}:{Month:00}:{DayOfMonth:00}";

        public GameDate(int year, int month, int day)
        {
            this.year = Mathf.Max(1, year);
            this.month = GameCalendar.ClampMonth(month);
            this.day = Mathf.Clamp(day, 1, GameCalendar.GetDaysInMonth(this.month));
        }

        public static GameDate FromDayOfYear(int year, int dayOfYear)
        {
            return GameCalendar.FromDayOfYear(year, dayOfYear);
        }

        public int CompareTo(GameDate other)
        {
            int yearComparison = Year.CompareTo(other.Year);
            return yearComparison != 0 ? yearComparison : DayOfYear.CompareTo(other.DayOfYear);
        }

        public bool Equals(GameDate other)
        {
            return Year == other.Year && Month == other.Month && DayOfMonth == other.DayOfMonth;
        }

        public override bool Equals(object obj)
        {
            return obj is GameDate other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Year, Month, DayOfMonth);
        }

        public string ToLongString()
        {
            return $"Y{Year} {MonthName} {DayOfMonth:00}, {WeekdayName}, Week {WeekOfYear}, {SeasonName} day {DayOfSeason}";
        }

        public override string ToString()
        {
            return $"Y{Year} M{Month:00} D{DayOfMonth:00} {WeekdayName}";
        }
    }
}
