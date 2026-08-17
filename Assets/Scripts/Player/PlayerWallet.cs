using System;
using UnityEngine;

namespace CityStateSim.Player
{
    public sealed class PlayerWallet : MonoBehaviour
    {
        [SerializeField] private int money;

        public int Money => money;

        public event Action<int> MoneyChanged;

        public bool CanAfford(int amount)
        {
            return amount <= 0 || money >= amount;
        }

        public void AddMoney(int amount)
        {
            if (amount == 0)
            {
                return;
            }

            money = Mathf.Max(0, money + amount);
            MoneyChanged?.Invoke(money);
        }

        public bool TrySpendMoney(int amount)
        {
            if (amount <= 0)
            {
                return true;
            }

            if (!CanAfford(amount))
            {
                return false;
            }

            AddMoney(-amount);
            return true;
        }
    }
}
