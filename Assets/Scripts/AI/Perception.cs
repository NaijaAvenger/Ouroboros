using UnityEngine;

namespace Ouroboros.AI
{
    /// <summary>
    /// Shared line-of-sight / field-of-view checks for AI and security devices.
    /// </summary>
    public static class Perception
    {
        /// <summary>
        /// True if <paramref name="target"/> is within <paramref name="range"/>, inside the horizontal cone of
        /// <paramref name="fieldOfView"/> degrees around <paramref name="forward"/>, and not occluded.
        /// </summary>
        public static bool CanSee(Vector3 eye, Vector3 forward, Vector3 target, float range, float fieldOfView, LayerMask occluders)
        {
            Vector3 delta = target - eye;
            float dist = delta.magnitude;
            if (dist > range || dist < 0.001f) return false;

            Vector3 flatDelta = new Vector3(delta.x, 0f, delta.z);
            Vector3 flatForward = new Vector3(forward.x, 0f, forward.z);
            if (flatDelta.sqrMagnitude > 0.0001f && flatForward.sqrMagnitude > 0.0001f)
            {
                float angle = Vector3.Angle(flatForward, flatDelta);
                if (angle > fieldOfView * 0.5f) return false;
            }

            // Anything hit before reaching the target blocks vision. Targets themselves are expected to be
            // excluded from the occluder mask (or the ray stops just short of them).
            float checkDist = Mathf.Max(0f, dist - 0.3f);
            return !Physics.Raycast(eye, delta / dist, checkDist, occluders, QueryTriggerInteraction.Ignore);
        }

        /// <summary>
        /// Finds the closest visible, active enemy player. AI has no team so every player is hostile,
        /// but a <paramref name="ignoreTeam"/> can be supplied for faction-aligned guards.
        /// </summary>
        public static Network.NetworkPlayer FindVisiblePlayer(Vector3 eye, Vector3 forward, float range, float fieldOfView,
            LayerMask occluders, Core.TeamID ignoreTeam = Core.TeamID.None, bool respectStealth = true)
        {
            Network.NetworkPlayer best = null;
            float bestSq = float.MaxValue;

            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null || !p.IsActiveInMatch) continue;
                if (ignoreTeam != Core.TeamID.None && p.Team == ignoreTeam) continue;
                if (respectStealth && p.HasStatus(Core.StatusFlags.Stealthed)) continue;

                float dSq = (p.EyePosition - eye).sqrMagnitude;
                if (dSq >= bestSq) continue;

                // Revealed players are visible regardless of cone/occlusion (within range)
                bool visible = p.HasStatus(Core.StatusFlags.Revealed)
                    ? dSq <= range * range
                    : CanSee(eye, forward, p.EyePosition, range, fieldOfView, occluders);

                if (visible)
                {
                    best = p;
                    bestSq = dSq;
                }
            }
            return best;
        }
    }
}
