using System;
using System.Text;
using CityStateSim.Core;
using CityStateSim.Items;
using CityStateSim.Locations;
using CityStateSim.NPC;
using UnityEngine;

namespace CityStateSim.Quests
{
    [CreateAssetMenu(menuName = "City State Sim/Quests/Quest")]
    public sealed class QuestDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string questId;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private NpcProfile issuer;
        [SerializeField] private LocationDefinition targetLocation;

        [Header("Objective")]
        [SerializeField] private QuestObjectiveType objectiveType = QuestObjectiveType.Manual;
        [SerializeField] private string objectiveKey;
        [SerializeField, Min(1)] private int requiredProgress = 1;
        [SerializeField] private ItemAmount[] requiredItems;
        [SerializeField] private bool consumeRequiredItemsOnTurnIn = true;
        [SerializeField] private bool autoReadyWhenRequirementsMet = true;

        [Header("Availability")]
        [SerializeField, Min(0)] private int requiredTrust;
        [SerializeField, Min(0)] private int expiresAfterDays;
        [SerializeField] private bool repeatable;

        [Header("Rewards")]
        [SerializeField, Min(0)] private int rewardMoney;
        [SerializeField] private ItemAmount[] rewardItems;
        [SerializeField] private int trustDeltaOnComplete = 1;
        [SerializeField] private int affinityDeltaOnComplete = 1;
        [SerializeField] private int suspicionDeltaOnComplete;

        [Header("Memory")]
        [SerializeField, Range(1, 10)] private int memoryImportance = 5;
        [SerializeField, TextArea] private string acceptedMemory;
        [SerializeField, TextArea] private string completedMemory;
        [SerializeField, TextArea] private string failedMemory;

        public string QuestId => questId;
        public string DisplayName => displayName;
        public string Description => description;
        public NpcProfile Issuer => issuer;
        public LocationDefinition TargetLocation => targetLocation;
        public QuestObjectiveType ObjectiveType => objectiveType;
        public string ObjectiveKey => objectiveKey;
        public int RequiredProgress => Mathf.Max(1, requiredProgress);
        public ItemAmount[] RequiredItems => requiredItems ?? Array.Empty<ItemAmount>();
        public bool ConsumeRequiredItemsOnTurnIn => consumeRequiredItemsOnTurnIn;
        public bool AutoReadyWhenRequirementsMet => autoReadyWhenRequirementsMet;
        public int RequiredTrust => Mathf.Max(0, requiredTrust);
        public int ExpiresAfterDays => Mathf.Max(0, expiresAfterDays);
        public bool Repeatable => repeatable;
        public int RewardMoney => Mathf.Max(0, rewardMoney);
        public ItemAmount[] RewardItems => rewardItems ?? Array.Empty<ItemAmount>();
        public int TrustDeltaOnComplete => trustDeltaOnComplete;
        public int AffinityDeltaOnComplete => affinityDeltaOnComplete;
        public int SuspicionDeltaOnComplete => suspicionDeltaOnComplete;
        public int MemoryImportance => Mathf.Clamp(memoryImportance, 1, 10);
        public string AcceptedMemory => acceptedMemory;
        public string CompletedMemory => completedMemory;
        public string FailedMemory => failedMemory;

        public bool HasObjectiveKey(string key)
        {
            return !string.IsNullOrWhiteSpace(objectiveKey)
                && !string.IsNullOrWhiteSpace(key)
                && string.Equals(objectiveKey.Trim(), key.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        public string BuildObjectiveSummary()
        {
            switch (objectiveType)
            {
                case QuestObjectiveType.CollectItems:
                    return "Collect " + BuildItemSummary(RequiredItems);
                case QuestObjectiveType.DeliverItems:
                    return "Deliver " + BuildItemSummary(RequiredItems);
                case QuestObjectiveType.ObjectiveProgress:
                    return $"{CleanObjectiveKey()} {RequiredProgress} time(s)";
                case QuestObjectiveType.ReachLocation:
                    return targetLocation != null ? $"Reach {targetLocation.DisplayName}" : "Reach target location";
                default:
                    return string.IsNullOrWhiteSpace(description) ? "Manual objective" : description;
            }
        }

        public string BuildRewardSummary()
        {
            StringBuilder builder = new StringBuilder();
            if (RewardMoney > 0)
            {
                builder.Append(RewardMoney);
                builder.Append(" money");
            }

            string items = BuildItemSummary(RewardItems);
            if (!string.IsNullOrWhiteSpace(items))
            {
                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(items);
            }

            return builder.Length > 0 ? builder.ToString() : "(none)";
        }

        public string BuildSummaryLine()
        {
            string issuerId = issuer != null ? issuer.NpcId : "";
            string locationId = targetLocation != null ? targetLocation.LocationId : "";
            return $"id={questId}, name={displayName}, issuer={issuerId}, location={locationId}, objective={BuildObjectiveSummary()}, reward={BuildRewardSummary()}";
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(questId))
            {
                questId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }

            requiredProgress = Mathf.Max(1, requiredProgress);
            requiredTrust = Mathf.Max(0, requiredTrust);
            expiresAfterDays = Mathf.Max(0, expiresAfterDays);
            rewardMoney = Mathf.Max(0, rewardMoney);
            memoryImportance = Mathf.Clamp(memoryImportance, 1, 10);
        }

        private string CleanObjectiveKey()
        {
            return string.IsNullOrWhiteSpace(objectiveKey) ? "objective" : objectiveKey.Trim();
        }

        private static string BuildItemSummary(ItemAmount[] items)
        {
            if (items == null || items.Length == 0)
            {
                return string.Empty;
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < items.Length; i++)
            {
                ItemAmount item = items[i];
                if (item == null || !item.IsValid)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(item.ToSummaryText());
            }

            return builder.ToString();
        }
    }
}
