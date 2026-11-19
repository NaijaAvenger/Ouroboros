namespace Ouroboros.Core
{
    /// <summary>(v0.19) The one thing the HUD tells a player to do right now.</summary>
    public enum Objective
    {
        None = 0,
        /// <summary>The match has not started: read the briefing.</summary>
        GetReady,
        /// <summary>Nothing in hand: find a vault and open it.</summary>
        CrackVault,
        /// <summary>No vault left to open: break containers, take what rivals drop.</summary>
        FindLoot,
        /// <summary>Loot is lying close by.</summary>
        GrabLoot,
        /// <summary>Carrying enough to be worth a minute's defence: take it to a station.</summary>
        CashOut,
        /// <summary>The crew's own cash-out is running: stay on it.</summary>
        DefendCashOut,
        /// <summary>The crew's own cash-out is being hijacked right now.</summary>
        StopTheHijack,
        /// <summary>A rival crew is cashing out: take it.</summary>
        StealCashOut,
        /// <summary>A crewmate is down within reach.</summary>
        ReviveCrewmate,
        /// <summary>The getaway is called: reach a lift.</summary>
        GetOut,
        /// <summary>Standing in a lift: hold it until the crew is away.</summary>
        HoldTheLift,
        /// <summary>Down, and a crewmate could bring you back.</summary>
        AwaitRevive,
        /// <summary>Down, waiting for the redeploy (or out of redeploys).</summary>
        Redeploying,
        /// <summary>Extracted: nothing left to do but watch.</summary>
        // [v0.20] Away
        Away,
        // ---- (v0.21) Grand Vaults. Appended so the values above keep their meaning.
        /// <summary>Carrying a vault's key: take it to the door.</summary>
        UseVaultKey,
        /// <summary>The crew's drill is running on a vault door: stay near it.</summary>
        DefendDrill,
        /// <summary>Standing in an open vault whose perk nobody has taken.</summary>
        ClaimPerk,
        /// <summary>A vault key is lying on the floor close by.</summary>
        TakeVaultKey,
        /// <summary>Every Grand Vault is open; the small safes are what is left to crack.</summary>
        CrackSafe
    }

    /// <summary>Everything <see cref="ObjectiveRules.Choose"/> needs to know about the player's situation.</summary>
    public struct ObjectiveFacts
    {
        public bool MatchLive, Alive, Extracted;
        /// <summary>A living crewmate could revive this player with their ID card.</summary>
        public bool CanBeRevived;
        public bool GetawayOpen, InOpenExit;
        public bool OwnCashOutRunning, OwnCashOutBeingHijacked, RivalCashOutRunning;
        public bool CrewmateDownInReach;
        /// <summary>What the player is carrying, and how much makes a cash-out worth starting.</summary>
        public int Carrying, WorthBanking;
        /// <summary>A station is free, or already the crew's own.</summary>
        public bool StationAvailable;
        public bool LootInReach, VaultAvailable;
        // ---- (v0.21) Grand Vaults. VaultAvailable now means a Grand Vault is still shut.
        public bool CarryingVaultKey, OwnDrillRunning, PerkInReach, VaultKeyInReach;
        /// <summary>A small safe (the old kind of vault) can still be cracked.</summary>
        public bool SafeAvailable;
    }

    /// <summary>
    /// (v0.19) Picks the single most useful instruction for a player. The order is the game's priorities:
    /// stay alive and get crewmates up, never lose money you have put down, bank what you carry, then go and
    /// find more. No Unity or Fusion types, so the unit tests can run it.
    /// </summary>
    public static class ObjectiveRules
    {
        public static Objective Choose(ObjectiveFacts f)
        {
            if (f.Extracted) return Objective.Away;
            if (!f.MatchLive) return Objective.GetReady;
            if (!f.Alive) return f.CanBeRevived ? Objective.AwaitRevive : Objective.Redeploying;

            // The getaway overrides everything: nothing counts unless it leaves the building.
            if (f.GetawayOpen) return f.InOpenExit ? Objective.HoldTheLift : Objective.GetOut;

            if (f.OwnCashOutBeingHijacked) return Objective.StopTheHijack;      // money already down, being taken
            if (f.CrewmateDownInReach) return Objective.ReviveCrewmate;         // a gun back in the fight
            if (f.OwnCashOutRunning) return Objective.DefendCashOut;

            // (v0.21) A vault in the act of opening comes before anything that only makes more money later.
            if (f.CarryingVaultKey) return Objective.UseVaultKey;
            if (f.OwnDrillRunning) return Objective.DefendDrill;
            if (f.PerkInReach) return Objective.ClaimPerk;          // one per vault, first crew only: before the loot beside it

            if (f.StationAvailable && f.Carrying > 0 && f.Carrying >= f.WorthBanking) return Objective.CashOut;
            if (f.RivalCashOutRunning) return Objective.StealCashOut;
            if (f.LootInReach) return Objective.GrabLoot;
            if (f.VaultKeyInReach) return Objective.TakeVaultKey;   // (v0.21)
            if (f.VaultAvailable) return Objective.CrackVault;
            if (f.SafeAvailable) return Objective.CrackSafe;        // (v0.21)

            // Nothing left to open: bank whatever is in hand, however little, or go scavenging.
            if (f.StationAvailable && f.Carrying > 0) return Objective.CashOut;
            return Objective.FindLoot;
        }

        /// <summary>How much carried loot makes a cash-out worth starting: an eighth of the quota, and never trivial.</summary>
        public static int WorthBanking(int quota)
        {
            int eighth = quota / 8;
            return eighth < 300 ? 300 : eighth;
        }

        /// <summary>True for objectives that mean "drop what you are doing": the HUD shows them in the alert colour.</summary>
        public static bool IsUrgent(Objective objective)
        {
            return objective == Objective.StopTheHijack || objective == Objective.ReviveCrewmate
                || objective == Objective.GetOut || objective == Objective.StealCashOut;
        }
    }
}
