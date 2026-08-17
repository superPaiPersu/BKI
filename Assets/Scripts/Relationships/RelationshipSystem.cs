using System;
using System.Collections.Generic;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Relationships
{
    public sealed class RelationshipSystem : MonoBehaviour
    {
        [SerializeField] private string playerActorId = "player";
        [SerializeField] private bool logChanges = true;

        private readonly Dictionary<string, RelationshipRecord> records = new Dictionary<string, RelationshipRecord>();
        private readonly Dictionary<string, ImpressionRecord> impressions = new Dictionary<string, ImpressionRecord>();

        public string PlayerActorId => playerActorId;

        public event Action<RelationshipRecord> RelationshipChanged;
        public event Action<ImpressionRecord> ImpressionChanged;

        public RelationshipRecord GetOrCreate(string sourceActorId, string targetActorId)
        {
            sourceActorId = NormalizeActorId(sourceActorId);
            targetActorId = NormalizeActorId(targetActorId);
            string key = BuildKey(sourceActorId, targetActorId);
            if (!records.TryGetValue(key, out RelationshipRecord record))
            {
                record = new RelationshipRecord(sourceActorId, targetActorId);
                records.Add(key, record);
            }

            return record;
        }

        public RelationshipRecord GetOrCreateToPlayer(NpcProfile profile)
        {
            return GetOrCreate(GetNpcId(profile), playerActorId);
        }

        public void ApplyDelta(string sourceActorId, string targetActorId, int trustDelta, int affinityDelta, int suspicionDelta)
        {
            RelationshipRecord record = GetOrCreate(sourceActorId, targetActorId);
            record.ApplyDelta(trustDelta, affinityDelta, suspicionDelta);
            RelationshipChanged?.Invoke(record);

            if (logChanges)
            {
                Debug.Log($"[Relationship] {record.SourceActorId}->{record.TargetActorId}: {record.ToSummary()}", this);
            }
        }

        public void ApplyPlayerDelta(NpcProfile profile, int trustDelta, int affinityDelta, int suspicionDelta)
        {
            ApplyDelta(GetNpcId(profile), playerActorId, trustDelta, affinityDelta, suspicionDelta);
        }

        public ImpressionRecord GetOrCreateImpression(string sourceActorId, string targetActorId)
        {
            sourceActorId = NormalizeActorId(sourceActorId);
            targetActorId = NormalizeActorId(targetActorId);
            string key = BuildKey(sourceActorId, targetActorId);
            if (!impressions.TryGetValue(key, out ImpressionRecord record))
            {
                record = new ImpressionRecord(sourceActorId, targetActorId);
                impressions.Add(key, record);
            }

            return record;
        }

        public void ApplyImpressionDelta(
            string sourceActorId,
            string targetActorId,
            int cleanlinessDelta,
            int reliabilityDelta,
            int warmthDelta,
            int competenceDelta,
            int charmDelta,
            int concernDelta,
            string reason)
        {
            ImpressionRecord record = GetOrCreateImpression(sourceActorId, targetActorId);
            record.ApplyDelta(cleanlinessDelta, reliabilityDelta, warmthDelta, competenceDelta, charmDelta, concernDelta, reason);
            ImpressionChanged?.Invoke(record);

            if (logChanges)
            {
                Debug.Log($"[Impression] {record.SourceActorId}->{record.TargetActorId}: {record.ToSummary()}", this);
            }
        }

        public string GetSummary(string sourceActorId, string targetActorId)
        {
            RelationshipRecord relationship = GetOrCreate(sourceActorId, targetActorId);
            ImpressionRecord impression = GetOrCreateImpression(sourceActorId, targetActorId);
            return $"{relationship.ToSummary()}; impression: {impression.ToSummary()}";
        }

        public string GetPlayerSummary(NpcProfile profile)
        {
            return GetOrCreateToPlayer(profile).ToSummary();
        }

        private static string BuildKey(string sourceActorId, string targetActorId)
        {
            return $"{sourceActorId}->{targetActorId}";
        }

        private static string NormalizeActorId(string actorId)
        {
            return string.IsNullOrWhiteSpace(actorId) ? "unknown" : actorId.Trim();
        }

        private static string GetNpcId(NpcProfile profile)
        {
            return profile != null && !string.IsNullOrWhiteSpace(profile.NpcId) ? profile.NpcId : "unknown_npc";
        }
    }
}
