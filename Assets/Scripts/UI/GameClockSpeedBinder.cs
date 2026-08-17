using CityStateSim.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace CityStateSim.UI
{
    public sealed class GameClockSpeedBinder : MonoBehaviour
    {
        [SerializeField] private GameClock clock;
        [SerializeField] private Slider multiplierSlider;
        [SerializeField] private TMP_Text multiplierText;
        [SerializeField] private bool syncSliderRangeFromClock = true;
        [SerializeField] private string multiplierFormat = "{0:0.#}x";
        [SerializeField] private bool showEffectiveTimeScale;
        [SerializeField] private string effectiveTimeScaleFormat = "1:{0:0.#}";
        [SerializeField] private string pausedText = "Paused";

        private bool suppressSliderCallback;
        private bool initializedFromClock;

        private void Awake()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }
        }

        private void OnEnable()
        {
            if (clock != null)
            {
                clock.TimeMultiplierChanged += HandleTimeMultiplierChanged;
                clock.RunningChanged += HandleRunningChanged;
            }

            Refresh();
            initializedFromClock = true;

            if (multiplierSlider != null)
            {
                multiplierSlider.onValueChanged.AddListener(SetMultiplier);
            }
        }

        private void OnDisable()
        {
            initializedFromClock = false;

            if (multiplierSlider != null)
            {
                multiplierSlider.onValueChanged.RemoveListener(SetMultiplier);
            }

            if (clock != null)
            {
                clock.TimeMultiplierChanged -= HandleTimeMultiplierChanged;
                clock.RunningChanged -= HandleRunningChanged;
            }
        }

        public void SetMultiplier(float multiplier)
        {
            if (suppressSliderCallback || !initializedFromClock || clock == null)
            {
                return;
            }

            clock.SetTimeMultiplier(multiplier);
            Refresh();
        }

        public void SetPaused(bool paused)
        {
            if (clock == null)
            {
                return;
            }

            clock.SetRunning(!paused);
            Refresh();
        }

        public void Pause()
        {
            SetPaused(true);
        }

        public void Resume()
        {
            SetPaused(false);
        }

        public void TogglePaused()
        {
            if (clock == null)
            {
                return;
            }

            clock.ToggleRunning();
            Refresh();
        }

        public void ResetMultiplier()
        {
            SetMultiplier(1f);
        }

        public void SetHalfSpeed()
        {
            SetMultiplier(0.5f);
        }

        public void SetNormalSpeed()
        {
            SetMultiplier(1f);
        }

        public void SetDoubleSpeed()
        {
            SetMultiplier(2f);
        }

        public void SetQuadrupleSpeed()
        {
            SetMultiplier(4f);
        }

        public void Refresh()
        {
            if (clock == null)
            {
                return;
            }

            if (multiplierSlider != null)
            {
                suppressSliderCallback = true;
                try
                {
                    if (syncSliderRangeFromClock)
                    {
                        multiplierSlider.minValue = 0f;
                        multiplierSlider.maxValue = clock.MaxTimeMultiplier;
                    }

                    multiplierSlider.SetValueWithoutNotify(clock.TimeMultiplier);
                }
                finally
                {
                    suppressSliderCallback = false;
                }
            }

            if (multiplierText != null)
            {
                multiplierText.text = clock.IsRunning
                    ? FormatRunningText()
                    : pausedText;
            }
        }

        private string FormatRunningText()
        {
            if (!showEffectiveTimeScale)
            {
                return string.Format(multiplierFormat, clock.TimeMultiplier);
            }

            return string.Format(effectiveTimeScaleFormat, clock.EffectiveGameMinutesPerRealSecond * 60f);
        }

        private void HandleTimeMultiplierChanged(float multiplier)
        {
            Refresh();
        }

        private void HandleRunningChanged(bool running)
        {
            Refresh();
        }
    }
}
