using System;
using CityStateSim.Core;
using UnityEngine;

namespace CityStateSim.SecondaryEvents
{
    [Serializable]
    public sealed class SecondaryEventRecord
    {
        [SerializeField] private SecondaryEventScope scope;
        [SerializeField] private string ownerActorId;
        [SerializeField] private string locationId;
        [SerializeField] private string locationName;
        [SerializeField] private string eventType;
        [SerializeField] private string subjectActorId;
        [SerializeField] private string subjectName;
        [SerializeField, TextArea] private string summary;
        [SerializeField] private string tags;
        [SerializeField, Range(0, 10)] private int importance;
        [SerializeField] private GameDate date;
        [SerializeField] private GameTime time;

        public SecondaryEventRecord(
            SecondaryEventScope scope,
            string ownerActorId,
            string locationId,
            string locationName,
            string eventType,
            string subjectActorId,
            string subjectName,
            string summary,
            string tags,
            int importance,
            GameDate date,
            GameTime time)
        {
            this.scope = scope;
            this.ownerActorId = Clean(ownerActorId);
            this.locationId = Clean(locationId);
            this.locationName = Clean(locationName);
            this.eventType = Clean(eventType);
            this.subjectActorId = Clean(subjectActorId);
            this.subjectName = Clean(subjectName);
            this.summary = Clean(summary);
            this.tags = Clean(tags);
            this.importance = Mathf.Clamp(importance, 0, 10);
            this.date = date;
            this.time = time;
        }

        public SecondaryEventScope Scope => scope;
        public string OwnerActorId => ownerActorId;
        public string LocationId => locationId;
        public string LocationName => locationName;
        public string EventType => eventType;
        public string SubjectActorId => subjectActorId;
        public string SubjectName => subjectName;
        public string Summary => summary;
        public string Tags => tags;
        public int Importance => importance;
        public GameDate Date => date;
        public GameTime Time => time;

        public string ToSummaryLine()
        {
            string scopeText = scope == SecondaryEventScope.Location
                ? $"location={locationId}"
                : $"owner={ownerActorId}";
            string subjectText = string.IsNullOrWhiteSpace(subjectActorId)
                ? string.Empty
                : $", subject={subjectActorId}";
            string locationText = scope == SecondaryEventScope.Actor && !string.IsNullOrWhiteSpace(locationId)
                ? $", at={locationId}"
                : string.Empty;

            return $"{date} {time} [{eventType}; {scopeText}{subjectText}{locationText}; importance={importance}] {summary}";
        }

        public string BuildSearchText()
        {
            return
                $"{ownerActorId} {locationId} {locationName} {eventType} " +
                $"{subjectActorId} {subjectName} {summary} {tags}";
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
