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
            // (v0.12) the layered score needs no scene change: it rides along with the match feedback object
            if (GetComponent<Audio.MusicDirector>() == null) gameObject.AddComponent<Audio.MusicDirector>();
            // (v0.13) abilities, charges, traps and the drone all show something
            if (GetComponent<Art.AbilityEffects>() == null) gameObject.AddComponent<Art.AbilityEffects>();
            // (v0.15) shake, rumble and the stylised look
            if (GetComponent<JuiceEvents>() == null) gameObject.AddComponent<JuiceEvents>();
            if (GetComponent<Art.StyleDirector>() == null) gameObject.AddComponent<Art.StyleDirector>();
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
            AI.BaseAIAgent.AgentFired += OnAgentFired; // (v0.9)
            Interaction.SentryTurret.Fired += OnTurretFired; // (v0.11)
            GameMode.CashOutStation.Moment += OnCashOut;     // (v0.20)
            Network.NetworkPlayer.Pinged += OnPinged;
            Network.NetworkPlayer.Revived += OnRevived;
        }

        // ------------------------------------------------------------------
        // (v0.20) Money and crew cues. Each is heard at the place it happens, so it can be walked toward, and
        // again flat in the ear of the crews it concerns, so it is never missed.

        private void OnCashOut(GameMode.CashOutStation station, GameMode.CashOutMoment moment, Core.TeamID team, Core.TeamID from, int value)
        {
            if (station == null) return;
            var me = Network.NetworkPlayer.FindLocal();
            bool mine = me != null && me.Team == team, wasMine = me != null && me.Team == from && Core.TeamUtil.IsValid(from);
            Vector3 at = station.transform.position + Vector3.up * 1.5f;
            switch (moment)
            {
                case GameMode.CashOutMoment.Started:
                    At(Core.ProceduralSfx.CashStart, at, 1f);
                    if (mine) Play(Core.ProceduralSfx.CashStart, 0.5f);
                    break;
                case GameMode.CashOutMoment.Added:
                    At(Core.ProceduralSfx.CoinDrop, at, 0.9f);
                    break;
                case GameMode.CashOutMoment.Hijacked:
                    At(Core.ProceduralSfx.CashAlarm, at, 1f);
                    if (mine || wasMine) Play(Core.ProceduralSfx.CashAlarm, 0.6f);
                    break;
                case GameMode.CashOutMoment.Banked:
                    At(Core.ProceduralSfx.CashBanked, at, 1f);
                    if (mine) Play(Core.ProceduralSfx.CashBanked, 0.85f);
                    break;
            }
        }

        private void OnPinged(Network.NetworkPlayer by, Core.PingKind kind, Vector3 position, string label)
        {
            var me = Network.NetworkPlayer.FindLocal();
            if (me == null || by == null || by.Team != me.Team) return; // a crew's pings are its own
            Play(kind == Core.PingKind.Enemy ? Core.ProceduralSfx.PingEnemy : Core.ProceduralSfx.PingBlip, 0.7f);
        }

        private void OnRevived(Network.NetworkPlayer revived, Network.NetworkPlayer by)
        {
            if (revived == null) return;
            var me = Network.NetworkPlayer.FindLocal();
            if (me != null && (revived == me || by == me)) Play(Core.ProceduralSfx.ReviveChime, 0.8f);
            else At(Core.ProceduralSfx.ReviveChime, revived.transform.position, 0.9f);
        }

        private void At(AudioClip clip, Vector3 position, float volume)
        {
            var lib = Library;
            if (clip != null) AudioSource.PlayClipAtPoint(clip, position, volume * (lib != null ? lib.matchVolume : 1f));
        }

        private void Play(AudioClip clip, float volume)
        {
            if (clip == null || audioSource == null) return;
            var lib = Library;
            audioSource.PlayOneShot(clip, volume * (lib != null ? lib.matchVolume : 1f));
        }

        /// <summary>(v0.11) Sentry turret shots: tracer in the owner's colour (red for the facility) and a positional shot sound.</summary>
        private void OnTurretFired(Interaction.SentryTurret turret, Vector3 from, Vector3 to, bool hit)
        {
            Color c = turret != null && turret.Object != null && turret.IsHijacked ? Player.TeamSpawnPoint.TeamColor(turret.OwnerTeam) : GuardTracerColor;
            c.a = 0.9f;
            Combat.ShotTracer.Spawn(from, to, c, 0.03f, 0.08f);
            var lib = Library;
            AudioSource.PlayClipAtPoint(Core.ProceduralSfx.GuardShot, from, (lib != null ? lib.matchVolume : 1f) * 0.7f);
        }

        private static readonly Color GuardTracerColor = new Color(1f, 0.35f, 0.25f, 0.9f);

        /// <summary>(v0.9) Guard shots, hits and misses alike: a red tracer and a positional shot sound.</summary>
        private void OnAgentFired(AI.BaseAIAgent agent, Vector3 from, Vector3 to, bool hit)
        {
            Combat.ShotTracer.Spawn(from, to, GuardTracerColor, 0.035f, 0.1f);
            var lib = Library;
            AudioSource.PlayClipAtPoint(Core.ProceduralSfx.GuardShot, from, lib != null ? lib.matchVolume : 1f);
        }

        private void OnExploded(Vector3 point, float radius)
        {
            var lib = Library;
            // [v0.8] if (lib == null) return;
            // [v0.8] if (lib.explosion != null) AudioSource.PlayClipAtPoint(lib.explosion, point, lib.matchVolume);
            // (v0.9) placeholder fallback so explosions are never silent
            AudioSource.PlayClipAtPoint(lib != null && lib.explosion != null ? lib.explosion : Core.ProceduralSfx.Explosion, point, lib != null ? lib.matchVolume : 1f);
            // [v0.12] if (lib == null) return;
            // [v0.12] lib.SpawnVfx(lib.explosionVfx, point, Quaternion.identity);
            // (v0.13) an explosion is never invisible: the assigned effect if there is one, a code-built blast otherwise
            if (lib != null && lib.explosionVfx != null)
            {
                lib.SpawnVfx(lib.explosionVfx, point, Quaternion.identity);
                Core.Juice.ShakeAt(point, 0.11f, Mathf.Max(1.5f, radius) * 4f + 12f, 0.4f); // (v0.15)
            }
            else Art.Fx.Explosion(point, radius > 0f ? radius : 1.5f);
        }

        private void OnDisable()
        {
            AI.BaseAIAgent.AgentFired -= OnAgentFired;
            Interaction.SentryTurret.Fired -= OnTurretFired;
            GameMode.CashOutStation.Moment -= OnCashOut;
            Network.NetworkPlayer.Pinged -= OnPinged;
            Network.NetworkPlayer.Revived -= OnRevived;
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
