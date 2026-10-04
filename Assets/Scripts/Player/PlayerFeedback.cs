using UnityEngine;

namespace Ouroboros.Player
{
    /// <summary>
    /// Per-player audio/VFX hooks driven entirely by replicated events, so every peer hears and sees
    /// the same thing. Uses the <see cref="Data.FeedbackLibrary"/> assigned on the session manager
    /// (shared) or an override on this component.
    /// </summary>
    [RequireComponent(typeof(Network.NetworkPlayer))]
    public class PlayerFeedback : MonoBehaviour
    {
        [SerializeField] private Data.FeedbackLibrary libraryOverride;
        [SerializeField] private AudioSource audioSource;
        [Tooltip("Spatialise sounds for other players; the local player's sounds are 2D.")]
        [SerializeField] private float spatialBlendRemote = 1f;

        private Network.NetworkPlayer player;
        private int lastLoot;

        private Data.FeedbackLibrary Library => libraryOverride != null ? libraryOverride : Network.GameSessionManager.FeedbackLibrary;

        private void Awake()
        {
            player = GetComponent<Network.NetworkPlayer>();
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }

            player.Died += OnDied;
            player.Respawned += OnRespawned;
            player.Damaged += OnDamaged;
            player.StatusChanged += OnStatusChanged;
            Network.NetworkPlayer.AbilityUsed += OnAbilityUsed;
            Network.NetworkPlayer.PlayerExtracted += OnExtracted;
        }

        private void OnDestroy()
        {
            Network.NetworkPlayer.AbilityUsed -= OnAbilityUsed;
            Network.NetworkPlayer.PlayerExtracted -= OnExtracted;
        }

        private void Update()
        {
            // Loot gain has no dedicated event; cheap poll of the replicated value.
            if (player.CarriedLoot > lastLoot) Play(Library?.lootGained);
            lastLoot = player.CarriedLoot;
        }

        private void OnAbilityUsed(Network.NetworkPlayer who, int slot)
        {
            if (who != player) return;
            var lib = Library;
            if (lib == null) return;
            Play(lib.abilityUsed);
            lib.SpawnVfx(lib.abilityVfx, player.EyePosition, Quaternion.LookRotation(player.AimDirection));
        }

        private void OnDamaged(Network.NetworkPlayer who, float amount)
        {
            var lib = Library;
            if (lib == null) return;
            Play(lib.hurt);
            lib.SpawnVfx(lib.hurtVfx, player.transform.position + Vector3.up, Quaternion.identity);
        }

        private void OnDied(Network.NetworkPlayer who)
        {
            var lib = Library;
            if (lib == null) return;
            Play(lib.death);
            lib.SpawnVfx(lib.deathVfx, player.transform.position, Quaternion.identity);
        }

        private void OnRespawned(Network.NetworkPlayer who)
        {
            var lib = Library;
            if (lib == null) return;
            Play(lib.respawn);
            lib.SpawnVfx(lib.respawnVfx, player.transform.position, Quaternion.identity);
        }

        private void OnExtracted(Network.NetworkPlayer who)
        {
            if (who != player) return;
            var lib = Library;
            if (lib == null) return;
            Play(lib.extracted);
            lib.SpawnVfx(lib.extractedVfx, player.transform.position, Quaternion.identity);
        }

        private void OnStatusChanged(Network.NetworkPlayer who, Core.StatusFlags status)
        {
            var lib = Library;
            if (lib == null) return;
            // Rising edges only
            if ((status & Core.StatusFlags.Shielded) != 0 && !wasShielded) Play(lib.shieldOn);
            if ((status & Core.StatusFlags.Stealthed) != 0 && !wasStealthed) Play(lib.stealthOn);
            wasShielded = (status & Core.StatusFlags.Shielded) != 0;
            wasStealthed = (status & Core.StatusFlags.Stealthed) != 0;
        }

        private bool wasShielded, wasStealthed;

        private void Play(AudioClip clip)
        {
            if (clip == null || audioSource == null) return;
            var lib = Library;
            audioSource.spatialBlend = player.IsLocalPlayer ? 0f : spatialBlendRemote;
            audioSource.PlayOneShot(clip, lib != null ? lib.playerVolume : 1f);
        }
    }
}
