using System;
using UnityEngine;

namespace CityStateSim.Relationships
{
    [Serializable]
    public sealed class ImpressionRecord
    {
        [SerializeField] private string sourceActorId;
        [SerializeField] private string targetActorId;
        [SerializeField] private int cleanliness;
        [SerializeField] private int reliability;
        [SerializeField] private int warmth;
        [SerializeField] private int competence;
        [SerializeField] private int charm;
        [SerializeField] private int concern;
        [SerializeField] private string notes;

        public string SourceActorId => sourceActorId;
        public string TargetActorId => targetActorId;
        public int Cleanliness => cleanliness;
        public int Reliability => reliability;
        public int Warmth => warmth;
        public int Competence => competence;
        public int Charm => charm;
        public int Concern => concern;
        public string Notes => notes;

        public ImpressionRecord(string sourceActorId, string targetActorId)
        {
            this.sourceActorId = sourceActorId;
            this.targetActorId = targetActorId;
        }

        public void ApplyDelta(
            int cleanlinessDelta,
            int reliabilityDelta,
            int warmthDelta,
            int competenceDelta,
            int charmDelta,
            int concernDelta,
            string reason)
        {
            cleanliness = Clamp(cleanliness + cleanlinessDelta);
            reliability = Clamp(reliability + reliabilityDelta);
            warmth = Clamp(warmth + warmthDelta);
            competence = Clamp(competence + competenceDelta);
            charm = Clamp(charm + charmDelta);
            concern = Clamp(concern + concernDelta);

            if (!string.IsNullOrWhiteSpace(reason))
            {
                notes = reason.Trim();
            }
        }

        public string ToSummary()
        {
            string noteText = string.IsNullOrWhiteSpace(notes) ? string.Empty : $", notes: {notes}";
            return $"cleanliness {cleanliness}, reliability {reliability}, warmth {warmth}, competence {competence}, charm {charm}, concern {concern}{noteText}";
        }

        private static int Clamp(int value)
        {
            return Mathf.Clamp(value, -100, 100);
        }
    }
}
