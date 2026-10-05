using System;

namespace Ouroboros.Core
{
    /// <summary>
    /// Core game constants for the 4v4v4v4 extraction heist game.
    /// Balance values live in <see cref="Data.GameModeConfig"/> / <see cref="Data.ClassData"/>;
    /// only structural limits and tuning fallbacks belong here.
    /// </summary>
    public static class GameConstants
    {
        public const int MAX_TEAMS = 4;
        public const int PLAYERS_PER_TEAM = 4;
        public const int MAX_PLAYERS = MAX_TEAMS * PLAYERS_PER_TEAM;

        public const int MAX_ABILITY_SLOTS = 4;
        public const int MAX_EQUIPMENT_SLOTS = 6;
        public const int MAX_TECH_SLOTS = 3;
        /// <summary>Number of entries in <see cref="InputButtons"/> (keep in sync).</summary>
        public const int INPUT_BUTTON_COUNT = 14;

        // ---- v0.2 additions ----

        /// <summary>Stamina regenerated per second while not sprinting.</summary>
        public const float STAMINA_REGEN_PER_SECOND = 10f;
        /// <summary>Stamina drained per second while sprinting.</summary>
        public const float SPRINT_STAMINA_DRAIN_PER_SECOND = 15f;
        /// <summary>Minimum stamina required to begin sprinting (prevents stutter at 0).</summary>
        public const float SPRINT_MIN_STAMINA = 5f;
        /// <summary>Delay after stamina is spent before regeneration resumes.</summary>
        public const float STAMINA_REGEN_DELAY = 1.0f;

        /// <summary>How often (in ticks) zone-style objects re-scan their overlap volume.</summary>
        public const int ZONE_SCAN_INTERVAL_TICKS = 5;

        /// <summary>Maximum loot a single player can carry. Excess is refused by objectives.</summary>
        public const int MAX_CARRIED_LOOT = 10000;
    }

    /// <summary>
    /// Team identifiers for the 4-team system.
    /// </summary>
    public enum TeamID
    {
        None = 0,
        TeamAlpha = 1,
        TeamBravo = 2,
        TeamCharlie = 3,
        TeamDelta = 4
    }

    /// <summary>
    /// Helpers for converting between <see cref="TeamID"/> and zero-based array indices.
    /// </summary>
    public static class TeamUtil
    {
        public static int ToIndex(TeamID team) => (int)team - 1;
        public static TeamID FromIndex(int index) => (TeamID)(index + 1);
        public static bool IsValid(TeamID team) => team != TeamID.None && (int)team <= GameConstants.MAX_TEAMS;
    }

    /// <summary>
    /// Player class types. Easily expandable for future classes.
    /// </summary>
    public enum PlayerClassType
    {
        None = 0,
        Hacker = 1,
        Saboteur = 2,
        Demolitions = 3,
        Agent = 4
        // Future classes can be added here
    }

    /// <summary>
    /// Bit flags describing transient status effects on a player.
    /// Replicated so every peer can drive VFX / UI without extra RPCs.
    /// </summary>
    [Flags]
    public enum StatusFlags
    {
        None         = 0,
        Shielded     = 1 << 0,
        DamageBoost  = 1 << 1,
        Stealthed    = 1 << 2,
        Flashed      = 1 << 3,
        EMPDisabled  = 1 << 4,
        Revealed     = 1 << 5,
        Burning      = 1 << 6,
        Sprinting    = 1 << 7,
        Interacting  = 1 << 8,
        Extracted    = 1 << 9,
        /// <summary>Carrying a LootCase (v0.5): slowed, cannot pick up another.</summary>
        Encumbered   = 1 << 10,
        // v0.7 movement tech (set by PlayerController on the state authority; visuals / HUD only)
        Sliding      = 1 << 11,
        Gliding      = 1 << 12,
        Climbing     = 1 << 13,
        Grappling    = 1 << 14
    }

    /// <summary>
    /// Class movement perks (v0.7). Base movement (walk, sprint, slide, jump, mantle) is universal;
    /// these toggle the class-specific techniques. Defaults come from each class, overridable via ClassData.
    /// </summary>
    [System.Serializable]
    public struct MovementProfile
    {
        public bool CanDoubleJump;
        public bool CanGlide;
        public float GlideSeconds;
        public bool CanWallClimb;
        public float WallClimbSeconds;
        public bool CanGrapple;
        public float GrappleRange;

        public static MovementProfile Default => new MovementProfile { GlideSeconds = 2.5f, WallClimbSeconds = 1.5f, GrappleRange = 25f };

        public string Describe()
        {
            var parts = new System.Collections.Generic.List<string>();
            if (CanDoubleJump) parts.Add("Double jump");
            if (CanGlide) parts.Add($"Glide {GlideSeconds:0.#}s");
            if (CanWallClimb) parts.Add($"Wall climb {WallClimbSeconds:0.#}s");
            if (CanGrapple) parts.Add($"Grapple {GrappleRange:0}m");
            return parts.Count > 0 ? string.Join(" · ", parts) : "Slide · Mantle";
        }
    }

    /// <summary>
    /// Button indices packed into <see cref="NetworkInputData"/>.
    /// </summary>
    public enum InputButtons
    {
        Jump = 0,
        Sprint = 1,
        Interact = 2,
        Ability1 = 3,
        Ability2 = 4,
        Ability3 = 5,
        Ability4 = 6,
        Primary = 7,
        Secondary = 8,
        Utility = 9,
        Gadget = 10,
        Reload = 11,
        /// <summary>Sprint + Slide = power slide (v0.7).</summary>
        Slide = 12,
        /// <summary>Grappling hook for classes that have it (v0.7).</summary>
        Grapple = 13
    }
}
