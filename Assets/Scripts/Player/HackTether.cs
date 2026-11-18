using UnityEngine;

namespace Ouroboros.Player
{
    /// <summary>
    /// Draws the line between a player and the device they are quick-hacking (v0.11), on every peer, from the
    /// replicated <c>NetworkPlayer.HackTarget</c>. A hack is never secret: rivals can see who is doing it and
    /// to what, and the hacker can see their own tether stretch. Added at runtime by <see cref="PlayerFeedback"/>.
    /// </summary>
    [RequireComponent(typeof(Network.NetworkPlayer))]
    public class HackTether : MonoBehaviour
    {
        private Network.NetworkPlayer player;
        private LineRenderer line;
        private static Material sharedMaterial;

        private void Awake()
        {
            player = GetComponent<Network.NetworkPlayer>();
        }

        private void LateUpdate()
        {
            Interaction.IQuickHackable device = null;
            if (player != null && player.Object != null && player.IsQuickHacking) device = player.HackTargetDevice;

            if (device == null)
            {
                if (line != null && line.enabled) line.enabled = false;
                return;
            }

            if (line == null) line = CreateLine();
            Color team = TeamSpawnPoint.TeamColor(player.Team);
            // Pulses faster as the hack nears completion.
            float pulse = 0.55f + 0.45f * Mathf.Sin(Time.time * (8f + 14f * player.HackProgress01));
            Color start = team; start.a = 0.9f * pulse;
            Color end = Color.Lerp(team, Color.white, 0.6f); end.a = 0.9f;
            line.startColor = start;
            line.endColor = end;

            Vector3 from = player.Position + Vector3.up * 1.2f;
            if (player.IsLocalPlayer) from = player.EyePosition + player.AimDirection * 0.5f - Vector3.up * 0.25f;
            line.SetPosition(0, from);
            line.SetPosition(1, device.HackPoint);
            line.enabled = true;
        }

        private LineRenderer CreateLine()
        {
            var go = new GameObject("HackTether");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.startWidth = 0.05f;
            lr.endWidth = 0.02f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            if (sharedMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard");
                if (shader != null) sharedMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            if (sharedMaterial != null) lr.sharedMaterial = sharedMaterial;
            return lr;
        }
    }
}
