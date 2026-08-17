using System;
using UnityEngine;

namespace CityStateSim.Items
{
    [Serializable]
    public sealed class ItemAmount
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField, Min(1)] private int amount = 1;

        public ItemDefinition Item => item;
        public int Amount => Mathf.Max(1, amount);

        public bool IsValid => item != null && Amount > 0;

        public string ToSummaryText()
        {
            string name = item != null ? item.DisplayName : "(missing item)";
            return $"{name} x{Amount}";
        }
    }
}
