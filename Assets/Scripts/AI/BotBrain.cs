using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Fusion;

namespace Ouroboros.AI
{
    /// <summary>
    /// Plays a crew member (v0.10, milestone M2). A bot is an ordinary player object with no input authority:
    /// instead of a person's input, <c>Player.PlayerController</c> asks this component for a
    /// <see cref="Core.NetworkInputData"/> every tick on the state authority. Everything downstream is the same
    /// code a human goes through, so bots move, shoot, crack vaults, carry loot, die, respawn and extract under
    /// exactly the same rules, and guards treat them like any other intruder.
    ///
    /// What it does, in priority order:
    ///   1. Fight what it can see: rival players, and guards that have noticed someone.
    ///   2. With a human on its crew: stay with them, help at the vault they are at, leave when they leave.
    ///   3. Otherwise, once the getaway is called: carry loot to an exit, or go hunt at the central one.
    ///   4. Pick up loot lying nearby.
    ///   5. Work the nearest vault it knows how to open (it handles all four lock types).
    ///
    /// It is a placeholder opponent, not a clever one: no cover use, no grenades on purpose, no coordination
    /// beyond following a human. Added at runtime by <c>Network.GameSessionManager</c>; server-side only, no
    /// networked state of its own.
    /// </summary>
    public class BotBrain : MonoBehaviour
    {
        [Tooltip("0 = slow and inaccurate, 1 = sharp. Set from GameModeConfig.botSkill when the bot is spawned.")]
        [Range(0f, 1f)] public float skill = 0.5f;

        private const float SightRange = 45f;
        private const float GuardSightRange = 30f;
        private const float ThinkSeconds = 0.2f;
        private const int MaxCorners = 32;

        private Network.NetworkPlayer me;
        private Equipment.NetworkLoadout loadout;
        private System.Random rng;

        // Aim (what goes into the input)
        private float yaw, pitch;
        private bool aimReady;
        private float aimPhase;

        // Intent, refreshed by Think()
        private bool hasGoal;
        private Vector3 goal;
        private float goalStopDistance;
        private bool interactAtGoal;
        private GameMode.LootObjective currentObjective;
        private readonly Dictionary<GameMode.LootObjective, int> avoidUntil = new Dictionary<GameMode.LootObjective, int>();

        // Navigation
        private NavMeshPath path;
        private readonly Vector3[] corners = new Vector3[MaxCorners];
        private int cornerCount, cornerIndex, repathTick;
        private Vector3 pathGoal;
        private bool pathFailed;

        // Combat
        private Network.NetworkPlayer enemyPlayer;
        private BaseAIAgent enemyAgent;
        private Interaction.SentryTurret enemyTurret; // (v0.11)
        private int nextAnswerTick;
        private bool lineOfFire;
        private int firstSeenTick, strafeUntilTick, burstUntilTick, burstPauseUntilTick, nextAbilityTick;
        private float strafeSign = 1f;
        private int abilityToPress = -1;
        private Vector3 alertPoint;
        private int alertUntilTick;
        private float lastHealth = -1f;

        // Stuck detection
        private Vector3 progressAnchor;
        private int progressCheckTick, stuckCount, jumpTick;

        private int thinkTick;
        private bool pulse;

        private NetworkRunner Runner => me != null ? me.Runner : null;
        // [v0.10] private bool HasEnemy => enemyPlayer != null || enemyAgent != null;
        private bool HasEnemy => enemyPlayer != null || enemyAgent != null || enemyTurret != null;
        private int Ticks(float seconds) => Mathf.Max(1, Mathf.CeilToInt(seconds / Runner.DeltaTime));

        private float ReactionSeconds => Mathf.Lerp(0.7f, 0.2f, skill);
        private float AimErrorDegrees => Mathf.Lerp(5f, 1f, skill);
        private float TurnRate => Mathf.Lerp(200f, 540f, skill);
        private float FireTolerance => Mathf.Lerp(7f, 3f, skill);

