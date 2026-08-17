using System;
using UnityEngine;

namespace CityStateSim.Relationships
{
    [Serializable]
    public sealed class RelationshipRecord
    {
        [SerializeField] private string sourceActorId;
        [SerializeField] private string targetActorId;
        [SerializeField] private int trust;
        [SerializeField] private int affinity;
        [SerializeField] private int suspicion;
        [SerializeField] private string tags;

        public string SourceActorId => sourceActorId;
        public string TargetActorId => targetActorId;
        public int Trust => trust;
        public int Affinity => affinity;
        public int Suspicion => suspicion;
        public string Tags => tags;

        public RelationshipRecord(string sourceActorId, string targetActorId)
        {
            this.sourceActorId = sourceActorId;
            this.targetActorId = targetActorId;
        }

        public void ApplyDelta(int trustDelta, int affinityDelta, int suspicionDelta)
        {
            trust = Mathf.Clamp(trust + trustDelta, -100, 100);
            affinity = Mathf.Clamp(affinity + affinityDelta, -100, 100);
            suspicion = Mathf.Clamp(suspicion + suspicionDelta, 0, 100);
        }

        public void SetTags(string value)
        {
            tags = value;
        }

        public string ToSummary()
        {
            string tagText = string.IsNullOrWhiteSpace(tags) ? string.Empty : $", tags: {tags}";
            return $"trust {trust}, affinity {affinity}, suspicion {suspicion}{tagText}";
        }
    }
}
