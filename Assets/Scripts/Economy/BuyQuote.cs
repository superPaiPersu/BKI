using CityStateSim.Items;

namespace CityStateSim.Economy
{
    public sealed class BuyQuote
    {
        public BuyQuote(
            ItemDefinition item,
            int requestedAmount,
            int availableAmount,
            int buyableAmount,
            int unitPrice,
            int totalPrice,
            bool canBuy,
            string failureReason)
        {
            Item = item;
            RequestedAmount = requestedAmount;
            AvailableAmount = availableAmount;
            BuyableAmount = buyableAmount;
            UnitPrice = unitPrice;
            TotalPrice = totalPrice;
            CanBuy = canBuy;
            FailureReason = failureReason ?? string.Empty;
        }

        public ItemDefinition Item { get; }
        public int RequestedAmount { get; }
        public int AvailableAmount { get; }
        public int BuyableAmount { get; }
        public int UnitPrice { get; }
        public int TotalPrice { get; }
        public bool CanBuy { get; }
        public string FailureReason { get; }
    }
}
