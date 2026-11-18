using Fusion;
using UnityEngine;

namespace Ouroboros.Core
{
    /// <summary>
    /// Everything a player class needs from its owning networked player in order to run
    /// abilities deterministically on the state authority. Keeps <see cref="BasePlayerClass"/>
    /// decoupled from <c>NetworkPlayer</c> (classes can be unit-tested with a fake context).
    /// </summary>
    public interface IAbilityContext
    {
        NetworkRunner Runner { get; }
        /// <summary>True on the peer that owns the authoritative state for this player.</summary>
        bool HasStateAuthority { get; }
        PlayerRef PlayerRef { get; }
        TeamID Team { get; }
        Transform Transform { get; }
        /// <summary>World position of the eyes / camera pivot, used for aim raycasts.</summary>
        Vector3 EyePosition { get; }
        /// <summary>Normalised aim direction built from networked yaw/pitch.</summary>
        Vector3 AimDirection { get; }

        float Health { get; }
        float Stamina { get; }
        bool IsAlive { get; }

        bool TrySpendStamina(float amount);
        void Heal(float amount);

        bool IsAbilityReady(int slot);
        void StartAbilityCooldown(int slot, float seconds);
        float AbilityCooldownRemaining(int slot);

        void SetStatus(StatusFlags flag, bool enabled);
        bool HasStatus(StatusFlags flag);

        /// <summary>Convenience: a tick timer bound to this context's runner.</summary>
        TickTimer CreateTimer(float seconds);
        bool TimerExpired(TickTimer timer);
    }
}
