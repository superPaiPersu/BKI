using CityStateSim.Locations;
using CityStateSim.NPC;
using CityStateSim.Items;
using UnityEngine;

namespace CityStateSim.Jobs
{
    [CreateAssetMenu(menuName = "City State Sim/Jobs/Shop")]
    public sealed class ShopDefinition : ScriptableObject
    {
        [SerializeField] private string shopId;
        [SerializeField] private string displayName;
        [SerializeField] private LocationDefinition location;
        [SerializeField] private NpcProfile owner;

        [Header("Selling")]
        [SerializeField, Min(0f)] private float sellPriceMultiplier = 1f;
        [SerializeField] private bool acceptAllCategories = true;
        [SerializeField] private ItemCategory[] acceptedCategories;
        [SerializeField] private bool acceptQuestItems;
        [SerializeField] private bool requireLocationOpen = true;

        [Header("Buying")]
        [SerializeField] private bool enableBuying = false;
        [SerializeField, Min(0f)] private float buyPriceMultiplier = 1f;
        [SerializeField] private ItemAmount[] initialStock;

        public string ShopId => shopId;
        public string DisplayName => displayName;
        public LocationDefinition Location => location;
        public NpcProfile Owner => owner;
        public float SellPriceMultiplier => Mathf.Max(0f, sellPriceMultiplier);
        public bool AcceptAllCategories => acceptAllCategories;
        public ItemCategory[] AcceptedCategories => acceptedCategories ?? System.Array.Empty<ItemCategory>();
        public bool AcceptQuestItems => acceptQuestItems;
        public bool RequireLocationOpen => requireLocationOpen;
        public bool EnableBuying => enableBuying;
        public float BuyPriceMultiplier => Mathf.Max(0f, buyPriceMultiplier);
        public ItemAmount[] InitialStock => initialStock ?? System.Array.Empty<ItemAmount>();

        public bool Accepts(ItemDefinition item, out string reason)
        {
            reason = string.Empty;
            if (item == null)
            {
                reason = "Item is missing.";
                return false;
            }

            if (item.QuestItem && !acceptQuestItems)
            {
                reason = "Quest items cannot be sold here.";
                return false;
            }

            if (!acceptAllCategories)
            {
                ItemCategory[] categories = AcceptedCategories;
                bool accepted = false;
                for (int i = 0; i < categories.Length; i++)
                {
                    if (categories[i] == item.Category)
                    {
                        accepted = true;
                        break;
                    }
                }

                if (!accepted)
                {
                    reason = $"This shop does not buy {item.Category} items.";
                    return false;
                }
            }

            if (item.BaseSellPrice <= 0 || SellPriceMultiplier <= 0f)
            {
                reason = "This item has no sell value here.";
                return false;
            }

            return true;
        }

        public bool CanBuy(ItemDefinition item, out string reason)
        {
            reason = string.Empty;
            if (!enableBuying)
            {
                reason = "This shop does not sell items.";
                return false;
            }

            if (item == null)
            {
                reason = "Item is missing.";
                return false;
            }

            if (item.QuestItem)
            {
                reason = "Quest items cannot be bought here.";
                return false;
            }

            if (item.BaseSellPrice <= 0 || BuyPriceMultiplier <= 0f)
            {
                reason = "This item has no buy value here.";
                return false;
            }

            if (!acceptAllCategories)
            {
                ItemCategory[] categories = AcceptedCategories;
                bool accepted = false;
                for (int i = 0; i < categories.Length; i++)
                {
                    if (categories[i] == item.Category)
                    {
                        accepted = true;
                        break;
                    }
                }

                if (!accepted)
                {
                    reason = $"This shop does not sell {item.Category} items.";
                    return false;
                }
            }

            return true;
        }

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(shopId))
            {
                shopId = name;
            }

            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = name;
            }
        }
    }
}