        private void Awake()
        {
            me = GetComponent<Network.NetworkPlayer>();
            loadout = GetComponent<Equipment.NetworkLoadout>();
            path = new NavMeshPath();
            rng = new System.Random(GetInstanceID());
            aimPhase = (float)rng.NextDouble() * 10f;
        }

        // ------------------------------------------------------------------
        // Entry point: one input per simulation tick

        /// <summary>
        /// Called by <c>PlayerController</c> on the state authority in place of <c>GetInput</c>. Returns false
        /// only when the bot cannot act at all (not spawned yet).
        /// </summary>
        public bool TryGetInput(out Core.NetworkInputData input)
        {
            input = default;
            if (me == null || me.Object == null || Runner == null) return false;

            if (!aimReady) { yaw = transform.eulerAngles.y; pitch = 0f; aimReady = true; }
            input.Yaw = yaw;
            input.Pitch = pitch;

            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm == null || gm.Object == null || !gm.IsMatchLive || !me.IsActiveInMatch)
            {
                enemyPlayer = null; enemyAgent = null; enemyTurret = null; hasGoal = false; cornerCount = 0; lastHealth = -1f;
                return true;
            }

            int tick = (int)Runner.Tick;
            if (tick >= thinkTick)
            {
                thinkTick = tick + Ticks(ThinkSeconds);
                Think(gm, tick);
            }

            BuildInput(ref input, tick);
            return true;
        }

        // ------------------------------------------------------------------
        // Deciding (a few times a second)

        private void Think(GameMode.ExtractionHeistGameMode gm, int tick)
        {
            // Being shot from somewhere unseen: turn to face it for a few seconds.
            if (lastHealth >= 0f && me.Health < lastHealth - 0.5f && (me.LastDamageOrigin - me.Position).sqrMagnitude > 1f)
            {
                alertPoint = me.LastDamageOrigin;
                alertUntilTick = tick + Ticks(3f);
            }
            lastHealth = me.Health;

            AcquireEnemy(tick);
            AnswerSigns(tick);
            ChooseGoal(gm, tick);
            UpdatePath(tick);
        }

        /// <summary>
        /// (v0.11) Jacked into a vault: a bot has no screen, so it "reads" a sign every couple of seconds and is
        /// right more often the more skilled it is. The result goes through the same rules as a person's answer.
        /// </summary>
        private void AnswerSigns(int tick)
        {
            if (!me.IsJackedIn) { nextAnswerTick = 0; return; }
            if (nextAnswerTick == 0) nextAnswerTick = tick + Ticks(Mathf.Lerp(3.2f, 1.4f, skill));
            if (tick < nextAnswerTick) return;
            nextAnswerTick = tick + Ticks(Mathf.Lerp(3.2f, 1.4f, skill) + (float)rng.NextDouble() * 0.8f);
            me.SubmitBotAnswer(rng.NextDouble() < Mathf.Lerp(0.55f, 0.9f, skill));
        }

