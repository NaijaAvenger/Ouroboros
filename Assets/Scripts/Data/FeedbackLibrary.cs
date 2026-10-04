using UnityEngine;

namespace Ouroboros.Data
{
    /// <summary>
    /// Audio clips and VFX prefabs for gameplay events. Every field is optional: unassigned entries are
    /// skipped silently, so the hooks can ship before the assets exist. Consumed by
    /// <c>Player.PlayerFeedback</c> (per player) and <c>UI.MatchFeedback</c> (match-wide, 2D).
    /// </summary>
    [CreateAssetMenu(fileName = "Feedback Library", menuName = "Ouroboros/Feedback Library")]
    public class FeedbackLibrary : ScriptableObject
    {
        [Header("Player - audio")]
        public AudioClip abilityUsed;
        public AudioClip hurt;
        public AudioClip death;
        public AudioClip respawn;
        public AudioClip extracted;
        public AudioClip shieldOn;
        public AudioClip stealthOn;
        public AudioClip lootGained;
        public AudioClip weaponFire;
        public AudioClip reload;
        public AudioClip explosion;

        [Header("Player - VFX prefabs (auto-destroyed)")]
        public GameObject abilityVfx;
        public GameObject hurtVfx;
        public GameObject deathVfx;
        public GameObject respawnVfx;
        public GameObject extractedVfx;
        public GameObject muzzleVfx;
        public GameObject impactVfx;
        public GameObject explosionVfx;

        [Header("Match - audio (2D)")]
        public AudioClip matchStart;
        public AudioClip extractionOpen;
        public AudioClip extractionPhase;
        public AudioClip matchEnd;
        public AudioClip objectiveComplete;
        public AudioClip killConfirmed;
        public AudioClip teamExtracted;

        [Header("Mix")]
        [Range(0f, 1f)] public float playerVolume = 1f;
        [Range(0f, 1f)] public float matchVolume = 1f;
        [Tooltip("Seconds before a spawned VFX prefab without a ParticleSystem is destroyed.")]
        public float vfxFallbackLifetime = 3f;

        /// <summary>Spawns a VFX prefab at a position and schedules its destruction.</summary>
        public void SpawnVfx(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null) return;
            var go = Instantiate(prefab, position, rotation);
            var ps = go.GetComponentInChildren<ParticleSystem>();
            float life = ps != null ? ps.main.duration + ps.main.startLifetime.constantMax : vfxFallbackLifetime;
            Destroy(go, Mathf.Max(0.1f, life));
        }
    }
}
