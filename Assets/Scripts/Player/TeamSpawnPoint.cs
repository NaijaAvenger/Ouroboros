using UnityEngine;

namespace Ouroboros.Player
{
    /// <summary>
    /// Scene marker for a team spawn location. Place several per team; the session manager picks one
    /// at random for spawns and respawns. A point with <c>TeamID.None</c> is a fallback for any team.
    /// </summary>
    public class TeamSpawnPoint : MonoBehaviour
    {
        [SerializeField] private Core.TeamID team = Core.TeamID.None;
        [SerializeField] private float radius = 1.5f;

        public Core.TeamID Team => team;

        /// <summary>(v0.23) Every spawn point in the loaded scene.</summary>
        public static readonly System.Collections.Generic.List<TeamSpawnPoint> All = new System.Collections.Generic.List<TeamSpawnPoint>();

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        /// <summary>
        /// (v0.23) True if <paramref name="position"/> is at one of <paramref name="crew"/>'s own spawn points:
        /// within <paramref name="zoneRadius"/> metres on the flat, and on the same floor. This is the crew's
        /// gate, where class and gear can be changed during a match.
        /// </summary>
        public static bool InZone(Core.TeamID crew, Vector3 position, float zoneRadius)
        {
            if (!Core.TeamUtil.IsValid(crew)) return false;
            for (int i = 0; i < All.Count; i++)
            {
                var point = All[i];
                if (point == null || point.team != crew) continue;
                Vector3 d = position - point.transform.position;
                if (Mathf.Abs(d.y) < 4f && d.x * d.x + d.z * d.z <= zoneRadius * zoneRadius) return true;
            }
            return false;
        }

        /// <summary>A random position on the spawn disc so stacked spawns don't overlap.</summary>
        public Vector3 GetSpawnPosition()
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            return transform.position + new Vector3(offset.x, 0f, offset.y);
        }

        public Quaternion GetSpawnRotation() => Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        private void OnDrawGizmos()
        {
            Gizmos.color = TeamColor(team);
            Gizmos.DrawWireSphere(transform.position, radius);
            Gizmos.DrawRay(transform.position, transform.forward * 2f);
        }

        public static Color TeamColor(Core.TeamID team)
        {
            // (v0.12) crew colours come from the crew's style (teal, red, gold, blue); the switch below is the [v0.11] set
            if (Core.TeamUtil.IsValid(team)) return Art.CrewStyle.Get(team).Primary;
            switch (team)
            {
                case Core.TeamID.TeamAlpha:   return Color.red;
                case Core.TeamID.TeamBravo:   return Color.blue;
                case Core.TeamID.TeamCharlie: return Color.green;
                case Core.TeamID.TeamDelta:   return Color.yellow;
                default: return Color.white;
            }
        }
    }
}
