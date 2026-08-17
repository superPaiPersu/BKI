using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Schedule
{
    [CreateAssetMenu(menuName = "City State Sim/Schedule/NPC Schedule")]
    public sealed class NpcSchedule : ScriptableObject
    {
        [SerializeField] private NpcProfile npc;
        [SerializeField] private ScheduleEntry[] entries;

        public NpcProfile Npc => npc;
        public ScheduleEntry[] Entries => entries;
    }
}
