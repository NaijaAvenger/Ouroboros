# Phase 0 — Unity Bring-up Checklist

Goal: get the v0.2 loop running Host + Client once, and record what the first compile needed.
Everything below that can be automated is: the menu item **Ouroboros > Setup > Create Dev Scene (Phase 0)**
builds the config asset, prefabs and a playable `DevArena` scene.

## 1. Open the project
- [x] Project files are committed on `main`: **Unity 6000.4.1f1**, built-in render pipeline, Active Input Handling = **Both** (`activeInputHandler: 2`). `Core/LocalInputSource.cs` prefers the Input System and falls back to legacy, so either setting works.
- [ ] Unity Hub → Add project → this folder.
- [ ] Package Manager → install **AI Navigation** (`com.unity.ai.navigation` 2.x). `NavMeshAgent` compiles without it, but Unity 6 has no NavMesh baking UI without the package (needed for GuardAI).

## 2. Photon Fusion 2
- [x] **Fusion 2.1.3** is committed under `Assets/Photon`.
- [ ] Fusion Hub (Window > Fusion > Fusion Hub) → paste your **App ID**.
- [ ] Wait for the Fusion ILWeaver to run (console: "Weaving ... Assembly-CSharp").

## 3. First compile — adjustments found
| Symptom | Fix |
|---------|-----|
| `FusionDemos/.../IntroInput.cs`: namespace `InputSystem` does not exist in `UnityEngine` | **Fixed.** Switching Active Input Handling to "Both" defines `ENABLE_INPUT_SYSTEM`, but the `com.unity.inputsystem` package was not installed. Added to `Packages/manifest.json` (1.11.2; upgrade from the Package Manager if you like) together with `com.unity.ugui` 2.0.0, which the Fusion Menu assembly needs for TextMeshPro. |
| `GameSessionManager` does not implement `INetworkRunnerCallbacks.OnReliableDataReceived` | **Fixed.** Fusion 2.1 changed the last parameter from `ArraySegment<byte>` to `ReadOnlySpan<byte>`. Signatures were verified against `Assets/Photon/Fusion/Runtime/Utilities/RunnerVisibility/RunnerEnableVisibility.cs`. |
| `NetworkDictionary` has no `Set` | If it happens: replace `PlayerTeams.Set(player, team)` with `PlayerTeams.Add(player, team)` guarded by `ContainsKey`, or the indexer. (`Network/TeamManager.cs`) |
| `NetworkTransform.Teleport` overload | Use `Teleport(position, rotation)` positional form that exists in your version. (`Player/PlayerController.cs`) |
| `SceneRef.FromIndex` missing | Present in 2.1.3 (verified in `Fusion.Runtime.dll`). |
| Hiding warning: `NetworkPlayer.HasStateAuthority` hides inherited member | Harmless; add `new` or delete the property (the inherited one satisfies the interface). |
| `FindObjectOfType` obsolete warning (2023+) | Already guarded by `UNITY_2023_1_OR_NEWER` in `Core/SceneUtil.cs`. |

Record anything else here so the next person doesn't rediscover it.

## 4. Build the dev scene
- [ ] Menu **Ouroboros > Setup > Create Dev Scene (Phase 0)**. It creates:
  - `Assets/Ouroboros/DevGameModeConfig.asset` (5-min match, 1 player to start, extraction opens at 30 s)
  - `Assets/Ouroboros/Prefabs/Player.prefab` (NetworkObject, NetworkTransform, CharacterController, NetworkPlayer, PlayerController, EquipmentLoadout, camera)
  - `LootDrop.prefab`, `ProximityTrap.prefab`
  - `Assets/Ouroboros/Scenes/DevArena.unity` (ground, cover, 4 team spawns, 2 extraction points, 3 vaults, managers, DevHUD), added to Build Settings
- [ ] If Fusion complains the prefab is not in the table: **Tools/Fusion > Rebuild Prefab Table** (name varies by version), or select the prefab and reimport.
- [ ] Open `DevArena`, press **Play**. The session auto-starts as `AutoHostOrClient`.

