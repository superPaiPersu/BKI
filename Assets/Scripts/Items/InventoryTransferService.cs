using System;
using System.Collections.Generic;

namespace CityStateSim.Items
{
    /// <summary>
    /// Moves items between two inventories without exposing either inventory's
    /// internal stack list to UI code.
    /// </summary>
    public static class InventoryTransferService
    {
        public static bool TryTransfer(
            InventorySystem source,
            InventorySystem destination,
            ItemDefinition item,
            int requestedAmount,
            out int movedAmount)
        {
            movedAmount = 0;
            if (source == null || destination == null || ReferenceEquals(source, destination) || item == null)
            {
                return false;
            }

            int requested = Math.Max(0, Math.Min(requestedAmount, source.CountItem(item)));
            if (requested <= 0)
            {
                return false;
            }

            int transferable = FindLargestTransferableAmount(destination, item, requested);
            if (transferable <= 0 || !source.HasItem(item, transferable))
            {
                return false;
            }

            if (!source.TryRemove(item, transferable))
            {
                return false;
            }

            if (!destination.TryAdd(item, transferable))
            {
                // CanAdd was checked above, so this is only a defensive rollback
                // for an unexpected external mutation during the operation.
                source.TryAdd(item, transferable);
                return false;
            }

            movedAmount = transferable;
            return true;
        }

        public static bool TryTransferBetweenSlots(
            InventorySystem source,
            int sourceSlotIndex,
            InventorySystem destination,
            int destinationSlotIndex,
            int requestedAmount,
            out int movedAmount)
        {
            return TryTransferOrSwapBetweenSlots(
                source,
                sourceSlotIndex,
                destination,
                destinationSlotIndex,
                requestedAmount,
                out movedAmount,
                out _);
        }

        public static bool TryTransferOrSwapBetweenSlots(
            InventorySystem source,
            int sourceSlotIndex,
            InventorySystem destination,
            int destinationSlotIndex,
            int requestedAmount,
            out int movedAmount,
            out bool swapped)
        {
            movedAmount = 0;
            swapped = false;
            if (source == null
                || destination == null
                || ReferenceEquals(source, destination)
                || !source.TryGetStackAt(sourceSlotIndex, out InventoryStack sourceStack))
            {
                return false;
            }

            int requested = Math.Max(0, Math.Min(requestedAmount, sourceStack.Amount));
            int transferable = destination.GetAddableAmountAtSlot(
                sourceStack.Item,
                destinationSlotIndex,
                requested);
            if (transferable <= 0)
            {
                return TrySwapDifferentFullStacks(
                    source,
                    sourceSlotIndex,
                    sourceStack,
                    destination,
                    destinationSlotIndex,
                    requested,
                    out movedAmount,
                    out swapped);
            }

            ItemDefinition item = sourceStack.Item;
            if (!source.TryRemoveFromSlot(sourceSlotIndex, transferable))
            {
                return false;
            }

            if (!destination.TryAddAtSlot(item, transferable, destinationSlotIndex))
            {
                source.TryAddAtSlot(item, transferable, sourceSlotIndex);
                return false;
            }

            movedAmount = transferable;
            return true;
        }

        public static int TransferAll(InventorySystem source, InventorySystem destination)
        {
            return TransferItems(source, destination, false);
        }

        public static int TransferMatchingItemTypes(
            InventorySystem source,
            InventorySystem destination)
        {
            return TransferItems(source, destination, true);
        }

        public static bool HasMatchingItemTypes(
            InventorySystem source,
            InventorySystem destination)
        {
            if (source == null || destination == null || ReferenceEquals(source, destination))
            {
                return false;
            }

            List<ItemDefinition> items = CollectItems(source);
            for (int i = 0; i < items.Count; i++)
            {
                if (destination.CountItem(items[i]) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static int TransferItems(
            InventorySystem source,
            InventorySystem destination,
            bool requireExistingDestinationItem)
        {
            if (source == null || destination == null || ReferenceEquals(source, destination))
            {
                return 0;
            }

            List<ItemDefinition> items = CollectItems(source);
            int movedTotal = 0;
            for (int i = 0; i < items.Count; i++)
            {
                ItemDefinition item = items[i];
                if (requireExistingDestinationItem && destination.CountItem(item) <= 0)
                {
                    continue;
                }

                if (TryTransfer(source, destination, item, source.CountItem(item), out int moved))
                {
                    movedTotal += moved;
                }
            }

            return movedTotal;
        }

        private static List<ItemDefinition> CollectItems(InventorySystem source)
        {
            List<ItemDefinition> items = new List<ItemDefinition>();
            for (int i = 0; i < source.Stacks.Count; i++)
            {
                InventoryStack stack = source.Stacks[i];
                if (stack == null || stack.IsEmpty || stack.Item == null)
                {
                    continue;
                }

                bool alreadyCollected = false;
                for (int j = 0; j < items.Count; j++)
                {
                    if (SameItem(items[j], stack.Item))
                    {
                        alreadyCollected = true;
                        break;
                    }
                }

                if (!alreadyCollected)
                {
                    items.Add(stack.Item);
                }
            }

            return items;
        }

        private static int FindLargestTransferableAmount(
            InventorySystem destination,
            ItemDefinition item,
            int requested)
        {
            int low = 0;
            int high = requested;
            while (low < high)
            {
                int candidate = low + (high - low + 1) / 2;
                if (destination.CanAdd(item, candidate))
                {
                    low = candidate;
                }
                else
                {
                    high = candidate - 1;
                }
            }

            return low;
        }

        private static bool TrySwapDifferentFullStacks(
            InventorySystem source,
            int sourceSlotIndex,
            InventoryStack sourceStack,
            InventorySystem destination,
            int destinationSlotIndex,
            int requested,
            out int movedAmount,
            out bool swapped)
        {
            movedAmount = 0;
            swapped = false;
            if (sourceStack == null
                || sourceStack.IsEmpty
                || requested != sourceStack.Amount
                || !destination.TryGetStackAt(destinationSlotIndex, out InventoryStack destinationStack)
                || destinationStack == null
                || destinationStack.IsEmpty
                || SameItem(sourceStack.Item, destinationStack.Item))
            {
                return false;
            }

            if (!source.TrySwapSlotWith(destination, sourceSlotIndex, destinationSlotIndex))
            {
                return false;
            }

            movedAmount = requested;
            swapped = true;
            return true;
        }

        private static bool SameItem(ItemDefinition left, ItemDefinition right)
        {
            if (left == null || right == null)
            {
                return false;
            }

            return ReferenceEquals(left, right)
                || (!string.IsNullOrWhiteSpace(left.ItemId)
                    && string.Equals(left.ItemId, right.ItemId, StringComparison.OrdinalIgnoreCase));
        }
    }
}
