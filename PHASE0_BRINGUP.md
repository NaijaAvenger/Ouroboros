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
- [ ] Install **AI Navigation** (Package Manager > Unity Registry) and re-run the setup menu: it adds a NavMeshSurface to Ground and bakes automatically (or bake by hand); then guards walk and the door carves a hole while closed
- [ ] Fire an unsuppressed weapon near guards: they converge on the shot (noise); the suppressed SMG draws them far less
- [ ] A camera spotting you sends a nearby guard to investigate even if it never saw you
- [ ] Alarm Alert: waves bring red-tinted elites that strafe and fire bursts; they take ~30% less damage
- [ ] Snipers on the posts: you get a REVEALED chip while they aim, then a heavy hit; break line of sight to escape
- [ ] Demolitions Breaching Charge (or Hacker System Hack) on the door: visual disappears, guards path through

## 7c4. Vaults, movement, cones (v0.7)
- [ ] Re-run the setup menu: four walled vaults (N keycard, E ID card, S passcode, W lockdown), a red keycard SE, a code note NW, `IDCard.prefab`
- [ ] Console on Play: `[AISpawner] ready ... guard='Guard'` then `[AISpawner] spawned Guard at ...` ×3 once the match is live; if `guard='NONE'`, the spawner lost its prefab reference
- [ ] Ground cones under cameras and guards: yellow idle, orange searching, red when they have a target, grey when a camera is disabled
- [ ] Interiors show "Open the vault door first" until the exterior is open; Hacker's System Hack cannot crack a gated vault through the wall
- [ ] Keycard: pick it up (hold F), swipe at the north door
- [ ] Passcode: press F at the south door → keypad; wrong code → LOCKDOWN + keypad lockout; read the note (hold F) → code shown on the keypad
- [ ] Kill a rival → ID card drops; hold F to take it; swipe at the east door
- [ ] West door: hold F → lockdown, survive 45 s → door opens
- [ ] Movement: Ctrl while sprinting slides; jump into a cover block to mantle; Agent double jump; Hacker hold Space to glide; Saboteur hold Space on a wall to climb; Demolitions G to grapple to a surface

## 7e. M1 validation: movement and gun feel (v0.9)
Setup, once:
- [ ] Re-run **Ouroboros > Setup > Create Dev Scene**. Console should log `Player prefab: NetworkTransform replaced by NetworkCharacterController`, `added HitboxRoot with Body and Head hitboxes` (player and each guard prefab). Weapon assets get recoil / bloom / falloff numbers (`feelVersion` becomes 1).
- [ ] Select `Assets/Photon/Fusion/Resources/NetworkProjectConfig`: **Lag Compensation > Enabled** is ticked (the file now ships with it on). On Play the `NetworkRunner` object has a `HitboxManager`.
- [ ] Open `Assets/Ouroboros/Prefabs/Player.prefab`: it has `NetworkCharacterController` and **no** `NetworkTransform`; children `Hitbox_Body` and `Hitbox_Head` exist.

Solo (host):
- [ ] Movement feels as before: walk, sprint, slide, jump, mantle, class perk. Jump works on the first press every time (ground contact is now networked state).
- [ ] A crosshair is on screen. Its gap widens while firing, jumping and sprinting, and closes when you stop.
- [ ] Firing kicks the view up and it settles back when you release. Holding the Assault Rifle trigger climbs; the Marksman Rifle kicks hard once.
- [ ] Every shot draws a pale tracer and makes a sound, with no audio assets assigned. Reloading clicks.
- [ ] Shoot a guard in the body: white hit marker and a tick. In the head: gold marker, higher tick, more damage. Kill: red marker, "ELIMINATED".
- [ ] Guards no longer hit every shot. Red tracers show their misses; standing still at close range gets you hit far more than sprinting across their view. After breaking line of sight a guard needs a beat before its first shot.
- [ ] Take damage from the side or behind: a red arc around the crosshair points at the source. Fire damage shows no arc.
- [ ] Empty the magazine: `RELOAD [R]` appears under the crosshair.

