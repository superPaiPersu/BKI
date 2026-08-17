using System;
using System.Collections.Generic;
using System.Text;
using CityStateSim.Core;
using CityStateSim.Locations;
using CityStateSim.Movement;
using CityStateSim.NPC;
using CityStateSim.Perception;
using UnityEngine;

namespace CityStateSim.SecondaryEvents
{
    public sealed class SecondaryEventSystem : MonoBehaviour
    {
        [Serializable]
        public sealed class LocationEventAccessRule
        {
            [SerializeField] private string npcId;
            [SerializeField] private LocationDefinition[] readableLocations;
            [SerializeField] private string[] readableLocationIds;

            public string NpcId => CleanId(npcId);

            public bool Allows(string actorId, string locationId)
            {
                if (string.IsNullOrWhiteSpace(actorId)
                    || string.IsNullOrWhiteSpace(locationId)
                    || !string.Equals(NpcId, CleanId(actorId), StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                string cleanLocationId = CleanId(locationId);
                if (readableLocations != null)
                {
                    for (int i = 0; i < readableLocations.Length; i++)
                    {
                        LocationDefinition location = readableLocations[i];
                        if (location != null
                            && string.Equals(location.LocationId, cleanLocationId, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                if (readableLocationIds != null)
                {
                    for (int i = 0; i < readableLocationIds.Length; i++)
                    {
                        if (string.Equals(CleanId(readableLocationIds[i]), cleanLocationId, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            public void AppendReadableLocationIds(StringBuilder builder)
            {
                if (builder == null)
                {
                    return;
                }

                HashSet<string> added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (readableLocations != null)
                {
                    for (int i = 0; i < readableLocations.Length; i++)
                    {
                        LocationDefinition location = readableLocations[i];
                        if (location == null || string.IsNullOrWhiteSpace(location.LocationId) || !added.Add(location.LocationId))
                        {
                            continue;
                        }

                        builder.Append("- locationId=");
                        builder.Append(location.LocationId);
                        builder.Append(", name=");
                        builder.Append(location.DisplayName);
                        builder.Append(", type=");
                        builder.Append(location.Type);
                        builder.AppendLine();
                    }
                }

                if (readableLocationIds == null)
                {
                    return;
                }

                for (int i = 0; i < readableLocationIds.Length; i++)
                {
                    string id = CleanId(readableLocationIds[i]);
                    if (string.IsNullOrWhiteSpace(id) || !added.Add(id))
                    {
                        continue;
                    }

                    builder.Append("- locationId=");
                    builder.Append(id);
                    builder.AppendLine();
                }
            }
        }

        [Header("References")]
        [SerializeField] private GameClock clock;
        [SerializeField] private LocationSystem locationSystem;

        [Header("Sources")]
        [SerializeField] private bool recordLocationEntries = true;
        [SerializeField] private bool recordLocationExits = true;
        [SerializeField] private bool recordNpcPerceptionObservations = true;

        [Header("Access")]
        [SerializeField] private LocationEventAccessRule[] locationAccessRules;

        [Header("Retention")]
        [SerializeField, Min(1)] private int maxActorEventsPerNpc = 80;
        [SerializeField, Min(1)] private int maxEventsPerLocation = 160;
        [SerializeField, Min(1)] private int defaultMaxQueryResults = 8;
        [SerializeField, Min(0f)] private float repeatedLocationEventSuppressionSeconds = 2f;
        [SerializeField, Min(0f)] private float repeatedPerceptionSuppressionSeconds = 30f;

        [Header("Debug")]
        [SerializeField] private bool logWrites;

        private readonly Dictionary<string, List<SecondaryEventRecord>> actorEventsByNpc = new Dictionary<string, List<SecondaryEventRecord>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<SecondaryEventRecord>> locationEventsByLocation = new Dictionary<string, List<SecondaryEventRecord>>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> lastPerceptionSignatureByObserverAndEntity = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> lastPerceptionRealtimeByObserverAndEntity = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> lastLocationEventRealtimeByKey = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly List<NpcPerceptionSensor> subscribedSensors = new List<NpcPerceptionSensor>();

        private float nextSensorRegistrationRealtime;

        private void Awake()
        {
            ResolveReferences();
        }

        private void OnEnable()
        {
            ResolveReferences();
            SubscribeLocationSystem();
            RegisterSceneSensors();
        }

        private void Update()
        {
            if (!recordNpcPerceptionObservations || Time.realtimeSinceStartup < nextSensorRegistrationRealtime)
            {
                return;
            }

            nextSensorRegistrationRealtime = Time.realtimeSinceStartup + 5f;
            RegisterSceneSensors();
        }

        private void OnDisable()
        {
            UnsubscribeLocationSystem();
            UnsubscribeSensors();
        }

        public SecondaryEventRecord AddActorEvent(
            string ownerActorId,
            string summary,
            string eventType = "minor_event",
            int importance = 1,
            string subjectActorId = "",
            string subjectName = "",
            LocationDefinition location = null,
            string tags = "")
        {
            ownerActorId = CleanId(ownerActorId);
            if (string.IsNullOrWhiteSpace(ownerActorId) || string.IsNullOrWhiteSpace(summary))
            {
                return null;
            }

            SecondaryEventRecord record = CreateRecord(
                SecondaryEventScope.Actor,
                ownerActorId,
                location != null ? location.LocationId : string.Empty,
                location != null ? location.DisplayName : string.Empty,
                eventType,
                subjectActorId,
                subjectName,
                summary,
                tags,
                importance);

            AddRecord(actorEventsByNpc, ownerActorId, record, maxActorEventsPerNpc);
            LogRecord(record);
            return record;
        }

        public SecondaryEventRecord AddLocationEvent(
            LocationDefinition location,
            string summary,
            string eventType = "minor_event",
            int importance = 1,
            string subjectActorId = "",
            string subjectName = "",
            string tags = "")
        {
            if (location == null)
            {
                return null;
            }

            return AddLocationEvent(
                location.LocationId,
                location.DisplayName,
                summary,
                eventType,
                importance,
                subjectActorId,
                subjectName,
                tags);
        }

        public SecondaryEventRecord AddLocationEvent(
            string locationId,
            string locationName,
            string summary,
            string eventType = "minor_event",
            int importance = 1,
            string subjectActorId = "",
            string subjectName = "",
            string tags = "")
        {
            locationId = CleanId(locationId);
            if (string.IsNullOrWhiteSpace(locationId) || string.IsNullOrWhiteSpace(summary))
            {
                return null;
            }

            SecondaryEventRecord record = CreateRecord(
                SecondaryEventScope.Location,
                string.Empty,
                locationId,
                locationName,
                eventType,
                subjectActorId,
                subjectName,
                summary,
                tags,
                importance);

            AddRecord(locationEventsByLocation, locationId, record, maxEventsPerLocation);
            LogRecord(record);
            return record;
        }

        public string BuildAccessSummaryForNpc(string npcId)
        {
            npcId = CleanId(npcId);
            if (string.IsNullOrWhiteSpace(npcId))
            {
                return "(no npc id; secondary event lookup unavailable)";
            }

            StringBuilder builder = new StringBuilder();
            builder.AppendLine("- personal secondary events: readable");
            builder.AppendLine("- location secondary events: readable only for locations configured below");

            bool hasLocationAccess = false;
            if (locationAccessRules != null)
            {
                for (int i = 0; i < locationAccessRules.Length; i++)
                {
                    LocationEventAccessRule rule = locationAccessRules[i];
                    if (rule == null || !string.Equals(rule.NpcId, npcId, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int before = builder.Length;
                    rule.AppendReadableLocationIds(builder);
                    hasLocationAccess |= builder.Length > before;
                }
            }

            if (!hasLocationAccess)
            {
                builder.AppendLine("- location access: none configured");
            }

            builder.Append("Use concise queries with exact ids when possible, for example: actorId=<actor_id> locationId=<location_id> eventType=location_entered.");
            return builder.ToString();
        }

        public string QueryForNpc(string npcId, string query, int maxResults = -1)
        {
            npcId = CleanId(npcId);
            if (string.IsNullOrWhiteSpace(npcId))
            {
                return "(lookup failed: missing npc id)";
            }

            int resultLimit = maxResults > 0 ? maxResults : defaultMaxQueryResults;
            QuerySpec spec = QuerySpec.Parse(query);
            List<ScoredRecord> scored = new List<ScoredRecord>();

            AddMatches(actorEventsByNpc, npcId, spec, scored);
            AddReadableLocationMatches(npcId, spec, scored);
            scored.Sort(CompareScoredRecords);

            StringBuilder builder = new StringBuilder();
            builder.Append("Secondary event lookup for ");
            builder.Append(npcId);
            builder.Append(" query=\"");
            builder.Append(string.IsNullOrWhiteSpace(query) ? "(empty)" : query.Trim());
            builder.AppendLine("\":");

            if (scored.Count == 0)
            {
                builder.Append("(no matched secondary events, or this NPC has no access to the matching location records)");
                return builder.ToString();
            }

            int count = Mathf.Min(resultLimit, scored.Count);
            for (int i = 0; i < count; i++)
            {
                builder.Append("- ");
                builder.AppendLine(scored[i].Record.ToSummaryLine());
            }

            return builder.ToString();
        }

        private void ResolveReferences()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (locationSystem == null)
            {
                locationSystem = FindFirstObjectByType<LocationSystem>();
            }
        }

        private void SubscribeLocationSystem()
        {
            if (locationSystem == null)
            {
                return;
            }

            locationSystem.ActorEnteredLocation -= HandleActorEnteredLocation;
            locationSystem.ActorExitedLocation -= HandleActorExitedLocation;
            locationSystem.ActorEnteredLocation += HandleActorEnteredLocation;
            locationSystem.ActorExitedLocation += HandleActorExitedLocation;
        }

        private void UnsubscribeLocationSystem()
        {
            if (locationSystem == null)
            {
                return;
            }

            locationSystem.ActorEnteredLocation -= HandleActorEnteredLocation;
            locationSystem.ActorExitedLocation -= HandleActorExitedLocation;
        }

        private void RegisterSceneSensors()
        {
            if (!recordNpcPerceptionObservations)
            {
                return;
            }

            NpcPerceptionSensor[] sensors = FindObjectsByType<NpcPerceptionSensor>(FindObjectsSortMode.None);
            for (int i = 0; i < sensors.Length; i++)
            {
                RegisterSensor(sensors[i]);
            }
        }

        private void RegisterSensor(NpcPerceptionSensor sensor)
        {
            if (sensor == null || subscribedSensors.Contains(sensor))
            {
                return;
            }

            subscribedSensors.Add(sensor);
            sensor.ObservationsRefreshed += HandleSensorObservationsRefreshed;
        }

        private void UnsubscribeSensors()
        {
            for (int i = 0; i < subscribedSensors.Count; i++)
            {
                if (subscribedSensors[i] != null)
                {
                    subscribedSensors[i].ObservationsRefreshed -= HandleSensorObservationsRefreshed;
                }
            }

            subscribedSensors.Clear();
        }

        private void HandleActorEnteredLocation(LocationDefinition location, GameObject actor)
        {
            if (!recordLocationEntries
                || location == null
                || actor == null
                || !TryResolveActor(actor, out string actorId, out string actorName, out bool isNpc))
            {
                return;
            }

            string key = $"enter:{location.LocationId}:{actorId}";
            if (IsDuplicateLocationEvent(key))
            {
                return;
            }

            AddLocationEvent(
                location,
                $"{actorName} ({actorId}) entered {location.DisplayName}.",
                "location_entered",
                1,
                actorId,
                actorName,
                "visit presence");

            if (isNpc)
            {
                AddActorEvent(
                    actorId,
                    $"I entered {location.DisplayName} ({location.LocationId}).",
                    "self_location_entered",
                    1,
                    actorId,
                    actorName,
                    location,
                    "movement visit presence");
            }
        }

        private void HandleActorExitedLocation(LocationDefinition location, GameObject actor)
        {
            if (!recordLocationExits
                || location == null
                || actor == null
                || !TryResolveActor(actor, out string actorId, out string actorName, out bool isNpc))
            {
                return;
            }

            string key = $"exit:{location.LocationId}:{actorId}";
            if (IsDuplicateLocationEvent(key))
            {
                return;
            }

            AddLocationEvent(
                location,
                $"{actorName} ({actorId}) exited {location.DisplayName}.",
                "location_exited",
                1,
                actorId,
                actorName,
                "visit presence");

            if (isNpc)
            {
                AddActorEvent(
                    actorId,
                    $"I exited {location.DisplayName} ({location.LocationId}).",
                    "self_location_exited",
                    1,
                    actorId,
                    actorName,
                    location,
                    "movement visit presence");
            }
        }

        private void HandleSensorObservationsRefreshed(NpcPerceptionSensor sensor)
        {
            if (!recordNpcPerceptionObservations || sensor == null)
            {
                return;
            }

            NpcRuntimeState observer = sensor.GetComponent<NpcRuntimeState>();
            if (observer == null || observer.Profile == null || string.IsNullOrWhiteSpace(observer.Profile.NpcId))
            {
                return;
            }

            IReadOnlyList<PerceptionObservation> observations = sensor.Observations;
            for (int i = 0; i < observations.Count; i++)
            {
                PerceptionObservation observation = observations[i];
                if (observation == null || string.IsNullOrWhiteSpace(observation.EntityId))
                {
                    continue;
                }

                string key = $"{observer.Profile.NpcId}:{observation.EntityId}";
                string previousSignature = lastPerceptionSignatureByObserverAndEntity.TryGetValue(key, out string value)
                    ? value
                    : string.Empty;
                float previousRealtime = lastPerceptionRealtimeByObserverAndEntity.TryGetValue(key, out float time)
                    ? time
                    : -999f;
                bool sameObservation = string.Equals(previousSignature, observation.Signature, StringComparison.OrdinalIgnoreCase);
                if (sameObservation && Time.realtimeSinceStartup - previousRealtime < repeatedPerceptionSuppressionSeconds)
                {
                    continue;
                }

                lastPerceptionSignatureByObserverAndEntity[key] = observation.Signature;
                lastPerceptionRealtimeByObserverAndEntity[key] = Time.realtimeSinceStartup;

                AddActorEvent(
                    observer.Profile.NpcId,
                    $"I noticed {observation.DisplayName} ({observation.EntityId}, type={observation.EntityType}) nearby: {observation.Description}.",
                    "perception_observation",
                    2,
                    observation.EntityId,
                    observation.DisplayName,
                    observer.ActualLocation,
                    $"perception {observation.Channels} {observation.EntityType}");
            }
        }

        private bool IsDuplicateLocationEvent(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return true;
            }

            float now = Time.realtimeSinceStartup;
            if (lastLocationEventRealtimeByKey.TryGetValue(key, out float previousRealtime)
                && now - previousRealtime < repeatedLocationEventSuppressionSeconds)
            {
                return true;
            }

            lastLocationEventRealtimeByKey[key] = now;
            return false;
        }

        private SecondaryEventRecord CreateRecord(
            SecondaryEventScope scope,
            string ownerActorId,
            string locationId,
            string locationName,
            string eventType,
            string subjectActorId,
            string subjectName,
            string summary,
            string tags,
            int importance)
        {
            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            GameTime time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            return new SecondaryEventRecord(
                scope,
                ownerActorId,
                locationId,
                locationName,
                string.IsNullOrWhiteSpace(eventType) ? "minor_event" : eventType,
                subjectActorId,
                subjectName,
                summary,
                tags,
                importance,
                date,
                time);
        }

        private static void AddRecord(
            Dictionary<string, List<SecondaryEventRecord>> recordsByOwner,
            string key,
            SecondaryEventRecord record,
            int maxRecords)
        {
            if (recordsByOwner == null || record == null || string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            key = CleanId(key);
            if (!recordsByOwner.TryGetValue(key, out List<SecondaryEventRecord> records))
            {
                records = new List<SecondaryEventRecord>();
                recordsByOwner.Add(key, records);
            }

            records.Add(record);
            while (records.Count > Mathf.Max(1, maxRecords))
            {
                records.RemoveAt(0);
            }
        }

        private void AddMatches(
            Dictionary<string, List<SecondaryEventRecord>> recordsByOwner,
            string ownerKey,
            QuerySpec spec,
            List<ScoredRecord> scored)
        {
            if (recordsByOwner == null
                || string.IsNullOrWhiteSpace(ownerKey)
                || !recordsByOwner.TryGetValue(ownerKey, out List<SecondaryEventRecord> records))
            {
                return;
            }

            AddMatches(records, spec, scored);
        }

        private void AddReadableLocationMatches(string npcId, QuerySpec spec, List<ScoredRecord> scored)
        {
            if (locationEventsByLocation.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<string, List<SecondaryEventRecord>> pair in locationEventsByLocation)
            {
                if (!CanAccessLocationEvents(npcId, pair.Key))
                {
                    continue;
                }

                AddMatches(pair.Value, spec, scored);
            }
        }

        private static void AddMatches(
            List<SecondaryEventRecord> records,
            QuerySpec spec,
            List<ScoredRecord> scored)
        {
            if (records == null || scored == null)
            {
                return;
            }

            for (int i = 0; i < records.Count; i++)
            {
                SecondaryEventRecord record = records[i];
                int score = spec.Score(record);
                if (score <= 0)
                {
                    continue;
                }

                scored.Add(new ScoredRecord(record, score));
            }
        }

        private bool CanAccessLocationEvents(string npcId, string locationId)
        {
            if (locationAccessRules == null)
            {
                return false;
            }

            for (int i = 0; i < locationAccessRules.Length; i++)
            {
                LocationEventAccessRule rule = locationAccessRules[i];
                if (rule != null && rule.Allows(npcId, locationId))
                {
                    return true;
                }
            }

            return false;
        }

        private static int CompareScoredRecords(ScoredRecord left, ScoredRecord right)
        {
            int scoreComparison = right.Score.CompareTo(left.Score);
            if (scoreComparison != 0)
            {
                return scoreComparison;
            }

            int dateComparison = CompareDate(right.Record.Date, left.Record.Date);
            if (dateComparison != 0)
            {
                return dateComparison;
            }

            return right.Record.Time.TotalMinutes.CompareTo(left.Record.Time.TotalMinutes);
        }

        private static int CompareDate(GameDate left, GameDate right)
        {
            return left.CompareTo(right);
        }

        private static bool TryResolveActor(GameObject actor, out string actorId, out string actorName, out bool isNpc)
        {
            actorId = string.Empty;
            actorName = string.Empty;
            isNpc = false;
            if (actor == null)
            {
                return false;
            }

            NpcRuntimeState npc = actor.GetComponentInParent<NpcRuntimeState>();
            if (npc != null && npc.Profile != null)
            {
                actorId = npc.Profile.NpcId;
                actorName = npc.Profile.DisplayName;
                isNpc = true;
                return !string.IsNullOrWhiteSpace(actorId);
            }

            PlayerMovementController player = actor.GetComponentInParent<PlayerMovementController>();
            if (player != null || actor.CompareTag("Player"))
            {
                actorId = "player";
                actorName = "Player";
                return true;
            }

            return false;
        }

        private void LogRecord(SecondaryEventRecord record)
        {
            if (logWrites && record != null)
            {
                Debug.Log($"[Secondary Event] {record.ToSummaryLine()}", this);
            }
        }

        private static string CleanId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }

        private readonly struct ScoredRecord
        {
            public ScoredRecord(SecondaryEventRecord record, int score)
            {
                Record = record;
                Score = score;
            }

            public SecondaryEventRecord Record { get; }
            public int Score { get; }
        }

        private sealed class QuerySpec
        {
            private static readonly char[] SplitChars =
            {
                ' ', '\t', '\n', '\r', ',', ';', '|'
            };

            private readonly string normalizedQuery;
            private readonly string[] terms;

            private QuerySpec(string rawQuery, string locationId, string actorId, string eventType, string[] terms)
            {
                RawQuery = rawQuery ?? string.Empty;
                LocationId = CleanId(locationId);
                ActorId = CleanId(actorId);
                EventType = CleanId(eventType);
                this.terms = terms ?? Array.Empty<string>();
                normalizedQuery = NormalizeSearchText(rawQuery);
            }

            public string RawQuery { get; }
            public string LocationId { get; }
            public string ActorId { get; }
            public string EventType { get; }

            public static QuerySpec Parse(string query)
            {
                string locationId = ExtractFilterValue(query, "locationId", "location", "placeId", "place");
                string actorId = ExtractFilterValue(query, "actorId", "subjectActorId", "subject", "npcId", "person");
                string eventType = ExtractFilterValue(query, "eventType", "type");
                string[] terms = BuildTerms(query);
                return new QuerySpec(query, locationId, actorId, eventType, terms);
            }

            public int Score(SecondaryEventRecord record)
            {
                if (record == null)
                {
                    return 0;
                }

                int score = 0;
                if (!string.IsNullOrWhiteSpace(LocationId))
                {
                    if (!string.Equals(record.LocationId, LocationId, StringComparison.OrdinalIgnoreCase))
                    {
                        return 0;
                    }

                    score += 12;
                }

                if (!string.IsNullOrWhiteSpace(ActorId))
                {
                    bool actorMatches = string.Equals(record.OwnerActorId, ActorId, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(record.SubjectActorId, ActorId, StringComparison.OrdinalIgnoreCase)
                        || ContainsNormalized(record.BuildSearchText(), ActorId);
                    if (!actorMatches)
                    {
                        return 0;
                    }

                    score += 12;
                }

                if (!string.IsNullOrWhiteSpace(EventType))
                {
                    if (!string.Equals(record.EventType, EventType, StringComparison.OrdinalIgnoreCase))
                    {
                        return 0;
                    }

                    score += 10;
                }

                string haystack = NormalizeSearchText(record.BuildSearchText());
                if (!string.IsNullOrWhiteSpace(normalizedQuery) && haystack.Contains(normalizedQuery))
                {
                    score += 6;
                }

                int termHits = 0;
                for (int i = 0; i < terms.Length; i++)
                {
                    if (ContainsNormalized(haystack, terms[i]))
                    {
                        termHits++;
                    }
                }

                score += termHits;

                bool hasFilter = !string.IsNullOrWhiteSpace(LocationId)
                    || !string.IsNullOrWhiteSpace(ActorId)
                    || !string.IsNullOrWhiteSpace(EventType);
                bool hasTerms = terms.Length > 0 || !string.IsNullOrWhiteSpace(normalizedQuery);
                if (!hasFilter && !hasTerms)
                {
                    return 1;
                }

                return score;
            }

            private static string ExtractFilterValue(string query, params string[] keys)
            {
                if (string.IsNullOrWhiteSpace(query) || keys == null)
                {
                    return string.Empty;
                }

                string[] tokens = query.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0; i < tokens.Length; i++)
                {
                    string token = tokens[i];
                    int equalsIndex = token.IndexOf('=');
                    if (equalsIndex <= 0 || equalsIndex >= token.Length - 1)
                    {
                        continue;
                    }

                    string key = token.Substring(0, equalsIndex);
                    string value = token.Substring(equalsIndex + 1);
                    for (int j = 0; j < keys.Length; j++)
                    {
                        if (string.Equals(key, keys[j], StringComparison.OrdinalIgnoreCase))
                        {
                            return value.Trim();
                        }
                    }
                }

                return string.Empty;
            }

            private static string[] BuildTerms(string query)
            {
                if (string.IsNullOrWhiteSpace(query))
                {
                    return Array.Empty<string>();
                }

                string[] rawTerms = query.Split(SplitChars, StringSplitOptions.RemoveEmptyEntries);
                List<string> cleaned = new List<string>();
                for (int i = 0; i < rawTerms.Length; i++)
                {
                    string term = rawTerms[i];
                    if (term.Contains("="))
                    {
                        continue;
                    }

                    term = NormalizeSearchText(term);
                    if (term.Length <= 1 || cleaned.Contains(term))
                    {
                        continue;
                    }

                    cleaned.Add(term);
                }

                return cleaned.ToArray();
            }

            private static bool ContainsNormalized(string text, string term)
            {
                if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(term))
                {
                    return false;
                }

                return NormalizeSearchText(text).Contains(NormalizeSearchText(term));
            }

            private static string NormalizeSearchText(string value)
            {
                return string.IsNullOrWhiteSpace(value)
                    ? string.Empty
                    : value.ToLowerInvariant()
                        .Replace(" ", string.Empty)
                        .Replace("_", string.Empty)
                        .Replace("-", string.Empty)
                        .Replace(":", string.Empty)
                        .Replace("=", string.Empty)
                        .Trim();
            }
        }
    }
}
