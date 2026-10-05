using System;
using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Network
{
    /// <summary>
    /// Base networked player class using Photon Fusion 2.
    /// Handles network synchronization for player state and class abilities.
    ///
    /// v0.2: this component is the single source of truth for health, stamina, status effects,
    /// cooldowns and loot. The attached <see cref="Core.BasePlayerClass"/> only supplies stats and
    /// ability behaviour through <see cref="Core.IAbilityContext"/>. Class components are created
    /// locally on every peer from the replicated <see cref="ClassType"/> (so late joiners get them),
    /// but abilities only execute on the state authority.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour, Core.IAbilityContext, Combat.IDamageable
    {
        /// <summary>Live registry of every spawned player on this peer. Cheap radius queries iterate this.</summary>
        public static readonly List<NetworkPlayer> All = new List<NetworkPlayer>();

        private const int STATUS_TIMER_SLOTS = 16;

        [Header("Network Configuration")]
        [Networked] public PlayerRef PlayerRef { get; set; }
        /// <summary>Team assigned by the server at spawn. Prefer <see cref="Team"/>, which also falls back to TeamManager.</summary>
        [Networked] public Core.TeamID AssignedTeam { get; set; }
        [Networked] public Core.PlayerClassType ClassType { get; set; }
        [Networked] public float Health { get; set; }
        [Networked] public float Stamina { get; set; }
        [Networked] public NetworkBool IsAlive { get; set; }
        [Networked] public NetworkBool IsExtracted { get; set; }
        [Networked] public Core.StatusFlags Status { get; set; }
        [Networked] public int CarriedLoot { get; set; }
        [Networked] public int Kills { get; set; }
        [Networked] public int Deaths { get; set; }
        [Networked] public int RespawnsUsed { get; set; }
        [Networked] public TickTimer RespawnTimer { get; set; }
        [Networked] public float LookYaw { get; set; }
        [Networked] public float LookPitch { get; set; }
        [Networked] public NetworkString<_32> DisplayName { get; set; }
        /// <summary>Bitmask of <see cref="Interaction.KeycardColor"/> carried (v0.7).</summary>
        [Networked] public int KeycardMask { get; set; }
        /// <summary>ID cards looted from dead players of other teams (v0.7).</summary>
        [Networked] public int EnemyCards { get; set; }

        [Networked, Capacity(Core.GameConstants.MAX_ABILITY_SLOTS)]
        public NetworkArray<TickTimer> AbilityCooldowns => default;

        /// <summary>One expiry timer per <see cref="Core.StatusFlags"/> bit.</summary>
        [Networked, Capacity(STATUS_TIMER_SLOTS)]
        private NetworkArray<TickTimer> StatusTimers => default;

        [Networked] private TickTimer StaminaRegenDelay { get; set; }
        [Networked] private TickTimer BurnTimer { get; set; }
        [Networked] private float BurnDamagePerSecond { get; set; }
        [Networked] private PlayerRef BurnAttacker { get; set; }

        [Header("Player Components")]
        [SerializeField] private Transform playerModel;
        [SerializeField] private float eyeHeight = 1.6f;

        private Core.BasePlayerClass currentClass;
        private Equipment.NetworkLoadout loadout; // optional (v0.4)
        private ChangeDetector changeDetector;

        /// <summary>Fired on every peer when the class component is (re)created.</summary>
        public event Action<NetworkPlayer, Core.BasePlayerClass> ClassChanged;
        /// <summary>Fired on every peer when <see cref="IsAlive"/> flips to false.</summary>
        public event Action<NetworkPlayer> Died;
        /// <summary>Fired on every peer when <see cref="IsAlive"/> flips back to true.</summary>
        public event Action<NetworkPlayer> Respawned;
        /// <summary>Fired on every peer when <see cref="Status"/> changes.</summary>
        public event Action<NetworkPlayer, Core.StatusFlags> StatusChanged;

        /// <summary>Fired on every peer when any player dies (victim, killer or <c>PlayerRef.None</c>). Drives kill feeds (v0.3).</summary>
        public static event Action<NetworkPlayer, PlayerRef> PlayerKilled;
        /// <summary>Fired on every peer when any player extracts (v0.3).</summary>
        public static event Action<NetworkPlayer> PlayerExtracted;
        /// <summary>Fired on every peer when a player successfully uses an ability (player, slot) (v0.3).</summary>
        public static event Action<NetworkPlayer, int> AbilityUsed;
        /// <summary>Fired on every peer when this player's replicated health drops (player, amount lost) (v0.3).</summary>
        public event Action<NetworkPlayer, float> Damaged;

        private float lastRenderedHealth = -1f;

        public Core.BasePlayerClass CurrentClass => currentClass;
        public bool IsLocalPlayer => Object != null && Object.HasInputAuthority;
        /// <summary>Alive, not extracted: still participating in the match.</summary>
        public bool IsActiveInMatch => IsAlive && !IsExtracted;
        public float MaxHealth => currentClass != null ? currentClass.MaxHealth : 100f;
        public float MaxStamina => currentClass != null ? currentClass.MaxStamina : 100f;
        public float MovementSpeed => currentClass != null ? currentClass.MovementSpeed : 5f;
        public float SprintSpeed => currentClass != null ? currentClass.SprintSpeed : 7f;
        public float OutgoingDamageMultiplier =>
            (currentClass != null ? currentClass.OutgoingDamageMultiplier : 1f) * (loadout != null ? loadout.OutgoingDamageMultiplier : 1f);
        public Equipment.NetworkLoadout Loadout => loadout != null ? loadout : (loadout = GetComponent<Equipment.NetworkLoadout>());

        /// <summary>Movement multiplier from carried loot and a carried case (v0.5). Read on every peer (prediction-safe).</summary>
        public float CarryWeightSpeedMultiplier
        {
            get
            {
                var gm = GameMode.ExtractionHeistGameMode.Instance;
                if (gm == null) return 1f;
                var cfg = gm.Config;
                float loot = cfg.lootWeightReference > 0 ? Mathf.Clamp01((float)CarriedLoot / cfg.lootWeightReference) : 0f;
                float m = 1f - cfg.lootWeightMaxSlowdown * loot;
                if (HasStatus(Core.StatusFlags.Encumbered)) m *= cfg.caseCarrySpeedMultiplier;
                return Mathf.Clamp(m, 0.3f, 1f);
            }
        }

        // ---- IAbilityContext / IDamageable ----
        public Core.TeamID Team
        {
            get
            {
                if (AssignedTeam != Core.TeamID.None) return AssignedTeam;
                var tm = TeamManager.Instance;
                return tm != null && Object != null ? tm.GetPlayerTeam(Object.InputAuthority) : Core.TeamID.None;
            }
        }
        public bool HasStateAuthority => Object != null && Object.HasStateAuthority;
        public Transform Transform => transform;
        public Vector3 Position => transform.position;
        public Vector3 EyePosition => transform.position + Vector3.up * eyeHeight;
        public Vector3 AimDirection => Quaternion.Euler(LookPitch, LookYaw, 0f) * Vector3.forward;
        bool Core.IAbilityContext.IsAlive => IsAlive;
        bool Combat.IDamageable.IsAlive => IsAlive;

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
            loadout = GetComponent<Equipment.NetworkLoadout>();

            changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);

            if (PlayerRef == default && Object != null)
            {
                // Spawner may not have set it explicitly
                if (Object.HasStateAuthority) PlayerRef = Object.InputAuthority;
            }

            // Late joiners (and the spawner itself) build the class component from the replicated type.
            EnsureClassComponent();

            if (Object.HasStateAuthority)
            {
                IsAlive = true;
                IsExtracted = false;
                Status = Core.StatusFlags.None;
                // [v0.1] Health = 100f; Stamina = 100f;  // magic numbers; now derived from the class
                Health = MaxHealth;
                Stamina = MaxStamina;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            if (!IsAlive)
            {
                if (RespawnTimer.IsRunning && RespawnTimer.Expired(Runner))
                {
                    RespawnTimer = TickTimer.None;
                    GameSessionManager.Instance?.RespawnPlayer(this);
                }
                return;
            }

            if (IsExtracted) return;

            TickStatusTimers();
            TickBurning();
            TickStamina();

            if (currentClass != null)
            {
                currentClass.UpdateClass(Runner.DeltaTime);
            }
        }

        public override void Render()
        {
            if (changeDetector == null) return;

            foreach (var change in changeDetector.DetectChanges(this))
            {
                switch (change)
                {
                    case nameof(ClassType):
                        EnsureClassComponent();
                        break;
                    case nameof(IsAlive):
                        if (IsAlive) Respawned?.Invoke(this); else Died?.Invoke(this);
                        break;
                    case nameof(Status):
                        StatusChanged?.Invoke(this, Status);
                        break;
                    case nameof(Health):
                        if (lastRenderedHealth >= 0f && Health < lastRenderedHealth)
                        {
                            Damaged?.Invoke(this, lastRenderedHealth - Health);
                        }
                        lastRenderedHealth = Health;
                        break;
                }
            }
        }

        // ------------------------------------------------------------------
        // Class management
        // ------------------------------------------------------------------

        /// <summary>
        /// Assigns a class to this networked player. State authority only; every peer then builds the
        /// matching component via change detection.
        /// </summary>
        public void AssignClass(Core.PlayerClassType classType)
        {
            if (Object.HasStateAuthority)
            {
                ClassType = classType;
                EnsureClassComponent();
                // [v0.1] RPC_AssignClass(classType); // superseded: RPCs are not delivered to late joiners
                Health = MaxHealth;
                Stamina = MaxStamina;
            }
        }

        /// <summary>Requests a class from the input-authority client (e.g. a lobby class picker).</summary>
        public void RequestClass(Core.PlayerClassType classType)
        {
            if (Object.HasStateAuthority) AssignClass(classType);
            else if (Object.HasInputAuthority) RPC_RequestClass(classType);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_RequestClass(Core.PlayerClassType classType)
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm != null && !gm.AllowClassChange) return;
            AssignClass(classType);
        }

        private void EnsureClassComponent()
        {
            Type wanted = ClassComponentType(ClassType);

            if (currentClass != null)
            {
                if (wanted != null && currentClass.GetType() == wanted) return;
                Destroy(currentClass);
                currentClass = null;
            }

            if (wanted == null) return;

            currentClass = (Core.BasePlayerClass)gameObject.AddComponent(wanted);
            currentClass.Initialize(this);

            // v0.3: designer overrides from the active ClassRegistry (same asset on every peer)
            var registry = Data.ClassRegistry.Active;
            var data = registry != null ? registry.Get(ClassType) : null;
            if (data != null) currentClass.ApplyClassData(data);

            currentClass.OnClassSelected();
            ClassChanged?.Invoke(this, currentClass);
        }

        /// <summary>Maps the enum to a component type. Add new classes here (one line) — see IMPLEMENTATION_GUIDE.md.</summary>
        public static Type ClassComponentType(Core.PlayerClassType classType)
        {
            switch (classType)
            {
                case Core.PlayerClassType.Hacker:      return typeof(Classes.HackerClass);
                case Core.PlayerClassType.Saboteur:    return typeof(Classes.SaboteurClass);
                case Core.PlayerClassType.Demolitions: return typeof(Classes.DemolitionsClass);
                case Core.PlayerClassType.Agent:       return typeof(Classes.AgentClass);
                default: return null;
            }
        }

        // [v0.1] [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        // [v0.1] private void RPC_AssignClass(Core.PlayerClassType classType)
        // [v0.1] {
        // [v0.1]     ... AddComponent switch ...
        // [v0.1]     Health = currentClass.MaxHealth;   // BUG: wrote [Networked] properties on clients
        // [v0.1]     Stamina = currentClass.MaxStamina;
        // [v0.1] }

        // ------------------------------------------------------------------
        // Abilities
        // ------------------------------------------------------------------

        /// <summary>
        /// Triggers a class ability. Executes directly when this peer has state authority (host player,
        /// shared mode, or the input pipeline on the server); otherwise sends an RPC to the authority.
        /// </summary>
        public bool UseAbility(int abilityIndex)
        {
            if (Object.HasStateAuthority)
            {
                return ExecuteAbility(abilityIndex);
            }
            if (Object.HasInputAuthority && currentClass != null)
            {
                RPC_UseAbility(abilityIndex);
            }
            return false;
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_UseAbility(int abilityIndex)
        {
            ExecuteAbility(abilityIndex);
        }

        private bool ExecuteAbility(int abilityIndex)
        {
            if (currentClass == null || !IsActiveInMatch) return false;
            if (HasStatus(Core.StatusFlags.EMPDisabled)) return false;

            // v0.3: abilities are locked until the match is live unless the config allows them
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm != null && gm.Object != null && !gm.IsMatchLive && !gm.Config.allowAbilitiesBeforeMatch) return false;

            bool used = currentClass.UseAbility(abilityIndex);
            if (used) RPC_OnAbilityUsed(abilityIndex);
            return used;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OnAbilityUsed(int slot)
        {
            AbilityUsed?.Invoke(this, slot);
        }

        public bool IsAbilityReady(int slot)
        {
            if (slot < 0 || slot >= Core.GameConstants.MAX_ABILITY_SLOTS) return false;
            return AbilityCooldowns[slot].ExpiredOrNotRunning(Runner);
        }

        public void StartAbilityCooldown(int slot, float seconds)
        {
            if (!Object.HasStateAuthority) return;
            if (slot < 0 || slot >= Core.GameConstants.MAX_ABILITY_SLOTS) return;
            AbilityCooldowns.Set(slot, TickTimer.CreateFromSeconds(Runner, seconds));
        }

        public float AbilityCooldownRemaining(int slot)
        {
            if (slot < 0 || slot >= Core.GameConstants.MAX_ABILITY_SLOTS) return 0f;
            return AbilityCooldowns[slot].RemainingTime(Runner) ?? 0f;
        }

        public TickTimer CreateTimer(float seconds) => TickTimer.CreateFromSeconds(Runner, seconds);
        public bool TimerExpired(TickTimer timer) => timer.ExpiredOrNotRunning(Runner);

        // ------------------------------------------------------------------
        // Stamina
        // ------------------------------------------------------------------

        public bool TrySpendStamina(float amount)
        {
            if (!Object.HasStateAuthority) return false;
            if (amount <= 0f) return true;
            if (Stamina < amount) return false;

            Stamina -= amount;
            StaminaRegenDelay = TickTimer.CreateFromSeconds(Runner, Core.GameConstants.STAMINA_REGEN_DELAY);
            return true;
        }

        /// <summary>Drains stamina for continuous actions (sprinting). Returns the amount actually drained.</summary>
        public float DrainStamina(float amount)
        {
            if (!Object.HasStateAuthority || amount <= 0f) return 0f;
            float drained = Mathf.Min(Stamina, amount);
            Stamina -= drained;
            StaminaRegenDelay = TickTimer.CreateFromSeconds(Runner, Core.GameConstants.STAMINA_REGEN_DELAY);
            return drained;
        }

        private void TickStamina()
        {
            if (HasStatus(Core.StatusFlags.Sprinting)) return;
            if (!StaminaRegenDelay.ExpiredOrNotRunning(Runner)) return;
            if (Stamina < MaxStamina)
            {
                Stamina = Mathf.Min(Stamina + Core.GameConstants.STAMINA_REGEN_PER_SECOND * Runner.DeltaTime, MaxStamina);
            }
        }

        // ------------------------------------------------------------------
        // Status effects
        // ------------------------------------------------------------------

        public bool HasStatus(Core.StatusFlags flag) => (Status & flag) != 0;

        public void SetStatus(Core.StatusFlags flag, bool enabled)
        {
            if (!Object.HasStateAuthority) return;
            Status = enabled ? (Status | flag) : (Status & ~flag);
            if (!enabled)
            {
                int bit = StatusBit(flag);
                if (bit >= 0 && bit < STATUS_TIMER_SLOTS) StatusTimers.Set(bit, TickTimer.None);
            }
        }

        /// <summary>Applies a flag that auto-clears after <paramref name="duration"/> seconds (0 clears immediately).</summary>
        public void ApplyTimedStatus(Core.StatusFlags flag, float duration)
        {
            if (!Object.HasStateAuthority) return;
            int bit = StatusBit(flag);
            if (bit < 0 || bit >= STATUS_TIMER_SLOTS) return;

            if (duration <= 0f)
            {
                SetStatus(flag, false);
                return;
            }

            // Keep the longer of an existing and the new duration.
            float existing = StatusTimers[bit].RemainingTime(Runner) ?? 0f;
            if (duration > existing)
            {
                StatusTimers.Set(bit, TickTimer.CreateFromSeconds(Runner, duration));
            }
            Status |= flag;
        }

        private void TickStatusTimers()
        {
            for (int bit = 0; bit < STATUS_TIMER_SLOTS; bit++)
            {
                TickTimer timer = StatusTimers[bit];
                if (timer.IsRunning && timer.Expired(Runner))
                {
                    StatusTimers.Set(bit, TickTimer.None);
                    Status &= ~(Core.StatusFlags)(1 << bit);
                }
            }
        }

        private static int StatusBit(Core.StatusFlags flag)
        {
            int value = (int)flag;
            if (value <= 0) return -1;
            int bit = 0;
            while ((value & 1) == 0) { value >>= 1; bit++; }
            return bit;
        }

        public void ApplyBurning(float duration, float damagePerSecond, NetworkPlayer attacker)
        {
            if (!Object.HasStateAuthority || !IsAlive) return;
            BurnTimer = TickTimer.CreateFromSeconds(Runner, duration);
            BurnDamagePerSecond = Mathf.Max(BurnDamagePerSecond, damagePerSecond);
            BurnAttacker = attacker != null ? attacker.PlayerRef : default;
            Status |= Core.StatusFlags.Burning;
        }

        private void TickBurning()
        {
            if (!HasStatus(Core.StatusFlags.Burning)) return;
            if (BurnTimer.ExpiredOrNotRunning(Runner))
            {
                Status &= ~Core.StatusFlags.Burning;
                BurnDamagePerSecond = 0f;
                return;
            }
            NetworkPlayer attacker = Resolve(BurnAttacker);
            ApplyDamage(BurnDamagePerSecond * Runner.DeltaTime, attacker, ignoreShield: true);
        }

        // ------------------------------------------------------------------
        // Damage / death / respawn
        // ------------------------------------------------------------------

        /// <summary>
        /// Applies damage to the networked player. Honors friendly-fire rules, class damage modifiers
        /// and the attacker's outgoing multiplier. State authority only.
        /// </summary>
        public void ApplyDamage(float amount, NetworkPlayer attacker)
        {
            ApplyDamage(amount, attacker, ignoreShield: false);
        }

        private void ApplyDamage(float amount, NetworkPlayer attacker, bool ignoreShield)
        {
            if (!Object.HasStateAuthority) return;
            if (!IsAlive || IsExtracted || amount <= 0f) return;

            if (attacker != null && attacker != this && attacker.Team == Team)
            {
                var gm = GameMode.ExtractionHeistGameMode.Instance;
                if (gm == null || !gm.Config.allowFriendlyFire) return;
            }

            float damage = amount;
            if (!ignoreShield && currentClass != null)
            {
                damage = currentClass.ModifyIncomingDamage(damage);
            }
            if (attacker != null && attacker != this)
            {
                damage *= attacker.OutgoingDamageMultiplier;
            }
            if (!ignoreShield && loadout != null)
            {
                damage *= loadout.DamageTakenMultiplier; // v0.4: armor
            }

            // [v0.1] currentClass.TakeDamage(damage);            // BUG: class kept a second health value
            // [v0.1] Health = Mathf.Max(0, Health - damage);     // ...and the shield modifier was ignored here
            Health = Mathf.Max(0f, Health - damage);

            if (Health <= 0f)
            {
                Die(attacker);
            }
        }

        /// <summary>Legacy entry point kept for callers that have no attacker reference.</summary>
        public void TakeDamage(float damage)
        {
            ApplyDamage(damage, null);
        }

        public void Heal(float amount)
        {
            if (!Object.HasStateAuthority || !IsAlive || amount <= 0f) return;
            Health = Mathf.Min(MaxHealth, Health + amount);
        }

        private void Die(NetworkPlayer killer)
        {
            IsAlive = false;
            Deaths++;
            Status = Core.StatusFlags.None;
            for (int i = 0; i < STATUS_TIMER_SLOTS; i++) StatusTimers.Set(i, TickTimer.None);
            BurnTimer = TickTimer.None;

            currentClass?.OnOwnerDied();
            KeycardMask = 0;   // v0.7: keys and looted cards are lost on death (your own ID card drops via the game mode)
            EnemyCards = 0;

            if (killer != null && killer != this)
            {
                killer.Kills++;
            }

            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm != null)
            {
                gm.OnPlayerDied(this, killer);
                if (gm.CanRespawn(this))
                {
                    RespawnTimer = TickTimer.CreateFromSeconds(Runner, gm.Config.respawnDelay);
                }
            }

            RPC_OnPlayerDeath(killer != null ? killer.PlayerRef : default);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OnPlayerDeath(PlayerRef killer)
        {
            Debug.Log($"[NetworkPlayer] Player {PlayerRef} ({DisplayName}) died" + (killer != default ? $" to {killer}" : ""));
            PlayerKilled?.Invoke(this, killer);
        }

        /// <summary>Restores state for a respawn. Called by <see cref="GameSessionManager"/> after teleporting.</summary>
        public void ResetForRespawn()
        {
            if (!Object.HasStateAuthority) return;
            RespawnsUsed++;
            Health = MaxHealth;
            Stamina = MaxStamina;
            Status = Core.StatusFlags.None;
            IsAlive = true;
            RespawnTimer = TickTimer.None;
            currentClass?.OnOwnerRespawned();
        }

        // ------------------------------------------------------------------
        // Loot / extraction
        // ------------------------------------------------------------------

        /// <summary>Adds loot to the carried pool. Returns the amount actually accepted (cap-limited).</summary>
        public int AddLoot(int amount)
        {
            if (!Object.HasStateAuthority || amount <= 0) return 0;
            int accepted = Mathf.Min(amount, Core.GameConstants.MAX_CARRIED_LOOT - CarriedLoot);
            CarriedLoot += Mathf.Max(0, accepted);
            return accepted;
        }

        /// <summary>Removes and returns all carried loot (death drop / extraction banking).</summary>
        public int TakeAllLoot()
        {
            if (!Object.HasStateAuthority) return 0;
            int loot = CarriedLoot;
            CarriedLoot = 0;
            return loot;
        }

        /// <summary>Marks this player as extracted: out of the match, untargetable, loot banked by the game mode.</summary>
        public void MarkExtracted()
        {
            if (!Object.HasStateAuthority || IsExtracted) return;
            IsExtracted = true;
            Status = Core.StatusFlags.Extracted;
            RespawnTimer = TickTimer.None;
            RPC_OnExtracted();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OnExtracted()
        {
            Debug.Log($"[NetworkPlayer] Player {PlayerRef} ({DisplayName}) extracted");
            // [v0.2] if (playerModel != null) playerModel.gameObject.SetActive(false); // now handled by PlayerPresentation
            PlayerExtracted?.Invoke(this);
        }

        // ------------------------------------------------------------------
        // Vault keys (v0.7)

        public bool HasKeycard(Interaction.KeycardColor color) => (KeycardMask & (int)color) != 0;
        public void AddKeycard(Interaction.KeycardColor color) { if (Object.HasStateAuthority) KeycardMask |= (int)color; }
        public void ConsumeKeycard(Interaction.KeycardColor color) { if (Object.HasStateAuthority) KeycardMask &= ~(int)color; }
        public void AddEnemyCard() { if (Object.HasStateAuthority) EnemyCards++; }
        public void ConsumeEnemyCards(int count) { if (Object.HasStateAuthority) EnemyCards = Mathf.Max(0, EnemyCards - count); }

        // ------------------------------------------------------------------
        // Static helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Players within <paramref name="radius"/> of <paramref name="origin"/>. Pass <c>TeamID.None</c> as
        /// <paramref name="excludeTeam"/> to include everyone (callers then filter by <see cref="Team"/>).
        /// </summary>
        public static List<NetworkPlayer> FindPlayersInRadius(Vector3 origin, float radius, Core.TeamID excludeTeam, bool aliveOnly)
        {
            var results = new List<NetworkPlayer>();
            float radiusSq = radius * radius;
            for (int i = 0; i < All.Count; i++)
            {
                var p = All[i];
                if (p == null || p.Object == null) continue;
                if (aliveOnly && !p.IsActiveInMatch) continue;
                if (excludeTeam != Core.TeamID.None && p.Team == excludeTeam) continue;
                if ((p.transform.position - origin).sqrMagnitude > radiusSq) continue;
                results.Add(p);
            }
            return results;
        }

        public static NetworkPlayer Resolve(Core.IAbilityContext context) => context as NetworkPlayer;

        public static NetworkPlayer Resolve(PlayerRef player)
        {
            if (player == default) return null;
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i] != null && All[i].PlayerRef == player) return All[i];
            }
            return null;
        }
    }
}
