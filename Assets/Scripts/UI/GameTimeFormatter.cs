using CityStateSim.Core;

namespace CityStateSim.UI
{
    public enum GameTimeDisplayMode
    {
        TimeOnly = 0,
        DateAndTime = 1,
        DateOnly = 2
    }

    public static class GameTimeFormatter
    {
        public static string Format(GameDate date, GameTime time, GameTimeDisplayMode mode)
        {
            switch (mode)
            {
                case GameTimeDisplayMode.TimeOnly:
                    return FormatTime(time);
                case GameTimeDisplayMode.DateOnly:
                    return FormatDate(date);
                case GameTimeDisplayMode.DateAndTime:
                    return $"{FormatDate(date)} {FormatTime(time)}";
                default:
                    return FormatTime(time);
            }
        }

        public static string FormatDate(GameDate date)
        {
            return $"Day {GameCalendar.ToAbsoluteDayIndex(date) + 1}";
        }

        public static string FormatTime(GameTime time)
        {
            return $"{time.Hour:00}:{time.Minute:00}";
        }
    }
}
