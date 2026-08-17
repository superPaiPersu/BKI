using System;
using CityStateSim.Behavior;
using CityStateSim.Core;
using UnityEngine;

namespace CityStateSim.Festivals
{
    public sealed class FestivalSystem : MonoBehaviour
    {
        [SerializeField] private GameClock clock;
        [SerializeField] private FestivalDefinition[] festivals;
        [SerializeField] private bool requestNpcDecisionOnFestivalStart = true;
        [SerializeField] private bool logFestivalChanges = true;

        private FestivalDefinition activeFestival;

        public FestivalDefinition ActiveFestival => activeFestival;
        public bool IsOppositeDay => activeFestival != null && activeFestival.OppositeDay;

        public event Action<FestivalDefinition> FestivalStarted;
        public event Action<FestivalDefinition> FestivalEnded;

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
                clock.MinuteChanged += HandleMinuteChanged;
            }
        }

        private void Start()
        {
            RefreshFestivalState();
        }

        private void OnDisable()
        {
            if (clock != null)
            {
                clock.MinuteChanged -= HandleMinuteChanged;
            }
        }

        public string BuildRuleSummary()
        {
            if (activeFestival == null)
            {
                return string.Empty;
            }

            string oppositeText = activeFestival.OppositeDay ? " This is Opposite Day: NPCs lean toward behavior unlike their usual personality and role." : string.Empty;
            return $"{activeFestival.DisplayName}: {activeFestival.RuleSummary}{oppositeText}";
        }

        public void RefreshFestivalState()
        {
            if (clock == null)
            {
                return;
            }

            FestivalDefinition nextFestival = FindActiveFestival(clock.CurrentDate, clock.CurrentTime);
            if (nextFestival == activeFestival)
            {
                return;
            }

            FestivalDefinition previous = activeFestival;
            activeFestival = nextFestival;

            if (previous != null)
            {
                FestivalEnded?.Invoke(previous);
                if (logFestivalChanges)
                {
                    Debug.Log($"[Festival] Ended: {previous.DisplayName}", this);
                }
            }

            if (activeFestival != null)
            {
                ApplyFestivalContextToNpcs();
                FestivalStarted?.Invoke(activeFestival);
                if (logFestivalChanges)
                {
                    Debug.Log($"[Festival] Started: {activeFestival.DisplayName}", this);
                }
            }
            else
            {
                ClearFestivalContextFromNpcs();
            }
        }

        private FestivalDefinition FindActiveFestival(GameDate date, GameTime time)
        {
            if (festivals == null)
            {
                return null;
            }

            for (int i = 0; i < festivals.Length; i++)
            {
                FestivalDefinition festival = festivals[i];
                if (festival != null && festival.IsActive(date, time))
                {
                    return festival;
                }
            }

            return null;
        }

        private void ApplyFestivalContextToNpcs()
        {
            string summary = BuildRuleSummary();
            NpcBehaviorController[] controllers = FindObjectsByType<NpcBehaviorController>(FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                controllers[i].SetFestivalRuleSummary(summary);
                if (requestNpcDecisionOnFestivalStart)
                {
                    controllers[i].ForceRequestDecision();
                }
            }
        }

        private void ClearFestivalContextFromNpcs()
        {
            NpcBehaviorController[] controllers = FindObjectsByType<NpcBehaviorController>(FindObjectsSortMode.None);
            for (int i = 0; i < controllers.Length; i++)
            {
                controllers[i].SetFestivalRuleSummary(string.Empty);
            }
        }

        private void HandleMinuteChanged(GameDate date, GameTime time)
        {
            RefreshFestivalState();
        }
    }
}
