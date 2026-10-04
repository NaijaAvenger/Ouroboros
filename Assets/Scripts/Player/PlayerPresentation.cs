using UnityEngine;

namespace Ouroboros.Player
{
    /// <summary>
    /// Pure presentation for a player: team colour, status tints, stealth visibility relative to the
    /// local viewer, hiding the local player's own model (first person), and death / extraction hiding.
    /// Reads only replicated state, so it behaves identically on every peer. Swap the colour logic for
    /// animations / VFX without touching gameplay code.
    /// </summary>
    [RequireComponent(typeof(Network.NetworkPlayer))]
    public class PlayerPresentation : MonoBehaviour
    {
        [SerializeField] private Renderer modelRenderer;
        [SerializeField] private bool hideOwnModel = true;
        [SerializeField] private float refreshInterval = 0.1f;

        private Network.NetworkPlayer player;
        private MaterialPropertyBlock block;
        private float nextRefresh;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            player = GetComponent<Network.NetworkPlayer>();
            block = new MaterialPropertyBlock();
            if (modelRenderer == null) modelRenderer = GetComponentInChildren<Renderer>();

            player.StatusChanged += (_, __) => Apply();
            player.Died += _ => Apply();
            player.Respawned += _ => Apply();
            player.ClassChanged += (_, __) => Apply();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + refreshInterval;
            Apply();
        }

        private void Apply()
        {
            if (modelRenderer == null || player == null || player.Object == null) return;

            bool visible = player.IsAlive && !player.IsExtracted;

            if (visible && hideOwnModel && player.IsLocalPlayer)
            {
                visible = false;
            }

            // Stealth hides the model from enemies (and from the AI's perception, see Perception.cs),
            // unless the player has been revealed.
            if (visible && player.HasStatus(Core.StatusFlags.Stealthed) && !player.HasStatus(Core.StatusFlags.Revealed))
            {
                var viewer = LocalViewer();
                if (viewer != null && viewer != player && viewer.Team != player.Team)
                {
                    visible = false;
                }
            }

            modelRenderer.enabled = visible;
            if (!visible) return;

            Color color = TeamSpawnPoint.TeamColor(player.Team);
            Color emission = Color.black;

            if (player.HasStatus(Core.StatusFlags.Shielded))    { color = Color.Lerp(color, Color.cyan, 0.5f); emission = Color.cyan * 0.3f; }
            if (player.HasStatus(Core.StatusFlags.DamageBoost)) { emission += Color.red * 0.4f; }
            if (player.HasStatus(Core.StatusFlags.Revealed))    { emission += new Color(1f, 0f, 1f) * 0.4f; }
            if (player.HasStatus(Core.StatusFlags.Burning))     { color = Color.Lerp(color, new Color(1f, 0.5f, 0f), 0.6f); }
            if (player.HasStatus(Core.StatusFlags.EMPDisabled)) { color = Color.Lerp(color, Color.gray, 0.6f); }
            if (player.HasStatus(Core.StatusFlags.Stealthed))   { color = Color.Lerp(color, Color.black, 0.5f); }

            modelRenderer.GetPropertyBlock(block);
            block.SetColor(ColorId, color);
            block.SetColor(BaseColorId, color);
            block.SetColor(EmissionId, emission);
            modelRenderer.SetPropertyBlock(block);
        }

        private static Network.NetworkPlayer LocalViewer()
        {
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].IsLocalPlayer) return all[i];
            }
            return null;
        }
    }
}
