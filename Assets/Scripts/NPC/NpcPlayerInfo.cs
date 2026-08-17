using CityStateSim.Behavior;
using CityStateSim.Relationships;
using UnityEngine;

namespace CityStateSim.NPC
{
    [RequireComponent(typeof(NpcRuntimeState))]
    public sealed class NpcPlayerInfo : MonoBehaviour
    {
        [SerializeField] private RelationshipSystem relationshipSystem;
        [SerializeField] private string knownPlayerName = "Player";
        [SerializeField] private string moodOverride;
        [SerializeField, TextArea] private string privateThoughtAboutPlayer;

        private NpcRuntimeState runtimeState;
        private NpcBehaviorState behaviorState;

        public NpcProfile Profile => runtimeState != null ? runtimeState.Profile : null;
        public string NpcId => Profile != null ? Profile.NpcId : string.Empty;
        public string DisplayName => Profile != null ? Profile.DisplayName : name;
        public string KnownPlayerName => knownPlayerName;
        public string PrivateThoughtAboutPlayer => privateThoughtAboutPlayer;

        public string CurrentMood
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(moodOverride))
                {
                    return moodOverride;
                }

                return behaviorState != null ? behaviorState.Emotion : "neutral";
            }
        }

        public RelationshipRecord RelationshipToPlayer
        {
            get
            {
                if (relationshipSystem == null || Profile == null)
                {
                    return null;
                }

                return relationshipSystem.GetOrCreateToPlayer(Profile);
            }
        }

        public string RelationshipSummary
        {
            get
            {
                RelationshipRecord record = RelationshipToPlayer;
                return record != null ? record.ToSummary() : "unknown";
            }
        }

        public int RelationshipValue
        {
            get
            {
                RelationshipRecord record = RelationshipToPlayer;
                if (record == null)
                {
                    return 0;
                }

                return record.Trust + record.Affinity - record.Suspicion;
            }
        }

        public int RelationshipLevel
        {
            get
            {
                // RelationshipValue is roughly -200..200. UI affection is shown as 1..10.
                float normalized = Mathf.InverseLerp(-100f, 100f, RelationshipValue);
                return Mathf.Clamp(Mathf.RoundToInt(normalized * 9f) + 1, 1, 10);
            }
        }

        private void Awake()
        {
            runtimeState = GetComponent<NpcRuntimeState>();
            behaviorState = GetComponent<NpcBehaviorState>();

            if (relationshipSystem == null)
            {
                relationshipSystem = FindFirstObjectByType<RelationshipSystem>();
            }
        }

        public void SetKnownPlayerName(string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                knownPlayerName = value;
            }
        }

        public void SetMoodOverride(string value)
        {
            moodOverride = value;
        }

        public void SetPrivateThoughtAboutPlayer(string value)
        {
            privateThoughtAboutPlayer = value;
        }
    }
}
