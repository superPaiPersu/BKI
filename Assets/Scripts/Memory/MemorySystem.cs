using System;
using System.Collections.Generic;
using System.Text;
using CityStateSim.Core;
using UnityEngine;

namespace CityStateSim.Memory
{
    public sealed class MemorySystem : MonoBehaviour
    {
        [SerializeField] private GameClock clock;
        [SerializeField, Min(1)] private int maxMemoriesPerActor = 30;
        [SerializeField, Min(1)] private int maxLongTermMemoriesPerActor = 60;
        [SerializeField, Range(1, 10)] private int autoKeepImportanceThreshold = 7;
        [SerializeField] private bool logWrites = true;
        [SerializeField, Min(1)] private int maxFactsPerActor = 80;
        [SerializeField] private bool logFactWrites;

        private readonly Dictionary<string, List<MemoryRecord>> memoriesByActor = new Dictionary<string, List<MemoryRecord>>();
        private readonly Dictionary<string, List<NpcFactRecord>> factsByActor = new Dictionary<string, List<NpcFactRecord>>();
        private readonly Dictionary<string, List<PlayerDialogueTranscriptLine>> playerDialogueByActor = new Dictionary<string, List<PlayerDialogueTranscriptLine>>();

        public event Action<MemoryRecord> MemoryAdded;
        public event Action<NpcFactRecord> FactAdded;
        public event Action<string, GameDate> MemoriesConsolidated;

