using UnityEngine;

namespace Ouroboros.Combat
{
    /// <summary>
    /// Anything that can take damage from players, traps, explosions or AI.
    /// Implemented by <c>NetworkPlayer</c> and <c>BaseAIAgent</c>. Damage must only be applied on the
    /// state authority; implementations are expected to early-out otherwise.
    /// </summary>
    public interface IDamageable
    {
        bool IsAlive { get; }
        Core.TeamID Team { get; }
        Vector3 Position { get; }

        /// <summary>
        /// Applies damage. <paramref name="attacker"/> may be null for environmental damage.
        /// </summary>
        void ApplyDamage(float amount, Network.NetworkPlayer attacker);
    }
}
