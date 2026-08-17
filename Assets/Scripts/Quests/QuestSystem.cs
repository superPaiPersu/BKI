using System;
using System.Collections.Generic;
using System.Text;
using CityStateSim.Core;
using CityStateSim.Economy;
using CityStateSim.Items;
using CityStateSim.Locations;
using CityStateSim.Memory;
using CityStateSim.Relationships;
using UnityEngine;

namespace CityStateSim.Quests
{
    public sealed class QuestSystem : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private GameClock clock;
        [SerializeField] private InventorySystem inventory;
        [SerializeField] private PlayerEconomySystem economySystem;
        [SerializeField] private RelationshipSystem relationshipSystem;
        [SerializeField] private MemorySystem memorySystem;
        [SerializeField] private LocationSystem locationSystem;

        [Header("Quest Catalog")]
        [SerializeField] private QuestDefinition[] availableQuests;

        [Header("Policy")]
        [SerializeField] private bool failExpiredQuestsOnDayChanged = true;
        [SerializeField] private bool writeNpcMemories = true;
        [SerializeField] private bool logQuestEvents = true;

        private readonly List<QuestInstance> activeQuests = new List<QuestInstance>();
        private readonly HashSet<string> completedQuestIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public QuestDefinition[] AvailableQuests => availableQuests ?? Array.Empty<QuestDefinition>();
        public IReadOnlyList<QuestInstance> ActiveQuests => activeQuests;

        public event Action<QuestInstance> QuestAccepted;
        public event Action<QuestInstance> QuestReadyToTurnIn;
        public event Action<QuestInstance> QuestCompleted;
        public event Action<QuestInstance> QuestFailed;
        public event Action<QuestInstance> QuestProgressChanged;
        public event Action<string> QuestOperationFailed;

        private void Awake()
        {
            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }

            if (inventory == null)
            {
                inventory = FindFirstObjectByType<InventorySystem>();
            }

            if (economySystem == null)
            {
                economySystem = FindFirstObjectByType<PlayerEconomySystem>();
            }

            if (relationshipSystem == null)
            {
                relationshipSystem = FindFirstObjectByType<RelationshipSystem>();
            }

            if (memorySystem == null)
            {
                memorySystem = FindFirstObjectByType<MemorySystem>();
            }

