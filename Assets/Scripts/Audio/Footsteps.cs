using UnityEngine;

namespace Ouroboros.Audio
{
    /// <summary>
    /// Footsteps for anything that walks (v0.20). A step sounds every stride of ground covered, so the rhythm
    /// follows the speed without any animation to drive it; there is a thud on landing and a scrape when a
    /// slide starts.
    ///
    /// What it is for: hearing who is near. Other players and guards are positional and carry about 26 m;
    /// guards wear boots, lower and heavier, so a patrol is told from a rival by ear. A sprint is louder
    /// than a walk. Your own steps are quiet and not positional. A stealthed rival makes no sound at all.
    ///
    /// It reads only how far the object's transform moved this frame, so it works the same for the local
    /// player, for other players as the network shows them, and for guards.
    /// </summary>
    public class Footsteps : MonoBehaviour
    {
        public enum Kind { Player, Guard }

        private const float WalkStride = 1.9f, RunStride = 2.5f, RunSpeed = 6.5f, MoveSpeed = 1.5f;

        private Kind kind;
        private AudioSource source;
        private Network.NetworkPlayer player;
        private Player.PlayerController controller;
        private Vector3 lastPosition;
        private float travelled, fallSpeed;
        private bool wasGrounded = true, wasSliding, started;
        private int take;

        /// <summary>Adds footsteps to <paramref name="owner"/>, or returns the ones it already has.</summary>
        public static Footsteps Attach(GameObject owner, Kind kind)
        {
            if (owner == null) return null;
            var steps = owner.GetComponent<Footsteps>();
            if (steps == null) steps = owner.AddComponent<Footsteps>();
            steps.kind = kind;
            return steps;
        }

        private void Awake()
        {
            player = GetComponent<Network.NetworkPlayer>();
            controller = GetComponent<Player.PlayerController>();
            // Its own source: the player's other sounds must not change how far a step carries.
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            // [v0.22] source.maxDistance = 26f;
            source.maxDistance = 20f; // (v0.23) the next room, not the one after: twenty guards' boots were a constant patter
            source.dopplerLevel = 0f;
        }

        private void Update()
        {
            Vector3 position = transform.position;
            if (!started) { started = true; lastPosition = position; return; }
            Vector3 delta = position - lastPosition;
            lastPosition = position;
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            if (delta.sqrMagnitude > 16f) { travelled = 0f; fallSpeed = 0f; return; } // a respawn or a teleport, not a stride

            if (player != null && (player.Object == null || !player.IsActiveInMatch)) { travelled = 0f; wasSliding = false; return; }

            var listener = Network.NetworkPlayer.FindLocal();
            bool mine = player != null && player.IsLocalPlayer;
            bool crew = player != null && listener != null && listener.Team == player.Team;
            if (player != null && !crew && player.HasStatus(Core.StatusFlags.Stealthed) && !player.HasStatus(Core.StatusFlags.Revealed)) { travelled = 0f; return; }
            source.spatialBlend = mine ? 0f : 1f;
            source.maxDistance = kind == Kind.Guard ? 16f : 20f; // (v0.23)

            bool grounded = controller == null || controller.IsGrounded;
            if (!grounded) fallSpeed = Mathf.Max(fallSpeed, -delta.y / dt);
            else
            {
                if (!wasGrounded && fallSpeed > 4.5f) Play(Core.ProceduralSfx.Land, Mathf.Clamp01(fallSpeed / 14f) * (mine ? 0.45f : 0.9f), 1f);
                fallSpeed = 0f;
            }
            wasGrounded = grounded;

            bool sliding = controller != null && controller.IsSliding;
            if (sliding && !wasSliding) Play(Core.ProceduralSfx.Slide, mine ? 0.35f : 0.7f, Random.Range(0.92f, 1.08f));
            wasSliding = sliding;

            float speed = new Vector2(delta.x, delta.z).magnitude / dt;
            if (!grounded || sliding || speed < MoveSpeed)
            {
                travelled = Mathf.Min(travelled, WalkStride * 0.6f); // the first step after a stop comes soon, not at once
                return;
            }

            bool running = speed >= RunSpeed;
            travelled += speed * dt;
            float stride = running ? RunStride : WalkStride;
            if (travelled < stride) return;
            travelled -= stride;

            take++;
            float volume = kind == Kind.Guard ? (running ? 0.9f : 0.7f)
                         : mine ? (running ? 0.3f : 0.2f)
                         : (running ? 0.95f : 0.6f);
            var clip = kind == Kind.Guard ? Core.ProceduralSfx.BootStep(take) : Core.ProceduralSfx.Footstep(take);
            Play(clip, volume, Random.Range(0.92f, 1.08f));
        }

        private void Play(AudioClip clip, float volume, float pitch)
        {
            if (clip == null || source == null) return;
            source.pitch = pitch;
            source.PlayOneShot(clip, volume);
        }
    }
}
