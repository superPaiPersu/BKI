using System.Collections.Generic;
using CityStateSim.Core;

namespace CityStateSim.Schedule
{
    public sealed class NpcDailyPlan
    {
        private readonly List<RuntimeScheduleEntry> entries = new List<RuntimeScheduleEntry>();
        private readonly List<NpcDailyIntent> intents = new List<NpcDailyIntent>();

        public string NpcId { get; }
        public GameDate Date { get; }
        public string Summary { get; private set; }
        public IReadOnlyList<RuntimeScheduleEntry> Entries => entries;
        public IReadOnlyList<NpcDailyIntent> Intents => intents;

        public NpcDailyPlan(string npcId, GameDate date, string summary)
        {
            NpcId = npcId;
            Date = date;
            Summary = summary;
        }

        public void SetSummary(string summary)
        {
            Summary = summary;
        }

        public void AddEntry(RuntimeScheduleEntry entry)
        {
            if (entry != null)
            {
                entries.Add(entry);
            }
        }

        public void AddIntent(NpcDailyIntent intent)
        {
            if (intent != null)
            {
                intents.Add(intent);
            }
        }

        public void Clear()
        {
            entries.Clear();
            intents.Clear();
        }
    }
}