Host + client (this is the part that cannot be checked offline):
- [ ] **Client movement** tracks the host's view of it. On the host, watch the client's capsule while the client strafes, jumps and slides: no drift, no snapping. On the client, own movement is smooth. If the client slowly desyncs or teleports, the prefab still has the old `NetworkTransform`.
- [ ] **Client firing is immediate**: on the client, tracer, sound, recoil and the ammo counter react on the click, not a moment later. The host sees the same shots.
- [ ] **Lag compensation**: on the client, shoot the host player while they strafe; aim directly at them, do not lead. Hits register. Repeat with about 100 ms of simulated latency (a network shaping tool such as `clumsy` on Windows).
- [ ] Client hit markers appear only for the shooter. Kill feed and health agree on both sides.
- [ ] No `non-authority write` or `Failed to find HitboxManager` messages in either console.

If something fails, note which line and what you saw; the three risky areas are the sync component swap, predicted weapon state, and the lag-compensated ray.

## 7f. M2 validation: The Run (v0.10)
Setup, once:
- [ ] Menu **Ouroboros > Setup > Create Freeport Map (M2)**. It upgrades the prefabs (as 7e), migrates `DevGameModeConfig` (console: `migrated to the M2 rules`), builds `Assets/Ouroboros/Scenes/Freeport.unity`, bakes its NavMesh and makes it the first scene in Build Settings.
- [ ] Open `Freeport`. Fly round it in the Scene view: a raised plaza in the middle with four ramps, a mezzanine to the north, a catwalk and raised server floor to the east, container stacks to the west, a dock in each corner. If two things overlap or a ramp does not meet its platform, note where.
- [ ] The NavMesh overlay (Window > AI > Navigation) covers the ground, the ramps and the tops of the plaza, mezzanine, catwalk and server floor.

Solo (host), press Play and then **Start match now**:
- [ ] Console: `Filled empty seats with 11 bot(s): 4 crews of 3`. The scoreboard shows Lagos, Nairobi, Accra and Joburg, each `3/3 up`. You are on Lagos with two bots.
- [ ] Your two bots follow you, shoot guards and rivals they see, and hold Interact with you at a vault.
- [ ] The other crews' bots walk to vaults and crack them; their scoreboard `carried / quota` numbers rise. Watch one bot crew for a minute: do they get stuck anywhere?
- [ ] Crack **Loading Bay** (south, no door). It spills gold spheres. Walking over one gives `+ <item name> ($value)` and its story line in the feed; the loot readout shows `1/4 items`. A fifth item is refused.
- [ ] No exit works before the getaway: standing on one says `Locked until the getaway is called`.
- [ ] Die while carrying: your items drop as separate spheres around the body. A bot or you can pick them up.
- [ ] You respawn after 8 s while a crewmate is alive. Get the whole crew killed: `CREW WIPED - redeploying together`, 15 s. A second wipe: `CREW ELIMINATED`, no respawn.
- [ ] When any crew is carrying $2500, or at 8:00: banner `... hit the quota`, the alarm goes to LOCKDOWN, the top bar reads `GETAWAY - <crew> holds the most`, and that crew's carriers show as Revealed.
- [ ] Stand on the central plaza disc: `CENTRAL GETAWAY - stand here to bank everything`. Stand on an edge exit: `Side exit - banks 60%`. Extract at each in different runs and compare the score with what you carried.
- [ ] Bot crews head for an exit during the getaway; crews with nothing to bank converge on the plaza.
- [ ] The match ends when the 2:00 getaway timer runs out or every crew is away or out. Highest score is the crew that extracted the most value; kills alone score nothing.
- [ ] Each lock works: red keycard (on the server floor, east) opens Gallery West; the code note (top of the double container stack, west) lets you open Gallery East; holding Interact at the Archive Strongroom starts a lockdown you must outlast; Customs needs an ID card from a dead rival.

Host + client:
- [ ] The client joins the same crew as the host (crews fill before they spread), replacing a bot if the match already started.
- [ ] Bots move smoothly on the client's screen and take damage from the client's shots.
- [ ] Item pickups, the carried count, drops on death and the getaway banner agree on both screens.

Known limits of the bots (by design for now): no use of cover, they do not climb containers, and they leave vaults that need a Hacker's unlock to people.

## 7g. M3 validation: hacking (v0.11)
Setup: re-run **Ouroboros > Setup > Create Freeport Map (M2)**. On an existing Freeport scene it adds a `Devices` group (console: `Freeport devices ready: 3 shutter gates, 3 power junctions, 3 lifts, 3 sentry turrets`) and re-bakes the NavMesh. No prefab changes are needed for M3.

