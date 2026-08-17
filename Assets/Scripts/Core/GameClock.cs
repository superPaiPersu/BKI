using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace CityStateSim.Core
{
    public interface IDayEndSettlementTask
    {
        bool IsDayEndSettlementRunning { get; }
    }

    public sealed class GameClock : MonoBehaviour
    {
        [Header("Start")]
        [SerializeField] private int startYear = 1;
        [FormerlySerializedAs("startSeason")]
        [SerializeField, Range(1, 12)] private int startMonth = 1;
        [SerializeField, Range(1, 31)] private int startDay = 1;
        [SerializeField, Range(0, 23)] private int startHour = 6;
        [SerializeField, Range(0, 59)] private int startMinute;

        [Header("Clock")]
        [SerializeField] private bool useGameMinutesPerRealSecondScale = true;
        [SerializeField, Min(0.001f)] private float gameMinutesPerRealSecondAtOneX = 1f;
        [SerializeField, Min(0f)] private float realSecondsPerGameMinute = 1f;
        [SerializeField, Min(0f)] private float timeMultiplier = 1f;
        [SerializeField, Min(0.01f)] private float maxTimeMultiplier = 20f;
        [SerializeField] private bool runOnStart = true;
        [SerializeField] private bool waitForDayEndSettlementTasks = true;
        [SerializeField] private bool waitForDaySettlementContinueSignal = true;
        [SerializeField, Min(0f)] private float maxDayEndSettlementWaitSeconds = 180f;
        [SerializeField] private bool logDayEndSettlementTimeout = true;

        private float elapsedRealSeconds;
        private GameDate currentDate;
        private GameTime currentTime;
        private bool isRunning;
        private bool isCompletingDay;
        private bool isWaitingForDaySettlementContinue;
        private bool daySettlementContinueRequested;

        public GameDate CurrentDate => currentDate;
        public GameTime CurrentTime => currentTime;
        public bool IsRunning => isRunning;
        public bool IsCompletingDay => isCompletingDay;
        public bool IsWaitingForDaySettlementContinue => isWaitingForDaySettlementContinue;
        public float GameMinutesPerRealSecondAtOneX => gameMinutesPerRealSecondAtOneX;
        public float RealSecondsPerGameMinute => realSecondsPerGameMinute;
        public float TimeMultiplier => timeMultiplier;
        public float MaxTimeMultiplier => maxTimeMultiplier;
        public int DaysPerYear => GameCalendar.DaysPerYear;
        public int MonthsPerYear => GameCalendar.MonthsPerYear;
        public float EffectiveRealSecondsPerGameMinute => timeMultiplier > 0f ? realSecondsPerGameMinute / timeMultiplier : float.PositiveInfinity;
        public float EffectiveGameMinutesPerRealSecond => realSecondsPerGameMinute > 0f ? timeMultiplier / realSecondsPerGameMinute : 0f;

        public event Action<GameDate> DayEnding;
        public event Action<GameDate, GameTime> MinuteChanged;
        public event Action<GameDate> DayChanged;
        public event Action<GameDate> DaySettlementStarted;
        public event Action<GameDate> DaySettlementFinished;
        public event Action<bool> RunningChanged;
        public event Action<float> TimeMultiplierChanged;

        private void Awake()
        {
            ClampClockSettings();
            currentDate = new GameDate(startYear, startMonth, startDay);
            currentTime = new GameTime(startHour, startMinute);
            isRunning = runOnStart;
        }

        private void OnValidate()
        {
            ClampClockSettings();
        }

        private void Update()
        {
            if (!isRunning || isCompletingDay || realSecondsPerGameMinute <= 0f || timeMultiplier <= 0f)
            {
                return;
            }

            elapsedRealSeconds += Time.deltaTime * timeMultiplier;
            while (elapsedRealSeconds >= realSecondsPerGameMinute)
            {
                elapsedRealSeconds -= realSecondsPerGameMinute;
                AdvanceMinutes(1);
            }
        }

        public void SetRunning(bool running)
        {
            if (isRunning == running)
            {
                return;
            }

            isRunning = running;
            RunningChanged?.Invoke(isRunning);
        }

        public void Pause()
        {
            SetRunning(false);
        }

        public void Resume()
        {
            SetRunning(true);
        }

        public void ToggleRunning()
        {
            SetRunning(!isRunning);
        }

        public void SetRealSecondsPerGameMinute(float seconds)
        {
            useGameMinutesPerRealSecondScale = false;
            realSecondsPerGameMinute = Mathf.Max(0f, seconds);
            if (realSecondsPerGameMinute > 0f)
            {
                gameMinutesPerRealSecondAtOneX = 1f / realSecondsPerGameMinute;
            }

            elapsedRealSeconds = 0f;
        }

        public void SetGameMinutesPerRealSecondAtOneX(float gameMinutes)
        {
            useGameMinutesPerRealSecondScale = true;
            gameMinutesPerRealSecondAtOneX = Mathf.Max(0.001f, gameMinutes);
            ApplyBaseTimeScale();
            elapsedRealSeconds = 0f;
        }

        public void SetTimeMultiplier(float multiplier)
        {
            float clamped = Mathf.Clamp(multiplier, 0f, maxTimeMultiplier);
            if (Mathf.Approximately(timeMultiplier, clamped))
            {
                return;
            }

            timeMultiplier = clamped;
            TimeMultiplierChanged?.Invoke(timeMultiplier);
        }

        public void ResetTimeMultiplier()
        {
            SetTimeMultiplier(1f);
        }

        public void SetMaxTimeMultiplier(float value)
        {
            maxTimeMultiplier = Mathf.Max(0.01f, value);
            SetTimeMultiplier(timeMultiplier);
        }

        public void SetTime(GameDate date, GameTime time)
        {
            currentDate = date;
            currentTime = time;
            elapsedRealSeconds = 0f;
            MinuteChanged?.Invoke(currentDate, currentTime);
        }

        public void SkipCurrentDay()
        {
            if (isWaitingForDaySettlementContinue)
            {
                ContinueAfterDaySettlement();
                return;
            }

            BeginCompleteCurrentDay();
        }

        public void EndCurrentDayNow()
        {
            if (isWaitingForDaySettlementContinue)
            {
                ContinueAfterDaySettlement();
                return;
            }

            BeginCompleteCurrentDay();
        }

        public void ContinueAfterDaySettlement()
        {
            if (!isWaitingForDaySettlementContinue)
            {
                return;
            }

            daySettlementContinueRequested = true;
        }

        public void AdvanceMinutes(int minutes)
        {
            if (minutes <= 0 || isCompletingDay)
            {
                return;
            }

            for (int i = 0; i < minutes; i++)
            {
                int nextMinute = currentTime.TotalMinutes + 1;
                if (nextMinute >= 1440)
                {
                    BeginCompleteCurrentDay();
                    return;
                }
                else
                {
                    currentTime = GameTime.FromTotalMinutes(nextMinute);
                    MinuteChanged?.Invoke(currentDate, currentTime);
                }
            }
        }

        public GameDate GetNextDate(GameDate date)
        {
            return GameCalendar.GetNextDate(date);
        }

        private void AdvanceDay()
        {
            currentDate = GetNextDate(currentDate);
            DayChanged?.Invoke(currentDate);
        }

        private void BeginCompleteCurrentDay()
        {
            if (isCompletingDay)
            {
                return;
            }

            StartCoroutine(CompleteCurrentDayRoutine());
        }

        private IEnumerator CompleteCurrentDayRoutine()
        {
            isCompletingDay = true;
            bool wasRunning = isRunning;
            if (wasRunning)
            {
                SetRunning(false);
            }

            elapsedRealSeconds = 0f;
            GameDate endingDate = currentDate;
            DaySettlementStarted?.Invoke(endingDate);
            DayEnding?.Invoke(endingDate);

            if (waitForDayEndSettlementTasks)
            {
                yield return null;
                yield return WaitForDayEndSettlementTasks(endingDate);
            }

            DaySettlementFinished?.Invoke(endingDate);

            if (waitForDaySettlementContinueSignal)
            {
                yield return WaitForDaySettlementContinue();
            }

            currentTime = GameTime.FromTotalMinutes(0);
            AdvanceDay();
            MinuteChanged?.Invoke(currentDate, currentTime);

            isCompletingDay = false;
            if (wasRunning)
            {
                SetRunning(true);
            }
        }

        private IEnumerator WaitForDayEndSettlementTasks(GameDate endingDate)
        {
            List<IDayEndSettlementTask> tasks = CollectDayEndSettlementTasks();
            float startRealtime = Time.realtimeSinceStartup;
            while (HasRunningDayEndSettlementTasks(tasks))
            {
                if (maxDayEndSettlementWaitSeconds > 0f
                    && Time.realtimeSinceStartup - startRealtime >= maxDayEndSettlementWaitSeconds)
                {
                    if (logDayEndSettlementTimeout)
                    {
                        Debug.LogWarning($"[Clock] Day-end settlement timed out for {endingDate} after {maxDayEndSettlementWaitSeconds:0.#} seconds.", this);
                    }

                    yield break;
                }

                yield return null;
            }
        }

        private IEnumerator WaitForDaySettlementContinue()
        {
            isWaitingForDaySettlementContinue = true;
            daySettlementContinueRequested = false;

            while (!daySettlementContinueRequested && global::DayOverCheck.IsUserInputLocked)
            {
                yield return null;
            }

            daySettlementContinueRequested = false;
            isWaitingForDaySettlementContinue = false;
        }

        private static List<IDayEndSettlementTask> CollectDayEndSettlementTasks()
        {
            List<IDayEndSettlementTask> tasks = new List<IDayEndSettlementTask>();
            MonoBehaviour[] behaviours = FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is IDayEndSettlementTask task)
                {
                    tasks.Add(task);
                }
            }

            return tasks;
        }

        private static bool HasRunningDayEndSettlementTasks(List<IDayEndSettlementTask> tasks)
        {
            if (tasks == null)
            {
                return false;
            }

            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i] != null && tasks[i].IsDayEndSettlementRunning)
                {
                    return true;
                }
            }

            return false;
        }

        private void ClampClockSettings()
        {
            startYear = Mathf.Max(1, startYear);
            startMonth = GameCalendar.ClampMonth(startMonth);
            startDay = Mathf.Clamp(startDay, 1, GameCalendar.GetDaysInMonth(startMonth));
            ApplyBaseTimeScale();
            realSecondsPerGameMinute = Mathf.Max(0f, realSecondsPerGameMinute);
            gameMinutesPerRealSecondAtOneX = Mathf.Max(0.001f, gameMinutesPerRealSecondAtOneX);
            maxTimeMultiplier = Mathf.Max(0.01f, maxTimeMultiplier);
            timeMultiplier = Mathf.Clamp(timeMultiplier, 0f, maxTimeMultiplier);
        }

        private void ApplyBaseTimeScale()
        {
            gameMinutesPerRealSecondAtOneX = Mathf.Max(0.001f, gameMinutesPerRealSecondAtOneX);
            if (useGameMinutesPerRealSecondScale)
            {
                realSecondsPerGameMinute = 1f / gameMinutesPerRealSecondAtOneX;
            }
        }
    }
}
