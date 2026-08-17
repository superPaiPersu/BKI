using System;

namespace CityStateSim.AI
{
    [Serializable]
    public sealed class NpcMemoryConsolidationResponse
    {
        public string summary;
        public string[] keepMemoryKeywords;
        public string[] discardMemoryKeywords;
        public NpcImpressionDeltaAiEntry[] impressionChanges;
        public NpcRelationshipDeltaAiEntry[] relationshipChanges;
    }

    [Serializable]
    public sealed class NpcImpressionDeltaAiEntry
    {
        public string targetActorId;
        public int cleanlinessDelta;
        public int reliabilityDelta;
        public int warmthDelta;
        public int competenceDelta;
        public int charmDelta;
        public int concernDelta;
        public string reason;
    }

    [Serializable]
    public sealed class NpcRelationshipDeltaAiEntry
    {
        public string targetActorId;
        public int trustDelta;
        public int affinityDelta;
        public int suspicionDelta;
        public string reason;
    }
}
