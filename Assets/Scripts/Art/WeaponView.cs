using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// Shows the primary weapon's model (v0.12, milestone M4): in the right hand of other players' characters,
    /// and as a first-person view model in front of the local player's camera. The model is whatever prefab
    /// <c>EquipmentData.equipmentPrefab</c> names; the scene builder points the starter weapons at the imported
    /// gun pack. Colliders are stripped from the instance, so a view model can never stop a bullet.
    /// Added at runtime by <c>Player.PlayerPresentation</c>.
    /// </summary>
    [RequireComponent(typeof(Network.NetworkPlayer))]
    public class WeaponView : MonoBehaviour
    {
        private Network.NetworkPlayer player;
        private Equipment.NetworkLoadout loadout;
        private GameObject instance;
        private Data.EquipmentData shown;
        private Transform hand, cameraTransform;
        private Vector3 restPosition;
        private float kick;

        /// <summary>The character's hand joint. Set by the presentation whenever the model is rebuilt.</summary>
        public void SetHand(Transform handJoint)
        {
            hand = handJoint;
            shown = null; // re-parent on the next refresh
        }

        private void Awake()
        {
            player = GetComponent<Network.NetworkPlayer>();
            loadout = GetComponent<Equipment.NetworkLoadout>();
            Equipment.NetworkLoadout.WeaponFired += OnWeaponFired;
        }

        private void OnDestroy()
        {
            Equipment.NetworkLoadout.WeaponFired -= OnWeaponFired;
            if (instance != null) Destroy(instance);
        }

        private void OnWeaponFired(Equipment.NetworkLoadout source, int slot, Vector3 point, bool hit)
        {
            if (source == loadout && slot == (int)Equipment.EquipmentSlotType.Primary) kick = 1f;
        }

        private void LateUpdate()
        {
            if (player == null || player.Object == null || loadout == null || loadout.Object == null) return;

            var data = loadout.GetData((int)Equipment.EquipmentSlotType.Primary);
            if (data != shown) Rebuild(data);
            if (instance == null) return;

            bool visible = player.IsAlive && !player.IsExtracted;
            if (instance.activeSelf != visible) instance.SetActive(visible);

            // Recoil kick: the model jumps back and settles.
            kick = Mathf.Max(0f, kick - Time.deltaTime * 9f);
            instance.transform.localPosition = restPosition - Vector3.forward * (kick * 0.06f) + Vector3.up * (kick * 0.012f);
        }

        private void Rebuild(Data.EquipmentData data)
        {
            shown = data;
            if (instance != null) { Destroy(instance); instance = null; }
            if (data == null || data.equipmentPrefab == null) return;

            bool firstPerson = player.IsLocalPlayer;
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

            instance = Instantiate(data.equipmentPrefab, parent);
            instance.name = "WeaponView";
            foreach (var c in instance.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var body in instance.GetComponentsInChildren<Rigidbody>(true)) Destroy(body);

            restPosition = firstPerson ? data.viewPosition : data.handPosition;
            instance.transform.localPosition = restPosition;
            instance.transform.localRotation = Quaternion.Euler(data.viewEuler);
            instance.transform.localScale = Vector3.one * Mathf.Max(0.01f, data.viewScale);
        }
    }
}