            if (locationSystem == null)
            {
                locationSystem = FindFirstObjectByType<LocationSystem>();
            }
        }

        private void OnEnable()
        {
            if (clock != null)
            {
                clock.DayChanged += HandleDayChanged;
            }

            if (inventory != null)
            {
                inventory.InventoryChanged += RefreshQuestReadiness;
            }

            if (locationSystem != null)
            {
                locationSystem.CurrentLocationChanged += ReportLocationReached;
            }
        }

        private void OnDisable()
        {
            if (clock != null)
            {
                clock.DayChanged -= HandleDayChanged;
            }

            if (inventory != null)
            {
                inventory.InventoryChanged -= RefreshQuestReadiness;
            }

            if (locationSystem != null)
            {
                locationSystem.CurrentLocationChanged -= ReportLocationReached;
            }
        }

        public bool CanAcceptQuest(QuestDefinition quest, out string reason)
        {
            if (quest == null)
            {
                reason = "No quest selected.";
                return false;
            }

            string questId = GetQuestId(quest);
            if (!quest.Repeatable && completedQuestIds.Contains(questId))
            {
                reason = "This quest has already been completed.";
                return false;
            }

            if (FindActiveQuest(quest) != null)
            {
                reason = "This quest is already active.";
                return false;
            }

            if (quest.RequiredTrust > 0 && relationshipSystem != null && quest.Issuer != null)
            {
                int trust = relationshipSystem.GetOrCreateToPlayer(quest.Issuer).Trust;
                if (trust < quest.RequiredTrust)
                {
                    reason = $"Requires trust {quest.RequiredTrust}, current trust {trust}.";
                    return false;
                }
            }

            reason = string.Empty;
            return true;
        }

        public bool TryAcceptQuest(QuestDefinition quest)
        {
            if (!CanAcceptQuest(quest, out string reason))
            {
                Fail(reason);
                return false;
            }

            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            GameTime time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            QuestInstance instance = new QuestInstance(quest, date, time);
            activeQuests.Add(instance);

            WriteIssuerMemory(quest, BuildMemoryText(quest.AcceptedMemory, $"Player accepted quest: {quest.DisplayName}. Objective: {quest.BuildObjectiveSummary()}"), "quest_accepted", quest.MemoryImportance);
            QuestAccepted?.Invoke(instance);
            RefreshQuestReadiness(instance);

            if (logQuestEvents)
            {
                Debug.Log($"[Quest] Accepted {quest.BuildSummaryLine()}", this);
            }

            return true;
        }

        public bool TryAcceptQuestById(string questId)
        {
            QuestDefinition quest = FindAvailableQuestById(questId);
            return quest != null && TryAcceptQuest(quest);
        }

        public bool TryTurnInQuest(QuestDefinition quest)
        {
            QuestInstance instance = FindActiveQuest(quest);
            if (instance == null)
            {
                Fail("Quest is not active.");
                return false;
            }

            return TryTurnInQuest(instance);
        }

        public bool TryTurnInQuest(QuestInstance instance)
        {
            if (instance == null || instance.Definition == null)
            {
                Fail("Quest instance is missing.");
                return false;
            }

            if (!instance.IsActive)
            {
                Fail("Quest is not active.");
                return false;
            }

            QuestDefinition quest = instance.Definition;
            if (!AreRequirementsMet(instance))
            {
                Fail($"Quest requirements are not met: {quest.BuildObjectiveSummary()}.");
                return false;
            }

            if (!CanGrantRewards(quest, out string rewardReason))
            {
                Fail(rewardReason);
                return false;
            }

            if (quest.ConsumeRequiredItemsOnTurnIn && RequiresItems(quest) && inventory != null && !inventory.TryRemoveAll(quest.RequiredItems))
            {
                return false;
            }

            if (!GrantRewards(quest))
            {
                Fail("Could not grant quest rewards.");
                return false;
            }

            ApplyRelationshipReward(quest);
            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            GameTime time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            instance.MarkCompleted(date, time);
            activeQuests.Remove(instance);
            completedQuestIds.Add(GetQuestId(quest));

            WriteIssuerMemory(quest, BuildMemoryText(quest.CompletedMemory, $"Player completed quest: {quest.DisplayName}. Reward: {quest.BuildRewardSummary()}"), "quest_completed", quest.MemoryImportance);
            QuestCompleted?.Invoke(instance);

            if (logQuestEvents)
            {
                Debug.Log($"[Quest] Completed {quest.DisplayName}.", this);
            }

            return true;
        }

        public bool TryFailQuest(QuestDefinition quest, string reason)
        {
            QuestInstance instance = FindActiveQuest(quest);
            if (instance == null)
            {
                Fail("Quest is not active.");
                return false;
            }

            FailQuest(instance, reason);
            return true;
        }

        public void ReportObjectiveProgress(string objectiveKey, int amount = 1, string source = "")
        {
            if (string.IsNullOrWhiteSpace(objectiveKey) || amount <= 0)
            {
                return;
            }

            for (int i = 0; i < activeQuests.Count; i++)
            {
                QuestInstance instance = activeQuests[i];
                QuestDefinition quest = instance != null ? instance.Definition : null;
                if (quest == null || !instance.IsActive || !quest.HasObjectiveKey(objectiveKey))
                {
                    continue;
                }

                if (instance.AddProgress(amount))
                {
                    QuestProgressChanged?.Invoke(instance);
                    RefreshQuestReadiness(instance);

                    if (logQuestEvents)
                    {
                        Debug.Log($"[Quest] Progress {quest.DisplayName}: +{amount} from {Clean(source)}.", this);
                    }
                }
            }
        }

        public void ReportLocationReached(LocationDefinition location)
        {
            if (location == null)
            {
                return;
            }

            string locationId = location.LocationId;
            for (int i = 0; i < activeQuests.Count; i++)
            {
                QuestInstance instance = activeQuests[i];
                QuestDefinition quest = instance != null ? instance.Definition : null;
                if (quest == null
                    || !instance.IsActive
                    || quest.ObjectiveType != QuestObjectiveType.ReachLocation
                    || quest.TargetLocation == null
                    || !string.Equals(quest.TargetLocation.LocationId, locationId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                instance.MarkReady();
                QuestReadyToTurnIn?.Invoke(instance);
            }
        }

        public void MarkQuestReady(QuestDefinition quest)
        {
            QuestInstance instance = FindActiveQuest(quest);
            if (instance == null)
            {
                return;
            }

            MarkReadyIfNeeded(instance);
        }

        public string BuildActiveQuestSummary()
        {
            if (activeQuests.Count == 0)
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < activeQuests.Count; i++)
            {
                QuestInstance instance = activeQuests[i];
                if (instance == null)
                {
                    continue;
                }

                builder.AppendLine("- " + instance.ToSummaryLine());
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        public string BuildQuestSummaryForNpc(string npcId, int maxLines = 8)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                return "(none)";
            }

            StringBuilder builder = new StringBuilder();
            int added = 0;
            int limit = Mathf.Max(1, maxLines);
            for (int i = 0; i < activeQuests.Count && added < limit; i++)
            {
                QuestInstance instance = activeQuests[i];
                QuestDefinition quest = instance != null ? instance.Definition : null;
                if (quest == null || quest.Issuer == null || !string.Equals(quest.Issuer.NpcId, npcId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                builder.AppendLine("- " + instance.ToSummaryLine());
                added++;
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        private void RefreshQuestReadiness()
        {
            for (int i = 0; i < activeQuests.Count; i++)
            {
                RefreshQuestReadiness(activeQuests[i]);
            }
        }

        private void RefreshQuestReadiness(QuestInstance instance)
        {
            if (instance == null || !instance.IsActive || instance.State == QuestState.ReadyToTurnIn)
            {
                return;
            }

            QuestDefinition quest = instance.Definition;
            if (quest != null && quest.AutoReadyWhenRequirementsMet && AreRequirementsMet(instance))
            {
                MarkReadyIfNeeded(instance);
            }
        }

        private void MarkReadyIfNeeded(QuestInstance instance)
        {
            if (instance == null || instance.State == QuestState.ReadyToTurnIn)
            {
                return;
            }

            instance.MarkReady();
            QuestReadyToTurnIn?.Invoke(instance);

            if (logQuestEvents && instance.Definition != null)
            {
                Debug.Log($"[Quest] Ready to turn in {instance.Definition.DisplayName}.", this);
            }
        }

        private bool AreRequirementsMet(QuestInstance instance)
        {
            QuestDefinition quest = instance != null ? instance.Definition : null;
            if (quest == null)
            {
                return false;
            }

            switch (quest.ObjectiveType)
            {
                case QuestObjectiveType.CollectItems:
                case QuestObjectiveType.DeliverItems:
                    return inventory != null && inventory.HasAll(quest.RequiredItems);
                case QuestObjectiveType.ObjectiveProgress:
                    return instance.Progress >= quest.RequiredProgress;
                case QuestObjectiveType.ReachLocation:
                    return instance.State == QuestState.ReadyToTurnIn;
                default:
                    return instance.State == QuestState.ReadyToTurnIn;
            }
        }

        private bool GrantRewards(QuestDefinition quest)
        {
            if (quest == null)
            {
                return false;
            }

            if (!CanGrantRewards(quest, out _))
            {
                return false;
            }

            if (HasRewardItems(quest) && !inventory.TryAddAll(quest.RewardItems))
            {
                return false;
            }

            if (quest.RewardMoney > 0)
            {
                if (economySystem != null)
                {
                    economySystem.AddMoney(quest.RewardMoney, $"quest_reward:{quest.QuestId}");
                }
            }

            return true;
        }

        private bool CanGrantRewards(QuestDefinition quest, out string reason)
        {
            reason = string.Empty;
            if (quest == null)
            {
                reason = "Quest is missing.";
                return false;
            }

            if (quest.RewardMoney > 0 && economySystem == null)
            {
                reason = "PlayerEconomySystem is missing.";
                return false;
            }

            if (HasRewardItems(quest))
            {
                if (inventory == null)
                {
                    reason = "InventorySystem is missing.";
                    return false;
                }

                if (!inventory.CanAddAll(quest.RewardItems))
                {
                    reason = "Not enough inventory space for quest rewards.";
                    return false;
                }
            }

            return true;
        }

        private void ApplyRelationshipReward(QuestDefinition quest)
        {
            if (relationshipSystem == null || quest == null || quest.Issuer == null)
            {
                return;
            }

            relationshipSystem.ApplyPlayerDelta(
                quest.Issuer,
                quest.TrustDeltaOnComplete,
                quest.AffinityDeltaOnComplete,
                quest.SuspicionDeltaOnComplete);
        }

        private void FailQuest(QuestInstance instance, string reason)
        {
            if (instance == null || instance.Definition == null || !instance.IsActive)
            {
                return;
            }

            QuestDefinition quest = instance.Definition;
            GameDate date = clock != null ? clock.CurrentDate : new GameDate(1, 1, 1);
            GameTime time = clock != null ? clock.CurrentTime : new GameTime(0, 0);
            instance.MarkFailed(date, time, reason);
            activeQuests.Remove(instance);

            WriteIssuerMemory(quest, BuildMemoryText(quest.FailedMemory, $"Player failed quest: {quest.DisplayName}. Reason: {Clean(reason)}"), "quest_failed", quest.MemoryImportance);
            QuestFailed?.Invoke(instance);

            if (logQuestEvents)
            {
                Debug.LogWarning($"[Quest] Failed {quest.DisplayName}. reason={Clean(reason)}", this);
            }
        }

        private void HandleDayChanged(GameDate date)
        {
            if (!failExpiredQuestsOnDayChanged)
            {
                return;
            }

            for (int i = activeQuests.Count - 1; i >= 0; i--)
            {
                QuestInstance instance = activeQuests[i];
                QuestDefinition quest = instance != null ? instance.Definition : null;
                if (quest == null || quest.ExpiresAfterDays <= 0)
                {
                    continue;
                }

                int elapsedDays = GameCalendar.DaysBetween(instance.AcceptedDate, date);
                if (elapsedDays > quest.ExpiresAfterDays)
                {
                    FailQuest(instance, $"expired after {quest.ExpiresAfterDays} day(s)");
                }
            }
        }

        private QuestDefinition FindAvailableQuestById(string questId)
        {
            if (string.IsNullOrWhiteSpace(questId))
            {
                return null;
            }

            QuestDefinition[] quests = AvailableQuests;
            for (int i = 0; i < quests.Length; i++)
            {
                QuestDefinition quest = quests[i];
                if (quest != null && string.Equals(GetQuestId(quest), questId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return quest;
                }
            }

            return null;
        }

        private QuestInstance FindActiveQuest(QuestDefinition quest)
        {
            if (quest == null)
            {
                return null;
            }

            string questId = GetQuestId(quest);
            for (int i = 0; i < activeQuests.Count; i++)
            {
                QuestInstance instance = activeQuests[i];
                if (instance != null && instance.IsActive && string.Equals(GetQuestId(instance.Definition), questId, StringComparison.OrdinalIgnoreCase))
                {
                    return instance;
                }
            }

            return null;
        }

        private void WriteIssuerMemory(QuestDefinition quest, string text, string source, int importance)
        {
            if (!writeNpcMemories || memorySystem == null || quest == null || quest.Issuer == null || string.IsNullOrWhiteSpace(quest.Issuer.NpcId))
            {
                return;
            }

            memorySystem.AddMemory(quest.Issuer.NpcId, text, source, importance);
        }

        private void Fail(string reason)
        {
            QuestOperationFailed?.Invoke(reason);
            if (logQuestEvents)
            {
                Debug.LogWarning($"[Quest] {reason}", this);
            }
        }

        private static bool RequiresItems(QuestDefinition quest)
        {
            return quest != null
                && (quest.ObjectiveType == QuestObjectiveType.CollectItems || quest.ObjectiveType == QuestObjectiveType.DeliverItems)
                && quest.RequiredItems.Length > 0;
        }

        private static bool HasRewardItems(QuestDefinition quest)
        {
            if (quest == null || quest.RewardItems == null)
            {
                return false;
            }

            for (int i = 0; i < quest.RewardItems.Length; i++)
            {
                if (quest.RewardItems[i] != null && quest.RewardItems[i].IsValid)
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetQuestId(QuestDefinition quest)
        {
            return quest != null && !string.IsNullOrWhiteSpace(quest.QuestId) ? quest.QuestId.Trim() : "unknown_quest";
        }

        private static string BuildMemoryText(string configured, string fallback)
        {
            return string.IsNullOrWhiteSpace(configured) ? fallback : configured.Trim();
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }
}
