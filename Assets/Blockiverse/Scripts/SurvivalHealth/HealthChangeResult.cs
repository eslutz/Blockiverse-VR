namespace Blockiverse.Survival
{
    public readonly struct HealthChangeResult
    {
        public HealthChangeResult(
            HealthChangeKind kind,
            int requestedAmount,
            int appliedAmount,
            int previousHealth,
            int currentHealth,
            int maxHealth,
            bool wasDead)
        {
            Kind = kind;
            RequestedAmount = requestedAmount;
            AppliedAmount = appliedAmount;
            PreviousHealth = previousHealth;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            WasDead = wasDead;
        }

        public HealthChangeKind Kind { get; }
        public int RequestedAmount { get; }
        public int AppliedAmount { get; }
        public int PreviousHealth { get; }
        public int CurrentHealth { get; }
        public int MaxHealth { get; }
        public bool WasDead { get; }
        public bool IsDead => CurrentHealth <= 0;
        public bool Changed => PreviousHealth != CurrentHealth;
        public bool DidDie => !WasDead && IsDead;
    }
}
