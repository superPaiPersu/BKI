using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace CityStateSim.Items
{
    public sealed class InventorySystem : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxSlots = 24;
        [SerializeField] private bool logChanges = true;

        private readonly List<InventoryStack> stacks = new List<InventoryStack>();

        public int MaxSlots => Mathf.Max(1, maxSlots);
        public IReadOnlyList<InventoryStack> Stacks => stacks;

        public event Action InventoryChanged;
        public event Action<ItemDefinition, int> ItemAdded;
        public event Action<ItemDefinition, int> ItemRemoved;
        public event Action<string> InventoryOperationFailed;

        public bool TryGetStackAt(int index, out InventoryStack stack)
        {
            stack = null;
            if (index < 0 || index >= stacks.Count)
            {
                return false;
            }

            stack = stacks[index];
            return stack != null && !stack.IsEmpty && stack.Item != null;
        }

        public bool CanAdd(ItemDefinition item, int amount)
        {
            if (item == null || amount <= 0)
            {
                return false;
            }

            int remaining = amount;
            for (int i = 0; i < stacks.Count && remaining > 0; i++)
            {
                InventoryStack stack = stacks[i];
                if (stack != null && !stack.IsEmpty && SameItem(stack.Item, item))
                {
                    remaining -= stack.RemainingStackSpace;
                }
            }

            int emptySlots = Mathf.Max(0, MaxSlots - CountUsedStacks());
            while (remaining > 0 && emptySlots > 0)
            {
                remaining -= item.MaxStack;
                emptySlots--;
            }

            return remaining <= 0;
        }

        public bool TryAdd(ItemDefinition item, int amount)
        {
            if (item == null)
            {
                Fail("Cannot add a missing item.");
                return false;
            }

            if (amount <= 0)
            {
                return true;
            }

            if (!CanAdd(item, amount))
            {
                Fail($"Not enough inventory space for {item.DisplayName} x{amount}.");
                return false;
            }

            int remaining = amount;
            for (int i = 0; i < stacks.Count && remaining > 0; i++)
            {
                InventoryStack stack = stacks[i];
                if (stack != null && !stack.IsEmpty && SameItem(stack.Item, item))
                {
                    remaining = stack.Add(remaining);
                }
            }

            while (remaining > 0)
            {
                int stackAmount = Mathf.Min(remaining, item.MaxStack);
                int slotIndex = FindFirstEmptySlotIndex();
                if (slotIndex >= 0)
                {
                    stacks[slotIndex] = new InventoryStack(item, stackAmount);
                }
                else if (stacks.Count < MaxSlots)
                {
                    stacks.Add(new InventoryStack(item, stackAmount));
                }
                else
                {
                    Fail("Not enough inventory space.");
                    return false;
                }

                remaining -= stackAmount;
            }

            ItemAdded?.Invoke(item, amount);
            InventoryChanged?.Invoke();

            if (logChanges)
            {
                Debug.Log($"[Inventory] Added {item.DisplayName} x{amount}.", this);
            }

            return true;
        }

        public int GetAddableAmountAtSlot(ItemDefinition item, int slotIndex, int requestedAmount)
        {
            if (item == null || requestedAmount <= 0 || slotIndex < 0 || slotIndex >= MaxSlots)
            {
                return 0;
            }

            InventoryStack target = slotIndex < stacks.Count ? stacks[slotIndex] : null;
            if (target == null || target.IsEmpty)
            {
                return Mathf.Min(requestedAmount, item.MaxStack);
            }

            if (!SameItem(target.Item, item))
            {
                return 0;
            }

            return Mathf.Min(requestedAmount, target.RemainingStackSpace);
        }

        public bool TryAddAtSlot(ItemDefinition item, int amount, int slotIndex)
        {
            if (GetAddableAmountAtSlot(item, slotIndex, amount) != amount)
            {
                return false;
            }

            EnsureSlotCapacity(slotIndex);
            InventoryStack target = stacks[slotIndex];
            if (target == null || target.IsEmpty)
            {
                stacks[slotIndex] = new InventoryStack(item, amount);
            }
            else if (target.Add(amount) > 0)
            {
                return false;
            }

            ItemAdded?.Invoke(item, amount);
            InventoryChanged?.Invoke();

            if (logChanges)
            {
                Debug.Log($"[Inventory] Added {item.DisplayName} x{amount} to slot {slotIndex}.", this);
            }

            return true;
        }

        public bool TryAdd(ItemAmount itemAmount)
        {
            return itemAmount == null || TryAdd(itemAmount.Item, itemAmount.Amount);
        }

        public bool TryAddAll(IEnumerable<ItemAmount> itemAmounts)
        {
            if (itemAmounts == null)
            {
                return true;
            }

            List<RuntimeItemAmount> valid = BuildAggregatedAmounts(itemAmounts);
            if (!CanAddAllAggregated(valid))
            {
                Fail("Not enough inventory space for all items.");
                return false;
            }

            for (int i = 0; i < valid.Count; i++)
            {
                TryAdd(valid[i].Item, valid[i].Amount);
            }

            return true;
        }

        public bool CanAddAll(IEnumerable<ItemAmount> itemAmounts)
        {
            return CanAddAllAggregated(BuildAggregatedAmounts(itemAmounts));
        }

        public bool HasItem(ItemDefinition item, int amount)
        {
            return CountItem(item) >= Mathf.Max(0, amount);
        }

        public bool HasAll(IEnumerable<ItemAmount> itemAmounts)
        {
            List<RuntimeItemAmount> required = BuildAggregatedAmounts(itemAmounts);
            for (int i = 0; i < required.Count; i++)
            {
                if (!HasItem(required[i].Item, required[i].Amount))
                {
                    return false;
                }
            }

            return true;
        }

        public int CountItem(ItemDefinition item)
        {
            if (item == null)
            {
                return 0;
            }

            int total = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                InventoryStack stack = stacks[i];
                if (stack != null && SameItem(stack.Item, item))
                {
                    total += stack.Amount;
                }
            }

            return total;
        }

        public bool TryRemove(ItemDefinition item, int amount)
        {
            if (item == null)
            {
                Fail("Cannot remove a missing item.");
                return false;
            }

            if (amount <= 0)
            {
                return true;
            }

            if (!HasItem(item, amount))
            {
                Fail($"Missing {item.DisplayName} x{amount}. Current amount: {CountItem(item)}.");
                return false;
            }

            int remaining = amount;
            for (int i = stacks.Count - 1; i >= 0 && remaining > 0; i--)
            {
                InventoryStack stack = stacks[i];
                if (stack == null || stack.IsEmpty || !SameItem(stack.Item, item))
                {
                    continue;
                }

                remaining -= stack.Remove(remaining);
                if (stack.IsEmpty)
                {
                    stacks[i] = null;
                }
            }

            ItemRemoved?.Invoke(item, amount);
            InventoryChanged?.Invoke();

            if (logChanges)
            {
                Debug.Log($"[Inventory] Removed {item.DisplayName} x{amount}.", this);
            }

            return true;
        }

        public bool TryRemoveFromSlot(int slotIndex, int amount)
        {
            if (!IsValidOccupiedIndex(slotIndex) || amount <= 0)
            {
                return false;
            }

            InventoryStack source = stacks[slotIndex];
            if (source == null || source.IsEmpty || amount > source.Amount)
            {
                return false;
            }

            ItemDefinition item = source.Item;
            int removed = source.Remove(amount);
            if (removed != amount)
            {
                return false;
            }

            if (source.IsEmpty)
            {
                stacks[slotIndex] = null;
            }

            ItemRemoved?.Invoke(item, amount);
            InventoryChanged?.Invoke();

            if (logChanges)
            {
                Debug.Log($"[Inventory] Removed {item.DisplayName} x{amount} from slot {slotIndex}.", this);
            }

            return true;
        }

        public bool TryRemoveAll(IEnumerable<ItemAmount> itemAmounts)
        {
            if (itemAmounts == null)
            {
                return true;
            }

            List<RuntimeItemAmount> valid = BuildAggregatedAmounts(itemAmounts);

            if (!HasAllAggregated(valid))
            {
                Fail("Missing required items.");
                return false;
            }

            for (int i = 0; i < valid.Count; i++)
            {
                TryRemove(valid[i].Item, valid[i].Amount);
            }

            return true;
        }

        public string BuildSummary()
        {
            if (stacks.Count == 0)
            {
                return "(empty)";
            }

            StringBuilder builder = new StringBuilder();
            for (int i = 0; i < stacks.Count; i++)
            {
                InventoryStack stack = stacks[i];
                if (stack == null || stack.IsEmpty)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.AppendLine();
                }

                builder.Append("- ");
                builder.Append(stack.Item.DisplayName);
                builder.Append(" x");
                builder.Append(stack.Amount);
            }

            return builder.Length > 0 ? builder.ToString() : "(empty)";
        }

        public bool TryOrganize()
        {
            List<RuntimeItemAmount> combined = new List<RuntimeItemAmount>();
            for (int i = 0; i < stacks.Count; i++)
            {
                InventoryStack stack = stacks[i];
                if (stack == null || stack.IsEmpty || stack.Item == null)
                {
                    continue;
                }

                AddAggregated(combined, stack.Item, stack.Amount);
            }

            if (combined.Count == 0)
            {
                return false;
            }

            combined.Sort(CompareRuntimeItemAmounts);

            List<InventoryStack> organized = new List<InventoryStack>(MaxSlots);
            for (int i = 0; i < combined.Count; i++)
            {
                RuntimeItemAmount itemAmount = combined[i];
                int remaining = itemAmount.Amount;
                while (remaining > 0 && organized.Count < MaxSlots)
                {
                    int stackAmount = Mathf.Min(remaining, itemAmount.Item.MaxStack);
                    organized.Add(new InventoryStack(itemAmount.Item, stackAmount));
                    remaining -= stackAmount;
                }

                if (remaining > 0)
                {
                    Fail("Not enough inventory slots to organize items.");
                    return false;
                }
            }

            while (organized.Count < MaxSlots)
            {
                organized.Add(null);
            }

            stacks.Clear();
            stacks.AddRange(organized);
            InventoryChanged?.Invoke();

            if (logChanges)
            {
                Debug.Log("[Inventory] Organized inventory stacks.", this);
            }

            return true;
        }

        private bool HasAllAggregated(List<RuntimeItemAmount> itemAmounts)
        {
            if (itemAmounts == null)
            {
                return true;
            }

            for (int i = 0; i < itemAmounts.Count; i++)
            {
                RuntimeItemAmount itemAmount = itemAmounts[i];
                if (itemAmount != null && itemAmount.Item != null && itemAmount.Amount > 0 && !HasItem(itemAmount.Item, itemAmount.Amount))
                {
                    return false;
                }
            }

            return true;
        }

        private void Fail(string reason)
        {
            InventoryOperationFailed?.Invoke(reason);
            if (logChanges)
            {
                Debug.LogWarning($"[Inventory] {reason}", this);
            }
        }

        private bool CanAddAllAggregated(List<RuntimeItemAmount> itemAmounts)
        {
            if (itemAmounts == null || itemAmounts.Count == 0)
            {
                return true;
            }

            List<RuntimeItemAmount> simulatedStacks = new List<RuntimeItemAmount>();
            for (int i = 0; i < stacks.Count; i++)
            {
                InventoryStack stack = stacks[i];
                if (stack != null && !stack.IsEmpty)
                {
                    simulatedStacks.Add(new RuntimeItemAmount(stack.Item, stack.Amount));
                }
            }

            for (int i = 0; i < itemAmounts.Count; i++)
            {
                RuntimeItemAmount itemAmount = itemAmounts[i];
                if (itemAmount == null || itemAmount.Item == null || itemAmount.Amount <= 0)
                {
                    continue;
                }

                int remaining = itemAmount.Amount;
                for (int j = 0; j < simulatedStacks.Count && remaining > 0; j++)
                {
                    RuntimeItemAmount stack = simulatedStacks[j];
                    if (stack == null || !SameItem(stack.Item, itemAmount.Item))
                    {
                        continue;
                    }

                    int accepted = Mathf.Min(remaining, Mathf.Max(0, stack.Item.MaxStack - stack.Amount));
                    stack.Amount += accepted;
                    remaining -= accepted;
                }

                while (remaining > 0)
                {
                    if (simulatedStacks.Count >= MaxSlots)
                    {
                        return false;
                    }

                    int accepted = Mathf.Min(remaining, itemAmount.Item.MaxStack);
                    simulatedStacks.Add(new RuntimeItemAmount(itemAmount.Item, accepted));
                    remaining -= accepted;
                }
            }

            return true;
        }

        private static List<RuntimeItemAmount> BuildAggregatedAmounts(IEnumerable<ItemAmount> itemAmounts)
        {
            List<RuntimeItemAmount> result = new List<RuntimeItemAmount>();
            if (itemAmounts == null)
            {
                return result;
            }

            foreach (ItemAmount itemAmount in itemAmounts)
            {
                if (itemAmount == null || !itemAmount.IsValid)
                {
                    continue;
                }

                AddAggregated(result, itemAmount.Item, itemAmount.Amount);
            }

            return result;
        }

        private static void AddAggregated(List<RuntimeItemAmount> amounts, ItemDefinition item, int amount)
        {
            if (amounts == null || item == null || amount <= 0)
            {
                return;
            }

            for (int i = 0; i < amounts.Count; i++)
            {
                RuntimeItemAmount existing = amounts[i];
                if (existing != null && SameItem(existing.Item, item))
                {
                    existing.Amount += amount;
                    return;
                }
            }

            amounts.Add(new RuntimeItemAmount(item, amount));
        }

        private static int CompareRuntimeItemAmounts(RuntimeItemAmount left, RuntimeItemAmount right)
        {
            if (ReferenceEquals(left, right))
            {
                return 0;
            }

            if (left == null)
            {
                return 1;
            }

            if (right == null)
            {
                return -1;
            }

            int categoryComparison = left.Item.Category.CompareTo(right.Item.Category);
            if (categoryComparison != 0)
            {
                return categoryComparison;
            }

            int nameComparison = string.Compare(
                left.Item.DisplayName,
                right.Item.DisplayName,
                StringComparison.OrdinalIgnoreCase);
            if (nameComparison != 0)
            {
                return nameComparison;
            }

            return string.Compare(
                left.Item.ItemId,
                right.Item.ItemId,
                StringComparison.OrdinalIgnoreCase);
        }

        public bool TryMoveStack(int fromIndex, int toIndex)
        {
            if (!IsValidOccupiedIndex(fromIndex))
            {
                Fail("Cannot move from an empty slot.");
                return false;
            }

            if (toIndex < 0 || toIndex >= MaxSlots)
            {
                Fail("Target slot is outside inventory capacity.");
                return false;
            }

            if (fromIndex == toIndex)
            {
                return true;
            }

            EnsureSlotCapacity(Mathf.Max(fromIndex, toIndex));

            InventoryStack source = stacks[fromIndex];
            InventoryStack target = stacks[toIndex];
            if (target == null || target.IsEmpty)
            {
                stacks[toIndex] = source;
                stacks[fromIndex] = null;
                InventoryChanged?.Invoke();
                LogMove(source, fromIndex, toIndex);
                return true;
            }

            if (SameItem(source.Item, target.Item))
            {
                int accepted = Mathf.Min(source.Amount, target.RemainingStackSpace);
                if (accepted <= 0)
                {
                    Fail("Target stack is full.");
                    return false;
                }

                target.Add(accepted);
                source.Remove(accepted);
                if (source.IsEmpty)
                {
                    stacks[fromIndex] = null;
                }

                InventoryChanged?.Invoke();
                LogMove(source, fromIndex, toIndex);
                return true;
            }

            stacks[fromIndex] = target;
            stacks[toIndex] = source;
            InventoryChanged?.Invoke();
            LogMove(source, fromIndex, toIndex);
            return true;
        }

        public bool TrySwapSlotWith(InventorySystem other, int thisSlotIndex, int otherSlotIndex)
        {
            if (other == null || ReferenceEquals(this, other))
            {
                return false;
            }

            if (!IsValidOccupiedIndex(thisSlotIndex))
            {
                Fail("Cannot swap from an empty slot.");
                return false;
            }

            if (!other.IsValidOccupiedIndex(otherSlotIndex))
            {
                other.Fail("Cannot swap with an empty slot.");
                return false;
            }

            if (thisSlotIndex < 0 || thisSlotIndex >= MaxSlots || otherSlotIndex < 0 || otherSlotIndex >= other.MaxSlots)
            {
                Fail("Swap slot is outside inventory capacity.");
                return false;
            }

            EnsureSlotCapacity(thisSlotIndex);
            other.EnsureSlotCapacity(otherSlotIndex);

            InventoryStack thisStack = stacks[thisSlotIndex];
            InventoryStack otherStack = other.stacks[otherSlotIndex];
            if (thisStack == null || thisStack.IsEmpty || otherStack == null || otherStack.IsEmpty)
            {
                return false;
            }

            stacks[thisSlotIndex] = otherStack;
            other.stacks[otherSlotIndex] = thisStack;
            InventoryChanged?.Invoke();
            other.InventoryChanged?.Invoke();

            if (logChanges)
            {
                Debug.Log($"[Inventory] Swapped {thisStack.Item.DisplayName} x{thisStack.Amount} with {otherStack.Item.DisplayName} x{otherStack.Amount}.", this);
            }

            if (other.logChanges)
            {
                Debug.Log($"[Inventory] Swapped {otherStack.Item.DisplayName} x{otherStack.Amount} with {thisStack.Item.DisplayName} x{thisStack.Amount}.", other);
            }

            return true;
        }

        public bool TrySplitStack(int fromIndex, int toIndex, int amount)
        {
            if (!IsValidOccupiedIndex(fromIndex))
            {
                Fail("Cannot split from an empty slot.");
                return false;
            }

            if (toIndex < 0 || toIndex >= MaxSlots)
            {
                Fail("Target slot is outside inventory capacity.");
                return false;
            }

            if (amount <= 0)
            {
                return false;
            }

            InventoryStack source = stacks[fromIndex];
            if (source == null || source.IsEmpty)
            {
                Fail("Cannot split a missing stack.");
                return false;
            }

            if (amount >= source.Amount)
            {
                return TryMoveStack(fromIndex, toIndex);
            }

            EnsureSlotCapacity(Mathf.Max(fromIndex, toIndex));
            InventoryStack target = stacks[toIndex];
            if (target != null && !target.IsEmpty)
            {
                if (!SameItem(target.Item, source.Item))
                {
                    Fail("Target slot is occupied.");
                    return false;
                }

                int accepted = Mathf.Min(amount, target.RemainingStackSpace);
                if (accepted <= 0)
                {
                    Fail("Target stack is full.");
                    return false;
                }

                target.Add(accepted);
                source.Remove(accepted);
                if (source.IsEmpty)
                {
                    stacks[fromIndex] = null;
                }

                InventoryChanged?.Invoke();
                LogSplit(source, fromIndex, toIndex, accepted);
                return true;
            }

            stacks[toIndex] = new InventoryStack(source.Item, amount);
            source.Remove(amount);
            if (source.IsEmpty)
            {
                stacks[fromIndex] = null;
            }

            InventoryChanged?.Invoke();
            LogSplit(source, fromIndex, toIndex, amount);
            return true;
        }

        public bool TryDropStackOnSlot(int fromIndex, int toIndex, int amount)
        {
            if (!IsValidOccupiedIndex(fromIndex))
            {
                return false;
            }

            InventoryStack source = stacks[fromIndex];
            if (source == null || source.IsEmpty)
            {
                return false;
            }

            if (fromIndex == toIndex)
            {
                return true;
            }

            if (amount <= 0 || amount >= source.Amount)
            {
                return TryMoveStack(fromIndex, toIndex);
            }

            return TrySplitStack(fromIndex, toIndex, amount);
        }

        private int CountUsedStacks()
        {
            int count = 0;
            for (int i = 0; i < stacks.Count; i++)
            {
                InventoryStack stack = stacks[i];
                if (stack != null && !stack.IsEmpty)
                {
                    count++;
                }
            }

            return count;
        }

        private int FindFirstEmptySlotIndex()
        {
            for (int i = 0; i < stacks.Count; i++)
            {
                InventoryStack stack = stacks[i];
                if (stack == null || stack.IsEmpty)
                {
                    return i;
                }
            }

            return -1;
        }

        private void EnsureSlotCapacity(int index)
        {
            if (index < 0)
            {
                return;
            }

            while (stacks.Count <= index && stacks.Count < MaxSlots)
            {
                stacks.Add(null);
            }
        }

        private bool IsValidOccupiedIndex(int index)
        {
            return index >= 0 && index < stacks.Count
                && stacks[index] != null
                && !stacks[index].IsEmpty;
        }

        private void LogMove(InventoryStack stack, int fromIndex, int toIndex)
        {
            if (logChanges && stack != null && stack.Item != null)
            {
                Debug.Log($"[Inventory] Moved {stack.Item.DisplayName} x{stack.Amount} from slot {fromIndex} to {toIndex}.", this);
            }
        }

        private void LogSplit(InventoryStack stack, int fromIndex, int toIndex, int amount)
        {
            if (logChanges && stack != null && stack.Item != null)
            {
                Debug.Log($"[Inventory] Split {stack.Item.DisplayName} x{amount} from slot {fromIndex} to {toIndex}.", this);
            }
        }

        private static bool SameItem(ItemDefinition left, ItemDefinition right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            if (ReferenceEquals(left, right))
            {
                return true;
            }

            return !string.IsNullOrWhiteSpace(left.ItemId)
                && string.Equals(left.ItemId, right.ItemId, StringComparison.OrdinalIgnoreCase);
        }

        private sealed class RuntimeItemAmount
        {
            public RuntimeItemAmount(ItemDefinition item, int amount)
            {
                Item = item;
                Amount = amount;
            }

            public ItemDefinition Item { get; }
            public int Amount { get; set; }
        }
    }
}
