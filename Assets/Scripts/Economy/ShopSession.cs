using System;
using System.Collections.Generic;
using CityStateSim.Core;
using CityStateSim.Items;
using CityStateSim.Jobs;
using UnityEngine;

namespace CityStateSim.Economy
{
    public sealed class ShopSession : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private PlayerEconomySystem economySystem;
        [SerializeField] private GameClock clock;
        [SerializeField] private bool closeWhenPlayerLeaves = true;

        private ShopInteraction activeShop;
        private InventorySystem playerInventory;
        private GameObject interactor;
        private readonly Dictionary<string, Dictionary<string, RuntimeShopStock>> runtimeStockByShopId =
            new Dictionary<string, Dictionary<string, RuntimeShopStock>>(StringComparer.OrdinalIgnoreCase);

        public bool IsOpen => activeShop != null && playerInventory != null && economySystem != null;
        public ShopInteraction ActiveShop => activeShop;
        public ShopDefinition CurrentShop => activeShop != null ? activeShop.Definition : null;
        public InventorySystem PlayerInventory => playerInventory;
        public PlayerEconomySystem EconomySystem => economySystem;

        public event Action<ShopDefinition> SessionOpened;
        public event Action<ShopDefinition> SessionClosed;
        public event Action<SellQuote> QuoteBuilt;
        public event Action<SaleResult> SaleAttempted;
        public event Action<SaleResult> SaleCompleted;
        public event Action<BuyQuote> BuyQuoteBuilt;
        public event Action<PurchaseResult> PurchaseAttempted;
        public event Action<PurchaseResult> PurchaseCompleted;
        public event Action<string> TransactionFailed;

        private void Awake()
        {
            ResolveReferences();
        }

        private void Update()
        {
            if (!IsOpen)
            {
                return;
            }

            if (global::DayOverCheck.IsUserInputLocked
                || (closeWhenPlayerLeaves && (interactor == null || !activeShop.CanInteract(interactor))))
            {
                Close();
            }
        }

        public bool Open(ShopInteraction shop, GameObject newInteractor)
        {
            ResolveReferences();
            if (shop == null || shop.Definition == null || newInteractor == null)
            {
                Fail("Shop or interactor is missing.");
                return false;
            }

            if (economySystem == null)
            {
                Fail("PlayerEconomySystem is missing.");
                return false;
            }

            if (!shop.CanInteract(newInteractor))
            {
                Fail("The player is too far away to trade here.");
                return false;
            }

            InventorySystem newInventory = newInteractor.GetComponentInParent<InventorySystem>();
            if (newInventory == null)
            {
                newInventory = newInteractor.GetComponentInChildren<InventorySystem>(true);
            }

            if (newInventory == null)
            {
                Fail("Player inventory is missing.");
                return false;
            }

            if (!IsShopOpen(shop.Definition, out string reason))
            {
                Fail(reason);
                return false;
            }

            if (IsOpen)
            {
                Close();
            }

            activeShop = shop;
            playerInventory = newInventory;
            interactor = newInteractor;
            EnsureRuntimeStock(shop.Definition);
            SessionOpened?.Invoke(shop.Definition);
            return true;
        }

        public void Close()
        {
            if (activeShop == null)
            {
                return;
            }

            ShopDefinition closedShop = activeShop.Definition;
            activeShop = null;
            playerInventory = null;
            interactor = null;
            SessionClosed?.Invoke(closedShop);
        }

        public bool IsShopOpen(out string reason)
        {
            if (CurrentShop == null)
            {
                reason = "No active shop session.";
                return false;
            }

            return IsShopOpen(CurrentShop, out reason);
        }

        public SellQuote BuildQuote(ItemDefinition item, int requestedAmount)
        {
            int requested = Mathf.Max(0, requestedAmount);
            if (!IsOpen)
            {
                return BuildInvalidQuote(item, requested, "No active shop session.");
            }

            if (!IsShopOpen(CurrentShop, out string openReason))
            {
                return BuildInvalidQuote(item, requested, openReason);
            }

            if (!CurrentShop.Accepts(item, out string acceptanceReason))
            {
                return BuildInvalidQuote(item, requested, acceptanceReason);
            }

            int available = playerInventory.CountItem(item);
            if (available <= 0)
            {
                return BuildInvalidQuote(item, requested, "The player does not have this item.", available);
            }

            int sellableAmount = Mathf.Min(requested, available);
            int unitPrice = economySystem.GetSellUnitPrice(
                item,
                CurrentShop.SellPriceMultiplier,
                CurrentShop.AcceptQuestItems);
            int totalPrice = CalculateTotalPrice(unitPrice, sellableAmount);
            bool canSell = sellableAmount > 0 && unitPrice > 0 && totalPrice > 0;
            string reason = canSell ? string.Empty : "This item has no sell value.";

            SellQuote quote = new SellQuote(
                item,
                requested,
                available,
                sellableAmount,
                unitPrice,
                totalPrice,
                canSell,
                reason);
            QuoteBuilt?.Invoke(quote);
            return quote;
        }

