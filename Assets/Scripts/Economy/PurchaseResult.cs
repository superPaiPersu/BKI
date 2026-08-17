using CityStateSim.Items;

namespace CityStateSim.Economy
{
    public sealed class PurchaseResult
    {
        public PurchaseResult(
            bool succeeded,
            ItemDefinition item,
            int amount,
            int totalValue,
            string failureReason)
        {
            Succeeded = succeeded;
            Item = item;
            Amount = amount;
            TotalValue = totalValue;
            FailureReason = failureReason ?? string.Empty;
        }

        public bool Succeeded { get; }
        public ItemDefinition Item { get; }
        public int Amount { get; }
        public int TotalValue { get; }
        public string FailureReason { get; }
    }
}
