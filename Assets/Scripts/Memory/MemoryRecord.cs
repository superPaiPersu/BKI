using System;
using CityStateSim.Core;
using UnityEngine;

namespace CityStateSim.Memory
{
    [Serializable]
    public sealed class MemoryRecord
    {
        [SerializeField] private string ownerActorId;
        [SerializeField] private string summary;
        [SerializeField] private string source;
        [SerializeField] private int importance = 1;
        [SerializeField] private GameDate date;
        [SerializeField] private GameTime time;
        [SerializeField] private MemoryRetentionType retentionType = MemoryRetentionType.ShortTerm;
        [SerializeField] private string targetActorId;
        [SerializeField] private string tags;

        public string OwnerActorId => ownerActorId;
        public string Summary => summary;
        public string Source => source;
        public int Importance => importance;
        public GameDate Date => date;
        public GameTime Time => time;
        public MemoryRetentionType RetentionType => retentionType;
        public string TargetActorId => targetActorId;
        public string Tags => tags;

        public MemoryRecord(string ownerActorId, string summary, string source, int importance, GameDate date, GameTime time)
        {
            this.ownerActorId = ownerActorId;
            this.summary = summary;
            this.source = source;
            this.importance = Mathf.Clamp(importance, 1, 10);
            this.date = date;
            this.time = time;
        }

        public void MarkLongTerm(string newTags = "")
        {
            retentionType = MemoryRetentionType.LongTerm;
            if (!string.IsNullOrWhiteSpace(newTags))
            {
                tags = newTags;
            }
        }

        public void MarkDiscarded()
        {
            retentionType = MemoryRetentionType.Discarded;
        }

        public void SetTargetActor(string actorId)
        {
            targetActorId = actorId;
        }

        public void SetTags(string value)
        {
            tags = value;
        }

        public string ToSummaryLine()
        {
            string sourceText = string.IsNullOrWhiteSpace(source) ? string.Empty : $" ({source})";
            string targetText = string.IsNullOrWhiteSpace(targetActorId) ? string.Empty : $" target={targetActorId}";
            string retentionText = retentionType == MemoryRetentionType.ShortTerm ? string.Empty : $" retention={retentionType}";
            return $"{date} {time}: {summary}{sourceText}{targetText}{retentionText}";
        }
    }
}