Quick hacks (any class). Aim at a device within about 18 m and hold **F**:
- [ ] A prompt appears under the crosshair before you press anything: `Hold [F] Blind camera`, `Close shutter`, `Cut the power`, `Start lift`, `Hijack turret`.
- [ ] Holding F draws a line in your crew colour from you to the device and fills the bar. Look away, break line of sight, or get shot: the hack breaks and the bar resets.
- [ ] **Camera**: its ground cone turns grey for 20 s.
- [ ] **Shutter gate** (south, east and west of the atrium; aim at the blue control box): the barrier drops and guards path around it. Hack again to open. It reopens on its own after 25 s.
- [ ] **Power junction** (green box in each district): the district light goes out, nearby camera cones go grey, nearby turrets stop, and the feed says who cut it. It cannot be cut again for 45 s (box turns orange).
- [ ] **Lift pad** (grey disc below the mezzanine, the catwalk and a plaza corner): turns cyan for 25 s and throws anyone who steps on it up to the level above. On a client, check the launch is not followed by a snap back to the ground.
- [ ] **Sentry turret** (three vault fronts): unhacked, it turns red, waits about a second, then fires at you. Hijacked, it shows your crew colour for 30 s and fires at rivals and guards. Shooting it to zero shuts it down for 30 s.
- [ ] On a second screen, another player's tether line is visible.

Deep hacks (vaults). Stand at an open vault and **press** F once:
- [ ] The console opens: one gold sign, four cyan candidates numbered 1 to 4. Movement, weapons and abilities are locked. Press F again to jack out. The crosshair is hidden while the console is open.
- [ ] Press the number of the matching sign: the bar jumps by about a fifth, a new puzzle appears. Five right answers crack a vault.
- [ ] Press a wrong one: red flash, the bar drops a little, the alarm meter rises, and input is refused for a moment.
- [ ] Do nothing: the bar creeps on its own, slowly (about three times the old crack time).
- [ ] Jack out mid-crack and wait: the bar drains instead of resetting.
- [ ] Rare vaults (Gallery, Archive, Customs) show candidates that differ from the target by two marks instead of three; they are harder to tell apart.
- [ ] With two crews at one vault: a rival who jacks in sees `COUNTER-HACK - <crew> holds NN%`. Their right answers push the bar down; past zero the vault changes hands and the feed says so.
- [ ] Bots crack vaults at a steady pace without anyone watching (they answer on a timer).

The Cipher (pick the first class in the lobby; it is still `Hacker` in code):
- [ ] Quick hacks reach noticeably further and take half as long; a second hack within 4 s of the first is faster still.
- [ ] **1, Remote Hack**: aim at a device up to 40 m away and press 1. It is hacked at once, with no tether.
- [ ] **2, Net Sight**: for 10 s, labels appear on devices through walls with their action and distance.
- [ ] At a vault, the Cipher's signs are easier to tell apart and four right answers are enough.

Host + client:
- [ ] The client's answers register (the bar moves on both screens) and a fast double press does not count twice.
- [ ] Tethers, shutter states, blackouts and turret ownership agree on both screens.

## 7h. M3 follow-up and M4 validation: zero-g, jamming, pulse, identity (v0.12)
Setup: re-run **Ouroboros > Setup > Create Freeport Map (M2)**. On an existing Freeport scene it adds a `Gravity Zones` group. The Gallery dressing and dock banners are only added to a scene built from scratch, so either use **Recreate Freeport Map** or, to keep hand edits, open the scene and run **Ouroboros > Setup > Apply Art Pass (M4)**, then save.

Zero gravity (two zones: the air over the atrium, and the container yard):
- [ ] Aim at a gravity control box and hold **F**: `Kill gravity` (and `Restore gravity` while it is off). The zone shows a faint volume and the feed says who switched it.
- [ ] Walk in: you lift off. You fly where you look, **Space** thrusts up, **Slide** thrusts down, and you keep your speed until you thrust the other way.
- [ ] Guards inside stay on the floor. Bots inside steer for their target and do not get stuck on the ceiling.
- [ ] After 30 s, or when someone hacks the box again, everyone in the air falls. No fall is fatal and nobody ends up inside geometry.
- [ ] Host + client: the client's flight is smooth, with no snap back when entering or leaving the zone.

