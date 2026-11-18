using System;
using UnityEngine;
using Fusion;

namespace Ouroboros.GameMode
{
    public enum MapEventKind
    {
        /// <summary>Dormant containers appear: an airdrop through the skylight, an armoured delivery.</summary>
        Delivery = 0,
        /// <summary>A vault's time lock releases and its door opens for everyone.</summary>
        TimeLock = 1,
        /// <summary>A district loses power on its own: lights, cameras, turrets, lasers and maglocks.</summary>
        Blackout = 2
    }

    [Serializable]
    public class MapEvent
    {
        public string title = "Event";
        [Tooltip("Where it happens, in words: shown after the title.")]
        public string place = "";
        [Tooltip("Seconds after the match goes live.")]
        public float atSeconds = 120f;
        public MapEventKind kind = MapEventKind.Delivery;
        [Tooltip("Marker position on the HUD. Defaults to this object's position.")]
        public Transform point;
        public Interaction.Breakable[] containers;
        public Interaction.VaultDoor door;
        public Interaction.PowerNode power;
    }

    /// <summary>
    /// Timed events that pull crews to a place (v0.14), the way a supply drop or a boss spawn does in other
    /// extraction games. The schedule is fixed per map and public: everyone is warned shortly before an event
    /// and told where it is, so each one is a fight to plan for, not a surprise.
    ///
    /// State authority fires events; every peer hears about them through <see cref="Warned"/> and
    /// <see cref="Fired"/>, which the HUD turns into a banner, a feed line and a marker.
    /// </summary>
    public class MapEventDirector : NetworkBehaviour
    {
        public static MapEventDirector Instance { get; private set; }

        [SerializeField] private MapEvent[] events = new MapEvent[0];
        [Tooltip("Seconds of warning before each event.")]
        [SerializeField] private float warningSeconds = 25f;
        [Tooltip("How long an event's marker stays on the HUD after it fires.")]
        [SerializeField] private float markerSeconds = 75f;

        [Networked] private int StartTick { get; set; }
        [Networked] private int WarnedMask { get; set; }
        [Networked] private int FiredMask { get; set; }

        /// <summary>Raised on every peer shortly before an event (event, seconds until it happens).</summary>
        public static event Action<MapEvent, float> Warned;
        /// <summary>Raised on every peer when an event happens.</summary>
        public static event Action<MapEvent> Fired;

        private int shownWarned, shownFired;
        private readonly float[] firedAt = new float[32];

        public int EventCount => events != null ? Mathf.Min(events.Length, 32) : 0;
        public MapEvent GetEvent(int index) => index >= 0 && index < EventCount ? events[index] : null;
        public float MarkerSeconds => markerSeconds;

        /// <summary>True while event <paramref name="index"/> should be marked on the HUD.</summary>
        public bool IsMarked(int index)
        {
            return index >= 0 && index < EventCount && (FiredMask & (1 << index)) != 0 && Time.time - firedAt[index] < markerSeconds;
        }

        public static Vector3 PointOf(MapEvent e, Transform fallback)
        {
            if (e != null && e.point != null) return e.point.position;
            if (e != null && e.containers != null && e.containers.Length > 0 && e.containers[0] != null) return e.containers[0].transform.position;
            if (e != null && e.door != null) return e.door.transform.position;
            if (e != null && e.power != null) return e.power.transform.position;
            return fallback != null ? fallback.position : Vector3.zero;
        }

        public override void Spawned()
        {
            Instance = this;
            // Late joiners must not replay events that already happened.
            shownWarned = WarnedMask;
            shownFired = FiredMask;
            for (int i = 0; i < firedAt.Length; i++) firedAt[i] = -9999f;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            var gm = ExtractionHeistGameMode.Instance;
            if (gm == null || gm.Object == null || !gm.IsMatchLive) return;
            if (StartTick == 0) StartTick = (int)Runner.Tick;

            float elapsed = ((int)Runner.Tick - StartTick) * Runner.DeltaTime;
            for (int i = 0; i < EventCount; i++)
            {
                var e = events[i];
                if (e == null) continue;
                int bit = 1 << i;
                if ((WarnedMask & bit) == 0 && elapsed >= e.atSeconds - warningSeconds) WarnedMask |= bit;
                if ((FiredMask & bit) == 0 && elapsed >= e.atSeconds)
                {
                    FiredMask |= bit;
                    Fire(e);
                }
            }
        }

        private void Fire(MapEvent e)
        {
            switch (e.kind)
            {
                case MapEventKind.Delivery:
                    if (e.containers != null) foreach (var c in e.containers) if (c != null) c.Arm();
                    break;
                case MapEventKind.TimeLock:
                    if (e.door != null && e.door.Object != null) e.door.IsOpen = true;
                    break;
                case MapEventKind.Blackout:
                    if (e.power != null && e.power.Object != null) e.power.CompleteHack(null);
                    break;
            }
            AI.AIBlackboard.ReportNoise(PointOf(e, transform), 30f); // security goes to look as well
        }

        public override void Render()
        {
            if (WarnedMask != shownWarned)
            {
                int fresh = WarnedMask & ~shownWarned;
                shownWarned = WarnedMask;
                for (int i = 0; i < EventCount; i++)
                {
                    if ((fresh & (1 << i)) != 0 && events[i] != null && (FiredMask & (1 << i)) == 0) Warned?.Invoke(events[i], warningSeconds);
                }
            }
            if (FiredMask != shownFired)
            {
                int fresh = FiredMask & ~shownFired;
                shownFired = FiredMask;
                for (int i = 0; i < EventCount; i++)
                {
                    if ((fresh & (1 << i)) == 0 || events[i] == null) continue;
                    firedAt[i] = Time.time;
                    Fired?.Invoke(events[i]);
                }
            }
        }
    }
}
