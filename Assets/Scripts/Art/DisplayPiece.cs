using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// A show copy of an artefact on a plinth (v0.12, milestone M4). Pure set dressing: it builds the same
    /// code-made model the loot uses, at play time, so scenes stay small and the look stays in one place.
    /// It has no collider, is not networked and cannot be picked up; the real pieces are in the vaults.
    /// </summary>
    public class DisplayPiece : MonoBehaviour
    {
        public Data.LootShape shape = Data.LootShape.Leopard;
        public Data.LootTier tier = Data.LootTier.Rare;
        [Tooltip("Show size relative to the pickup model. Gallery pieces read better a little larger.")]
        public float scale = 1.6f;
        [Tooltip("Degrees per second. 0 keeps the piece still.")]
        public float turnSpeed = 12f;

        private Transform model;

        private void Start()
        {
            model = ArtefactBuilder.Build(transform, shape, tier);
            if (model == null) return;
            model.localScale = Vector3.one * scale;
            // The pickup version spins and bobs to be noticed. A museum piece only turns slowly.
            var spin = model.GetComponentInChildren<PickupSpin>();
            if (spin != null) Destroy(spin);
        }

        private void Update()
        {
            if (model != null && turnSpeed != 0f) model.Rotate(0f, turnSpeed * Time.deltaTime, 0f, Space.World);
        }
    }
}
