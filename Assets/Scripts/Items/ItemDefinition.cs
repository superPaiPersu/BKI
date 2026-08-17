using UnityEngine;

namespace CityStateSim.Items
{
    [CreateAssetMenu(menuName = "City State Sim/Items/Item")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [SerializeField] private string itemId;
        [SerializeField] private string displayName;
        [SerializeField, TextArea] private string description;
        [SerializeField] private ItemCategory category = ItemCategory.Misc;
        [SerializeField, Min(0)] private int baseSellPrice = 1;
        [SerializeField, Min(1)] private int maxStack = 99;
        [SerializeField] private bool questItem;
        [SerializeField] private Sprite icon;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public string Description => description;
        public ItemCategory Category => category;
        public int BaseSellPrice => baseSellPrice;
        public int MaxStack => Mathf.Max(1, maxStack);
        public bool QuestItem => questItem || category == ItemCategory.Quest;
        public Sprite Icon => icon;

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(itemId))
            {
                itemId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }

            baseSellPrice = Mathf.Max(0, baseSellPrice);
            maxStack = Mathf.Max(1, maxStack);
        }
    }
}
