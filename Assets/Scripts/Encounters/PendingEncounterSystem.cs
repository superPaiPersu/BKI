using System;
using System.Collections.Generic;
using System.Text;
using CityStateSim.AI;
using CityStateSim.Core;
using CityStateSim.NPC;
using CityStateSim.Perception;
using UnityEngine;

namespace CityStateSim.Encounters
{
    [Serializable]
    public sealed class PendingEncounterRecord
    {
        [SerializeField] private string encounterId;
        [SerializeField] private string ownerNpcId;
        [SerializeField] private string targetActorId;
        [SerializeField] private string actionKind;
        [SerializeField] private string topic;
        [SerializeField] private int priority;
        [SerializeField] private string reason;
        [SerializeField] private bool consumeOnTrigger = true;
        [SerializeField] private int expiresAfterDays;
        [SerializeField] private string interruptPolicy = PendingEncounterSystem.InterruptOnlyIfFree;
        [SerializeField] private int cooldownMinutes = 30;
        [SerializeField] private GameDate createdDate;
        [SerializeField] private int createdRuntimeDayIndex;
        [SerializeField] private int expiresRuntimeDayIndex = -1;
        [SerializeField] private int lastTriggeredRuntimeMinute = -999999;

        public string EncounterId => encounterId;
        public string OwnerNpcId => ownerNpcId;
        public string TargetActorId => targetActorId;
        public string ActionKind => actionKind;
        public string Topic => topic;
        public int Priority => priority;
        public string Reason => reason;
        public bool ConsumeOnTrigger => consumeOnTrigger;
        public int ExpiresAfterDays => expiresAfterDays;
        public string InterruptPolicy => interruptPolicy;
        public int CooldownMinutes => cooldownMinutes;
        public GameDate CreatedDate => createdDate;

        public void Initialize(
            string ownerNpcId,
            NpcPendingEncounterChange change,
            GameDate date,
            int runtimeDayIndex)
        {
            this.ownerNpcId = CleanId(ownerNpcId);
            targetActorId = CleanId(change.targetActorId);
            actionKind = CleanText(change.actionKind);
            topic = CleanText(change.topic);
            priority = Mathf.Clamp(change.priority, 0, 100);
            reason = CleanText(change.reason);
            consumeOnTrigger = change.consumeOnTrigger;
            expiresAfterDays = Mathf.Clamp(change.expiresAfterDays, 0, 365);
            interruptPolicy = PendingEncounterSystem.CleanInterruptPolicy(change.interruptPolicy);
            cooldownMinutes = Mathf.Clamp(change.cooldownMinutes, 1, 1440);
            createdDate = date;
            createdRuntimeDayIndex = runtimeDayIndex;
            expiresRuntimeDayIndex = expiresAfterDays > 0 ? runtimeDayIndex + expiresAfterDays : -1;
            encounterId = BuildId(this.ownerNpcId, targetActorId, actionKind, topic);
        }

        public void UpdateFrom(NpcPendingEncounterChange change, GameDate date, int runtimeDayIndex)
        {
            actionKind = CleanText(change.actionKind);
            topic = CleanText(change.topic);
            priority = Mathf.Clamp(change.priority, 0, 100);
            reason = CleanText(change.reason);
            consumeOnTrigger = change.consumeOnTrigger;
            expiresAfterDays = Mathf.Clamp(change.expiresAfterDays, 0, 365);
            interruptPolicy = PendingEncounterSystem.CleanInterruptPolicy(change.interruptPolicy);
            cooldownMinutes = Mathf.Clamp(change.cooldownMinutes, 1, 1440);
            createdDate = date;
            createdRuntimeDayIndex = runtimeDayIndex;
            expiresRuntimeDayIndex = expiresAfterDays > 0 ? runtimeDayIndex + expiresAfterDays : -1;
            encounterId = BuildId(ownerNpcId, targetActorId, actionKind, topic);
        }

        public bool IsExpired(int runtimeDayIndex)
        {
            return expiresRuntimeDayIndex >= 0 && runtimeDayIndex > expiresRuntimeDayIndex;
        }

