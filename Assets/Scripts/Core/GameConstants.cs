namespace Ouroboros.Core
{
    /// <summary>
    /// Core game constants for the 4v4v4v4 extraction heist game.
    /// </summary>
    public static class GameConstants
    {
        public const int MAX_TEAMS = 4;
        public const int PLAYERS_PER_TEAM = 4;
        public const int MAX_PLAYERS = MAX_TEAMS * PLAYERS_PER_TEAM;
        
        public const int MAX_ABILITY_SLOTS = 4;
        public const int MAX_EQUIPMENT_SLOTS = 6;
        public const int MAX_TECH_SLOTS = 3;
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
}
