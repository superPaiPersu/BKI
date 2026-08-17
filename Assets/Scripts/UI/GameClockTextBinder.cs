using CityStateSim.Core;
using TMPro;
using UnityEngine;

namespace CityStateSim.UI
{
    [RequireComponent(typeof(TMP_Text))]
    public sealed class GameClockTextBinder : MonoBehaviour
    {
        [SerializeField] private GameClock clock;
        [SerializeField] private TMP_Text targetText;
        [SerializeField] private GameTimeDisplayMode displayMode = GameTimeDisplayMode.DateAndTime;
        [SerializeField] private string prefix;
        [SerializeField] private string suffix;

        private void Awake()
        {
            if (targetText == null)
            {
                targetText = GetComponent<TMP_Text>();
            }

            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }
        }

        private void OnEnable()
        {
            if (clock != null)
            {
                clock.MinuteChanged += HandleMinuteChanged;
                clock.DayChanged += HandleDayChanged;
                Refresh();
            }
        }

        private void OnDisable()
        {
            if (clock != null)
            {
                clock.MinuteChanged -= HandleMinuteChanged;
                clock.DayChanged -= HandleDayChanged;
            }
        }

        public void Refresh()
        {
            if (clock == null || targetText == null)
            {
                return;
            }

            string value = GameTimeFormatter.Format(clock.CurrentDate, clock.CurrentTime, displayMode);
            targetText.text = $"{prefix}{value}{suffix}";
        }

        private void HandleMinuteChanged(GameDate date, GameTime time)
        {
            Refresh();
        }

        private void HandleDayChanged(GameDate date)
        {
            Refresh();
        }
    }
}
