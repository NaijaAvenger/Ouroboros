using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// Shows abilities in the world (v0.13). It listens for <c>NetworkPlayer.AbilityUsed</c>, which is raised on
    /// every peer, asks the user's class what that ability looks like (<c>BasePlayerClass.GetAbilityVisual</c>)
    /// and spawns the matching code-built effect. It also owns the Smith's placed charges, which are not
    /// networked objects, and the blasts of traps and projectiles when no effect asset is assigned.
    ///
    /// Cosmetic only. Positions come from each peer's view of the player, so a charge can sit a few centimetres
    /// from where the host has it. Added automatically by <c>UI.MatchFeedback</c>.
    /// </summary>
    public class AbilityEffects : MonoBehaviour
    {
        private class Charge
        {
            public GameObject Model;
            public Renderer Light;
            public Vector3 Position;
            public float Radius;
        }

        private readonly Dictionary<Network.NetworkPlayer, List<Charge>> charges = new Dictionary<Network.NetworkPlayer, List<Charge>>();
        private float blink;

        private void OnEnable()
        {
            Network.NetworkPlayer.AbilityUsed += OnAbilityUsed;
            Network.NetworkPlayer.PlayerKilled += OnPlayerKilled;
            Interaction.ProximityTrap.Detonated += OnTrapDetonated;
            Equipment.NetworkLoadout.GadgetUsed += OnGadgetUsed; // (v0.14)
        }

        private void OnDisable()
        {
            Network.NetworkPlayer.AbilityUsed -= OnAbilityUsed;
            Network.NetworkPlayer.PlayerKilled -= OnPlayerKilled;
            Interaction.ProximityTrap.Detonated -= OnTrapDetonated;
            Equipment.NetworkLoadout.GadgetUsed -= OnGadgetUsed;
            foreach (var list in charges.Values) foreach (var c in list) if (c.Model != null) Destroy(c.Model);
            charges.Clear();
        }

        private void Update()
        {
            // Armed charges blink, faster than a trap, so nobody mistakes one for scenery.
            blink += Time.deltaTime;
            bool on = Mathf.Repeat(blink, 0.5f) < 0.25f;
            foreach (var list in charges.Values)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].Light != null) list[i].Light.enabled = on;
                }
            }
        }

        private void OnAbilityUsed(Network.NetworkPlayer player, int slot)
        {
            if (player == null || player.Object == null || player.CurrentClass == null) return;
            var visual = player.CurrentClass.GetAbilityVisual(slot);
            if (visual.Kind == Core.AbilityVisualKind.None) return;

            Vector3 feet = player.transform.position;
            Vector3 chest = feet + Vector3.up * 1.1f;
            Vector3 eye = player.EyePosition;
            Vector3 aim = player.AimDirection;
            Color colour = visual.Colour;
            Color crew = Core.TeamUtil.IsValid(player.Team) ? CrewStyle.Get(player.Team).Primary : Color.white;

            switch (visual.Kind)
            {
                case Core.AbilityVisualKind.Pulse:
                {
                    Color rim = colour; rim.a = 0.85f;
                    Fx.Ring(feet + Vector3.up * 0.08f, visual.Radius, rim, Mathf.Max(0.4f, visual.Seconds), 0.14f);
                    Fx.Ring(feet + Vector3.up * 0.08f, visual.Radius * 0.6f, rim, Mathf.Max(0.4f, visual.Seconds) * 1.25f, 0.08f);
                    Fx.Burst(chest, rim, 14, 3f, 0.09f, 0.45f, -0.2f);
                    break;
                }
                case Core.AbilityVisualKind.Shock:
                    Fx.Shock(chest, visual.Radius, colour, Mathf.Max(0.3f, visual.Seconds));
                    break;

                case Core.AbilityVisualKind.Smoke:
                    Fx.Burst(feet + Vector3.up * 0.3f, colour, 18, 4f, 0.3f, 0.6f, 0.2f);
                    Fx.Smoke(feet, visual.Radius, Mathf.Max(1f, visual.Seconds), colour);
                    break;

                case Core.AbilityVisualKind.Fire:
                {
                    Vector3 at = Ground(eye + aim * visual.Forward);
                    Combat.ShotTracer.Spawn(eye + aim * 0.5f - Vector3.up * 0.2f, at, new Color(1f, 0.6f, 0.2f, 0.8f), 0.05f, 0.18f);
                    Fx.Fire(at, visual.Radius, Mathf.Max(0.6f, visual.Seconds));
                    Fx.Ring(at + Vector3.up * 0.06f, visual.Radius, new Color(1f, 0.5f, 0.1f, 0.8f), 0.5f, 0.14f);
                    break;
                }
                case Core.AbilityVisualKind.Flash:
                {
                    Vector3 at = eye + aim * visual.Forward;
                    Fx.Sphere(at, visual.Radius * 0.5f, new Color(1f, 1f, 1f, 0.85f), Mathf.Max(0.15f, visual.Seconds));
                    Fx.Sphere(at, visual.Radius, new Color(1f, 1f, 0.9f, 0.3f), Mathf.Max(0.15f, visual.Seconds) * 1.4f);
                    Fx.Flash(at, Color.white, visual.Radius * 4f, 8f, 0.35f);
                    Fx.Burst(at, Color.white, 20, visual.Radius, 0.12f, 0.35f, 0f);
                    break;
                }
                case Core.AbilityVisualKind.Beam:
                {
                    Vector3 from = eye + aim * 0.6f - Vector3.up * 0.15f;
                    Vector3 to = eye + aim * visual.Radius;
                    if (Physics.Raycast(eye, aim, out RaycastHit hit, visual.Radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) to = hit.point;
                    Color bolt = colour; bolt.a = 0.95f;
                    Combat.ShotTracer.Spawn(from, to, bolt, 0.07f, Mathf.Max(0.12f, visual.Seconds));
                    Fx.Burst(to, bolt, 16, 3.5f, 0.09f, 0.35f, 0.4f);
                    Fx.Flash(to, colour, 6f, 3f, 0.2f);
                    break;
                }
                case Core.AbilityVisualKind.PlaceCharge:
                    PlaceCharge(player, feet, visual.Radius, crew);
                    break;

                case Core.AbilityVisualKind.DetonateCharges:
                    DetonateCharges(player);
                    break;

                case Core.AbilityVisualKind.Drone:
                    ReconDrone.Launch(feet + Vector3.up * 1.6f, visual.Radius, Mathf.Max(2f, visual.Seconds), colour, crew);
                    break;

                case Core.AbilityVisualKind.Blast:
                {
                    Vector3 at = eye + aim * visual.Forward;
                    Fx.Explosion(at, visual.Radius);
                    PlayBlastSound(at, 0.7f);
                    break;
                }
                case Core.AbilityVisualKind.Shimmer:
                {
                    Color shell = colour; shell.a = 0.3f;
                    Color mote = colour; mote.a = 0.9f;
                    Fx.Sphere(chest, visual.Radius, shell, Mathf.Max(0.2f, visual.Seconds));
                    Fx.Burst(chest, mote, 16, 2.2f, 0.08f, 0.5f, -0.3f);
                    break;
                }
            }
        }

        // ------------------------------------------------------------------
        // The Smith's charges

        private void PlaceCharge(Network.NetworkPlayer player, Vector3 position, float radius, Color crew)
        {
            if (!charges.TryGetValue(player, out var list)) charges[player] = list = new List<Charge>();

            var root = new GameObject("Placed Charge");
            root.transform.position = Ground(position + Vector3.up * 0.5f);
            root.transform.rotation = Quaternion.Euler(0f, player.transform.eulerAngles.y, 0f);
            GearBuilder.BuildCharge(root.transform, crew, out Renderer light);
            list.Add(new Charge { Model = root, Light = light, Position = root.transform.position, Radius = radius });
            Fx.Ring(root.transform.position + Vector3.up * 0.05f, radius, new Color(1f, 0.4f, 0.15f, 0.5f), 0.7f, 0.06f); // how far it will reach
        }

        private void DetonateCharges(Network.NetworkPlayer player)
        {
            if (!charges.TryGetValue(player, out var list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                Fx.Explosion(list[i].Position + Vector3.up * 0.2f, list[i].Radius);
                PlayBlastSound(list[i].Position, 1f);
                if (list[i].Model != null) Destroy(list[i].Model);
            }
            list.Clear();
        }

        private void OnPlayerKilled(Network.NetworkPlayer victim, Fusion.PlayerRef killer)
        {
            // The class forgets its charges when its owner dies, so the models go too.
            if (victim == null || !charges.TryGetValue(victim, out var list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Model == null) continue;
                Fx.Burst(list[i].Position + Vector3.up * 0.1f, new Color(0.7f, 0.7f, 0.7f, 0.8f), 6, 1.5f, 0.08f, 0.4f);
                Destroy(list[i].Model);
            }
            list.Clear();
        }

        /// <summary>(v0.14) What each gadget looks like when it goes off.</summary>
        private void OnGadgetUsed(Equipment.NetworkLoadout loadout, Data.GadgetType type, Vector3 point, float reach)
        {
            switch (type)
            {
                case Data.GadgetType.BreachingKit:
                    Fx.Explosion(point, Mathf.Max(2f, reach));
                    PlayBlastSound(point, 1f);
                    break;
                case Data.GadgetType.EmpEmitter:
                    Fx.Shock(point, reach, Fx.ShockColour, 0.6f);
                    break;
                case Data.GadgetType.BypassKey:
                    Fx.Burst(point, new Color(0.3f, 1f, 0.5f, 1f), 14, 2.5f, 0.07f, 0.4f, 0.2f);
                    Fx.Flash(point, new Color(0.3f, 1f, 0.5f), 5f, 2.5f, 0.3f);
                    break;
                case Data.GadgetType.LootScanner:
                    Fx.Ring(Ground(point) + Vector3.up * 0.08f, reach, new Color(0.3f, 0.95f, 1f, 0.8f), 1.1f, 0.12f);
                    Fx.Ring(Ground(point) + Vector3.up * 0.08f, reach * 0.55f, new Color(0.3f, 0.95f, 1f, 0.8f), 1.4f, 0.07f);
                    break;
            }
        }

        private void OnTrapDetonated(Vector3 position, float radius)
        {
            Fx.Explosion(position + Vector3.up * 0.2f, radius);
            PlayBlastSound(position, 0.9f);
        }

        private static void PlayBlastSound(Vector3 position, float volume)
        {
            AudioSource.PlayClipAtPoint(Core.ProceduralSfx.Explosion, position, volume);
        }

        /// <summary>The floor under <paramref name="point"/>, or the point itself when there is none close below.</summary>
        public static Vector3 Ground(Vector3 point)
        {
            return Physics.Raycast(point + Vector3.up * 0.5f, Vector3.down, out RaycastHit hit, 8f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                ? hit.point
                : point;
        }
    }

    /// <summary>
    /// The Shield's recon drone (v0.13): climbs over the spot it was released, sweeps scan rings across the area
    /// it revealed, then flies off. It is a picture of the reveal, not the thing that does it.
    /// </summary>
    public class ReconDrone : MonoBehaviour
    {
        private Transform[] rotors;
        private Vector3 origin;
        private float radius, life, age, nextScan;
        private Color scanColour;

        public static void Launch(Vector3 from, float scanRadius, float seconds, Color scan, Color crew)
        {
            var go = new GameObject("Recon Drone");
            go.transform.position = from;
            var drone = go.AddComponent<ReconDrone>();
            GearBuilder.BuildDrone(go.transform, crew, out drone.rotors);
            drone.origin = from;
            drone.radius = scanRadius;
            drone.life = seconds;
            drone.scanColour = scan;
        }

        private void Update()
        {
            age += Time.deltaTime;
            float climb = Mathf.Clamp01(age / 0.8f);
            float leave = Mathf.Clamp01((age - (life - 0.8f)) / 0.8f);
            float height = Mathf.Lerp(0f, 5f, 1f - (1f - climb) * (1f - climb)) + leave * leave * 14f;
            Vector3 orbit = new Vector3(Mathf.Cos(age * 1.3f), 0f, Mathf.Sin(age * 1.3f)) * (1.2f * climb);
            transform.position = origin + Vector3.up * height + orbit + Vector3.up * (Mathf.Sin(age * 3f) * 0.08f);
            transform.rotation = Quaternion.Euler(0f, age * 75f, 0f);
            if (rotors != null)
            {
                for (int i = 0; i < rotors.Length; i++) if (rotors[i] != null) rotors[i].Rotate(0f, 2200f * Time.deltaTime, 0f, Space.Self);
            }

            if (age >= nextScan && leave <= 0f)
            {
                nextScan = age + 1.5f;
                Color rim = scanColour; rim.a = 0.7f;
                Fx.Ring(AbilityEffects.Ground(origin) + Vector3.up * 0.08f, radius, rim, 1.2f, 0.1f);
            }
            if (age >= life) Destroy(gameObject);
        }
    }
}