        public int GetSellUnitPrice(ItemDefinition item)
        {
            if (!IsOpen || item == null || !IsShopOpen(out _)
                || !CurrentShop.Accepts(item, out _))
            {
                return 0;
            }

            return economySystem.GetSellUnitPrice(
                item,
                CurrentShop.SellPriceMultiplier,
                CurrentShop.AcceptQuestItems);
        }

        public int GetBuyUnitPrice(ItemDefinition item)
        {
            if (!IsOpen || item == null || !IsShopOpen(out _)
                || !CurrentShop.CanBuy(item, out _))
            {
                return 0;
            }

            return CalculateBuyUnitPrice(item);
        }

        public int GetAvailableBuyStock(ItemDefinition item)
        {
            return GetShopStockAmount(item);
        }

        public IReadOnlyList<ItemDefinition> GetAvailableBuyItems()
        {
            return CollectStockItems(null);
        }

        public BuyQuote BuildBuyQuote(ItemDefinition item, int requestedAmount)
        {
            int requested = Mathf.Max(0, requestedAmount);
            if (!IsOpen)
            {
                return BuildInvalidBuyQuote(item, requested, "No active shop session.");
            }

            if (!IsShopOpen(CurrentShop, out string openReason))
            {
                return BuildInvalidBuyQuote(item, requested, openReason);
            }

            if (!CurrentShop.CanBuy(item, out string acceptanceReason))
            {
                return BuildInvalidBuyQuote(item, requested, acceptanceReason);
            }

            int unitPrice = CalculateBuyUnitPrice(item);
            int affordable = unitPrice > 0 ? Mathf.Min(requested, economySystem != null ? economySystem.Money / unitPrice : 0) : 0;
            int available = GetShopStockAmount(item);
            int buyableAmount = Mathf.Min(requested, Mathf.Min(affordable, available));
            int totalPrice = CalculateTotalPrice(unitPrice, buyableAmount);
            bool canBuy = buyableAmount > 0 && unitPrice > 0 && totalPrice > 0;
            string reason = canBuy ? string.Empty : "This item cannot be purchased.";

            BuyQuote quote = new BuyQuote(
                item,
                requested,
                available,
                buyableAmount,
                unitPrice,
                totalPrice,
                canBuy,
                reason);
            BuyQuoteBuilt?.Invoke(quote);
            return quote;
        }

        public bool TryBuy(ItemDefinition item, int amount)
        {
            BuyQuote quote = BuildBuyQuote(item, amount);
            if (!quote.CanBuy)
            {
                PurchaseResult failed = new PurchaseResult(false, item, 0, 0, quote.FailureReason);
                PurchaseAttempted?.Invoke(failed);
                Fail(quote.FailureReason);
                return false;
            }

            bool success = economySystem.TryBuyItem(
                quote.Item,
                quote.BuyableAmount,
                quote.TotalPrice,
                $"buy_at_{CurrentShop.ShopId}");

            if (success)
            {
                DecreaseShopStock(quote.Item, quote.BuyableAmount);
            }

            PurchaseResult result = new PurchaseResult(
                success,
                quote.Item,
                success ? quote.BuyableAmount : 0,
                success ? quote.TotalPrice : 0,
                success ? string.Empty : "The purchase could not be completed.");
            PurchaseAttempted?.Invoke(result);
            if (success)
            {
                PurchaseCompleted?.Invoke(result);
            }
            else
            {
                TransactionFailed?.Invoke(result.FailureReason);
            }

            return success;
        }

        public bool TrySell(ItemDefinition item, int amount)
        {
            SellQuote quote = BuildQuote(item, amount);
            if (!quote.CanSell)
            {
                SaleResult failed = new SaleResult(false, item, 0, 0, quote.FailureReason);
                SaleAttempted?.Invoke(failed);
                Fail(quote.FailureReason);
                return false;
            }

            bool success = economySystem.TryCompleteSale(
                playerInventory,
                quote.Item,
                quote.SellableAmount,
                quote.TotalPrice,
                $"sell_at_{CurrentShop.ShopId}",
                CurrentShop.AcceptQuestItems);

            SaleResult result = new SaleResult(
                success,
                quote.Item,
                success ? quote.SellableAmount : 0,
                success ? quote.TotalPrice : 0,
                success ? string.Empty : "The sale could not be completed.");
            SaleAttempted?.Invoke(result);
            if (success)
            {
                SaleCompleted?.Invoke(result);
            }
            else
            {
                TransactionFailed?.Invoke(result.FailureReason);
            }

            return success;
        }