## 5. Solo smoke test (host only)
Watch the **DevHUD** (F1) and console:
- [ ] `[GameSessionManager] Session 'ouroboros-dev' started as AutoHostOrClient`
- [ ] `[TeamManager] Assigned player ... to TeamAlpha`, `[GameSessionManager] Spawned ...`
- [ ] `[ExtractionHeist] Match starting in 5s` → `Match started!`
- [ ] WASD/mouse moves; **Shift** sprint drains stamina and it regenerates after ~1 s
- [ ] **1–4** fire abilities; HUD shows cooldowns counting down; stamina drops by the cost
- [ ] Walk to a vault, hold **F** → vault progress climbs → `secured 500 loot`, HUD `Loot 500`
- [ ] After 30 s: `Extraction points are now open`; stand on the green disc 6 s → `extracted 1 member`, team loot banked, `Status: Extracted`
- [ ] Match ends (`Winner: TeamAlpha`) because the only team is finished

## 6. Host + client test
- [ ] Second instance: **ParrelSync** clone (recommended) or a standalone build. Same session name joins automatically.
- [ ] Client spawns on a different team (`TeamBravo`), different class (rotation)
- [ ] Movement is smooth on both; the other player's capsule moves on each screen (NetworkTransform)
- [ ] Client uses an ability: cooldown appears on **its** HUD (replicated), effect logs on the **host** console (authority)
- [ ] Both stand on one vault, only one holds F → `CONTESTED`, progress pauses
- [ ] Damage test (temporary): on the host, Inspector → the client's `NetworkPlayer` → call `TakeDamage` via a debug button, or place a `ProximityTrap`. Death → `RespawnTimer` → respawn at team corner, loot drop sphere appears
- [ ] Extraction contested when an enemy stands on the disc
- [ ] Late join: start a third instance mid-match → it sees classes, statuses and door/vault states

## 7. Optional: AI
- [ ] Add a `GuardAI` (capsule + NavMeshAgent + NetworkObject + NetworkTransform + GuardAI) with 2–3 patrol points
- [ ] Window > AI > Navigation → bake the NavMesh for `DevArena`
- [ ] Guard patrols, spots you, chases and shoots (`HP` drops on the HUD), investigates where it lost you

## 7b. Phase 1 validation (v0.3)
- [ ] HUD appears (UI Toolkit). If it doesn't: the `HUD` object needs `UIDocument.panelSettings` = `Assets/Ouroboros/UI/HeistPanelSettings.asset` (re-run the setup menu).
- [ ] Vitals bars and ability slots update; cooldown fill drains; cost turns red when stamina is short
- [ ] Walk to a vault: zone prompt shows "Hold F to crack", progress bar fills, kill-feed line on completion
- [ ] Other players are tinted by team; your own capsule is hidden; Saboteur stealth hides them from enemies
- [ ] Death shows the red overlay with countdown; extraction shows the green overlay; match end shows the table
- [ ] `ClassData` edits (e.g. Hacker max health in `Assets/Ouroboros/Classes/Hacker.asset`) take effect on next spawn on every peer
- [ ] Pre-match: the class picker appears with the cursor free; clicking a card (or pressing 1-4 / D-pad) changes `ClassType` on the host; "Start match now" shows on the host only
- [ ] Abilities do nothing before the countdown ends (config `allowAbilitiesBeforeMatch` off)
- [ ] Taking damage flashes the screen red; assigning clips in `Assets/Ouroboros/FeedbackLibrary.asset` plays them on the matching events
- [ ] Gamepad: left stick moves, right stick looks, D-pad fires abilities; HUD key hints switch to gamepad labels

## 7c. Phase 2 validation (v0.4)
- [ ] Re-run the setup menu: `Assets/Ouroboros/Equipment/*` assets, `EquipmentRegistry`, `Projectile.prefab` exist; each `ClassData` has starting equipment; the Player prefab has `NetworkLoadout`
- [ ] HUD bottom-right shows the primary weapon with `mag / reserve`; LMB fires (auto weapons while held), R reloads, counts update on client and host
- [ ] Shooting another player lowers their health on every peer; damage flash on the victim; kill feed on death
- [ ] Demolitions: RMB launches a grenade that arcs and explodes (radial damage); the sticky charge bounces then detonates
- [ ] Lobby: `< >` cyclers swap Primary / Secondary; selection replicates; starting ammo matches the item
- [ ] Light Armor reduces damage taken; Stim Rig raises speed

