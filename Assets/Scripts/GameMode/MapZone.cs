using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.GameMode
{
    /// <summary>
    /// A named area of the map (v0.14): "Bank Lobby", "Loading Dock", "Security HQ". The HUD shows the zone the
    /// player is standing in, and events and callouts can name it. A zone is a box around this object; where
    /// boxes overlap, the smaller one wins, so a room inside a wing reads as the room.
    /// Not networked: every peer has the same scene.
    /// </summary>
    public class MapZone : MonoBehaviour
    {
        public static readonly List<MapZone> All = new List<MapZone>();

        public string zoneName = "Zone";
        [Tooltip("Size of the box in metres, centred on this object.")]
        public Vector3 size = new Vector3(10f, 5f, 10f);
        [Tooltip("True for places worth fighting over: vaults, anchors, the getaway. Shown in a warmer colour.")]
        public bool pointOfInterest = false;

        public Bounds Bounds => new Bounds(transform.position, size);
        private float Volume => size.x * size.y * size.z;

        private void OnEnable() { if (!All.Contains(this)) All.Add(this); }
        private void OnDisable() { All.Remove(this); }

        /// <summary>The smallest zone containing <paramref name="position"/>, or null.</summary>
        public static MapZone At(Vector3 position)
        {
            MapZone best = null;
            for (int i = 0; i < All.Count; i++)
            {
                var z = All[i];
                if (z == null || !z.Bounds.Contains(position)) continue;
                if (best == null || z.Volume < best.Volume) best = z;
            }
            return best;
        }

        public static string NameAt(Vector3 position)
        {
            var zone = At(position);
            return zone != null ? zone.zoneName : "";
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = pointOfInterest ? new Color(1f, 0.7f, 0.2f, 0.6f) : new Color(0.3f, 0.8f, 1f, 0.5f);
            Gizmos.DrawWireCube(transform.position, size);
        }
    }
}
