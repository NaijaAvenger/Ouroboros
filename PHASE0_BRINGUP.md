# Phase 0 — Unity Bring-up Checklist

Goal: get the v0.2 loop running Host + Client once, and record what the first compile needed.
Everything below that can be automated is: the menu item **Ouroboros > Setup > Create Dev Scene (Phase 0)**
builds the config asset, prefabs and a playable `DevArena` scene.

## 1. Open the project
- [ ] Unity Hub → Add project → this folder (`ProjectSettings/ProjectVersion.txt` pins **2022.3.20f1**; any 2022.3 LTS is fine, Hub will offer to switch).
- [ ] Let the package manager resolve `Packages/manifest.json` (includes **AI Navigation**, needed by `NavMeshAgent`).
- [ ] Expect compile errors at this point: every script references `Fusion`. That's the next step.

## 2. Import Photon Fusion 2
- [ ] Asset Store / Photon dashboard → import the **Fusion 2** SDK (2.0.x).
- [ ] Fusion Hub (Window > Fusion > Fusion Hub) → paste your **App ID**.
- [ ] Wait for the Fusion ILWeaver to run (console: "Weaving ... Assembly-CSharp").

## 3. First compile — expected adjustments
The code targets Fusion 2.0.x and has only been syntax-checked, never compiled. Fix in this order:

| Symptom | Fix |
|---------|-----|
| `GameSessionManager` does not implement interface member `INetworkRunnerCallbacks.X` / signature mismatch | Right-click the class → *Implement interface* and delete the old stub, or adjust the parameter list to your SDK's version. Only `OnPlayerJoined`, `OnPlayerLeft`, `OnInput`, `OnShutdown` carry logic. |
| `NetworkDictionary` has no `Set` | Replace `PlayerTeams.Set(player, team)` with `PlayerTeams.Add(player, team)` guarded by `ContainsKey`, or the indexer. (`Network/TeamManager.cs`) |
| `NetworkTransform.Teleport` overload | Use `Teleport(position, rotation)` positional form that exists in your version. (`Player/PlayerController.cs`) |
| `SceneRef.FromIndex` missing | `SceneRef.FromIndex(int)` is 2.0; on older betas use `SceneManager.GetActiveScene().buildIndex` cast. (`Network/GameSessionManager.cs`) |
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

## 8. Exit criteria
- Steps 5 and 6 pass with no non-authority write warnings in the console.
- Section 3's table updated with any further fixes.
- Then start **Phase 1** (real HUD on the same replicated data).