        private void Awake()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }
        }

        public MemoryRecord AddMemory(string ownerActorId, string summary, string source = "", int importance = 1)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId) || string.IsNullOrWhiteSpace(summary))
            {
                return null;
            }

            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            GameTime time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            MemoryRecord record = new MemoryRecord(ownerActorId.Trim(), summary.Trim(), source, importance, date, time);
            ApplyDefaultRetention(record);

            if (!memoriesByActor.TryGetValue(record.OwnerActorId, out List<MemoryRecord> records))
            {
                records = new List<MemoryRecord>();
                memoriesByActor.Add(record.OwnerActorId, records);
            }

            records.Add(record);
            while (records.Count > maxMemoriesPerActor)
            {
                RemoveOldestDiscardable(records);
            }

            MemoryAdded?.Invoke(record);
            if (logWrites)
            {
                Debug.Log($"[Memory] {record.OwnerActorId}: {record.ToSummaryLine()}", this);
            }

            return record;
        }

        public PlayerDialogueTranscriptLine AddPlayerDialogueLine(
            string ownerActorId,
            string speakerActorId,
            string speakerName,
            string text)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId) || string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            GameTime time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            PlayerDialogueTranscriptLine line = new PlayerDialogueTranscriptLine(
                ownerActorId.Trim(),
                CleanFactId(speakerActorId),
                CleanFactText(speakerName),
                text.Trim(),
                date,
                time);

            if (!playerDialogueByActor.TryGetValue(line.OwnerActorId, out List<PlayerDialogueTranscriptLine> lines))
            {
                lines = new List<PlayerDialogueTranscriptLine>();
                playerDialogueByActor.Add(line.OwnerActorId, lines);
            }

            lines.Add(line);
            return line;
        }

        public NpcFactRecord AddFact(
            string ownerActorId,
            string subjectActorId,
            string sourceActorId,
            string sourceActorName,
            string evidence,
            string source = "",
            int importance = 5)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId) || string.IsNullOrWhiteSpace(evidence))
            {
                return null;
            }

            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            GameTime time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            NpcFactRecord record = new NpcFactRecord(
                ownerActorId.Trim(),
                CleanFactId(subjectActorId),
                CleanFactId(sourceActorId),
                CleanFactText(sourceActorName),
                CleanFactText(evidence),
                CleanFactText(source),
                Mathf.Clamp(importance, 1, 10),
                date,
                time);

            if (!factsByActor.TryGetValue(record.OwnerActorId, out List<NpcFactRecord> records))
            {
                records = new List<NpcFactRecord>();
                factsByActor.Add(record.OwnerActorId, records);
            }

            if (IsDuplicateRecentFact(records, record))
            {
                return null;
            }

            records.Add(record);
            while (records.Count > maxFactsPerActor)
            {
                records.RemoveAt(0);
            }

            FactAdded?.Invoke(record);
            if (logFactWrites)
            {
                Debug.Log($"[Fact] {record.OwnerActorId}: {record.ToSummaryLine()}", this);
            }

            return record;
        }

        public string BuildRecentFactSummary(string ownerActorId, int maxLines = 16)
        {
            GameDate today = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            return BuildFactSummaryForDate(ownerActorId, today, maxLines);
        }

        public string BuildFactSummaryForDate(string ownerActorId, GameDate date, int maxLines = 16)
        {
            return BuildFactSummaryInternal(ownerActorId, maxLines, record =>
                record != null && (record.Date.Equals(date) || record.Importance >= autoKeepImportanceThreshold));
        }

        public void MarkLongTerm(string ownerActorId, MemoryRecord memory, string tags = "")
        {
            if (memory == null || !MatchesOwner(ownerActorId, memory))
            {
                return;
            }

            memory.MarkLongTerm(tags);
            TrimLongTerm(ownerActorId);
        }

        public void MarkDiscarded(string ownerActorId, MemoryRecord memory)
        {
            if (memory == null || !MatchesOwner(ownerActorId, memory))
            {
                return;
            }

            memory.MarkDiscarded();
        }

        public void ConsolidateDate(string ownerActorId, GameDate date)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId))
            {
                return;
            }

            string actorId = ownerActorId.Trim();
            bool changed = false;
            if (memoriesByActor.TryGetValue(actorId, out List<MemoryRecord> records))
            {
                for (int i = records.Count - 1; i >= 0; i--)
                {
                    MemoryRecord record = records[i];
                    if (!record.Date.Equals(date))
                    {
                        continue;
                    }

                    if (record.RetentionType == MemoryRetentionType.Discarded || ShouldAutoDiscard(record))
                    {
                        records.RemoveAt(i);
                        changed = true;
                    }
                }

                TrimLongTerm(actorId);
            }

            if (ClearPlayerDialogueTranscriptForDateInternal(actorId, date))
            {
                changed = true;
            }

            if (changed || memoriesByActor.ContainsKey(actorId))
            {
                MemoriesConsolidated?.Invoke(actorId, date);
            }
        }

        public string BuildPlayerDialogueTranscriptForDate(string ownerActorId, GameDate date)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId)
                || !playerDialogueByActor.TryGetValue(ownerActorId.Trim(), out List<PlayerDialogueTranscriptLine> lines))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                PlayerDialogueTranscriptLine line = lines[i];
                if (line == null || !line.Date.Equals(date))
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(line.ToTranscriptLine());
            }

            return builder.ToString();
        }

        public void ClearPlayerDialogueTranscriptForDate(string ownerActorId, GameDate date)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId))
            {
                return;
            }

            ClearPlayerDialogueTranscriptForDateInternal(ownerActorId.Trim(), date);
        }

        public void ConsolidateFactsBeforeDate(string ownerActorId, GameDate keepDate)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId) || !factsByActor.TryGetValue(ownerActorId.Trim(), out List<NpcFactRecord> records))
            {
                return;
            }

            for (int i = records.Count - 1; i >= 0; i--)
            {
                NpcFactRecord record = records[i];
                if (record == null)
                {
                    records.RemoveAt(i);
                    continue;
                }

                if (!record.Date.Equals(keepDate) && record.Importance < autoKeepImportanceThreshold)
                {
                    records.RemoveAt(i);
                }
            }
        }

        public IReadOnlyList<MemoryRecord> GetMemoriesForDate(string ownerActorId, GameDate date)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId) || !memoriesByActor.TryGetValue(ownerActorId.Trim(), out List<MemoryRecord> records))
            {
                return Array.Empty<MemoryRecord>();
            }

            List<MemoryRecord> result = new List<MemoryRecord>();
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Date.Equals(date) && records[i].RetentionType != MemoryRetentionType.Discarded)
                {
                    result.Add(records[i]);
                }
            }

            return result;
        }

        public string BuildRecentSummary(string ownerActorId, int maxLines = 5)
        {
            return BuildRecentSummaryInternal(ownerActorId, maxLines, _ => true);
        }

        public string BuildRecentSummaryWithoutDialogueChatter(string ownerActorId, int maxLines = 5)
        {
            return BuildRecentSummaryInternal(ownerActorId, maxLines, record =>
            {
                if (record == null)
                {
                    return false;
                }

                if (record.RetentionType == MemoryRetentionType.LongTerm)
                {
                    return true;
                }

                string source = record.Source != null ? record.Source.ToLowerInvariant() : string.Empty;
                return !source.Contains("dialogue")
                    && !source.Contains("ambient");
            });
        }

        private string BuildRecentSummaryInternal(string ownerActorId, int maxLines, Func<MemoryRecord, bool> includeRecord)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId) || !memoriesByActor.TryGetValue(ownerActorId.Trim(), out List<MemoryRecord> records))
            {
                return string.Empty;
            }

            List<MemoryRecord> selected = new List<MemoryRecord>();
            int limit = Mathf.Max(1, maxLines);
            for (int i = records.Count - 1; i >= 0 && selected.Count < limit; i--)
            {
                MemoryRecord record = records[i];
                if (record.RetentionType == MemoryRetentionType.Discarded)
                {
                    continue;
                }

                if (includeRecord != null && !includeRecord(record))
                {
                    continue;
                }

                selected.Add(record);
            }

            StringBuilder builder = new StringBuilder();
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(selected[i].ToSummaryLine());
            }

            return builder.ToString();
        }

        public string BuildSummaryForDate(string ownerActorId, GameDate date, int maxLines = 20)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId) || !memoriesByActor.TryGetValue(ownerActorId.Trim(), out List<MemoryRecord> records))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            int added = 0;
            for (int i = records.Count - 1; i >= 0 && added < Mathf.Max(1, maxLines); i--)
            {
                MemoryRecord record = records[i];
                if (!record.Date.Equals(date))
                {
                    continue;
                }

                if (record.RetentionType == MemoryRetentionType.Discarded)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Insert(0, Environment.NewLine);
                }

                builder.Insert(0, record.ToSummaryLine());
                added++;
            }

            return builder.ToString();
        }

        public string BuildLongTermSummary(string ownerActorId, int maxLines = 10)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId) || !memoriesByActor.TryGetValue(ownerActorId.Trim(), out List<MemoryRecord> records))
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            int added = 0;
            for (int i = records.Count - 1; i >= 0 && added < Mathf.Max(1, maxLines); i--)
            {
                MemoryRecord record = records[i];
                if (record.RetentionType != MemoryRetentionType.LongTerm)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Insert(0, Environment.NewLine);
                }

                builder.Insert(0, record.ToSummaryLine());
                added++;
            }

            return builder.ToString();
        }

        private void ApplyDefaultRetention(MemoryRecord record)
        {
            if (record.Importance >= autoKeepImportanceThreshold || IsLongTermSource(record.Source) || LooksLikeCommitment(record.Summary))
            {
                record.MarkLongTerm(record.Source);
            }
        }

        private static bool ShouldAutoDiscard(MemoryRecord record)
        {
            if (record.RetentionType == MemoryRetentionType.LongTerm)
            {
                return false;
            }

            if (record.Importance >= 5)
            {
                return false;
            }

            string source = record.Source != null ? record.Source.ToLowerInvariant() : string.Empty;
            string summary = record.Summary != null ? record.Summary.ToLowerInvariant() : string.Empty;
            return source.Contains("dialogue")
                || source.Contains("ambient")
                || summary.Contains("greet")
                || summary.Contains("hello")
                || summary.Contains("问好")
                || summary.Contains("寒暄");
        }

        private static bool IsLongTermSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
            {
                return false;
            }

            string normalized = source.ToLowerInvariant();
            return normalized.Contains("promise")
                || normalized.Contains("commitment")
                || normalized.Contains("plan")
                || normalized.Contains("event")
                || normalized.Contains("job")
                || normalized.Contains("one_shot");
        }

        private static bool LooksLikeCommitment(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
            {
                return false;
            }

            string normalized = summary.ToLowerInvariant();
            return normalized.Contains("promise")
                || normalized.Contains("agreed")
                || normalized.Contains("tomorrow")
                || normalized.Contains("约定")
                || normalized.Contains("答应")
                || normalized.Contains("明天");
        }

        private static bool MatchesOwner(string ownerActorId, MemoryRecord memory)
        {
            return string.IsNullOrWhiteSpace(ownerActorId)
                || string.Equals(ownerActorId.Trim(), memory.OwnerActorId, StringComparison.OrdinalIgnoreCase);
        }

        private void TrimLongTerm(string ownerActorId)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId) || !memoriesByActor.TryGetValue(ownerActorId.Trim(), out List<MemoryRecord> records))
            {
                return;
            }

            int count = 0;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (records[i].RetentionType != MemoryRetentionType.LongTerm)
                {
                    continue;
                }

                count++;
                if (count > maxLongTermMemoriesPerActor)
                {
                    records.RemoveAt(i);
                }
            }
        }

        private static void RemoveOldestDiscardable(List<MemoryRecord> records)
        {
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].RetentionType != MemoryRetentionType.LongTerm)
                {
                    records.RemoveAt(i);
                    return;
                }
            }

            if (records.Count > 0)
            {
                records.RemoveAt(0);
            }
        }

        private static string CleanFactId(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
        }

        private static string CleanFactText(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }

        private static bool IsDuplicateRecentFact(List<NpcFactRecord> records, NpcFactRecord candidate)
        {
            if (records == null || candidate == null)
            {
                return false;
            }

            for (int i = records.Count - 1; i >= 0 && i >= records.Count - 8; i--)
            {
                NpcFactRecord record = records[i];
                if (record == null)
                {
                    continue;
                }

                if (string.Equals(record.SubjectActorId, candidate.SubjectActorId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.SourceActorId, candidate.SourceActorId, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.Source, candidate.Source, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(record.Evidence, candidate.Evidence, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private string BuildFactSummaryInternal(string ownerActorId, int maxLines, Func<NpcFactRecord, bool> includeRecord)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId) || !factsByActor.TryGetValue(ownerActorId.Trim(), out List<NpcFactRecord> records))
            {
                return string.Empty;
            }

            List<NpcFactRecord> selected = new List<NpcFactRecord>();
            int limit = Mathf.Max(1, maxLines);
            for (int i = records.Count - 1; i >= 0 && selected.Count < limit; i--)
            {
                NpcFactRecord record = records[i];
                if (record == null)
                {
                    continue;
                }

                if (includeRecord == null || includeRecord(record))
                {
                    selected.Add(record);
                }
            }

            StringBuilder builder = new StringBuilder();
            for (int i = selected.Count - 1; i >= 0; i--)
            {
                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append(selected[i].ToSummaryLine());
            }

            return builder.ToString();
        }

        private bool ClearPlayerDialogueTranscriptForDateInternal(string ownerActorId, GameDate date)
        {
            if (string.IsNullOrWhiteSpace(ownerActorId)
                || !playerDialogueByActor.TryGetValue(ownerActorId, out List<PlayerDialogueTranscriptLine> lines))
            {
                return false;
            }

            bool removed = false;
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                PlayerDialogueTranscriptLine line = lines[i];
                if (line == null || line.Date.Equals(date))
                {
                    lines.RemoveAt(i);
                    removed = true;
                }
            }

            if (lines.Count == 0)
            {
                playerDialogueByActor.Remove(ownerActorId);
            }

            return removed;
        }
    }

    [Serializable]
    public sealed class PlayerDialogueTranscriptLine
    {
        [SerializeField] private string ownerActorId;
        [SerializeField] private string speakerActorId;
        [SerializeField] private string speakerName;
        [SerializeField] private string text;
        [SerializeField] private GameDate date;
        [SerializeField] private GameTime time;

        public string OwnerActorId => ownerActorId;
        public string SpeakerActorId => speakerActorId;
        public string SpeakerName => speakerName;
        public string Text => text;
        public GameDate Date => date;
        public GameTime Time => time;

        public PlayerDialogueTranscriptLine(
            string ownerActorId,
            string speakerActorId,
            string speakerName,
            string text,
            GameDate date,
            GameTime time)
        {
            this.ownerActorId = ownerActorId;
            this.speakerActorId = speakerActorId;
            this.speakerName = speakerName;
            this.text = text;
            this.date = date;
            this.time = time;
        }

        public string ToTranscriptLine()
        {
            string speakerText = string.IsNullOrWhiteSpace(speakerActorId)
                ? speakerName
                : $"{speakerName}({speakerActorId})";
            if (string.IsNullOrWhiteSpace(speakerText))
            {
                speakerText = "(unknown speaker)";
            }

            return $"{date} {time}: {speakerText}: {text}";
        }
    }

    [Serializable]
    public sealed class NpcFactRecord
    {
        [SerializeField] private string ownerActorId;
        [SerializeField] private string subjectActorId;
        [SerializeField] private string sourceActorId;
        [SerializeField] private string sourceActorName;
        [SerializeField] private string evidence;
        [SerializeField] private string source;
        [SerializeField] private int importance;
        [SerializeField] private GameDate date;
        [SerializeField] private GameTime time;

        public string OwnerActorId => ownerActorId;
        public string SubjectActorId => subjectActorId;
        public string SourceActorId => sourceActorId;
        public string SourceActorName => sourceActorName;
        public string Evidence => evidence;
        public string Source => source;
        public int Importance => importance;
        public GameDate Date => date;
        public GameTime Time => time;

        public NpcFactRecord(
            string ownerActorId,
            string subjectActorId,
            string sourceActorId,
            string sourceActorName,
            string evidence,
            string source,
            int importance,
            GameDate date,
            GameTime time)
        {
            this.ownerActorId = ownerActorId;
            this.subjectActorId = subjectActorId;
            this.sourceActorId = sourceActorId;
            this.sourceActorName = sourceActorName;
            this.evidence = evidence;
            this.source = source;
            this.importance = Mathf.Clamp(importance, 1, 10);
            this.date = date;
            this.time = time;
        }

        public string ToSummaryLine()
        {
            string subjectText = string.IsNullOrWhiteSpace(subjectActorId) ? "(unspecified)" : subjectActorId;
            string sourceActorText = string.IsNullOrWhiteSpace(sourceActorId) ? "(unknown)" : sourceActorId;
            string sourceNameText = string.IsNullOrWhiteSpace(sourceActorName) ? string.Empty : $" name={sourceActorName}";
            string sourceText = string.IsNullOrWhiteSpace(source) ? string.Empty : $" source={source}";
            return $"{date} {time}: subject={subjectText}, sourceActor={sourceActorText}{sourceNameText}{sourceText}, evidence={evidence}";
        }
    }
}