        public bool TrySellSlot(int slotIndex, int amount)
        {
            if (!IsOpen || !playerInventory.TryGetStackAt(slotIndex, out InventoryStack stack))
            {
                Fail("The selected inventory slot is empty.");
                return false;
            }

            int requested = amount > 0 ? amount : stack.Amount;
            return TrySell(stack.Item, requested);
        }

        public bool TryBuyStockIndex(int stockIndex, int amount)
        {
            IReadOnlyList<ItemDefinition> items = GetAvailableBuyItems();
            if (!IsOpen || stockIndex < 0 || stockIndex >= items.Count)
            {
                Fail("The selected shop stock slot is empty.");
                return false;
            }

            ItemDefinition item = items[stockIndex];
            int requested = amount > 0 ? amount : GetShopStockAmount(item);
            return TryBuy(item, requested);
        }

        public int TrySellAll()
        {
            return SellMatching(null);
        }

        public int TrySellCategory(int categoryValue)
        {
            if (!Enum.IsDefined(typeof(ItemCategory), categoryValue))
            {
                Fail("The item category is invalid.");
                return 0;
            }

            return SellMatching((ItemCategory)categoryValue);
        }

        public int TrySellCategory(ItemCategory category)
        {
            return SellMatching(category);
        }

        private int SellMatching(ItemCategory? category)
        {
            if (!IsOpen)
            {
                Fail("No active shop session.");
                return 0;
            }

            List<ItemDefinition> items = CollectItems(category);
            int soldAmount = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDefinition item = items[i];
                int amount = playerInventory.CountItem(item);
                if (amount > 0 && TrySell(item, amount))
                {
                    soldAmount += amount;
                }
            }

