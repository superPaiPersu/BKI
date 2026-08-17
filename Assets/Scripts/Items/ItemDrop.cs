using System;
using UnityEngine;

namespace CityStateSim.Items
{
    [Serializable]
    public sealed class ItemDrop
    {
        [SerializeField] private ItemDefinition item;
        [SerializeField, Min(0)] private int minAmount = 1;
        [SerializeField, Min(0)] private int maxAmount = 1;
        [SerializeField, Range(0f, 1f)] private float chance = 1f;

        public ItemDefinition Item => item;
        public int MinAmount => Mathf.Max(0, minAmount);
        public int MaxAmount => Mathf.Max(MinAmount, maxAmount);
        public float Chance => Mathf.Clamp01(chance);
        public bool IsValid => item != null && MaxAmount > 0 && Chance > 0f;

        public bool TryRoll(out ItemDefinition rolledItem, out int rolledAmount)
        {
            rolledItem = null;
            rolledAmount = 0;
            if (!IsValid || UnityEngine.Random.value > Chance)
            {
                return false;
            }

            rolledItem = item;
            rolledAmount = UnityEngine.Random.Range(MinAmount, MaxAmount + 1);
            return rolledAmount > 0;
        }

        private void OnValidate()
        {
            minAmount = Mathf.Max(0, minAmount);
            maxAmount = Mathf.Max(minAmount, maxAmount);
            chance = Mathf.Clamp01(chance);
        }
    }
}
