using System;
using CityStateSim.Items;
using CityStateSim.Player;
using UnityEngine;

namespace CityStateSim.Economy
{
    public sealed class PlayerEconomySystem : MonoBehaviour
    {
        [SerializeField] private PlayerWallet wallet;
        [SerializeField] private InventorySystem inventory;
        [SerializeField, Min(0f)] private float defaultSellPriceMultiplier = 1f;
        [SerializeField] private bool logTransactions = true;

        public int Money => wallet != null ? wallet.Money : 0;
        public PlayerWallet Wallet => wallet;
        public InventorySystem Inventory => inventory;
        public float DefaultSellPriceMultiplier => Mathf.Max(0f, defaultSellPriceMultiplier);

        public event Action<int, string> MoneyAdded;
        public event Action<int, string> MoneySpent;
        public event Action<ItemDefinition, int, int, string> ItemSold;
        public event Action<string> TransactionFailed;

        private void Awake()
        {
            if (wallet == null)
            {
                wallet = FindFirstObjectByType<PlayerWallet>();
            }

            if (inventory == null)
            {
                inventory = FindFirstObjectByType<InventorySystem>();
            }
        }

        public void AddMoney(int amount, string reason = "")
        {
            if (amount <= 0)
            {
                return;
            }

            wallet?.AddMoney(amount);
            MoneyAdded?.Invoke(amount, Clean(reason));

            if (logTransactions)
            {
                Debug.Log($"[Economy] Added {amount} money. reason={Clean(reason)}", this);
            }
        }

        public bool TrySpendMoney(int amount, string reason = "")
        {
            if (amount <= 0)
            {
                return true;
            }

            if (wallet == null)
            {
                Fail("PlayerWallet is missing.");
                return false;
            }

            if (!wallet.TrySpendMoney(amount))
            {
                Fail($"Not enough money. Need {amount}, current {wallet.Money}.");
                return false;
            }

            MoneySpent?.Invoke(amount, Clean(reason));

            if (logTransactions)
            {
                Debug.Log($"[Economy] Spent {amount} money. reason={Clean(reason)}", this);
            }

            return true;
        }

        public int GetSellUnitPrice(
            ItemDefinition item,
            float additionalMultiplier = 1f,
            bool allowQuestItem = false)
        {
            if (item == null || (item.QuestItem && !allowQuestItem))
            {
                return 0;
            }

            float multiplier = DefaultSellPriceMultiplier * Mathf.Max(0f, additionalMultiplier);
            return Mathf.Max(0, Mathf.RoundToInt(item.BaseSellPrice * multiplier));
        }

        public int GetSellValue(ItemDefinition item, int amount)
        {
            if (item == null || amount <= 0)
            {
                return 0;
            }

            long value = (long)GetSellUnitPrice(item) * amount;
            return (int)Math.Min(int.MaxValue, Math.Max(0L, value));
        }

        public bool TrySellItem(ItemDefinition item, int amount, string reason = "sell_item")
        {
            if (item == null)
            {
                Fail("Cannot sell a missing item.");
                return false;
            }

            if (item.QuestItem)
            {
                Fail($"{item.DisplayName} is a quest item and cannot be sold.");
                return false;
            }

            if (inventory == null)
            {
                Fail("InventorySystem is missing.");
                return false;
            }

            int value = GetSellValue(item, amount);
            if (value <= 0)
            {
                Fail($"{item.DisplayName} has no sell value.");
                return false;
            }

            return TryCompleteSale(inventory, item, amount, value, reason);
        }

        public bool TryBuyItem(
            ItemDefinition item,
            int amount,
            int totalValue,
            string reason = "buy_item")
        {
            if (item == null)
            {
                Fail("Cannot buy a missing item.");
                return false;
            }

            if (amount <= 0 || totalValue <= 0)
            {
                Fail("Purchase amount or value is invalid.");
                return false;
            }

            if (inventory == null)
            {
                Fail("InventorySystem is missing.");
                return false;
            }

            if (!TrySpendMoney(totalValue, reason))
            {
                return false;
            }

            if (!inventory.TryAdd(item, amount))
            {
                AddMoney(totalValue, "buy_refund");
                Fail($"Could not add {item.DisplayName} x{amount} to inventory.");
                return false;
            }

            if (logTransactions)
            {
                Debug.Log($"[Economy] Bought {item.DisplayName} x{amount} for {totalValue}. reason={Clean(reason)}", this);
            }

            return true;
        }

        public bool TryCompleteSale(
            InventorySystem sourceInventory,
            ItemDefinition item,
            int amount,
            int totalValue,
            string reason = "sell_item",
            bool allowQuestItem = false)
        {
            if (sourceInventory == null)
            {
                Fail("InventorySystem is missing.");
                return false;
            }

            if (wallet == null)
            {
                Fail("PlayerWallet is missing.");
                return false;
            }

            if (item == null || (item.QuestItem && !allowQuestItem))
            {
                Fail(item == null ? "Cannot sell a missing item." : $"{item.DisplayName} is a quest item and cannot be sold.");
                return false;
            }

            if (amount <= 0 || totalValue <= 0)
            {
                Fail("Sale amount or value is invalid.");
                return false;
            }

            if (!sourceInventory.HasItem(item, amount))
            {
                Fail($"Missing {item.DisplayName} x{amount}.");
                return false;
            }

            if (!sourceInventory.TryRemove(item, amount))
            {
                return false;
            }

            wallet.AddMoney(totalValue);
            ItemSold?.Invoke(item, amount, totalValue, Clean(reason));
            MoneyAdded?.Invoke(totalValue, Clean(reason));

            if (logTransactions)
            {
                Debug.Log($"[Economy] Sold {item.DisplayName} x{amount} for {totalValue}. reason={Clean(reason)}", this);
            }

            return true;
        }

        private void Fail(string reason)
        {
            TransactionFailed?.Invoke(reason);
            if (logTransactions)
            {
                Debug.LogWarning($"[Economy] {reason}", this);
            }
        }

        private static string Clean(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }
}