            return soldAmount;
        }

        public int TryBuyAll()
        {
            return BuyMatching(null);
        }

        public int TryBuyCategory(int categoryValue)
        {
            if (!Enum.IsDefined(typeof(ItemCategory), categoryValue))
            {
                Fail("The item category is invalid.");
                return 0;
            }

            return BuyMatching((ItemCategory)categoryValue);
        }

        public int TryBuyCategory(ItemCategory category)
        {
            return BuyMatching(category);
        }

        private List<ItemDefinition> CollectItems(ItemCategory? category)
        {
            List<ItemDefinition> items = new List<ItemDefinition>();
            IReadOnlyList<InventoryStack> stacks = playerInventory.Stacks;
            for (int i = 0; i < stacks.Count; i++)
            {
                InventoryStack stack = stacks[i];
                if (stack == null || stack.IsEmpty || stack.Item == null
                    || (category.HasValue && stack.Item.Category != category.Value))
                {
                    continue;
                }

                bool exists = false;
                for (int j = 0; j < items.Count; j++)
                {
                    if (SameItem(items[j], stack.Item))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists)
                {
                    items.Add(stack.Item);
                }
            }

            return items;
        }

        private int BuyMatching(ItemCategory? category)
        {
            if (!IsOpen)
            {
                Fail("No active shop session.");
                return 0;
            }

            List<ItemDefinition> items = CollectStockItems(category);
            int boughtAmount = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDefinition item = items[i];
                int amount = GetShopStockAmount(item);
                if (amount > 0 && TryBuy(item, amount))
                {
                    boughtAmount += amount;
                }
            }

            return boughtAmount;
        }

        private List<ItemDefinition> CollectStockItems(ItemCategory? category)
        {
            Dictionary<string, RuntimeShopStock> stockByItemId = GetRuntimeStock(CurrentShop);
            List<ItemDefinition> items = new List<ItemDefinition>();
            foreach (RuntimeShopStock stock in stockByItemId.Values)
            {
                if (stock == null || stock.Item == null || stock.Amount <= 0)
                {
                    continue;
                }

                if (category.HasValue && stock.Item.Category != category.Value)
                {
                    continue;
                }

                items.Add(stock.Item);
            }

            return items;
        }

        private int GetShopStockAmount(ItemDefinition item)
        {
            if (item == null || CurrentShop == null)
            {
                return 0;
            }

            Dictionary<string, RuntimeShopStock> stockByItemId = GetRuntimeStock(CurrentShop);
            return stockByItemId.TryGetValue(GetItemKey(item), out RuntimeShopStock stock)
                ? Mathf.Max(0, stock.Amount)
                : 0;
        }

        private void DecreaseShopStock(ItemDefinition item, int amount)
        {
            if (item == null || amount <= 0 || CurrentShop == null)
            {
                return;
            }

            Dictionary<string, RuntimeShopStock> stockByItemId = GetRuntimeStock(CurrentShop);
            if (stockByItemId.TryGetValue(GetItemKey(item), out RuntimeShopStock stock))
            {
                stock.Amount = Mathf.Max(0, stock.Amount - amount);
            }
        }

        private Dictionary<string, RuntimeShopStock> GetRuntimeStock(ShopDefinition shop)
        {
            if (shop == null)
            {
                return new Dictionary<string, RuntimeShopStock>(StringComparer.OrdinalIgnoreCase);
            }

            string shopKey = GetShopKey(shop);
            if (runtimeStockByShopId.TryGetValue(shopKey, out Dictionary<string, RuntimeShopStock> cached))
            {
                return cached;
            }

            Dictionary<string, RuntimeShopStock> stockByItemId =
                new Dictionary<string, RuntimeShopStock>(StringComparer.OrdinalIgnoreCase);
            ItemAmount[] stock = shop.InitialStock;
            for (int i = 0; i < stock.Length; i++)
            {
                ItemAmount entry = stock[i];
                if (entry == null || !entry.IsValid)
                {
                    continue;
                }

                string key = GetItemKey(entry.Item);
                if (stockByItemId.TryGetValue(key, out RuntimeShopStock existing))
                {
                    existing.Amount += entry.Amount;
                }
                else
                {
                    stockByItemId[key] = new RuntimeShopStock(entry.Item, entry.Amount);
                }
            }

            runtimeStockByShopId[shopKey] = stockByItemId;
            return stockByItemId;
        }

        private void EnsureRuntimeStock(ShopDefinition shop)
        {
            GetRuntimeStock(shop);
        }

        private int CalculateBuyUnitPrice(ItemDefinition item)
        {
            if (item == null || CurrentShop == null)
            {
                return 0;
            }

            float multiplier = Mathf.Max(0f, CurrentShop.BuyPriceMultiplier);
            return Mathf.Max(0, Mathf.RoundToInt(item.BaseSellPrice * multiplier));
        }

        private bool IsShopOpen(ShopDefinition shop, out string reason)
        {
            reason = string.Empty;
            if (shop == null)
            {
                reason = "Shop definition is missing.";
                return false;
            }

            if (!shop.RequireLocationOpen || shop.Location == null || clock == null)
            {
                return true;
            }

            if (!shop.Location.IsOpenAtHour(clock.CurrentTime.Hour))
            {
                reason = $"{shop.DisplayName} is closed at {clock.CurrentTime}.";
                return false;
            }

            return true;
        }

        private SellQuote BuildInvalidQuote(
            ItemDefinition item,
            int requested,
            string reason,
            int available = 0)
        {
            SellQuote quote = new SellQuote(item, requested, available, 0, 0, 0, false, reason);
            QuoteBuilt?.Invoke(quote);
            return quote;
        }

        private BuyQuote BuildInvalidBuyQuote(
            ItemDefinition item,
            int requested,
            string reason,
            int available = 0)
        {
            BuyQuote quote = new BuyQuote(item, requested, available, 0, 0, 0, false, reason);
            BuyQuoteBuilt?.Invoke(quote);
            return quote;
        }

        private static int CalculateTotalPrice(int unitPrice, int amount)
        {
            long total = (long)Mathf.Max(0, unitPrice) * Mathf.Max(0, amount);
            return (int)Math.Min(int.MaxValue, Math.Max(0L, total));
        }

        private void ResolveReferences()
        {
            if (economySystem == null)
            {
                economySystem = FindFirstObjectByType<PlayerEconomySystem>();
            }

            if (clock == null)
            {
                clock = FindFirstObjectByType<GameClock>();
            }
        }

        private void Fail(string reason)
        {
            string clean = string.IsNullOrWhiteSpace(reason) ? "Transaction failed." : reason.Trim();
            TransactionFailed?.Invoke(clean);
            Debug.LogWarning($"[ShopSession] {clean}", this);
        }

        private static bool SameItem(ItemDefinition left, ItemDefinition right)
        {
            return left != null && right != null
                && (ReferenceEquals(left, right)
                    || (!string.IsNullOrWhiteSpace(left.ItemId)
                        && string.Equals(left.ItemId, right.ItemId, StringComparison.OrdinalIgnoreCase)));
        }

        private static string GetItemKey(ItemDefinition item)
        {
            return item == null
                ? string.Empty
                : string.IsNullOrWhiteSpace(item.ItemId)
                    ? item.name
                    : item.ItemId.Trim();
        }

        private static string GetShopKey(ShopDefinition shop)
        {
            return shop == null
                ? string.Empty
                : string.IsNullOrWhiteSpace(shop.ShopId)
                    ? shop.name
                    : shop.ShopId.Trim();
        }

        private sealed class RuntimeShopStock
        {
            public RuntimeShopStock(ItemDefinition item, int amount)
            {
                Item = item;
                Amount = Mathf.Max(0, amount);
            }

            public ItemDefinition Item { get; }
            public int Amount { get; set; }
        }
    }
}
