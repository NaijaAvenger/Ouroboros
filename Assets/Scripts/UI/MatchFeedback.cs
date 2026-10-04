using UnityEngine;

namespace Ouroboros.UI
{
    /// <summary>
    /// Match-wide 2D audio hooks (state changes, extraction opening, objectives, kills). Lives on the
    /// HUD object. All triggers are replicated events, so clients hear them too.
    /// </summary>
    public class MatchFeedback : MonoBehaviour
    {
        [SerializeField] private Data.FeedbackLibrary libraryOverride;
        [SerializeField] private AudioSource audioSource;

        private Data.FeedbackLibrary Library => libraryOverride != null ? libraryOverride : Network.GameSessionManager.FeedbackLibrary;

        private void Awake()
        {
            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }
        }

        private void OnEnable()
        {
            GameMode.ExtractionHeistGameMode.StateChanged += OnStateChanged;
            GameMode.ExtractionHeistGameMode.ExtractionOpened += OnExtractionOpened;
            GameMode.ExtractionHeistGameMode.TeamExtractedEvent += OnTeamExtracted;
            GameMode.LootObjective.Completed += OnObjectiveCompleted;
            Network.NetworkPlayer.PlayerKilled += OnPlayerKilled;
            Equipment.Projectile.Exploded += OnExploded;
        }

        private void OnExploded(Vector3 point, float radius)
        {
            var lib = Library;
            if (lib == null) return;
            if (lib.explosion != null) AudioSource.PlayClipAtPoint(lib.explosion, point, lib.matchVolume);
            lib.SpawnVfx(lib.explosionVfx, point, Quaternion.identity);
        }

        private void OnDisable()
        {
            Equipment.Projectile.Exploded -= OnExploded;
            GameMode.ExtractionHeistGameMode.StateChanged -= OnStateChanged;
            GameMode.ExtractionHeistGameMode.ExtractionOpened -= OnExtractionOpened;
            GameMode.ExtractionHeistGameMode.TeamExtractedEvent -= OnTeamExtracted;
            GameMode.LootObjective.Completed -= OnObjectiveCompleted;
            Network.NetworkPlayer.PlayerKilled -= OnPlayerKilled;
        }

        private void OnStateChanged(GameMode.ExtractionHeistGameMode.GameState state)
        {
            var lib = Library;
            if (lib == null) return;
            switch (state)
            {
                case GameMode.ExtractionHeistGameMode.GameState.InProgress: Play(lib.matchStart); break;
                case GameMode.ExtractionHeistGameMode.GameState.Extraction: Play(lib.extractionPhase); break;
                case GameMode.ExtractionHeistGameMode.GameState.MatchEnded: Play(lib.matchEnd); break;
            }
        }

        private void OnExtractionOpened() => Play(Library?.extractionOpen);
        private void OnTeamExtracted(Core.TeamID team, int members, int loot) => Play(Library?.teamExtracted);
        private void OnObjectiveCompleted(GameMode.LootObjective objective, Core.TeamID team, int loot) => Play(Library?.objectiveComplete);

        private void OnPlayerKilled(Network.NetworkPlayer victim, Fusion.PlayerRef killerRef)
        {
            var killer = Network.NetworkPlayer.Resolve(killerRef);
            if (killer != null && killer.IsLocalPlayer && killer != victim) Play(Library?.killConfirmed);
        }

        private void Play(AudioClip clip)
        {
            if (clip == null || audioSource == null) return;
            var lib = Library;
            audioSource.PlayOneShot(clip, lib != null ? lib.matchVolume : 1f);
        }
    }
}
