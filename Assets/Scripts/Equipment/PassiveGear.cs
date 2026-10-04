namespace Ouroboros.Equipment
{
    /// <summary>
    /// Armor, accessories and other items with no active use. Their stat modifiers are summed by
    /// <see cref="NetworkLoadout"/> (defense, damage, speed) and consulted by NetworkPlayer /
    /// PlayerController, so this component exists mainly so the slot shows up in the loadout and
    /// cosmetic prefabs can hang off it.
    /// </summary>
    public class PassiveGear : BaseEquipment
    {
        protected override bool CanUse() => false;
    }
}
