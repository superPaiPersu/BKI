using System;
using UnityEngine;

namespace CityStateSim.Combat
{
    public sealed class CombatantHealth : MonoBehaviour
    {
        [SerializeField, Min(1)] private int maxHealth = 3;
        [SerializeField] private bool destroyOnDeath = true;

        private int currentHealth;

        public int MaxHealth => Mathf.Max(1, maxHealth);
        public int CurrentHealth => currentHealth;
        public bool IsDead => currentHealth <= 0;

        public event Action<CombatantHealth> Damaged;
        public event Action<CombatantHealth> Died;

        private void Awake()
        {
            currentHealth = MaxHealth;
        }

        public void ResetHealth()
        {
            currentHealth = MaxHealth;
        }

        public bool TryApplyDamage(int amount)
        {
            if (amount <= 0 || IsDead)
            {
                return false;
            }

            currentHealth = Mathf.Max(0, currentHealth - amount);
            Damaged?.Invoke(this);

            if (currentHealth <= 0)
            {
                Died?.Invoke(this);
                if (destroyOnDeath)
                {
                    Destroy(gameObject);
                }
            }

            return true;
        }

        public void Heal(int amount)
        {
            if (amount <= 0 || IsDead)
            {
                return;
            }

            currentHealth = Mathf.Min(MaxHealth, currentHealth + amount);
        }

        private void OnValidate()
        {
            maxHealth = Mathf.Max(1, maxHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0, MaxHealth);
        }
    }
}