Jamming a rival (any class):
- [ ] Aim at a rival player within hack range and hold **F**: `Jam gear`. It takes about 2 s and they see `BEING HACKED by <name> - break line of sight or shoot them`.
- [ ] On completion their abilities are off for 4 s and their weapon will not fire for 2 s. The feed names both players.
- [ ] The same player cannot be jammed again for 14 s. Breaking line of sight or shooting the hacker breaks the tether.

Pulse puzzle (the second deep-hack type; vaults default to a mix of both):
- [ ] Some puzzles are a moving marker and a highlighted window instead of signs. Press **Space** while the marker is in the window: the bar jumps. Outside it: `MISSED`, the bar drops and the alarm rises.
- [ ] Harder vaults have a narrower window and a faster marker.
- [ ] Host + client: a strike the client saw as a hit is a hit on the host (it is judged from the shared tick, not the client's clock).

Identity:
- [ ] The lobby shows the premise and four specialists called **Cipher, Spider, Smith, Shield**.
- [ ] Each crew wears its own colours and pattern (Lagos rings, Nairobi check, Accra strips, Joburg steps). You can tell crews apart at range by silhouette and colour. Guards look like guards, and the three guard types differ.
- [ ] Your HUD carries your crew's pattern band and colour.
- [ ] Weapons show a model from the SciFi Gun Pack in first person and in other players' hands. **Check the model points forward.** If it points sideways, change `viewEuler` on the weapon's Equipment asset (default `0, -90, 0`).
- [ ] Loot on the floor is a recognisable object per item (leopard, harp, sword, data core, and so on), glowing in its tier colour.
- [ ] Music: a quiet held chord in the lobby, a kick and bass once the match is live, a log-drum line that comes in at alarm tier 1 and is full at tier 2, and an alarm layer in lockdown and during the getaway. Layers fade, they do not cut, and they stay in time with each other.
- [ ] Crew radio lines appear above the ability bar at match start, when your crew opens a vault, when a crewmate goes down, at lockdown, at the getaway, and when you extract. Each has an English line under it.
- [ ] The Gallery (north) has a stone floor, an entrance arch signed `THE GALLERY`, colonnades, six plinths with slowly turning show pieces and name plates, and warm lamps. Cutting the Gallery power turns the lamps off too.
- [ ] Signs are hidden by walls. If they show through walls, the `Ouroboros/World Text` shader did not compile: send the console error.
- [ ] Each crew dock has a banner in the crew's pattern with its city over it.
- [ ] Impacts, explosions and extractions show Cartoon FX effects. If an effect is too big or too bright, swap it on `Assets/Ouroboros/FeedbackLibrary.asset`.

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
| `[Fusion] Failed to find HitboxManager on GameObject: (NetworkRunner)` on every shot | Fusion's `Runner.LagCompensation` getter logs when Lag Compensation is off in `NetworkProjectConfig`. | **Fixed (v0.8)**: `HitscanWeapon` probes for the `HitboxManager` component first. Enable *Lag Compensation* in the NetworkProjectConfig (Tools > Fusion > Network Project Config) if you want lag-compensated hits. |
| `[Ouroboros] Tried to assign a NULL value to 'lootDropPrefabObject' / 'feedbackLibrary'` while running the setup menu | Loading an asset by path immediately after `AssetDatabase.Refresh()` returned null. | **Fixed (v0.8)**: the scene build now runs on the next editor update, retries with a forced re-import and falls back to the object created in the same run. If it still logs, run *Ouroboros > Setup > Rebuild Scene References* once imports finish. |
| `[AISpawner] No guard prefab assigned` / `[ExtractionHeist] No GameModeConfig assigned` in play mode | The saved scene has empty references. | In the editor these fields now self-heal (`[Ouroboros] ... was empty: loaded ... as an editor-only fallback`); run *Ouroboros > Setup > Rebuild Scene References* to persist them. Builds have no fallback. |
| Cursor stays free after the picker closes | Editor released the lock (Esc / focus). | Click in the Game view: the session manager re-locks on primary fire whenever a local player exists and no picker is open. |
| No class picker, match already running | `autoStart` is on and `minPlayersToStart` is reached instantly. | Set `autoStart = false` on `Assets/Ouroboros/DevGameModeConfig.asset` (new configs default to off) to hold the lobby until Start. |

## 8. Exit criteria
- Steps 5 and 6 pass with no non-authority write warnings in the console.
- Section 3's table updated with any further fixes.
- Then start **Phase 1** (real HUD on the same replicated data).
