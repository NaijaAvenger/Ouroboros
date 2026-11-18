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

        // (v0.12, M4) the capsule is replaced by a code-built specialist in the crew's pattern
        private Art.CharacterModel model;
        private Art.CharacterAnimator animator;
        private Art.WeaponView weaponView;
        // (v0.13) status effects and worn passive gear
        private Art.StatusEffects statusEffects;
        private Equipment.NetworkLoadout loadout;
        private readonly System.Collections.Generic.List<Transform> wornGear = new System.Collections.Generic.List<Transform>();
        private bool gearDirty = true;
        private Core.PlayerClassType builtClass;
        private Core.TeamID builtTeam;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            player = GetComponent<Network.NetworkPlayer>();
            block = new MaterialPropertyBlock();
            if (modelRenderer == null) modelRenderer = GetComponentInChildren<Renderer>();
            weaponView = GetComponent<Art.WeaponView>();
            if (weaponView == null) weaponView = gameObject.AddComponent<Art.WeaponView>(); // (v0.12) no prefab change needed

            statusEffects = GetComponent<Art.StatusEffects>();
            if (statusEffects == null) statusEffects = gameObject.AddComponent<Art.StatusEffects>(); // (v0.13)
            loadout = GetComponent<Equipment.NetworkLoadout>();
            if (loadout != null) loadout.SlotChanged += (_, __) => gearDirty = true;

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

            EnsureModel(); // (v0.12)
            if (gearDirty && model != null) RefreshWornGear(); // (v0.13)

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

            // [v0.11] modelRenderer.enabled = visible;   // the capsule was the character
            // [v0.11] if (!visible) return;
            if (model == null)
            {
                modelRenderer.enabled = visible; // no class yet: fall back to the capsule
                if (!visible) return;
            }
            else
            {
                modelRenderer.enabled = false;
                if (model.Root.gameObject.activeSelf != visible) model.Root.gameObject.SetActive(visible);
                if (!visible) return;
            }

            // [v0.11] Color color = TeamSpawnPoint.TeamColor(player.Team);   // crew colour now comes from the model's own materials
            Color color = model != null ? Color.white : TeamSpawnPoint.TeamColor(player.Team);
            Color emission = Color.black;
            if (player.HasStatus(Core.StatusFlags.Shielded))    { color = Color.Lerp(color, Color.cyan, 0.5f); emission = Color.cyan * 0.3f; }
            if (player.HasStatus(Core.StatusFlags.DamageBoost)) { emission += Color.red * 0.4f; }
            if (player.HasStatus(Core.StatusFlags.Revealed))    { emission += new Color(1f, 0f, 1f) * 0.4f; }
            if (player.HasStatus(Core.StatusFlags.Burning))     { color = Color.Lerp(color, new Color(1f, 0.5f, 0f), 0.6f); }
            if (player.HasStatus(Core.StatusFlags.EMPDisabled)) { color = Color.Lerp(color, Color.gray, 0.6f); }
            if (player.HasStatus(Core.StatusFlags.Stealthed))   { color = Color.Lerp(color, Color.black, 0.5f); }

            if (model == null)
            {
                modelRenderer.GetPropertyBlock(block);
                block.SetColor(ColorId, color);
                block.SetColor(BaseColorId, color);
                block.SetColor(EmissionId, emission);
                modelRenderer.SetPropertyBlock(block);
                return;
            }

            // Status tints multiply each part's own colour, so the crew pattern stays readable through them.
            for (int i = 0; i < model.Renderers.Count; i++)
            {
                var r = model.Renderers[i];
                if (r == null) continue;
                Color tinted = model.BaseColours[i] * color;
                r.GetPropertyBlock(block);
                block.SetColor(ColorId, tinted);
                block.SetColor(BaseColorId, tinted);
                if (emission != Color.black) block.SetColor(EmissionId, emission);
                r.SetPropertyBlock(block);
            }
        }

        /// <summary>
        /// (v0.13) Passive gear is worn where it can be seen: armour as plates, a speed rig as injectors, damage
        /// gear as an ammunition belt. Weapons are handled by <see cref="Art.WeaponView"/>.
        /// </summary>
        private void RefreshWornGear()
        {
            gearDirty = false;
            for (int i = 0; i < wornGear.Count; i++) Art.GearBuilder.RemoveWornGear(model, wornGear[i]);
            wornGear.Clear();
            if (loadout == null || loadout.Object == null) { gearDirty = true; return; }

            Color crew = Core.TeamUtil.IsValid(player.Team) ? Art.CrewStyle.Get(player.Team).Primary : Color.white;
            for (int slot = 0; slot < Equipment.NetworkLoadout.SLOTS; slot++)
            {
                if (Equipment.NetworkLoadout.IsWeaponSlot(slot)) continue;
                var data = loadout.GetData(slot);
                if (data == null || data.kind != Data.EquipmentKind.PassiveGear) continue;
                var piece = Art.GearBuilder.BuildWornGear(model, data, crew);
                if (piece != null) wornGear.Add(piece);
            }
        }

        /// <summary>(v0.12) Builds (or rebuilds) the character when the class or crew is known or changes.</summary>
        private void EnsureModel()
        {
            var cls = player.ClassType;
            var team = player.Team;
            if (cls == Core.PlayerClassType.None || !Core.TeamUtil.IsValid(team)) return;
            if (model != null && cls == builtClass && team == builtTeam) return;

            if (model != null && model.Root != null) Destroy(model.Root.gameObject);
            model = Art.CharacterBuilder.BuildSpecialist(transform, cls, team);
            builtClass = cls;
            builtTeam = team;

            if (animator == null) animator = gameObject.AddComponent<Art.CharacterAnimator>();
            animator.Model = model;
            if (statusEffects != null) statusEffects.SetModel(model); // (v0.13)
            wornGear.Clear();                                           // the old pieces went with the old model
            gearDirty = true;
            // [v0.12] if (weaponView != null) weaponView.SetHand(model.HandR);
            if (weaponView != null) weaponView.SetJoints(model.HandR, model.Torso); // (v0.13) hand, and the back for the stowed weapon
        }

        // [v0.11]
        // [v0.11]     Color color = TeamSpawnPoint.TeamColor(player.Team);
        // [v0.11]     Color emission = Color.black;
        // [v0.11]
        // [v0.11]     if (player.HasStatus(Core.StatusFlags.Shielded))    { color = Color.Lerp(color, Color.cyan, 0.5f); emission = Color.cyan * 0.3f; }
        // [v0.11]     if (player.HasStatus(Core.StatusFlags.DamageBoost)) { emission += Color.red * 0.4f; }
        // [v0.11]     if (player.HasStatus(Core.StatusFlags.Revealed))    { emission += new Color(1f, 0f, 1f) * 0.4f; }
        // [v0.11]     if (player.HasStatus(Core.StatusFlags.Burning))     { color = Color.Lerp(color, new Color(1f, 0.5f, 0f), 0.6f); }
        // [v0.11]     if (player.HasStatus(Core.StatusFlags.EMPDisabled)) { color = Color.Lerp(color, Color.gray, 0.6f); }
        // [v0.11]     if (player.HasStatus(Core.StatusFlags.Stealthed))   { color = Color.Lerp(color, Color.black, 0.5f); }
        // [v0.11]
        // [v0.11]     modelRenderer.GetPropertyBlock(block);
        // [v0.11]     block.SetColor(ColorId, color);
        // [v0.11]     block.SetColor(BaseColorId, color);
        // [v0.11]     block.SetColor(EmissionId, emission);
        // [v0.11]     modelRenderer.SetPropertyBlock(block);
        // [v0.11] }
        // [v0.11]

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
