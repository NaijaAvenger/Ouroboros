using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    public enum VaultLockType
    {
        /// <summary>Find the matching coloured keycard somewhere in the facility.</summary>
        Keycard,
        /// <summary>Swipe an ID card looted from a dead player of another team.</summary>
        PlayerCard,
        /// <summary>Enter the 4-digit code (found on a code note). A wrong code triggers an immediate lockdown.</summary>
        Passcode,
        /// <summary>Overriding it slams the facility into lockdown; survive the timer and the door opens.</summary>
        Lockdown
    }

    [System.Flags]
    public enum KeycardColor
    {
        None = 0,
        Red = 1,
        Blue = 2,
        Green = 4,
        Yellow = 8
    }

    /// <summary>
    /// Exterior door of a vault room. Each vault uses one of four lock mechanisms (see
    /// <see cref="VaultLockType"/>); once open, the interior <see cref="GameMode.LootObjective"/> gated by this
    /// door can be cracked. Hacker's System Hack reveals a passcode / shortens a lockdown instead of opening.
    /// </summary>
    public class VaultDoor : NetworkBehaviour, IHackable
    {
        [Header("Vault")]
        [SerializeField] private string vaultName = "Vault";
        [SerializeField] private VaultLockType lockType = VaultLockType.Keycard;
        [SerializeField] private KeycardColor keycardColor = KeycardColor.Red;
        [SerializeField] private int enemyCardsRequired = 1;
        [SerializeField] private float lockdownSurviveSeconds = 45f;
        [Tooltip("Seconds the keypad refuses input after a wrong code.")]
        [SerializeField] private float wrongCodeLockoutSeconds = 15f;
        [SerializeField] private float interactRadius = 2.5f;

        [Header("Parts")]
        [SerializeField] private GameObject doorVisual;
        [SerializeField] private UnityEngine.AI.NavMeshObstacle navObstacle;

        [Networked] public NetworkBool IsOpen { get; set; }
        [Networked] public int Passcode { get; set; }
        [Networked] public NetworkBool LockdownActive { get; set; }
        [Networked] public TickTimer LockdownTimer { get; set; }
        [Networked] public TickTimer LockoutTimer { get; set; }
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS)] public NetworkArray<NetworkBool> CodeKnownByTeam => default;

        public static readonly List<VaultDoor> All = new List<VaultDoor>();
        public static event System.Action<VaultDoor, Core.TeamID> Opened;
        public static event System.Action<VaultDoor, Core.TeamID> WrongCode;
        public static event System.Action<VaultDoor> LockdownStarted;

        public string VaultName => vaultName;
        public VaultLockType LockType => lockType;
        public KeycardColor RequiredKeycard => keycardColor;
        public int EnemyCardsRequired => enemyCardsRequired;
        public float InteractRadius => interactRadius;
        public bool IsLockedOut => LockoutTimer.IsRunning && !LockoutTimer.Expired(Runner);
        public float? LockdownRemaining => LockdownActive ? LockdownTimer.RemainingTime(Runner) : null;
        public bool TeamKnowsCode(Core.TeamID team) { int i = Core.TeamUtil.ToIndex(team); return i >= 0 && i < Core.GameConstants.MAX_TEAMS && CodeKnownByTeam[i]; }

        private readonly List<Network.NetworkPlayer> interacting = new List<Network.NetworkPlayer>();

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
            if (Object.HasStateAuthority && Passcode == 0) Passcode = Random.Range(1000, 10000);
            ApplyVisual();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        public override void Render()
        {
            ApplyVisual();
        }

        private void ApplyVisual()
        {
            if (doorVisual != null && doorVisual.activeSelf == IsOpen) doorVisual.SetActive(!IsOpen);
            if (navObstacle == null && doorVisual != null) navObstacle = doorVisual.GetComponent<UnityEngine.AI.NavMeshObstacle>();
            if (navObstacle != null && navObstacle.enabled == IsOpen) navObstacle.enabled = !IsOpen;
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || IsOpen) return;

            if (LockdownActive && LockdownTimer.Expired(Runner))
            {
                Open(Core.TeamID.None, "lockdown survived");
                return;
            }

            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm == null || gm.Object == null || !gm.IsMatchLive) return;
            if (((int)Runner.Tick) % Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS != 0) return;

            interacting.Clear();
            float rSq = interactRadius * interactRadius;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p.Object == null || !p.IsActiveInMatch) continue;
                if ((p.transform.position - transform.position).sqrMagnitude > rSq) continue;
                if (p.HasStatus(Core.StatusFlags.Interacting) && !p.HasStatus(Core.StatusFlags.EMPDisabled)) interacting.Add(p);
            }
            if (interacting.Count == 0) return;

            switch (lockType)
            {
                case VaultLockType.Keycard:
                    foreach (var p in interacting)
                    {
                        if (p.HasKeycard(keycardColor))
                        {
                            p.ConsumeKeycard(keycardColor);
                            Open(p.Team, $"{keycardColor} keycard");
                            return;
                        }
                    }
                    break;

                case VaultLockType.PlayerCard:
                    foreach (var p in interacting)
                    {
                        if (p.EnemyCards >= enemyCardsRequired)
                        {
                            p.ConsumeEnemyCards(enemyCardsRequired);
                            Open(p.Team, "enemy ID card");
                            return;
                        }
                    }
                    break;

                case VaultLockType.Lockdown:
                    if (!LockdownActive)
                    {
                        LockdownActive = true;
                        LockdownTimer = TickTimer.CreateFromSeconds(Runner, lockdownSurviveSeconds);
                        GameMode.AlarmSystem.Raise(100f, vaultName + " override");
                        RPC_LockdownStarted();
                    }
                    break;

                case VaultLockType.Passcode:
                    // Entered through KeypadUI → SubmitCode. Holding Interact here does nothing.
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Passcode

        /// <summary>Client entry point from the keypad UI.</summary>
        public void SubmitCode(int code)
        {
            if (Object.HasStateAuthority)
            {
                var local = Network.NetworkPlayer.Resolve(Runner.LocalPlayer);
                ValidateCode(code, local);
            }
            else
            {
                RPC_SubmitCode(code);
            }
        }

        [Rpc(RpcSources.All, RpcTargets.StateAuthority)]
        private void RPC_SubmitCode(int code, RpcInfo info = default)
        {
            ValidateCode(code, Network.NetworkPlayer.Resolve(info.Source));
        }

        private void ValidateCode(int code, Network.NetworkPlayer by)
        {
            if (IsOpen || lockType != VaultLockType.Passcode || IsLockedOut) return;
            if (by == null || by.Object == null || !by.IsActiveInMatch) return;
            if ((by.transform.position - transform.position).sqrMagnitude > interactRadius * interactRadius * 2.5f) return;

            if (code == Passcode)
            {
                Open(by.Team, "passcode");
            }
            else
            {
                LockoutTimer = TickTimer.CreateFromSeconds(Runner, wrongCodeLockoutSeconds);
                GameMode.AlarmSystem.Raise(100f, vaultName + " wrong code");
                RPC_WrongCode(by.Team);
            }
        }

        /// <summary>Called by a CodeNote (or a Hacker's hack) to reveal the code to a team.</summary>
        public void RevealCodeToTeam(Core.TeamID team)
        {
            if (!Object.HasStateAuthority) return;
            int i = Core.TeamUtil.ToIndex(team);
            if (i < 0 || i >= Core.GameConstants.MAX_TEAMS) return;
            CodeKnownByTeam.Set(i, true);
            RPC_CodeRevealed(team);
        }

        public void OnHacked(Core.TeamID byTeam, float duration)
        {
            if (!Object.HasStateAuthority || IsOpen) return;
            switch (lockType)
            {
                case VaultLockType.Passcode:
                    RevealCodeToTeam(byTeam);
                    break;
                case VaultLockType.Lockdown:
                    if (LockdownActive)
                    {
                        float remaining = LockdownTimer.RemainingTime(Runner) ?? 0f;
                        LockdownTimer = TickTimer.CreateFromSeconds(Runner, remaining * 0.7f);
                    }
                    break;
            }
        }

        // ------------------------------------------------------------------

        private void Open(Core.TeamID by, string how)
        {
            IsOpen = true;
            LockdownActive = false;
            RPC_Opened(by, how);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Opened(Core.TeamID team, string how)
        {
            Debug.Log($"[VaultDoor] {vaultName} opened by {team} via {how}");
            Opened?.Invoke(this, team);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_WrongCode(Core.TeamID team)
        {
            Debug.Log($"[VaultDoor] {vaultName}: WRONG CODE by {team} - LOCKDOWN");
            WrongCode?.Invoke(this, team);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_LockdownStarted()
        {
            Debug.Log($"[VaultDoor] {vaultName}: override started - survive {lockdownSurviveSeconds:0}s");
            LockdownStarted?.Invoke(this);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_CodeRevealed(Core.TeamID team)
        {
            Debug.Log($"[VaultDoor] {vaultName}: passcode revealed to {team}");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.white;
            Gizmos.DrawWireSphere(transform.position, interactRadius);
        }
    }
}