## 7c2. Phase 3 validation (v0.5)
- [ ] Re-run the setup menu: `GuardSpawner`, two `SecurityCamera`s, `SecurityTerminal`, the orange `LootCase` and an `AlarmSystem` on `GameManagers` exist; Vault 1 shows "stage 1/2"
- [ ] Install **AI Navigation** (Package Manager) → add `NavMeshSurface` to `Ground` → Bake, or guards will stand still and only shoot
- [ ] Match start: 3 guards spawn; walking into a camera cone raises the alarm meter; guards seeing you raise it more
- [ ] Alarm ≥ 80 → "LOCKDOWN": reinforcement log lines every 20 s, extraction takes 1.5× longer
- [ ] Hold F at the terminal (or System Hack as Hacker): alarm drops, outer vaults get a progress boost
- [ ] Hold F on the case: "CARRYING CASE" status, you move slower; die → it drops; extract with it → banked with the 1.5× multiplier
- [ ] Carrying lots of loot slows you (25% at 1500)

## 7c3. Phase 4 validation (v0.6)
- [ ] Re-run the setup menu: `EliteGuard.prefab`, `SniperGuard.prefab`, two `SniperPost` platforms and a `BreachableDoor` in a wall south of Vault 3 exist; the spawner has elite / sniper refs
- [ ] Bake the NavMesh (AI Navigation package) — then guards walk and the door carves a hole while closed
- [ ] Fire an unsuppressed weapon near guards: they converge on the shot (noise); the suppressed SMG draws them far less
- [ ] A camera spotting you sends a nearby guard to investigate even if it never saw you
- [ ] Alarm Alert: waves bring red-tinted elites that strafe and fire bursts; they take ~30% less damage
- [ ] Snipers on the posts: you get a REVEALED chip while they aim, then a heavy hit; break line of sight to escape
- [ ] Demolitions Breaching Charge (or Hacker System Hack) on the door: visual disappears, guards path through

## 7d. Expected flow and troubleshooting
**Expected flow on Play (dev config):** spectator camera orbits the arena → session starts → your player spawns → the
**class picker** appears with the cursor free (state *Waiting for players*) → host presses **Start match now** (or the
match auto-starts if `autoStart` is on in the config) → 5 s countdown → picker closes, cursor locks, first-person camera.

| Symptom | Cause | Fix |
|---------|-------|-----|
| Straight into the arena from a high camera, cursor visible, mouse doesn't look | No local player spawned, so nothing locked the cursor or took over the camera. Console shows `No player prefab` or a Fusion spawn error. | Select `GameSessionManager`: **Player Prefab Object** must reference `Assets/Ouroboros/Prefabs/Player.prefab`. Re-run *Ouroboros > Setup > Create Dev Scene* (it now re-applies references without wiping the scene). |
| The prefab field empties after a refresh | Older builder versions rebuilt the whole scene on every run, and the Fusion `NetworkPrefabRef` field errors when the prefab isn't baked/labelled. | Use the direct **Player Prefab Object** field; run *Tools > Fusion > Rebuild Prefab Table* if the `NetworkPrefabRef` field shows an error. |
| Player spawns but no HUD / picker at all | The HUD's `UIDocument` lost its Panel Settings (`m_PanelSettings: {fileID: 0}` in the scene), so UI Toolkit renders nothing; console shows the `[HeistHUD] ... no Panel Settings` error. | Re-run *Ouroboros > Setup > Create Dev Scene* (it re-applies the reference), or assign `Assets/Ouroboros/UI/HeistPanelSettings.asset` on the `HUD` object's UIDocument. |
| `NullReferenceException` in `HitscanWeapon.OnUse` | `Runner.LagCompensation` is null on this runner. | **Fixed**: falls back to a plain physics raycast. |
| Cursor stays free after the picker closes | Editor released the lock (Esc / focus). | Click in the Game view: the session manager re-locks on primary fire whenever a local player exists and no picker is open. |
| No class picker, match already running | `autoStart` is on and `minPlayersToStart` is reached instantly. | Set `autoStart = false` on `Assets/Ouroboros/DevGameModeConfig.asset` (new configs default to off) to hold the lobby until Start. |

## 8. Exit criteria
- Steps 5 and 6 pass with no non-authority write warnings in the console.
- Section 3's table updated with any further fixes.
- Then start **Phase 1** (real HUD on the same replicated data).
