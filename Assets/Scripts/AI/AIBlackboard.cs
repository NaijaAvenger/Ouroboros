using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.AI
{
    /// <summary>
    /// Shared knowledge for the facility's AI, kept on the state authority only (AI simulates there).
    /// Guards, cameras and gunfire post <see cref="Sighting"/>s and <see cref="Noise"/>s with an expiry;
    /// idle guards query the board to pick something to investigate, which gives squads coordinated
    /// behaviour without any direct agent-to-agent messaging.
    /// </summary>
    public static class AIBlackboard
    {
        public struct Sighting
        {
            public Vector3 Position;
            public Network.NetworkPlayer Player; // may be null (camera / anonymous)
            public float Time;
            public float Priority; // higher = more interesting (elites > cameras > noise)
        }

        public struct Noise
        {
            public Vector3 Position;
            public float Radius;
            public float Time;
        }

        private const float SightingLifetime = 12f;
        private const float NoiseLifetime = 6f;
        private const int MaxEntries = 64;

        private static readonly List<Sighting> sightings = new List<Sighting>();
        private static readonly List<Noise> noises = new List<Noise>();

        public static IReadOnlyList<Sighting> Sightings => sightings;
        public static IReadOnlyList<Noise> Noises => noises;

        public static void ReportSighting(Vector3 position, Network.NetworkPlayer player, float priority = 1f)
        {
            Prune();
            // Merge with an existing sighting of the same player
            if (player != null)
            {
                for (int i = 0; i < sightings.Count; i++)
                {
                    if (sightings[i].Player == player)
                    {
                        sightings[i] = new Sighting { Position = position, Player = player, Time = Time.time, Priority = Mathf.Max(priority, sightings[i].Priority) };
                        return;
                    }
                }
            }
            if (sightings.Count >= MaxEntries) sightings.RemoveAt(0);
            sightings.Add(new Sighting { Position = position, Player = player, Time = Time.time, Priority = priority });
        }

        public static void ReportNoise(Vector3 position, float radius)
        {
            Prune();
            if (noises.Count >= MaxEntries) noises.RemoveAt(0);
            noises.Add(new Noise { Position = position, Radius = radius, Time = Time.time });
        }

        /// <summary>
        /// Best thing to investigate from <paramref name="from"/>: the freshest / highest-priority sighting
        /// within <paramref name="maxDistance"/>, else a noise whose radius reaches the listener.
        /// </summary>
        public static bool TryGetPointOfInterest(Vector3 from, float maxDistance, out Vector3 point, out float score)
        {
            Prune();
            point = default;
            score = 0f;
            bool found = false;
            float now = Time.time;

            for (int i = 0; i < sightings.Count; i++)
            {
                var s = sightings[i];
                if (s.Player != null && (s.Player.Object == null || !s.Player.IsActiveInMatch)) continue;
                float d = Vector3.Distance(from, s.Position);
                if (d > maxDistance) continue;
                float freshness = 1f - Mathf.Clamp01((now - s.Time) / SightingLifetime);
                float sc = s.Priority * (0.5f + freshness) * (1f - 0.5f * d / Mathf.Max(1f, maxDistance));
                if (sc > score) { score = sc; point = s.Position; found = true; }
            }

            for (int i = 0; i < noises.Count; i++)
            {
                var n = noises[i];
                float d = Vector3.Distance(from, n.Position);
                if (d > n.Radius || d > maxDistance) continue;
                float freshness = 1f - Mathf.Clamp01((now - n.Time) / NoiseLifetime);
                float sc = 0.6f * freshness;
                if (sc > score) { score = sc; point = n.Position; found = true; }
            }

            return found;
        }

        /// <summary>Most recently seen active player within range, for guards that want a live target hand-off.</summary>
        public static Network.NetworkPlayer RecentPlayerNear(Vector3 from, float maxDistance, float maxAge = 4f)
        {
            Prune();
            Network.NetworkPlayer best = null;
            float bestTime = -1f;
            for (int i = 0; i < sightings.Count; i++)
            {
                var s = sightings[i];
                if (s.Player == null || s.Player.Object == null || !s.Player.IsActiveInMatch) continue;
                if (Time.time - s.Time > maxAge) continue;
                if (Vector3.Distance(from, s.Position) > maxDistance) continue;
                if (s.Time > bestTime) { bestTime = s.Time; best = s.Player; }
            }
            return best;
        }

        public static void Clear()
        {
            sightings.Clear();
            noises.Clear();
        }

        private static void Prune()
        {
            float now = Time.time;
            for (int i = sightings.Count - 1; i >= 0; i--) if (now - sightings[i].Time > SightingLifetime) sightings.RemoveAt(i);
            for (int i = noises.Count - 1; i >= 0; i--) if (now - noises[i].Time > NoiseLifetime) noises.RemoveAt(i);
        }
    }
}
