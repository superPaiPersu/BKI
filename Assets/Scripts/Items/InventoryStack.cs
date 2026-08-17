using System;
using UnityEngine;

namespace CityStateSim.Items
{
    [Serializable]
    public sealed class InventoryStack
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField] private int amount;

        public ItemDefinition Item => item;
        public int Amount => amount;
        public bool IsEmpty => item == null || amount <= 0;
        public int RemainingStackSpace => item != null ? Mathf.Max(0, item.MaxStack - amount) : 0;

        public InventoryStack(ItemDefinition item, int amount)
        {
            this.item = item;
            this.amount = Mathf.Max(0, amount);
        }

        public int Add(int value)
        {
            if (item == null || value <= 0)
            {
                return value;
            }

            int accepted = Mathf.Min(value, RemainingStackSpace);
            amount += accepted;
            return value - accepted;
        }

        public int Remove(int value)
        {
            if (value <= 0 || IsEmpty)
            {
                return 0;
            }

            int removed = Mathf.Min(value, amount);
            amount -= removed;
            return removed;
        }
    }
}
