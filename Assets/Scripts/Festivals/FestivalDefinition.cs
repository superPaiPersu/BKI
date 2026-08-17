using CityStateSim.Core;
using CityStateSim.Locations;
using UnityEngine;

namespace CityStateSim.Festivals
{
    [CreateAssetMenu(menuName = "City State Sim/Festivals/Festival")]
    public sealed class FestivalDefinition : ScriptableObject
    {
        [SerializeField] private string festivalId;
        [SerializeField] private string displayName;
        [SerializeField] private bool useMonthDayDate;
        [SerializeField, Range(1, 12)] private int month = 1;
        [SerializeField, Range(1, 31)] private int dayOfMonth = 1;
        [SerializeField] private int season = 1;
        [SerializeField] private int day = 1;
        [SerializeField] private LocationDefinition mainLocation;
        [SerializeField, TextArea] private string ruleSummary;
        [SerializeField] private bool oppositeDay;
        [SerializeField] private GameTime startTime = new GameTime(6, 0);
        [SerializeField] private GameTime endTime = new GameTime(23, 59);

        public string FestivalId => festivalId;
        public string DisplayName => displayName;
        public bool UseMonthDayDate => useMonthDayDate;
        public int Month => month;
        public int DayOfMonth => dayOfMonth;
        public int Season => season;
        public int Day => day;
        public LocationDefinition MainLocation => mainLocation;
        public string RuleSummary => ruleSummary;
        public bool OppositeDay => oppositeDay;
        public GameTime StartTime => startTime;
        public GameTime EndTime => endTime;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(festivalId))
            {
                festivalId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }

            month = GameCalendar.ClampMonth(month);
            dayOfMonth = Mathf.Clamp(dayOfMonth, 1, GameCalendar.GetDaysInMonth(month));
            season = Mathf.Clamp(season, 1, 4);
            day = Mathf.Clamp(day, 1, 92);
        }

        public bool IsActive(GameDate date, GameTime time)
        {
            bool dateMatches = useMonthDayDate
                ? date.Month == month && date.DayOfMonth == dayOfMonth
                : date.Season == season && date.DayOfSeason == day;
            return dateMatches && Contains(time);
        }

        private bool Contains(GameTime time)
        {
            int start = startTime.TotalMinutes;
            int end = endTime.TotalMinutes;
            int current = time.TotalMinutes;
            if (start == end)
            {
                return true;
            }

            if (start < end)
            {
                return current >= start && current <= end;
            }

            return current >= start || current <= end;
        }
    }
}
