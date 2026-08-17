using CityStateSim.Core;
using UnityEngine;

namespace CityStateSim.Jobs
{
    [CreateAssetMenu(menuName = "City State Sim/Jobs/Job")]
    public sealed class JobDefinition : ScriptableObject
    {
        [SerializeField] private string jobId;
        [SerializeField] private string displayName;
        [SerializeField] private ShopDefinition shop;
        [SerializeField, Min(0)] private int requiredTrust;
        [SerializeField] private GameTime shiftStart = new GameTime(9, 0);
        [SerializeField] private GameTime shiftEnd = new GameTime(12, 0);
        [SerializeField, Min(0)] private int basePay = 20;
        [SerializeField, Min(0)] private int payPerScore = 5;
        [SerializeField] private JobTaskDefinition[] tasks;

        public string JobId => jobId;
        public string DisplayName => displayName;
        public ShopDefinition Shop => shop;
        public int RequiredTrust => requiredTrust;
        public GameTime ShiftStart => shiftStart;
        public GameTime ShiftEnd => shiftEnd;
        public int BasePay => basePay;
        public int PayPerScore => payPerScore;
        public JobTaskDefinition[] Tasks => tasks;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(jobId))
            {
                jobId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }

        public bool IsAvailableAt(GameTime time)
        {
            int start = shiftStart.TotalMinutes;
            int end = shiftEnd.TotalMinutes;
            int current = time.TotalMinutes;

            if (start == end)
            {
                return true;
            }

            if (start < end)
            {
                return current >= start && current < end;
            }

            return current >= start || current < end;
        }
    }
}
