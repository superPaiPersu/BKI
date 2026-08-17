using UnityEngine;

namespace CityStateSim.NPC
{
    [CreateAssetMenu(menuName = "City State Sim/NPC/NPC Value Profile")]
    public sealed class NpcValueProfile : ScriptableObject
    {
        [SerializeField, Range(0f, 1f)] private float cleanlinessPreference = 0.5f;
        [SerializeField, Range(0f, 1f)] private float kindnessPreference = 0.7f;
        [SerializeField, Range(0f, 1f)] private float reliabilityPreference = 0.7f;
        [SerializeField, Range(0f, 1f)] private float competencePreference = 0.5f;
        [SerializeField, Range(0f, 1f)] private float charmPreference = 0.3f;
        [SerializeField, Range(0f, 1f)] private float sympathyForVulnerability = 0.5f;

        public float CleanlinessPreference => cleanlinessPreference;
        public float KindnessPreference => kindnessPreference;
        public float ReliabilityPreference => reliabilityPreference;
        public float CompetencePreference => competencePreference;
        public float CharmPreference => charmPreference;
        public float SympathyForVulnerability => sympathyForVulnerability;

        public string ToSummary()
        {
            return
                $"values: cleanliness={cleanlinessPreference:0.00}, kindness={kindnessPreference:0.00}, " +
                $"reliability={reliabilityPreference:0.00}, competence={competencePreference:0.00}, " +
                $"charm={charmPreference:0.00}, sympathy={sympathyForVulnerability:0.00}";
        }
    }
}
