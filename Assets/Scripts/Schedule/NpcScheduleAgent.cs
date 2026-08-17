using System;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Schedule
{
    [RequireComponent(typeof(NpcRuntimeState))]
    public sealed class NpcScheduleAgent : MonoBehaviour
    {
        [SerializeField] private NpcSchedule schedule;

        private NpcRuntimeState runtimeState;

        public NpcSchedule Schedule => schedule;
        public NpcRuntimeState RuntimeState => runtimeState;

        private void Awake()
        {
            runtimeState = GetComponent<NpcRuntimeState>();
            if (schedule == null)
            {
                schedule = FindScheduleForProfile(runtimeState != null ? runtimeState.Profile : null);
            }
        }

        private static NpcSchedule FindScheduleForProfile(NpcProfile profile)
        {
            if (profile == null || string.IsNullOrWhiteSpace(profile.NpcId))
            {
                return null;
            }

            NpcSchedule[] schedules = Resources.LoadAll<NpcSchedule>("NpcConfig");
            for (int i = 0; i < schedules.Length; i++)
            {
                NpcSchedule candidate = schedules[i];
                if (candidate == null || candidate.Npc == null)
                {
                    continue;
                }

                if (string.Equals(candidate.Npc.NpcId, profile.NpcId, StringComparison.OrdinalIgnoreCase))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
