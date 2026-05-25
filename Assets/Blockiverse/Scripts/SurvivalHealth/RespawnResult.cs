using Blockiverse.Voxel;

namespace Blockiverse.Survival
{
    public readonly struct RespawnResult
    {
        public RespawnResult(BlockPosition spawnPosition, int previousHealth, int currentHealth, int maxHealth, bool wasDead)
        {
            SpawnPosition = spawnPosition;
            PreviousHealth = previousHealth;
            CurrentHealth = currentHealth;
            MaxHealth = maxHealth;
            WasDead = wasDead;
        }

        public BlockPosition SpawnPosition { get; }
        public int PreviousHealth { get; }
        public int CurrentHealth { get; }
        public int MaxHealth { get; }
        public bool WasDead { get; }
        public bool IsDead => CurrentHealth <= 0;
        public bool Changed => PreviousHealth != CurrentHealth;
    }
}
