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
