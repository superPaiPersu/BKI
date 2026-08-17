using System;
using CityStateSim.Core;
using UnityEngine;
using UnityEngine.Events;

namespace CityStateSim.UI
{
    public sealed class DaySettlementSignalBinder : MonoBehaviour
    {
        [Serializable]
        public sealed class SettlementTextEvent : UnityEvent<string>
        {
        }

        [Header("References")]
        [SerializeField] private GameClock clock;

        [Header("Settlement Events")]
        [SerializeField] private UnityEvent settlementStarted = new UnityEvent();
        [SerializeField] private SettlementTextEvent settlementStartedWithDate = new SettlementTextEvent();
        [SerializeField] private UnityEvent settlementFinished = new UnityEvent();
        [SerializeField] private SettlementTextEvent settlementFinishedWithDate = new SettlementTextEvent();

        public UnityEvent SettlementStarted => settlementStarted;
        public SettlementTextEvent SettlementStartedWithDate => settlementStartedWithDate;
        public UnityEvent SettlementFinished => settlementFinished;
        public SettlementTextEvent SettlementFinishedWithDate => settlementFinishedWithDate;

        private void Awake()
        {
            ResolveClock();
        }

        private void OnEnable()
        {
            ResolveClock();
            if (clock == null)
            {
                return;
            }

            clock.DaySettlementStarted += HandleSettlementStarted;
            clock.DaySettlementFinished += HandleSettlementFinished;
        }

        private void OnDisable()
        {
            if (clock == null)
            {
                return;
            }

            clock.DaySettlementStarted -= HandleSettlementStarted;
            clock.DaySettlementFinished -= HandleSettlementFinished;
        }

        public void EndCurrentDayNow()
        {
            ResolveClock();
            clock?.EndCurrentDayNow();
        }

        public void SkipCurrentDay()
        {
            ResolveClock();
            clock?.SkipCurrentDay();
        }

        public void ContinueAfterDaySettlement()
        {
            ResolveClock();
            clock?.ContinueAfterDaySettlement();
        }

        private void HandleSettlementStarted(GameDate endingDate)
        {
            settlementStarted?.Invoke();
            settlementStartedWithDate?.Invoke(GameTimeFormatter.FormatDate(endingDate));
        }

        private void HandleSettlementFinished(GameDate endingDate)
        {
            settlementFinished?.Invoke();
            settlementFinishedWithDate?.Invoke(GameTimeFormatter.FormatDate(endingDate));
        }

        private void ResolveClock()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }
        }
    }
}
