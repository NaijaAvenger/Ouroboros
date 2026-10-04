namespace Ouroboros.Interaction
{
    /// <summary>
    /// Something the Hacker's System Hack can target: doors, terminals, loot objectives, turrets.
    /// Called on the state authority only.
    /// </summary>
    public interface IHackable
    {
        void OnHacked(Core.TeamID byTeam, float duration);
    }

    /// <summary>
    /// Security equipment (cameras, alarms, sensors) that can be disabled for a duration by
    /// Hacker/Saboteur abilities and EMP effects.
    /// </summary>
    public interface ISecurityDevice
    {
        bool IsDisabled { get; }
        void Disable(float duration);
    }

    /// <summary>
    /// Reinforced doors and walls the Demolitions breaching charge can open permanently.
    /// </summary>
    public interface IBreachable
    {
        bool IsBreached { get; }
        void Breach(Core.TeamID byTeam);
    }
}
