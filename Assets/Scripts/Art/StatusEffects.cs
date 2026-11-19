using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// Shows a player's status on their character (v0.13): a shield bubble, a damage aura, flames, sparks when
    /// their gear is jammed, a marker when they are revealed, stars when they are blinded, a visor glow for
    /// Net Sight and the case on their back when they carry one. Every status a player can have shows something.
    ///
    /// The effects hang off the character model, so they are hidden whenever the model is (dead, extracted,
    /// cloaked from this viewer, or the local player's own body). Added by <c>Player.PlayerPresentation</c>.
    /// </summary>
    [RequireComponent(typeof(Network.NetworkPlayer))]
    public class StatusEffects : MonoBehaviour
    {
        private Network.NetworkPlayer player;
        private CharacterModel model;
        private Transform root;
        private GameObject shield, boost, flames, sparks, revealed, flashed, netSight, carried;
        private float spin;

        private void Awake()
        {
            player = GetComponent<Network.NetworkPlayer>();
        }

        /// <summary>Called by the presentation whenever the character is (re)built. Old effects went with the old model.</summary>
        public void SetModel(CharacterModel characterModel)
        {
            model = characterModel;
            root = null;
            shield = boost = flames = sparks = revealed = flashed = netSight = carried = null;
        }

        private void LateUpdate()
        {
            if (player == null || player.Object == null || model == null || model.Root == null) return;
            if (!model.Root.gameObject.activeInHierarchy) return; // nothing to dress

            if (root == null) root = Prim.Group(model.Root, "Status Effects");
            spin += Time.deltaTime;

            Show(ref shield, player.HasStatus(Core.StatusFlags.Shielded), BuildShield);
            Show(ref boost, player.HasStatus(Core.StatusFlags.DamageBoost), BuildBoost);
            Show(ref flames, player.HasStatus(Core.StatusFlags.Burning), () => Fx.AttachFlames(root, new Vector3(0f, 0.9f, 0f)));
            Show(ref sparks, player.HasStatus(Core.StatusFlags.EMPDisabled), () => Fx.AttachSparks(root, new Vector3(0f, 1.1f, 0f), Fx.ShockColour));
            // [v0.17] Show(ref revealed, player.HasStatus(Core.StatusFlags.Revealed), BuildRevealed);
            // (v0.18) the diamond over the head follows the same rule as the HUD marker: only a viewer whose
            //         crew holds a mark on this player sees it. Cameras and lasers no longer put it there.
            var viewer = Network.NetworkPlayer.FindLocal();
            Show(ref revealed, viewer != null && viewer != player && player.IsMarkedFor(viewer.Team), BuildRevealed);
            Show(ref flashed, player.HasStatus(Core.StatusFlags.Flashed), BuildFlashed);
            Show(ref netSight, player.HasStatus(Core.StatusFlags.NetSight), BuildNetSight);
            Show(ref carried, player.HasStatus(Core.StatusFlags.Encumbered), BuildCarried);

            if (shield != null && shield.activeSelf) shield.transform.localScale = new Vector3(1.5f, 2.15f, 1.5f) * (1f + Mathf.Sin(spin * 4f) * 0.02f);
            if (revealed != null && revealed.activeSelf) revealed.transform.localRotation = Quaternion.Euler(0f, spin * 120f, 0f);
            if (flashed != null && flashed.activeSelf) flashed.transform.localRotation = Quaternion.Euler(0f, spin * 260f, 0f);
            if (boost != null && boost.activeSelf) boost.transform.localRotation = Quaternion.Euler(0f, spin * 90f, 0f);
        }

        private static void Show(ref GameObject effect, bool on, System.Func<GameObject> build)
        {
            if (on && effect == null) effect = build();
            if (effect != null && effect.activeSelf != on) effect.SetActive(on);
        }

        private GameObject BuildShield()
        {
            var bubble = Prim.Ball(root, "Shield Bubble", new Vector3(0f, 1f, 0f), new Vector3(1.5f, 2.15f, 1.5f), ArtMaterials.Translucent(new Color(0.3f, 0.9f, 1f, 0.22f)));
            // A plate in front so the shield has a facing even from far away.
            Prim.Box(bubble.transform, "Shield Plate", new Vector3(0f, 0f, 0.5f), new Vector3(0.6f, 0.62f, 0.03f), ArtMaterials.Translucent(new Color(0.5f, 1f, 1f, 0.45f)));
            return bubble;
        }

        private GameObject BuildBoost()
        {
            var aura = Prim.Group(root, "Damage Aura", new Vector3(0f, 0.06f, 0f)).gameObject;
            var red = ArtMaterials.Translucent(new Color(1f, 0.25f, 0.15f, 0.8f));
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                Prim.Box(aura.transform, "Spoke", new Vector3(Mathf.Cos(a) * 0.55f, 0f, Mathf.Sin(a) * 0.55f), new Vector3(0.06f, 0.02f, 0.22f), red, new Vector3(0f, 90f - a * Mathf.Rad2Deg, 0f));
            }
            Fx.AttachMotes(aura.transform, new Vector3(0f, 0.6f, 0f), new Color(1f, 0.3f, 0.2f, 0.9f));
            return aura;
        }

        private GameObject BuildRevealed()
        {
            // A magenta diamond over the head: "your crew can see this one".
            var marker = Prim.Group(root, "Revealed Marker", new Vector3(0f, 2.35f, 0f)).gameObject;
            var mat = ArtMaterials.Emissive(new Color(1f, 0.2f, 0.9f));
            Prim.Box(marker.transform, "Diamond", Vector3.zero, new Vector3(0.2f, 0.2f, 0.2f), mat, new Vector3(45f, 0f, 45f));
            Prim.Box(marker.transform, "Stem", new Vector3(0f, -0.22f, 0f), new Vector3(0.03f, 0.16f, 0.03f), mat);
            return marker;
        }

        private GameObject BuildFlashed()
        {
            var stars = Prim.Group(root, "Blinded", new Vector3(0f, 2.05f, 0f)).gameObject;
            var white = ArtMaterials.Emissive(new Color(1f, 1f, 0.8f));
            for (int i = 0; i < 4; i++)
            {
                float a = i * Mathf.PI / 2f;
                Prim.Box(stars.transform, "Star", new Vector3(Mathf.Cos(a) * 0.3f, 0f, Mathf.Sin(a) * 0.3f), new Vector3(0.09f, 0.09f, 0.09f), white, new Vector3(45f, 45f, 0f));
            }
            return stars;
        }

        private GameObject BuildNetSight()
        {
            var band = Prim.Group(model.Head != null ? model.Head : root, "Net Sight Band").gameObject;
            Prim.Box(band.transform, "Band", new Vector3(0f, 0.03f, 0.13f), new Vector3(0.26f, 0.05f, 0.03f), ArtMaterials.Emissive(new Color(0.3f, 0.95f, 1f)));
            return band;
        }

        private GameObject BuildCarried()
        {
            // The loot case rides on the back; the networked case itself follows the carrier a little above.
            var pack = Prim.Group(model.Torso != null ? model.Torso : root, "Carried Case").gameObject;
            Prim.Box(pack.transform, "Case", new Vector3(0f, 0.05f, -0.3f), new Vector3(0.42f, 0.3f, 0.16f), ArtMaterials.Colour(new Color(0.75f, 0.6f, 0.2f)));
            Prim.Box(pack.transform, "Strap", new Vector3(0f, 0.05f, -0.21f), new Vector3(0.06f, 0.34f, 0.04f), ArtMaterials.Colour(new Color(0.12f, 0.12f, 0.14f)));
            Prim.Box(pack.transform, "Lock", new Vector3(0f, 0.05f, -0.385f), new Vector3(0.08f, 0.06f, 0.02f), ArtMaterials.Emissive(new Color(1f, 0.8f, 0.2f)));
            return pack;
        }
    }
}
