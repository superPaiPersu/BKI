using System;
using System.Collections.Generic;
using UnityEngine;

namespace CityStateSim.Dialogue
{
    public sealed class DialogueHistorySystem : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float keepSeconds = 300f;
        [SerializeField, Min(1)] private int maxRecords = 100;
        [SerializeField, Min(0f)] private float duplicateSuppressSeconds = 0.25f;

        private readonly List<DialogueHistoryRecord> records = new List<DialogueHistoryRecord>();

        public IReadOnlyList<DialogueHistoryRecord> Records => records;

        public event Action<DialogueHistoryRecord> RecordAdded;
        public event Action RecordsChanged;

        public void AddDisplayedLine(DialogueLine line)
        {
            if (line == null)
            {
                return;
            }

            AddDisplayedLine(line.SpeakerId, line.SpeakerName, line.Text);
        }

        public void AddDisplayedLine(string speakerId, string speakerName, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            TrimExpired();
            if (IsRecentDuplicate(speakerId, speakerName, text))
            {
                return;
            }

            DialogueHistoryRecord record = new DialogueHistoryRecord(speakerId, speakerName, text, Time.realtimeSinceStartup);
            records.Add(record);
            TrimOverflow();
            RecordAdded?.Invoke(record);
            RecordsChanged?.Invoke();
        }

        public void Clear()
        {
            records.Clear();
            RecordsChanged?.Invoke();
        }

        public string[] BuildDisplayLines()
        {
            TrimExpired();
            string[] lines = new string[records.Count];
            for (int i = 0; i < records.Count; i++)
            {
                lines[i] = records[i].ToDisplayText();
            }

            return lines;
        }

        private void Update()
        {
            TrimExpired();
        }

        private void TrimExpired()
        {
            float cutoff = Time.realtimeSinceStartup - keepSeconds;
            bool changed = false;
            while (records.Count > 0 && records[0].Realtime < cutoff)
            {
                records.RemoveAt(0);
                changed = true;
            }

            if (changed)
            {
                RecordsChanged?.Invoke();
            }
        }

        private void TrimOverflow()
        {
            while (records.Count > maxRecords)
            {
                records.RemoveAt(0);
            }
        }

        private bool IsRecentDuplicate(string speakerId, string speakerName, string text)
        {
            if (duplicateSuppressSeconds <= 0f || records.Count == 0)
            {
                return false;
            }

            DialogueHistoryRecord latest = records[records.Count - 1];
            if (Time.realtimeSinceStartup - latest.Realtime > duplicateSuppressSeconds)
            {
                return false;
            }

            return string.Equals(latest.SpeakerId, speakerId, StringComparison.Ordinal)
                && string.Equals(latest.SpeakerName, speakerName, StringComparison.Ordinal)
                && string.Equals(latest.Text, text, StringComparison.Ordinal);
        }
    }
}