        private void AcquireEnemy(int tick)
        {
            Vector3 eye = me.EyePosition;
            Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            bool alerted = tick < alertUntilTick || HasEnemy;
            float fov = alerted ? 360f : 150f;
            LayerMask occluders = ~0;

            var player = Perception.FindVisiblePlayer(eye, forward, SightRange, fov, occluders, me.Team);

            // Guards are only worth a fight once they are a threat: already hunting someone, or very close.
            BaseAIAgent agent = null;
            float agentSq = float.MaxValue;
            var agents = BaseAIAgent.All;
            for (int i = 0; i < agents.Count; i++)
            {
                var a = agents[i];
                if (a == null || a.Object == null || !a.IsAlive) continue;
                float dSq = (a.Position - me.Position).sqrMagnitude;
                if (dSq >= agentSq || dSq > GuardSightRange * GuardSightRange) continue;
                bool threat = a.NetworkedState == AIBehaviorState.Combat || a.NetworkedState == AIBehaviorState.Alert
                              || a.NetworkedState == AIBehaviorState.Investigating || dSq < 100f;
                if (!threat) continue;
                if (!Perception.CanSee(eye, forward, a.EyePosition, GuardSightRange, fov, occluders)) continue;
                agent = a; agentSq = dSq;
            }

            // Take whichever is closer.
            if (player != null && agent != null)
            {
                if ((player.Position - me.Position).sqrMagnitude <= agentSq) agent = null; else player = null;
            }

            // (v0.11) A sentry turret that would shoot this bot is a target too, but only when nothing that
            //         moves is in view: turrets do not chase.
            Interaction.SentryTurret turret = null;
            if (player == null && agent == null)
            {
                float turretSq = 26f * 26f;
                var turrets = Interaction.SentryTurret.All;
                for (int i = 0; i < turrets.Count; i++)
                {
                    var t = turrets[i];
                    if (t == null || t.Object == null || t.IsOffline || t.OwnerTeam == me.Team) continue;
                    float dSq = (t.Position - me.Position).sqrMagnitude;
                    if (dSq >= turretSq) continue;
                    if (!Perception.CanSee(eye, forward, t.MuzzlePosition, 26f, fov, occluders)) continue;
                    turret = t; turretSq = dSq;
                }
            }

            bool hadEnemy = HasEnemy;
            bool nowEnemy = player != null || agent != null || turret != null;
            if (hadEnemy && !nowEnemy)
            {
                // Lost them: keep looking at where they were for a moment.
                alertPoint = enemyPlayer != null ? enemyPlayer.Position + Vector3.up * 1.2f
                           : enemyAgent != null ? enemyAgent.Position
                           : enemyTurret != null ? enemyTurret.MuzzlePosition : alertPoint;
                alertUntilTick = tick + Ticks(2.5f);
            }
            if (!hadEnemy && nowEnemy)
            {
                firstSeenTick = tick;
                nextAbilityTick = tick + Ticks(1.5f + (float)rng.NextDouble() * 3f);
            }

            enemyPlayer = player;
            enemyAgent = agent;
            enemyTurret = turret;
            lineOfFire = HasEnemy; // FindVisiblePlayer / CanSee already required a clear line
        }

        private void ChooseGoal(GameMode.ExtractionHeistGameMode gm, int tick)
        {
            hasGoal = false;
            interactAtGoal = false;
            goalStopDistance = 1f;

            // 2. A human on the crew leads.
            var leader = FindHumanLeader();
            if (leader != null)
            {
                if (gm.ExtractionOpen)
                {
                    var exit = ExitContaining(leader.Position);
                    if (exit != null) { SetGoal(exit.transform.position, 1.5f, false); currentObjective = null; return; }
                }
                float toLeader = Flat(leader.Position - me.Position).magnitude;
                if (toLeader > 12f) { SetGoal(leader.Position, 4f, false); currentObjective = null; return; }
                if (TryLootGoal(12f)) return;
                if (TryObjectiveGoal(leader.Position, 30f, tick)) return;
                SetGoal(leader.Position, 5f, false);
                return;
            }

            // 3. The getaway: bank what the crew has, or go where the richest crew must come.
            if (gm.ExtractionOpen)
            {
                int crewCarried = gm.GetTeamCarried(me.Team);
                bool rich = gm.RichestTeam == me.Team || crewCarried * 2 >= gm.LootQuota; // [v0.13] gm.Config.lootQuota
                var exit = PickExit(preferCentral: crewCarried <= 0 || rich);
                if (exit != null)
                {
                    if (crewCarried <= 0 && TryLootGoal(25f)) return; // nothing to bank: grab what is lying around first
                    SetGoal(exit.transform.position, 1.5f, false);
                    currentObjective = null;
                    return;
                }
            }

            // 4. Loot on the floor nearby.
            if (TryLootGoal(30f)) return;

            // 5. A vault.
            if (TryObjectiveGoal(me.Position, 400f, tick)) return;

            // Nothing to do: drift toward the centre of the map, where things happen.
            var centre = PickExit(preferCentral: true);
            if (centre != null) SetGoal(centre.transform.position, 8f, false);
        }

