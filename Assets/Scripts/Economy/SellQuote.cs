using CityStateSim.Items;

namespace CityStateSim.Economy
{
    public sealed class SellQuote
    {
        public SellQuote(
            ItemDefinition item,
            int requestedAmount,
            int availableAmount,
            int sellableAmount,
            int unitPrice,
            int totalPrice,
            bool canSell,
            string failureReason)
        {
            Item = item;
            RequestedAmount = requestedAmount;
            AvailableAmount = availableAmount;
            SellableAmount = sellableAmount;
            UnitPrice = unitPrice;
            TotalPrice = totalPrice;
            CanSell = canSell;
            FailureReason = failureReason ?? string.Empty;
        }

        public ItemDefinition Item { get; }
        public int RequestedAmount { get; }
        public int AvailableAmount { get; }
        public int SellableAmount { get; }
        public int UnitPrice { get; }
        public int TotalPrice { get; }
        public bool CanSell { get; }
        public string FailureReason { get; }
    }
}
