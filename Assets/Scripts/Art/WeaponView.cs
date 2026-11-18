using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// Shows the weapon in hand (v0.12; reworked in v0.13 for one weapon in hand, swapping and aiming).
    ///
    ///  - Local player: a first-person view model in front of the camera. It comes up from below when drawn,
    ///    moves to the centre when aiming, dips during a reload and kicks when fired.
    ///  - Everyone else: the weapon sits in the character's right hand and the other weapon is slung on the back.
    ///
    /// The model is <c>EquipmentData.equipmentPrefab</c> when the item has one (the scene builder points the
    /// starter weapons at the imported gun pack). Items without a prefab get a code-built stand-in from
    /// <see cref="GearBuilder"/>, so no weapon is ever invisible. Colliders are stripped from the instance.
    /// Added at runtime by <c>Player.PlayerPresentation</c>.
    /// </summary>
    [RequireComponent(typeof(Network.NetworkPlayer))]
    public class WeaponView : MonoBehaviour
    {
        private Network.NetworkPlayer player;
        private Equipment.NetworkLoadout loadout;
        private Player.PlayerController controller;

        private GameObject instance, stowedInstance;
        private Data.EquipmentData shown, stowedShown;
        private bool shownFirstPerson;
        private Transform hand, back, cameraTransform;
        private Quaternion restRotation = Quaternion.identity;
        private float kick;

        /// <summary>World position of the muzzle of the weapon in hand, for flashes and tracers. False if nothing is shown.</summary>
        public bool TryGetMuzzle(out Vector3 position)
        {
            position = default;
            if (instance == null || !instance.activeInHierarchy || shown == null) return false;
            position = shown.equipmentPrefab != null
                ? instance.transform.TransformPoint(new Vector3(0.45f, 0.05f, 0f))      // the gun pack's barrels run along +X
                : instance.transform.TransformPoint(GearBuilder.MuzzleOf(GearBuilder.ShapeOf(shown)));
            return true;
        }

        /// <summary>The character's hand joint. Set by the presentation whenever the model is rebuilt.</summary>
        public void SetHand(Transform handJoint)
        {
            // [v0.12] hand = handJoint;
            // [v0.12] shown = null; // re-parent on the next refresh
            SetJoints(handJoint, null);
        }

        /// <summary>(v0.13) Hand for the weapon in use, back for the one that is stowed.</summary>
        public void SetJoints(Transform handJoint, Transform backJoint)
        {
            hand = handJoint;
            back = backJoint;
            shown = null; // re-parent on the next refresh
            stowedShown = null;
        }

        private void Awake()
        {
            player = GetComponent<Network.NetworkPlayer>();
            loadout = GetComponent<Equipment.NetworkLoadout>();
            controller = GetComponent<Player.PlayerController>();
            Equipment.NetworkLoadout.WeaponFired += OnWeaponFired;
        }

        private void OnDestroy()
        {
            Equipment.NetworkLoadout.WeaponFired -= OnWeaponFired;
            if (instance != null) Destroy(instance);
            if (stowedInstance != null) Destroy(stowedInstance);
        }

        private void OnWeaponFired(Equipment.NetworkLoadout source, int slot, Vector3 point, bool hit)
        {
            if (source == loadout && slot == loadout.ActiveSlot) kick = 1f;
        }

        private void LateUpdate()
        {
            if (player == null || player.Object == null || loadout == null || loadout.Object == null) return;

            bool firstPerson = player.IsLocalPlayer;
            int slot = loadout.ActiveSlot;
            var data = loadout.GetData(slot);
            if (data != shown || firstPerson != shownFirstPerson || (instance == null && data != null)) Rebuild(data, firstPerson);

            // The other weapon rides on the back of characters seen from outside.
            var other = Equipment.NetworkLoadout.IsWeaponSlot(slot) ? loadout.GetData(slot == 0 ? 1 : 0) : null;
            if (firstPerson) other = null;
            if (other != stowedShown || (stowedInstance == null && other != null && back != null)) RebuildStowed(other);

            bool visible = player.IsAlive && !player.IsExtracted;
            if (stowedInstance != null && stowedInstance.activeSelf != visible) stowedInstance.SetActive(visible);
            if (instance == null) return;
            if (instance.activeSelf != visible) instance.SetActive(visible);
            if (!visible || data == null) return;

            kick = Mathf.Max(0f, kick - Time.deltaTime * 9f);

            if (!firstPerson)
            {
                instance.transform.localPosition = data.handPosition - Vector3.forward * (kick * 0.03f);
                return;
            }

            // ---- First person: hip to sights, the draw, the reload dip and the kick ----
            float aim = controller != null ? controller.AimBlend : 0f;
            aim = aim * aim * (3f - 2f * aim);
            Vector3 position = Vector3.Lerp(data.viewPosition, data.adsPosition, aim);
            float pitch = 0f, roll = 0f;

            float draw = 1f - loadout.SwapProgress;          // 1 = just swapped, weapon still below the screen
            if (draw > 0f)
            {
                position += Vector3.down * (0.34f * draw * draw);
                pitch += 38f * draw;
            }

            if (loadout.IsReloading(slot))
            {
                float dip = Mathf.Sin(loadout.ReloadProgress(slot) * Mathf.PI);
                position += Vector3.down * (0.11f * dip) + Vector3.right * (0.03f * dip);
                pitch += 14f * dip;
                roll += 26f * dip;
            }

            float kickScale = Mathf.Lerp(1f, 0.55f, aim);     // a shouldered weapon moves less
            position += Vector3.back * (kick * 0.06f * kickScale) + Vector3.up * (kick * 0.012f * kickScale);
            pitch -= kick * 2.2f * kickScale;

            instance.transform.localPosition = position;
            instance.transform.localRotation = Quaternion.Euler(pitch, 0f, roll) * restRotation;
        }

        private void Rebuild(Data.EquipmentData data, bool firstPerson)
        {
            shown = data;
            shownFirstPerson = firstPerson;
            if (instance != null) { Destroy(instance); instance = null; }
            if (data == null) return;

            Transform parent;
            if (firstPerson)
            {
                if (cameraTransform == null)
                {
                    var cam = GetComponentInChildren<Camera>(true);
                    cameraTransform = cam != null ? cam.transform : null;
                }
                parent = cameraTransform;
            }
            else parent = hand;
            if (parent == null) { shown = null; return; } // not ready yet: try again next frame

            instance = Make(data, parent, out restRotation);
            instance.name = "WeaponView";
            instance.transform.localPosition = firstPerson ? data.viewPosition : data.handPosition;
            instance.transform.localRotation = restRotation;
        }

        private void RebuildStowed(Data.EquipmentData data)
        {
            stowedShown = data;
            if (stowedInstance != null) { Destroy(stowedInstance); stowedInstance = null; }
            if (data == null || back == null) return;

            stowedInstance = Make(data, back, out Quaternion rest);
            stowedInstance.name = "WeaponView (stowed)";
            // Slung across the back, muzzle up over the left shoulder.
            stowedInstance.transform.localPosition = new Vector3(0.02f, 0.02f, -0.24f);
            stowedInstance.transform.localRotation = Quaternion.Euler(-62f, 0f, 0f) * Quaternion.Euler(0f, 0f, 24f) * rest;
        }

        /// <summary>The item's prefab if it has one, a code-built stand-in otherwise. Never has colliders or bodies.</summary>
        private GameObject Make(Data.EquipmentData data, Transform parent, out Quaternion rest)
        {
            GameObject go;
            if (data.equipmentPrefab != null)
            {
                go = Instantiate(data.equipmentPrefab, parent);
                foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
                foreach (var body in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
                rest = Quaternion.Euler(data.viewEuler);
                go.transform.localScale = Vector3.one * Mathf.Max(0.01f, data.viewScale);
            }
            else
            {
                Color accent = Core.TeamUtil.IsValid(player.Team) ? CrewStyle.Get(player.Team).Accent : Color.white;
                go = GearBuilder.BuildWeapon(parent, data, accent).gameObject; // built barrel-forward: no turn needed
                rest = Quaternion.identity;
            }
            return go;
        }

        // ------------------------------------------------------------------
        // [v0.12] The previous version always showed the Primary slot, had no stand-in for items without a
        // [v0.12] prefab, and knew nothing about aiming, swapping or the stowed weapon.
        // [v0.12] private void OnWeaponFired(Equipment.NetworkLoadout source, int slot, Vector3 point, bool hit)
        // [v0.12] {
        // [v0.12]     if (source == loadout && slot == (int)Equipment.EquipmentSlotType.Primary) kick = 1f;
        // [v0.12] }
        // [v0.12]
        // [v0.12] private void LateUpdate()
        // [v0.12] {
        // [v0.12]     if (player == null || player.Object == null || loadout == null || loadout.Object == null) return;
        // [v0.12]
        // [v0.12]     var data = loadout.GetData((int)Equipment.EquipmentSlotType.Primary);
        // [v0.12]     if (data != shown) Rebuild(data);
        // [v0.12]     if (instance == null) return;
        // [v0.12]
        // [v0.12]     bool visible = player.IsAlive && !player.IsExtracted;
        // [v0.12]     if (instance.activeSelf != visible) instance.SetActive(visible);
        // [v0.12]
        // [v0.12]     // Recoil kick: the model jumps back and settles.
        // [v0.12]     kick = Mathf.Max(0f, kick - Time.deltaTime * 9f);
        // [v0.12]     instance.transform.localPosition = restPosition - Vector3.forward * (kick * 0.06f) + Vector3.up * (kick * 0.012f);
        // [v0.12] }
        // [v0.12]
        // [v0.12] private void Rebuild(Data.EquipmentData data)
        // [v0.12] {
        // [v0.12]     shown = data;
        // [v0.12]     if (instance != null) { Destroy(instance); instance = null; }
        // [v0.12]     if (data == null || data.equipmentPrefab == null) return;
        // [v0.12]
        // [v0.12]     bool firstPerson = player.IsLocalPlayer;
        // [v0.12]     Transform parent;
        // [v0.12]     if (firstPerson)
        // [v0.12]     {
        // [v0.12]         if (cameraTransform == null)
        // [v0.12]         {
        // [v0.12]             var cam = GetComponentInChildren<Camera>(true);
        // [v0.12]             cameraTransform = cam != null ? cam.transform : null;
        // [v0.12]         }
        // [v0.12]         parent = cameraTransform;
        // [v0.12]     }
        // [v0.12]     else parent = hand;
        // [v0.12]     if (parent == null) { shown = null; return; } // not ready yet: try again next frame
        // [v0.12]
        // [v0.12]     instance = Instantiate(data.equipmentPrefab, parent);
        // [v0.12]     instance.name = "WeaponView";
        // [v0.12]     foreach (var c in instance.GetComponentsInChildren<Collider>(true)) Destroy(c);
        // [v0.12]     foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);
        // [v0.12]
        // [v0.12]     restPosition = firstPerson ? data.viewPosition : data.handPosition;
        // [v0.12]     instance.transform.localPosition = restPosition;
        // [v0.12]     instance.transform.localRotation = Quaternion.Euler(data.viewEuler);
        // [v0.12]     instance.transform.localScale = Vector3.one * Mathf.Max(0.01f, data.viewScale);
        // [v0.12] }
    }
}