        private void SetGoal(Vector3 position, float stopDistance, bool interact)
        {
            hasGoal = true;
            goal = position;
            goalStopDistance = stopDistance;
            interactAtGoal = interact;
        }

        private Network.NetworkPlayer FindHumanLeader()
        {
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p != null && p != me && p.Object != null && !p.IsBot && p.Team == me.Team && p.IsActiveInMatch) return p;
            }
            return null;
        }

        private static GameMode.ExtractionPoint ExitContaining(Vector3 position)
        {
            var exits = GameMode.ExtractionPoint.All;
            for (int i = 0; i < exits.Count; i++)
            {
                var e = exits[i];
                if (e == null || e.Object == null || !e.IsActive) continue;
                if (Flat(e.transform.position - position).sqrMagnitude <= e.ExtractionRadius * e.ExtractionRadius) return e;
            }
            return null;
        }

        private GameMode.ExtractionPoint PickExit(bool preferCentral)
        {
            GameMode.ExtractionPoint best = null;
            float bestScore = float.MaxValue;
            var exits = GameMode.ExtractionPoint.All;
            for (int i = 0; i < exits.Count; i++)
            {
                var e = exits[i];
                if (e == null || e.Object == null || !e.IsActive) continue;
                float score = Flat(e.transform.position - me.Position).magnitude;
                if (e.IsCentral == preferCentral) score -= 1000f;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        /// <summary>Loot pickups and rival ID cards within <paramref name="range"/>.</summary>
        private bool TryLootGoal(float range)
        {
            float bestSq = range * range;
            bool found = false;
            Vector3 at = default;
            bool interact = false;

            var drops = GameMode.LootDrop.All;
            for (int i = 0; i < drops.Count; i++)
            {
                var d = drops[i];
                if (d == null || d.Object == null) continue;
                if (d.ItemId > 0 ? !me.CanCarryItem : me.CarriedLoot >= Core.GameConstants.MAX_CARRIED_LOOT) continue;
                float dSq = (d.transform.position - me.Position).sqrMagnitude;
                if (dSq < bestSq) { bestSq = dSq; at = d.transform.position; found = true; interact = false; }
            }

            var cards = Interaction.IDCardDrop.All;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c == null || c.Object == null || c.OwnerTeam == me.Team) continue;
                float dSq = (c.transform.position - me.Position).sqrMagnitude;
                if (dSq < Mathf.Min(bestSq, 15f * 15f)) { bestSq = dSq; at = c.transform.position; found = true; interact = true; }
            }

            if (!found) return false;
            SetGoal(at, interact ? 1f : 0.3f, interact);
            currentObjective = null;
            return true;
        }

        /// <summary>Picks a vault near <paramref name="reference"/> and the next step toward opening and cracking it.</summary>
        private bool TryObjectiveGoal(Vector3 reference, float range, int tick)
        {
            GameMode.LootObjective best = null;
            Vector3 bestPos = default; float bestStop = 1f; bool bestInteract = false;
            float bestScore = float.MaxValue;

            var objectives = GameMode.LootObjective.All;
            for (int i = 0; i < objectives.Count; i++)
            {
                var obj = objectives[i];
                if (obj == null || obj.Object == null || !obj.IsAvailable) continue;
                if (avoidUntil.TryGetValue(obj, out int until) && tick < until) continue;
                if (!PlanFor(obj, out Vector3 pos, out float stop, out bool interact)) continue;

                float score = Flat(pos - reference).magnitude + (obj.IsGated ? 10f : 0f);
                if (score > range) continue;
                if (obj == currentObjective) score -= 15f; // stay on the job already started
                if (score < bestScore) { bestScore = score; best = obj; bestPos = pos; bestStop = stop; bestInteract = interact; }
            }

            if (best == null) { currentObjective = null; return false; }
            currentObjective = best;
            SetGoal(bestPos, bestStop, bestInteract);
            return true;
        }

        /// <summary>The next place to stand, and whether to hold Interact there, to get <paramref name="obj"/> cracked.</summary>
        private bool PlanFor(GameMode.LootObjective obj, out Vector3 pos, out float stop, out bool interact)
        {
            pos = obj.transform.position; stop = Mathf.Max(0.8f, obj.InteractRadius * 0.6f); interact = true;

            if (!obj.IsGated) return obj.IsUnlocked; // vaults that need a Hacker first are left to humans

            var door = obj.GatedBy;
            if (door == null || door.Object == null) return false;
            pos = door.transform.position;
            stop = Mathf.Max(0.8f, door.InteractRadius * 0.7f);

            switch (door.LockType)
            {
                case Interaction.VaultLockType.Lockdown:
                    interact = !door.LockdownActive; // start the override, then hold the doorway until it opens
                    return true;

                case Interaction.VaultLockType.Keycard:
                    if (me.HasKeycard(door.RequiredKeycard)) return true;
                    var cards = Interaction.KeycardPickup.All;
                    for (int i = 0; i < cards.Count; i++)
                    {
                        var k = cards[i];
                        if (k == null || k.Object == null || k.Taken || k.Color != door.RequiredKeycard) continue;
                        pos = k.transform.position; stop = Mathf.Max(0.6f, k.PickupRadius * 0.6f);
                        return true;
                    }
                    return false;

                case Interaction.VaultLockType.PlayerCard:
                    return me.EnemyCards >= door.EnemyCardsRequired;

                case Interaction.VaultLockType.Passcode:
                    if (door.TeamKnowsCode(me.Team))
                    {
                        interact = false;
                        // Close enough to reach the keypad: enter the code the crew already knows.
                        if (!door.IsLockedOut && Flat(door.transform.position - me.Position).magnitude <= stop + 0.5f) door.SubmitKnownCode(me);
                        return true;
                    }
                    var notes = Interaction.CodeNote.All;
                    for (int i = 0; i < notes.Count; i++)
                    {
                        var n = notes[i];
                        if (n == null || n.Object == null || n.Door != door) continue;
                        pos = n.transform.position; stop = Mathf.Max(0.6f, n.ReadRadius * 0.6f);
                        return true;
                    }
                    return false;
            }
            return false;
        }

        // ------------------------------------------------------------------
        // Navigation

        private void UpdatePath(int tick)
        {
            if (!hasGoal) { cornerCount = 0; return; }
            bool goalMoved = (goal - pathGoal).sqrMagnitude > 1f;
            if (!goalMoved && tick < repathTick && cornerCount > 0) return;

            repathTick = tick + Ticks(1f);
            pathGoal = goal;
            cornerCount = 0;
            pathFailed = false;

            if (!NavMesh.SamplePosition(me.Position, out NavMeshHit from, 2.5f, NavMesh.AllAreas)) return; // no NavMesh here: walk straight
            if (!NavMesh.SamplePosition(goal, out NavMeshHit to, 4f, NavMesh.AllAreas)) { pathFailed = true; return; }
            if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, path) || path.status == NavMeshPathStatus.PathInvalid)
            {
                pathFailed = true;
                return;
            }

            cornerCount = path.GetCornersNonAlloc(corners);
            cornerIndex = cornerCount > 1 ? 1 : 0;
            // A partial path ends at a locked door or a gap: fine if that is close to where we wanted to be.
            if (path.status == NavMeshPathStatus.PathPartial && cornerCount > 0
                && Flat(corners[cornerCount - 1] - goal).magnitude > Mathf.Max(3f, goalStopDistance + 2f))
            {
                pathFailed = true;
            }

            if (pathFailed && currentObjective != null)
            {
                avoidUntil[currentObjective] = tick + Ticks(15f);
                currentObjective = null;
            }
        }

        // ------------------------------------------------------------------
        // Acting (every tick)

        private void BuildInput(ref Core.NetworkInputData input, int tick)
        {
            float dt = Runner.DeltaTime;
            Vector3 pos = me.Position;
            Vector3 eye = me.EyePosition;
            Vector3 moveWorld = Vector3.zero;
            Vector3 lookPoint = eye + Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 5f;
            bool sprint = false, interact = false, fighting = HasEnemy;
            pulse = !pulse;

            if (fighting)
            {
                // [v0.10] Vector3 target = enemyPlayer != null ? enemyPlayer.Position + Vector3.up * 1.25f : enemyAgent.Position + Vector3.up * 0.2f;
                Vector3 target = enemyPlayer != null ? enemyPlayer.Position + Vector3.up * 1.25f
                               : enemyAgent != null ? enemyAgent.Position + Vector3.up * 0.2f
                               : enemyTurret.MuzzlePosition;
                lookPoint = target;

                Vector3 toward = Flat(target - pos);
                float dist = toward.magnitude;
                toward = dist > 0.01f ? toward / dist : Vector3.forward;
                Vector3 right = Vector3.Cross(Vector3.up, toward);

                if (tick >= strafeUntilTick)
                {
                    strafeSign = rng.NextDouble() < 0.5 ? -1f : 1f;
                    strafeUntilTick = tick + Ticks(0.7f + (float)rng.NextDouble() * 0.9f);
                }
                float preferred = PreferredRange();
                float advance = dist > preferred ? 1f : (dist < 4f ? -1f : 0f);
                moveWorld = right * strafeSign * 0.8f + toward * advance;
            }
            else if (hasGoal)
            {
                Vector3 toGoal = Flat(goal - pos);
                float distToGoal = toGoal.magnitude;
                if (distToGoal > goalStopDistance)
                {
                    Vector3 next = goal;
                    if (cornerCount > 0)
                    {
                        while (cornerIndex < cornerCount - 1 && Flat(corners[cornerIndex] - pos).magnitude < 0.9f) cornerIndex++;
                        next = corners[Mathf.Min(cornerIndex, cornerCount - 1)];
                    }
                    Vector3 dir = Flat(next - pos);
                    if (dir.sqrMagnitude < 0.04f) dir = toGoal;
                    moveWorld = dir.normalized;
                    sprint = distToGoal > 9f;
                    lookPoint = eye + moveWorld * 6f;
                }
                else
                {
                    interact = interactAtGoal;
                    lookPoint = new Vector3(goal.x, eye.y, goal.z);
                }
                if (tick < alertUntilTick) lookPoint = alertPoint;
            }
            else if (tick < alertUntilTick)
            {
                lookPoint = alertPoint;
            }

            // (v0.12) Floating: thrust goes where the bot looks, so look at where it wants to be (height included),
            //         and use the lift keys when the way on is mostly up or down.
            bool floating = !fighting && hasGoal && me.Controller != null && me.Controller.IsFloating;
            if (floating)
            {
                Vector3 next = cornerCount > 0 ? corners[Mathf.Min(cornerIndex, cornerCount - 1)] : goal;
                if (Flat(next - pos).magnitude < 1.5f) next = goal;
                lookPoint = next + Vector3.up * 1.2f;
                float rise = next.y + 1f - pos.y;
                if (rise > 1f) input.Buttons.Set((int)Core.InputButtons.Jump, true);
                else if (rise < -1f) input.Buttons.Set((int)Core.InputButtons.Slide, true);
            }

            Aim(lookPoint, eye, dt, fighting);
            input.Yaw = yaw;
            input.Pitch = pitch;

            // Movement axes are relative to where the bot is facing.
            if (moveWorld.sqrMagnitude > 0.0001f)
            {
                Vector3 local = Quaternion.Euler(0f, -yaw, 0f) * moveWorld.normalized;
                input.Move = new Vector2(local.x, local.z);
                if (sprint && local.z > 0.5f) input.Buttons.Set((int)Core.InputButtons.Sprint, true);
                HandleStuck(ref input, pos, tick, fighting);
            }
            else
            {
                progressAnchor = pos;
                progressCheckTick = tick + Ticks(0.6f);
                stuckCount = 0;
            }

            // [v0.10] if (interact) input.Buttons.Set((int)Core.InputButtons.Interact, true);
            // (v0.11) Interact is "hold" for doors, cards and notes, but one press jacks into a vault and the next
            //         press jacks out. So: once jacked in, leave the key alone (unless a fight starts, then press
            //         once to get off the console); otherwise hold it with a one-tick gap every so often, which
            //         gives the vault its press without dropping the hold for anything else.
            if (me.IsJackedIn)
            {
                if (fighting && pulse) input.Buttons.Set((int)Core.InputButtons.Interact, true);
            }
            else if (interact && tick % 12 != 0)
            {
                input.Buttons.Set((int)Core.InputButtons.Interact, true);
            }
            if (fighting) Shoot(ref input, lookPoint, eye, tick);
            else MaintainWeapon(ref input);
        }

        private void Aim(Vector3 lookPoint, Vector3 eye, float dt, bool fighting)
        {
            Vector3 dir = lookPoint - eye;
            if (dir.sqrMagnitude < 0.01f) return;
            dir.Normalize();

            float wantYaw = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;
            float wantPitch = -Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;

            if (fighting)
            {
                // A slow wobble rather than per-shot noise: the aim drifts around the target the way a hand does.
                float t = Time.time + aimPhase;
                wantYaw += Mathf.Sin(t * 1.7f) * AimErrorDegrees;
                wantPitch += Mathf.Cos(t * 2.3f) * AimErrorDegrees * 0.6f;
            }

            yaw = Mathf.MoveTowardsAngle(yaw, wantYaw, TurnRate * dt);
            pitch = Mathf.Clamp(Mathf.MoveTowardsAngle(pitch, wantPitch, TurnRate * dt), -80f, 80f);
        }

        private void HandleStuck(ref Core.NetworkInputData input, Vector3 pos, int tick, bool fighting)
        {
            if (tick == jumpTick) input.Buttons.Set((int)Core.InputButtons.Jump, true);
            if (tick < progressCheckTick) return;

            bool moved = Flat(pos - progressAnchor).magnitude > 0.35f;
            progressAnchor = pos;
            progressCheckTick = tick + Ticks(0.6f);
            if (moved || fighting) { stuckCount = 0; return; }

            // Not getting anywhere: hop (this also mantles low obstacles), and after a few tries give the goal up.
            stuckCount++;
            jumpTick = tick + 1;
            if (stuckCount >= 5)
            {
                stuckCount = 0;
                if (currentObjective != null) avoidUntil[currentObjective] = tick + Ticks(12f);
                currentObjective = null;
                repathTick = 0;
                thinkTick = 0;
            }
        }

        private float PreferredRange()
        {
            var data = loadout != null ? loadout.GetData((int)Equipment.EquipmentSlotType.Primary) : null;
            float range = data != null ? data.range : 30f;
            return Mathf.Clamp(range * 0.4f, 6f, 16f);
        }

        /// <summary>Slot the bot should fire: the primary while it has rounds, then a hitscan secondary.</summary>
        private int FireSlot()
        {
            if (loadout == null || loadout.Object == null) return -1;
            int primary = (int)Equipment.EquipmentSlotType.Primary, secondary = (int)Equipment.EquipmentSlotType.Secondary;
            var p = loadout.GetData(primary);
            if (p != null && (!p.UsesAmmo || loadout.Magazine[primary] > 0 || loadout.Reserve[primary] > 0)) return primary;
            var s = loadout.GetData(secondary);
            if (s != null && s.kind == Data.EquipmentKind.HitscanWeapon && (!s.UsesAmmo || loadout.Magazine[secondary] > 0 || loadout.Reserve[secondary] > 0)) return secondary;
            return -1;
        }

        private void Shoot(ref Core.NetworkInputData input, Vector3 target, Vector3 eye, int tick)
        {
            // Abilities: now and then, whichever is ready.
            if (abilityToPress >= 0)
            {
                input.Buttons.Set((int)Core.InputButtons.Ability1 + abilityToPress, true);
                abilityToPress = -1;
            }
            else if (tick >= nextAbilityTick)
            {
                nextAbilityTick = tick + Ticks(4f + (float)rng.NextDouble() * 4f);
                int slot = rng.Next(Core.GameConstants.MAX_ABILITY_SLOTS);
                if (me.IsAbilityReady(slot)) abilityToPress = slot;
            }

            int slotToFire = FireSlot();
            if (slotToFire < 0) return;
            var data = loadout.GetData(slotToFire);
            if (data == null) return;

            // (v0.13) bring the wanted weapon to hand first, like a player would
            if (loadout.ActiveSlot != slotToFire)
            {
                if (pulse && !loadout.IsSwapping) input.Buttons.Set((int)Core.InputButtons.NextWeapon, true);
                return;
            }

            if (data.UsesAmmo && loadout.Magazine[slotToFire] <= 0)
            {
                if (!loadout.IsReloading(slotToFire) && pulse) input.Buttons.Set((int)Core.InputButtons.Reload, true);
                return;
            }

            if (!lineOfFire || tick - firstSeenTick < Ticks(ReactionSeconds)) return;

            Vector3 want = (target - eye).normalized;
            // (v0.13) bots shoulder the weapon at range, and always with a scope: hip fire is too loose there
            if (data.kind == Data.EquipmentKind.HitscanWeapon && ((target - eye).sqrMagnitude > 14f * 14f || data.adsZoom >= 2f))
            {
                input.Buttons.Set((int)Core.InputButtons.Secondary, true);
            }
            if (Vector3.Angle(me.AimDirection, want) > FireTolerance) return;

            // Short bursts with pauses: lets the bloom settle and gives the other side a window.
            if (tick >= burstUntilTick)
            {
                // The last burst is over: pause, then the next burst runs until burstUntilTick.
                burstPauseUntilTick = tick + Ticks(0.15f + (1f - skill) * 0.35f);
                burstUntilTick = burstPauseUntilTick + Ticks(0.35f + (float)rng.NextDouble() * 0.5f);
            }
            if (tick < burstPauseUntilTick) return;

            // [v0.12] int button = slotToFire == (int)Equipment.EquipmentSlotType.Primary ? (int)Core.InputButtons.Primary : (int)Core.InputButtons.Secondary;
            int button = (int)Core.InputButtons.Primary; // (v0.13) the fire button fires whatever is in hand
            // Automatic weapons want the trigger held; the rest need a fresh press for every shot.
            if (data.automatic || pulse) input.Buttons.Set(button, true);
        }

        /// <summary>Out of combat: top the magazine up.</summary>
        private void MaintainWeapon(ref Core.NetworkInputData input)
        {
            if (loadout == null || loadout.Object == null) return;
            // [v0.12] int slot = (int)Equipment.EquipmentSlotType.Primary;
            int slot = loadout.ActiveSlot; // (v0.13) only the weapon in hand reloads
            var data = loadout.GetData(slot);
            if (data == null || !data.UsesAmmo || loadout.IsReloading(slot) || loadout.Reserve[slot] <= 0) return;
            if (loadout.Magazine[slot] * 2 < data.magazineSize && pulse) input.Buttons.Set((int)Core.InputButtons.Reload, true);
        }

        private static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }
    }
}