        public bool IsCoolingDown(int runtimeMinute)
        {
            return cooldownMinutes > 0 && runtimeMinute - lastTriggeredRuntimeMinute < cooldownMinutes;
        }

        public void MarkTriggered(int runtimeMinute)
        {
            lastTriggeredRuntimeMinute = runtimeMinute;
        }

        public string ToPromptLine()
        {
            string expires = expiresRuntimeDayIndex >= 0 ? expiresAfterDays + " day(s) after creation" : "never";
            return
                $"id={encounterId}, targetActorId={targetActorId}, action={actionKind}, topic={topic}, " +
                $"priority={priority}, interruptPolicy={interruptPolicy}, consumeOnTrigger={consumeOnTrigger}, " +
                $"cooldownMinutes={cooldownMinutes}, expires={expires}, reason={reason}";
        }

        public static string BuildId(string ownerNpcId, string targetActorId, string actionKind, string topic)
        {
            return $"{CleanId(ownerNpcId)}->{CleanId(targetActorId)}:{NormalizeKey(actionKind)}:{NormalizeKey(topic)}";
        }

        private static string CleanId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

        private static string CleanText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }

        private static string NormalizeKey(string value)
        {
            return CleanText(value).ToLowerInvariant().Replace(" ", "_");
        }
    }

    public sealed class PendingEncounterSystem : MonoBehaviour
    {
        public const string InterruptOnlyIfFree = "only_if_free";
        public const string InterruptLeisure = "can_interrupt_leisure";
        public const string InterruptAnything = "can_interrupt_anything";

        [SerializeField] private GameClock clock;
        [SerializeField, Min(1)] private int maxEncountersPerNpc = 12;
        [SerializeField] private bool logChanges = true;
        [SerializeField] private List<PendingEncounterRecord> records = new List<PendingEncounterRecord>();

        private int runtimeDayIndex;

        public event Action<PendingEncounterRecord> EncounterAddedOrUpdated;
        public event Action<string, string> EncountersRemoved;
        public event Action<PendingEncounterRecord> EncounterTriggered;

        public IReadOnlyList<PendingEncounterRecord> Records => records;

        private void Awake()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }
        }

        private void OnEnable()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (clock != null)
            {
                clock.DayChanged += HandleDayChanged;
            }
        }

        private void OnDisable()
        {
            if (clock != null)
            {
                clock.DayChanged -= HandleDayChanged;
            }
        }

        public static PendingEncounterSystem GetOrCreate()
        {
            PendingEncounterSystem existing = FindFirstObjectByType<PendingEncounterSystem>();
            if (existing != null)
            {
                return existing;
            }

            GameObject host = new GameObject("PendingEncounterSystem");
            return host.AddComponent<PendingEncounterSystem>();
        }

        public void ApplyDecision(NpcProfile owner, NpcAiDecision decision)
        {
            if (owner == null || decision == null || decision.pendingEncounterChanges == null)
            {
                return;
            }

            for (int i = 0; i < decision.pendingEncounterChanges.Length; i++)
            {
                ApplyChange(owner.NpcId, decision.pendingEncounterChanges[i]);
            }
        }

        public string BuildSummaryForNpc(string ownerNpcId)
        {
            ownerNpcId = CleanId(ownerNpcId);
            if (string.IsNullOrWhiteSpace(ownerNpcId))
            {
                return "(none)";
            }

            PruneExpired();
            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < records.Count; i++)
            {
                PendingEncounterRecord record = records[i];
                if (record == null || !SameId(record.OwnerNpcId, ownerNpcId))
                {
                    continue;
                }

                builder.Append("- ");
                builder.AppendLine(record.ToPromptLine());
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        public bool TryGetBestTrigger(
            string ownerNpcId,
            IReadOnlyList<PerceptionObservation> observations,
            out PendingEncounterRecord best)
        {
            best = null;
            ownerNpcId = CleanId(ownerNpcId);
            if (string.IsNullOrWhiteSpace(ownerNpcId) || observations == null || observations.Count == 0)
            {
                return false;
            }

            PruneExpired();
            int currentMinute = GetCurrentRuntimeMinute();
            for (int i = 0; i < observations.Count; i++)
            {
                PerceptionObservation observation = observations[i];
                if (observation == null || string.IsNullOrWhiteSpace(observation.EntityId))
                {
                    continue;
                }

                string observedId = CleanId(observation.EntityId);
                for (int j = 0; j < records.Count; j++)
                {
                    PendingEncounterRecord record = records[j];
                    if (record == null
                        || !SameId(record.OwnerNpcId, ownerNpcId)
                        || !SameId(record.TargetActorId, observedId)
                        || record.IsCoolingDown(currentMinute))
                    {
                        continue;
                    }

                    if (best == null || record.Priority > best.Priority)
                    {
                        best = record;
                    }
                }
            }

            return best != null;
        }

        public void MarkTriggered(PendingEncounterRecord record)
        {
            if (record == null)
            {
                return;
            }

            record.MarkTriggered(GetCurrentRuntimeMinute());
            EncounterTriggered?.Invoke(record);
            if (logChanges)
            {
                Debug.Log($"[Pending Encounter] triggered {record.ToPromptLine()}", this);
            }
        }

        public void ResolveTriggeredEncounter(string ownerNpcId, string encounterId, NpcAiDecision decision)
        {
            if (string.IsNullOrWhiteSpace(encounterId) || decision == null)
            {
                return;
            }

            PendingEncounterRecord record = FindById(ownerNpcId, encounterId);
            if (record == null || !record.ConsumeOnTrigger)
            {
                return;
            }

            if (decision.ParsedIntent == NpcIntentType.ContinueCurrentAction)
            {
                return;
            }

            RemoveById(record.OwnerNpcId, record.EncounterId, "consumed after triggered decision");
        }

        public static string CleanInterruptPolicy(string value)
        {
            if (string.Equals(value, InterruptAnything, StringComparison.OrdinalIgnoreCase))
            {
                return InterruptAnything;
            }

            if (string.Equals(value, InterruptLeisure, StringComparison.OrdinalIgnoreCase))
            {
                return InterruptLeisure;
            }

            return InterruptOnlyIfFree;
        }

        private void ApplyChange(string ownerNpcId, NpcPendingEncounterChange change)
        {
            if (change == null)
            {
                return;
            }

            change.Clamp();
            ownerNpcId = CleanId(ownerNpcId);
            string targetActorId = CleanId(change.targetActorId);
            if (string.IsNullOrWhiteSpace(ownerNpcId)
                || string.IsNullOrWhiteSpace(targetActorId)
                || SameId(ownerNpcId, targetActorId))
            {
                return;
            }

            if (string.Equals(change.operation, "remove", StringComparison.OrdinalIgnoreCase))
            {
                RemoveMatching(ownerNpcId, targetActorId, change.actionKind, change.topic, "AI removed pending encounter");
                return;
            }

            if (!string.Equals(change.operation, "add_or_update", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            AddOrUpdate(ownerNpcId, change);
        }

        private void AddOrUpdate(string ownerNpcId, NpcPendingEncounterChange change)
        {
            string id = PendingEncounterRecord.BuildId(ownerNpcId, change.targetActorId, change.actionKind, change.topic);
            PendingEncounterRecord existing = FindById(ownerNpcId, id);
            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            if (existing != null)
            {
                existing.UpdateFrom(change, date, runtimeDayIndex);
                EncounterAddedOrUpdated?.Invoke(existing);
                if (logChanges)
                {
                    Debug.Log($"[Pending Encounter] updated {existing.ToPromptLine()}", this);
                }

                return;
            }

            PendingEncounterRecord record = new PendingEncounterRecord();
            record.Initialize(ownerNpcId, change, date, runtimeDayIndex);
            records.Add(record);
            TrimOwnerRecords(ownerNpcId);
            EncounterAddedOrUpdated?.Invoke(record);
            if (logChanges)
            {
                Debug.Log($"[Pending Encounter] added {record.ToPromptLine()}", this);
            }
        }

        private void RemoveMatching(string ownerNpcId, string targetActorId, string actionKind, string topic, string reason)
        {
            ownerNpcId = CleanId(ownerNpcId);
            targetActorId = CleanId(targetActorId);
            string action = CleanText(actionKind);
            string cleanTopic = CleanText(topic);
            int removed = records.RemoveAll(record =>
                record != null
                && SameId(record.OwnerNpcId, ownerNpcId)
                && SameId(record.TargetActorId, targetActorId)
                && (string.IsNullOrWhiteSpace(action) || SameText(record.ActionKind, action))
                && (string.IsNullOrWhiteSpace(cleanTopic) || SameText(record.Topic, cleanTopic)));

            if (removed <= 0)
            {
                return;
            }

            EncountersRemoved?.Invoke(ownerNpcId, reason);
            if (logChanges)
            {
                Debug.Log($"[Pending Encounter] removed {removed} for owner={ownerNpcId}, target={targetActorId}, reason={reason}", this);
            }
        }

        private void RemoveById(string ownerNpcId, string encounterId, string reason)
        {
            ownerNpcId = CleanId(ownerNpcId);
            int removed = records.RemoveAll(record =>
                record != null
                && SameId(record.OwnerNpcId, ownerNpcId)
                && SameText(record.EncounterId, encounterId));

            if (removed <= 0)
            {
                return;
            }

            EncountersRemoved?.Invoke(ownerNpcId, reason);
            if (logChanges)
            {
                Debug.Log($"[Pending Encounter] removed {encounterId}, reason={reason}", this);
            }
        }

        private PendingEncounterRecord FindById(string ownerNpcId, string encounterId)
        {
            ownerNpcId = CleanId(ownerNpcId);
            for (int i = 0; i < records.Count; i++)
            {
                PendingEncounterRecord record = records[i];
                if (record != null
                    && SameId(record.OwnerNpcId, ownerNpcId)
                    && SameText(record.EncounterId, encounterId))
                {
                    return record;
                }
            }

            return null;
        }

        private void TrimOwnerRecords(string ownerNpcId)
        {
            int count = 0;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i] != null && SameId(records[i].OwnerNpcId, ownerNpcId))
                {
                    count++;
                }
            }

            while (count > maxEncountersPerNpc)
            {
                int removeIndex = FindLowestPriorityIndex(ownerNpcId);
                if (removeIndex < 0)
                {
                    return;
                }

                records.RemoveAt(removeIndex);
                count--;
            }
        }

        private int FindLowestPriorityIndex(string ownerNpcId)
        {
            int index = -1;
            int priority = int.MaxValue;
            for (int i = 0; i < records.Count; i++)
            {
                PendingEncounterRecord record = records[i];
                if (record == null || !SameId(record.OwnerNpcId, ownerNpcId))
                {
                    continue;
                }

                if (record.Priority < priority)
                {
                    index = i;
                    priority = record.Priority;
                }
            }

            return index;
        }

        private void HandleDayChanged(GameDate date)
        {
            runtimeDayIndex++;
            PruneExpired();
        }

        private void PruneExpired()
        {
            int removed = records.RemoveAll(record => record == null || record.IsExpired(runtimeDayIndex));
            if (removed > 0 && logChanges)
            {
                Debug.Log($"[Pending Encounter] pruned expired count={removed}", this);
            }
        }

        private int GetCurrentRuntimeMinute()
        {
            GameTime time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            return runtimeDayIndex * 1440 + time.TotalMinutes;
        }

        private static string CleanId(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

        private static string CleanText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }

        private static bool SameId(string left, string right)
        {
            return string.Equals(CleanId(left), CleanId(right), StringComparison.OrdinalIgnoreCase);
        }

        private static bool SameText(string left, string right)
        {
            return string.Equals(CleanText(left), CleanText(right), StringComparison.OrdinalIgnoreCase);
        }
    }
}
