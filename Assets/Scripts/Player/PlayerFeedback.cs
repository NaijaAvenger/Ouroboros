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

            if (GetComponent<HackTether>() == null) gameObject.AddComponent<HackTether>(); // (v0.11) no prefab change needed
            Audio.Footsteps.Attach(gameObject, Audio.Footsteps.Kind.Player); // (v0.20)
            player.Died += OnDied;
            player.Respawned += OnRespawned;
            player.Damaged += OnDamaged;
            player.StatusChanged += OnStatusChanged;
            Network.NetworkPlayer.AbilityUsed += OnAbilityUsed;
            Network.NetworkPlayer.PlayerExtracted += OnExtracted;
            Equipment.NetworkLoadout.WeaponFired += OnWeaponFired;
            Equipment.NetworkLoadout.ReloadStarted += OnReloadStarted;
        }

        private void OnDestroy()
        {
            Network.NetworkPlayer.AbilityUsed -= OnAbilityUsed;
            Network.NetworkPlayer.PlayerExtracted -= OnExtracted;
            Equipment.NetworkLoadout.WeaponFired -= OnWeaponFired;
            Equipment.NetworkLoadout.ReloadStarted -= OnReloadStarted;
        }

        private static readonly Color PlayerTracerColor = new Color(1f, 0.92f, 0.55f, 0.9f);

        private void OnWeaponFired(Equipment.NetworkLoadout loadout, int slot, Vector3 point, bool hit)
        {
            if (loadout == null || loadout.Owner != player) return;
            var lib = Library;
            // [v0.8] if (lib == null) return;
            // [v0.8] Play(lib.weaponFire);
            // (v0.9) placeholder sound and a tracer when no assets are assigned, so shots are never silent or invisible
            Play(lib != null && lib.weaponFire != null ? lib.weaponFire : Core.ProceduralSfx.Gunshot, Audio.Sfx.Shot);

            Vector3 aim = player.AimDirection;
            Vector3 muzzle = player.EyePosition + aim * 0.6f;
            // (v0.13) start at the real muzzle when the weapon model is on screen
            var weaponView = GetComponent<Art.WeaponView>();
            bool hasMuzzle = false;
            if (weaponView != null && weaponView.TryGetMuzzle(out Vector3 modelMuzzle)) { muzzle = modelMuzzle; hasMuzzle = true; }
            var data = loadout.GetData(slot);
            if (data == null || data.kind == Data.EquipmentKind.HitscanWeapon)
            {
                // First person: start the line low and to the right of the camera so the shooter can see it.
                Vector3 from = muzzle;
                // [v0.12] if (player.IsLocalPlayer)
                if (player.IsLocalPlayer && !hasMuzzle)
                {
                    Vector3 right = Vector3.Cross(Vector3.up, aim).normalized;
                    from += right * 0.18f - Vector3.up * 0.14f + aim * 0.3f;
                }
                Combat.ShotTracer.Spawn(from, point, PlayerTracerColor);
            }

            // [v0.12] if (lib == null) return;
            // [v0.12] lib.SpawnVfx(lib.muzzleVfx, muzzle, Quaternion.LookRotation(aim));
            // [v0.12] if (hit) lib.SpawnVfx(lib.impactVfx, point, Quaternion.identity);
            // (v0.13) assigned effects when there are any, code-built ones otherwise: no shot without a flash and a hit mark
            if (lib != null && lib.muzzleVfx != null) lib.SpawnVfx(lib.muzzleVfx, muzzle, Quaternion.LookRotation(aim));
            else Art.Fx.MuzzleFlash(muzzle, aim, data != null && data.kind == Data.EquipmentKind.ProjectileWeapon ? 1.6f : 1f);
            if (hit)
            {
                if (lib != null && lib.impactVfx != null) lib.SpawnVfx(lib.impactVfx, point, Quaternion.identity);
                else Art.Fx.Impact(point);
            }
        }

        private void OnReloadStarted(Equipment.NetworkLoadout loadout, int slot)
        {
            if (loadout == null || loadout.Owner != player) return;
            // [v0.8] Play(Library?.reload);
            var lib = Library;
            Play(lib != null && lib.reload != null ? lib.reload : Core.ProceduralSfx.Reload, Audio.Sfx.Whisper);
        }

        private void Update()
        {
            // Loot gain has no dedicated event; cheap poll of the replicated value.
            // [v0.19] if (player.CarriedLoot > lastLoot) Play(Library?.lootGained);   // silent until a clip was assigned
            if (player.CarriedLoot > lastLoot)
            {
                var lib = Library;
                Play(lib != null && lib.lootGained != null ? lib.lootGained : Core.ProceduralSfx.Pickup, Audio.Sfx.Whisper); // (v0.20)
            }
            lastLoot = player.CarriedLoot;
        }

        private void OnAbilityUsed(Network.NetworkPlayer who, int slot)
        {
            if (who != player) return;
            var lib = Library;
            if (lib == null) return;
            Play(lib.abilityUsed, Audio.Sfx.Room);
            lib.SpawnVfx(lib.abilityVfx, player.EyePosition, Quaternion.LookRotation(player.AimDirection));
        }

        private void OnDamaged(Network.NetworkPlayer who, float amount)
        {
            var lib = Library;
            // [v0.8] if (lib == null) return;
            // [v0.8] Play(lib.hurt);
            Play(lib != null && lib.hurt != null ? lib.hurt : Core.ProceduralSfx.Hurt); // (v0.9) placeholder fallback
            if (lib == null) return;
            lib.SpawnVfx(lib.hurtVfx, player.transform.position + Vector3.up, Quaternion.identity);
        }

        private void OnDied(Network.NetworkPlayer who)
        {
            var lib = Library;
            // [v0.8] if (lib == null) return;
            // [v0.8] Play(lib.death);
            Play(lib != null && lib.death != null ? lib.death : Core.ProceduralSfx.Death, Audio.Sfx.Room); // (v0.9) placeholder fallback
            if (lib == null) return;
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

        // [v0.22] private void Play(AudioClip clip)
        // [v0.22] {
        // [v0.22]     if (clip == null || audioSource == null) return;
        // [v0.22]     var lib = Library;
        // [v0.22]     audioSource.spatialBlend = player.IsLocalPlayer ? 0f : spatialBlendRemote;
        // [v0.22]     audioSource.PlayOneShot(clip, lib != null ? lib.playerVolume : 1f);
        // [v0.22] }
        /// <summary>
        /// (v0.23) Your own sounds are flat in your ears. Anyone else's come from where they are standing and
        /// carry only as far as that kind of sound should: a rifle a couple of rooms, a reload a few steps.
        /// One shared source with Unity's default falloff used to let every remote sound be heard map-wide.
        /// </summary>
        private void Play(AudioClip clip, float reach = Audio.Sfx.Close)
        {
            if (clip == null) return;
            var lib = Library;
            float volume = lib != null ? lib.playerVolume : 1f;
            if (player.IsLocalPlayer)
            {
                if (audioSource == null) return;
                audioSource.spatialBlend = 0f;
                audioSource.PlayOneShot(clip, volume);
            }
            else if (spatialBlendRemote > 0f)
            {
                Audio.Sfx.PlayAt(clip, transform.position + Vector3.up * 0.4f, volume, reach);
            }
        }
    }
}
