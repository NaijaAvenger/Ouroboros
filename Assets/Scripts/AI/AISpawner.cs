using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.AI
{
    /// <summary>
    /// Spawns the facility's guards when the match goes live and reinforcement waves while the alarm is
    /// in lockdown. State authority only. Guards get their patrol route from <see cref="patrolPoints"/>.
    /// </summary>
    public class AISpawner : NetworkBehaviour
    {
        [Header("Prefab")]
        [SerializeField] private NetworkObject guardPrefab;

        [Header("Placement")]
        [SerializeField] private Transform[] spawnPoints;
        [SerializeField] private Transform[] patrolPoints;

        [Header("Population")]
        [SerializeField] private int initialGuards = 3;
        [SerializeField] private int maxAlive = 8;
        [SerializeField] private int reinforcementsPerWave = 2;
        [SerializeField] private float waveIntervalSeconds = 20f;
        [Tooltip("Also trickle replacements for dead guards while the alarm is at least Alert.")]
        [SerializeField] private bool replaceLossesWhenAlert = true;

        [Networked] private NetworkBool InitialSpawned { get; set; }
        [Networked] private TickTimer WaveTimer { get; set; }

        private readonly List<BaseAIAgent> mine = new List<BaseAIAgent>();

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || guardPrefab == null) return;

            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm == null || gm.Object == null || !gm.IsMatchLive) return;

            mine.RemoveAll(a => a == null || a.Object == null || !a.IsAlive);

            if (!InitialSpawned)
            {
                InitialSpawned = true;
                for (int i = 0; i < initialGuards; i++) SpawnGuard(i);
                WaveTimer = TickTimer.CreateFromSeconds(Runner, waveIntervalSeconds);
                return;
            }

            var alarm = GameMode.AlarmSystem.Instance;
            if (alarm == null || alarm.Object == null) return;

            if (WaveTimer.ExpiredOrNotRunning(Runner))
            {
                WaveTimer = TickTimer.CreateFromSeconds(Runner, waveIntervalSeconds);

                int wanted = 0;
                if (alarm.Lockdown) wanted = reinforcementsPerWave;
                else if (replaceLossesWhenAlert && alarm.Tier >= 2 && mine.Count < initialGuards) wanted = 1;

                for (int i = 0; i < wanted && mine.Count < maxAlive; i++) SpawnGuard(mine.Count + i);
                if (wanted > 0) RPC_Reinforcements(Mathf.Min(wanted, Mathf.Max(0, maxAlive - mine.Count)));
            }
        }

        private void SpawnGuard(int index)
        {
            if (spawnPoints == null || spawnPoints.Length == 0) return;
            var point = spawnPoints[index % spawnPoints.Length];
            if (point == null) return;

            var patrol = patrolPoints;
            var obj = Runner.Spawn(guardPrefab, point.position, point.rotation, null, (runner, o) =>
            {
                var guard = o.GetComponent<GuardAI>();
                if (guard != null) guard.SetPatrolPoints(patrol, startIndex: index);
            });
            var agent = obj != null ? obj.GetComponent<BaseAIAgent>() : null;
            if (agent != null) mine.Add(agent);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Reinforcements(int count)
        {
            Debug.Log($"[AISpawner] Reinforcements: {count} guard(s) inbound");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.magenta;
            if (spawnPoints != null) foreach (var p in spawnPoints) if (p != null) Gizmos.DrawWireSphere(p.position, 0.75f);
        }
    }
}
