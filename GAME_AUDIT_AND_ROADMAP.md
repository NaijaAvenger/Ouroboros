# Ouroboros: Full-Pass Audit and Roadmap

Date: 2026-11-18. Target: a fun extraction / hacker heist shooter in the spirit of SEGA's HYENAS, with an African-futurist identity.

## Status (updated 2026-11-18)

The six decisions in section 6 are made, and M0 is under way. Sections 1 to 3 are the audit as written
before M0 started; where M0 has already changed a finding, it is marked **(fixed in M0)**.

### M0 progress

| Item | State |
|------|-------|
| Merge `claude/vigilant-ramanujan-la3aua` | Done, and merged into `main` on 2026-11-18 |
| Clean compile | Every game script now passes an offline type-check against Fusion 2.1.3, Input System 1.11.2 and Unity reference assemblies (`Tools/typecheck/run.sh`). One error found and fixed: `[Header]` on a property in `NetworkPlayer.cs` |
| `com.unity.ai.navigation` in the manifest | Done (2.0.8). Unity resolves it on next open |
| Tests for pure logic | Done. 62 unit tests over winner selection, alarm tiers, loot split, carry weight, constants and the behaviour tree (`dotnet test Tools/tests`) |
| CI | Done. `.github/workflows/checks.yml` runs both checks on every push |
| README status claims | Done |
| Push the local Unity project | **Open, needs you.** `ViewMode.cs` and the Keybinds panel are still in no branch |
| Open in Unity 6000.4 and confirm a clean compile | **Open, needs you.** The offline check cannot run the Fusion weaver |
| Two-client checklist in `PHASE0_BRINGUP.md` | **Open, needs you.** This is M0's exit criterion |

The offline type-check is described in `Tools/README.md`, including what a pass does not prove.

### M2 progress (code complete, not yet play-tested)

Compiles against the real Fusion and Input System APIs; the new rules arithmetic is unit-tested. Not run in
Unity. The checklist is section 7f of `PHASE0_BRINGUP.md`.

| M2 item | State |
|---------|-------|
| Quota and getaway | Done in code. Exits stay shut until a crew is carrying the quota (or the timer runs out); then the central getaway and the side exits open, the facility locks down and the richest crew's carriers are revealed |
| Scoring | Done in code. Score is extracted value only; side exits bank 60% |
| Physical loot | Done in code. Vaults spill named items from a catalog with story cards; players carry up to four; death drops them one by one |
| Loot transfer on wipe, respawn rules | Done in code. Respawn needs a living crewmate; a wipe redeploys the crew once and then eliminates it. Revive and respawn stations are not built |
| Team format | Done in code. Four crews of three, config-driven, named Lagos, Nairobi, Accra and Joburg |
| Bot crews | Done in code. A bot is a normal player object fed input by `AI/BotBrain.cs`; bots fill empty seats, follow a human crewmate, fight, open all four lock types, carry loot and extract. Placeholder quality: no cover use or climbing |
| Fair contests | Done in code. Simultaneous claims no longer always go to the lowest team id |
| Blockout map | Done as a builder script (`Ouroboros > Setup > Create Freeport Map (M2)`): laid out by hand in code, built by the editor on your machine. The old generated arena is still available but is no longer the first scene |
| Loot catalog writing | Placeholder. Eighteen fictional items; names and stories need review with people from the cultures they draw on |

### M1 progress (first slice, code complete, not yet play-tested)

Everything below compiles against the real Fusion and Input System APIs and the pure maths is unit-tested.
None of it has been run in Unity. The checklist is section 7e of `PHASE0_BRINGUP.md`.

| M1 item | State |
|---------|-------|
| Prediction-safe movement | Done in code. The player prefab now uses Fusion's built-in `NetworkCharacterController` as its transform sync, and ground contact and velocity are networked. The KCC add-on could not be downloaded in the build environment; this fixes the same reconciliation fault without it. Revisit KCC only if the two-client test shows problems |
| Lag compensation and hit zones | Done in code. Enabled in the project config; the builder adds Head and Body hitboxes to the player and guard prefabs; shots ignore a target's present-time collider |
| Predicted firing | Done in code. Ammo, cooldown, reload and a shot counter are networked state simulated on the owning client; damage stays on the host. Shot and reload RPCs are gone |
| Recoil, bloom, falloff, headshots | Done in code, with a per-weapon profile applied by the builder. Spread is deterministic so the predicted tracer matches the real shot |
| Crosshair, hit markers, damage direction, reload prompt | Done in code (`UI/CombatHUD.cs`, added automatically by the HUD) |
| Guard accuracy | Done in code. Hit chance depends on distance, target speed and alarm tier; the first shot at a new target is delayed; misses draw tracers. The sniper keeps its guaranteed, telegraphed shot |
| Placeholder SFX and VFX | Partly done. Synthesised sounds for shots, reload, hurt, death, explosions and hit ticks; pooled tracers. Ability and match-event sounds are still silent |
| Aim-down-sights | **Not started.** Right mouse is bound to the Secondary weapon. ADS needs that button, which means adding weapon switching; that collides with the unpushed Keybinds work, so it waits for the local project |
| Third-person body with animations | **Not started.** Needs model and animation assets |
| URP switch | **Not started.** Needs the editor (package install, pipeline asset, material upgrade) |

## 0. What was audited, and what was not

| Source | Commit | Contents |
|--------|--------|----------|
| `main` (and this branch) | `34faff1` | Phase 0 and part of Phase 1: session, player, classes, match flow, vault hold-to-crack, extraction, basic guard, HUD |
| `origin/claude/vigilant-ramanujan-la3aua` | `44fa106` | Everything above plus 12 unmerged commits: weapons, alarm, guard spawner, elite and sniper guards, four-lock vaults, movement tech, class picker, rebindable input |
| Your local Unity project | not pushed | Unknown. It contains at least `Assets/Scripts/Infrastructure/Systems/ViewMode.cs` and a Keybinds panel, which exist in no branch |

Two limits on this audit:

- It is a code read. Unity is not available in the cloud environment, so nothing here was played. Items marked **likely** need an in-editor check.
- It describes the unmerged branch as "the game", because that is the most complete code. `main` is well behind it.

## 1. Verdict

Ouroboros today is a **networked rules prototype**. About 12,000 lines of gameplay C# describe a heist match correctly on paper. There is no content around them: 3 prefabs, 1 code-generated scene of primitives, and no models, animations, audio, VFX or textures.

The distance to "fun" is not more systems. It is feel, loop design, a real hacking mechanic, a map, and an identity. The current code is a reasonable foundation for all five.

## 2. Where we are

### 2.1 Inventory

| Area | What exists | Honest state |
|------|-------------|--------------|
| Networking | Fusion 2.1.3, host/client, replicated player state, tick timers, change detectors | Sound architecture. Never load-tested |
| Match flow | Waiting, countdown, in progress, extraction window, winner with tiebreaks | Works on paper. No climax |
| Player | First-person capsule, sprint, slide, mantle, class perks (double jump, glide, wall climb, grapple) | Good feature list. Netcode approach is risky (2.2 D) |
| Classes | Hacker, Saboteur, Demolitions, Agent, 4 abilities each, data-driven stats | Abilities are radius effects with log lines. No aiming, no VFX |
| Weapons | Hitscan, projectile, passive gear, ammo, reload, lobby loadout | Server-only firing. No recoil, ADS, headshots, crosshair or hit markers |
| Heist layer | Hold-to-crack vaults, four exterior lock types, keypad, carry case, loot weight, hack terminal | Good bones. Every interaction is "stand in a circle and hold F" |
| Alarm | Level 0 to 100, four tiers, lockdown, reinforcement waves | The best system in the project. No audio or visual payoff |
| AI | Guard, elite, sniper, behaviour tree, shared blackboard, noise, vision cones | Solid design. Guards deal flat damage whenever they have line of sight. Needs the AI Navigation package, which is not in the manifest |
| UI | UI Toolkit HUD, class picker, keypad, dev overlay | Functional. No menu, settings, or crosshair |
| Level | `DevArena`, generated by an editor script from cubes | A test box, not a map |
| Art, audio, animation | None | Zero assets. `FeedbackLibrary` slots are empty |
| Shell | One hard-coded session name, auto host-or-join | No menu, matchmaking, party, progression or persistence |
| Quality | None | No tests, no CI, no builds **(tests and CI added in M0)** |

### 2.2 Problems, ranked by how much they block "fun"

**A. The source of truth is split three ways (partly fixed in M0: the feature branch is merged; the local project is not yet pushed).** `main` has no weapons, alarm, vault doors or movement tech. Those sit on an unmerged branch. Your local project has further work that is in neither. Nothing else can be planned reliably until this is one branch.

**B. The docs overstate (fixed in M0: `README.md` now states what a tick means, and the code type-checks offline).** `README.md` ticks every feature. `DEVELOPMENT_PLAN.md` says each phase is "awaiting in-engine validation". Both are true at once: the code was written without a compiler or play mode. Treat every system as unverified until it passes a two-client test.

**C. Shooting cannot feel good yet.**
- Lag compensation is disabled in `NetworkProjectConfig.fusion`, and the player prefab has no `HitboxRoot`. Hits are raycast on the host against present-time positions, so remote players must lead their targets by their ping.
- Firing only runs on the state authority. A remote client's muzzle feedback arrives by RPC one round trip later.
- There is no recoil, aim-down-sights, damage falloff, hit zones, crosshair, hit marker or damage direction indicator.

**D. Movement netcode is likely to rubber-band for non-host players.** `PlayerController` drives a Unity `CharacterController` under a `NetworkTransform`. Unity's `CharacterController` ignores external position resets unless it is disabled first, and grounded state is not part of the networked state. Both break resimulation. Fusion 2's supported path is the KCC add-on.

**E. Hacking has no gameplay.** `HackerClass.HackSystem` finds the nearest hackable within 10 m and completes instantly. Terminals and vaults are hold-F progress bars. For a "hacker style" game this is the largest design gap.

**F. The loop has no stakes and no finale.** Score blends kills (25), objectives (100), extraction (500) and loot. By default players get 5 respawns, extraction opens on a timer, and vaults respawn every 60 s. Loot is a number on a HUD. Nobody knows who is winning, and nothing forces the crews to collide at the end.

**G. There is no identity.** Teams are Alpha to Delta. Classes are generic military archetypes. Nothing in the code, names, UI or data is African.

**H. There is no game around the match.** No menu, no session browser, no settings screen, no progression. Sixteen human players are required for the intended experience, and there are no bots to fill seats.

**I. Smaller findings.**
- Vault and extraction capture go to the lowest team id when two crews start on the same tick (Alpha always wins the race).
- A Photon App ID is committed in `PhotonAppSettings.asset`. If the repository is public, anyone can spend your CCU quota. Rotate it or keep the repo private.
- `com.unity.ai.navigation` is missing from `Packages/manifest.json`. Without it the NavMesh cannot be baked and guards stand still. **(fixed in M0)**
- The project uses the built-in render pipeline. Moving to URP is cheap now and expensive after art exists.

### 2.3 What is worth keeping

- The authority model: one networked source of truth, tick timers, change detectors, data-driven config.
- The alarm tiers, guard spawner and AI blackboard. This is a real "heat" system.
- The four vault lock types. Swiping an ID card looted from a dead rival is a strong idea because it turns PvP into a key.
- Per-class movement perks.
- The rebind API in `HeistInputActions`.

## 3. Where we need to be

### 3.1 The reference: what made HYENAS work

From published previews: 15 players in five teams of three, AI guards called MURFs defending vaults, and a three-part match (gear up, heist the vaults, extract). The first team to reach a loot quota (about 10,000 merch points) heads to a single VIP extraction at the centre of the map. Dying loses your loot, and a respawn station can bring teammates back. Around that sat zero-gravity combat, named specialists and a loud pop-culture tone.

The lesson is structural: **a quota race that ends in one contested exit**. Everything in the match points at that collision.

### 3.2 Target pillars for Ouroboros

1. **Crew heist first.** One crew against the facility is already fun. Rival crews make it better.
2. **Everyone hacks.** Hacking is a universal verb with skill in it. The Hacker specialist is simply the best at it.
3. **The snake eats its tail.** Loot changes hands. The richest crew is always the target.
4. **Fast, vertical movement and readable gunfights.**
5. **Unmistakably African-futurist** in place, sound, visual language and story.

### 3.3 Gap by pillar

| Pillar | Today | Target |
|--------|-------|--------|
| Crew heist | Guards and alarm exist; needs 16 humans | Playable solo or with friends; bot crews fill the rest |
| Everyone hacks | One instant ability; hold-F bars | Aimed quick hacks for all, a skill-based deep hack at vaults, counter-hacking |
| Tail-eating | Loot is a number; blended score | Physical loot, public quota race, one central getaway, wipes transfer loot |
| Movement and guns | Feature list present; netcode and feel missing | Predicted movement and fire, lag-compensated hits, full weapon feel |
| Identity | None | Setting, crews, specialists, music, UI language |

## 4. Proposed design direction

These are proposals. Section 6 lists the decisions that are yours.

### 4.1 Premise

Late 21st century. Looted African heritage and the data of a continent sit in **the Ouroboros**, an offshore freeport: a tax-free vault city for private collectors. Reclamation crews from African megacities break in to take it back, and race each other for the credit.

Why this fits:

- It is the HYENAS setup (rob the rich who hoard culture) with a motive players care about.
- The name is already African. The earliest known ouroboros is in Tutankhamun's tomb, and the Fon of Dahomey tell of Aido-Hwedo, the rainbow serpent often described as holding the world with its tail in its mouth.
- The name is also the rule of the game: whoever holds the loot gets eaten next.

Nearest neighbour: *Relooted* (Nyamakop, South Africa), an Africanfuturist heist game about reclaiming real artefacts from Western museums. It is a single-player parkour puzzle game. A competitive PvPvE shooter is open ground, and Relooted shows the theme has an audience.

Cultural care, stated once: draw on specific cultures, not a generic "Africa". Keep sacred objects and living religious practice fictionalised or consulted on. Avoid the scammer-hacker stereotype. Commission artists and musicians from the cultures shown, and credit them.

### 4.2 Crews instead of Alpha to Delta

Each team is a city crew with a colour, a pattern language and a voice: for example Lagos (Ankara wax print, Pidgin callouts), Nairobi (Maasai shuka reds, Sheng), Accra (Kente geometry, Twi), Johannesburg (Ndebele wall-painting geometry, isiZulu). Later: Kinshasa, Addis Ababa, Dakar, Cairo.

### 4.3 Specialists instead of generic classes

The existing kits map across almost one to one. Names are working titles.

| Today | Becomes | Fantasy | Kit carried over |
|-------|---------|---------|------------------|
| Hacker | The Cipher | Reads the building like a pattern. Hack UI built on 8-mark binary signs | System Hack, EMP, reveal, glide |
| Saboteur | The Spider | Anansi-style trickster. Webs, decoys, ambush | Traps, stealth, smoke, wall climb |
| Demolitions | The Smith | Ironworker and breacher. Makes doors where there are none | Charges, breach, incendiary, grapple |
| Agent | The Shield | Frontline protector inspired by Dahomey's Agojie | Shield, damage boost, flash, double jump |

Later candidates: a Griot (sound-based support and reveals), a Rider (okada courier, fastest loot carrier), a Healer.

### 4.4 Match loop: "The Run" (12 to 15 minutes)

1. **Drop and gear (0 to 2 min).** Crews insert at separate docks, grab gear, read the layout. The facility is quiet.
2. **Heist (2 to 9 min).** Vault districts. Each vault is an exterior lock (the four existing types) plus an interior deep hack. Loot is **physical artefacts** with weight, value and a story card. Cracking is loud and raises heat.
3. **The Tail.** Triggered when one crew reaches the quota, or by timer. Everyone is told who is richest and where the single central getaway is. Side exits exist but pay less.
4. **Getaway.** Hold the pad under lockdown while guards and rivals converge.

Win: most value extracted. A team wipe hands the loot to the killers.

Code impact: add a quota to `GameModeConfig`; generalise `LootCase` into carried loot items; reduce scoring to extracted value; replace five free respawns with a respawn station or revive; break the lowest-team-id tie.

### 4.5 Hacking as the signature mechanic

- **Quick hack, for everyone.** Aim at a device in range and hold. A visible tether runs from you to it and breaks if you lose line of sight or take damage. Targets: doors, cameras, turrets, lights, lifts, shutters.
- **Deep hack, at vaults and terminals.** A 10 to 20 second skill task (pattern matching on 8-mark signs, timing rings) that takes part of the hacker's screen. The crew must defend. Mistakes add heat instead of locking you out.
- **The Cipher specialist.** Hacks at range, chains hacks, jams rival gear (the existing EMP), hijacks traps, and sees the device network through walls (the existing reveal).
- **Counterplay.** Hacks are visible. A rival can counter-hack a vault to steal progress, which replaces today's "contested means paused".
- **The building is a weapon.** Blackouts, shutters, sprinklers as smoke, gravity lifts. This is where a zero-g homage can live later.

### 4.6 Feel targets

- Local muzzle flash and recoil on the same frame as the click. Hit confirmation within one round trip.
- Hits are fair at 100 ms ping without leading the target.
- No visible position correction during normal movement for a non-host client.
- Guards telegraph shots and can miss. They are dangerous in numbers, not individually.
- Every action answers with sound, VFX and a HUD response.

### 4.7 Art and audio on a small budget

- Stylised, flat-shaded low-poly with strong pattern and colour. It is cheap, distinctive and hides a small asset count.
- Music as layered stems driven by the alarm tier: amapiano, Afrobeats, gqom. Lockdown is the log-drum drop.
- Short voice callouts per crew in its own language flavour.

## 5. Roadmap

Effort figures assume one developer full time. Read them as ordering and relative size, not dates.

### M0. One codebase that runs (about 1 week)
- Push the local project. Merge `claude/vigilant-ramanujan-la3aua` into `main`. Delete stale branches.
- Clean compile in Unity 6000.4. Add `com.unity.ai.navigation` to the manifest.
- Run the two-client checklist in `PHASE0_BRINGUP.md` and record what fails.
- Correct the status claims in `README.md`.
- Add edit-mode tests for pure logic (behaviour tree, winner selection, alarm tiers).

Exit: host plus one client finish a match on `main`.

### M1. Feel (3 to 4 weeks)
- Move player movement to Fusion KCC.
- Enable lag compensation, add hitboxes with head and body zones.
- Predict firing on the input authority. Add recoil, spread bloom, ADS, falloff.
- Crosshair, hit markers, damage direction, kill confirm.
- Guard accuracy model with telegraphed shots.
- Placeholder SFX and VFX in every `FeedbackLibrary` slot.
- A third-person body with placeholder animations so other players read as people.
- Switch to URP.

Exit: five testers call a grey-box gunfight fun. No rubber-banding at 100 ms.

### M2. The Run (3 to 4 weeks)
- Quota, physical loot, central getaway, loot transfer on wipe, respawn station or revive.
- Bot crews that reuse the guard AI stack with player loadouts.
- One hand-built blockout map with three districts and real verticality. Retire the generated arena.

Exit: one human crew and three bot crews play a full match with a clear start, middle and climax. A new tester can explain the rules after one match.

### M3. Hacking (about 3 weeks)
- Device network, quick hacks with tethers, deep-hack task, counter-hack.
- Rework the Hacker into the Cipher. Add environment toggles.

Exit: non-hacker players hack at least five times a match.

### M4. Identity vertical slice (6 to 8 weeks, needs art and audio help)
- Setting, four renamed specialists with silhouettes, crews with pattern identities.
- One art-passed district. Tiered music system. Voice callouts.
- HUD reskin. Twenty artefacts with story cards.

Exit: a 60-second trailer can be cut. A single screenshot identifies the game.

### M5. Game shell (3 to 4 weeks)
- Main menu, quick play, session browser, party codes. The Fusion Menu sample is already in `Assets/Photon/FusionMenu`.
- Settings: controls and keybinds (in progress locally), audio, video.
- Loadout screen, post-match summary, cosmetic progression.
- Decide dedicated servers versus host migration.

### M6. Playtest and scale
- Closed playtests with telemetry. Balance. Second map. More specialists.
- Performance at full player count with guards: interest management and pooling.

## 6. Decisions (made 2026-11-18)

All six recommendations were accepted.

| # | Decision | Outcome | Takes effect |
|---|----------|---------|--------------|
| 1 | Team format | 4 crews of 3, bots fill empty seats. Constants stay configurable | M2, with the loop rework |
| 2 | Camera | First person only until M4 | Now. A view toggle is out of scope until then |
| 3 | Premise | The reclamation heist in 4.1 | M4 for content; names and data from M2 |
| 4 | PvE or PvP first | PvE first with bot crews | M2 |
| 5 | Hosting | Player-hosted through M4, revisit at M5 | Now |
| 6 | Render pipeline | Switch to URP | M1, before any art exists |

Decision 2 affects the local `ViewMode.cs`: if it is a first/third-person toggle, park it until M4.

## 7. What is needed from you next

1. Open the project in Unity 6000.4 and confirm it compiles. Nothing since M0 has been through the Fusion weaver.
2. Run **Ouroboros > Setup > Create Freeport Map (M2)**. It also applies the M1 prefab upgrades, which the
   committed `Player.prefab` does not have yet.
3. Work through sections 7e (M1) and 7f (M2) of `PHASE0_BRINGUP.md` and list what breaks.
4. With those results: fix what the playtest finds, tune the weapon and quota numbers, then finish M1
   (ADS and weapon switching, a third-person body) and move to M3, hacking.

Notes on the repository as of the M2 work:
- `main` holds M0, M1 and your Unity project state ("Squashed local work"). M2 is on `claude/awesome-darwin-awl360`.
- `ViewMode.cs` and the Keybinds panel are not in the pushed project, so nothing blocks ADS any more.
- The pushed project references a URP global settings asset but the URP package is not in `Packages/manifest.json`.
  Either finish the switch (install Universal RP, assign a pipeline asset, upgrade materials) or remove the stray asset.
- The imported packs (SciFi Gun Pack, LaserGunPack, Cartoon FX Remaster) are not used by any code yet. They are the
  obvious source for weapon view models, turrets and muzzle/impact effects in the next M1 pass.

## Sources

- HYENAS match structure: [Film Stories preview](https://filmstories.co.uk/features/hyenas-preview-could-have-been-one-of-the-best-arena-shooters-in-recent-years/), [GGRecon preview](https://ggrecon.com/articles/hyenas-preview), [TheSixthAxis](https://www.thesixthaxis.com/?p=443285), [GGRecon guide](https://www.ggrecon.com/guides/hyenas-trailers-gameplay/)
- Fusion 2 character controllers: [What's New in 2.0](https://doc.photonengine.com/fusion/v2/getting-started/release-notes/whats-new-2-0), [Advanced KCC features](https://doc.photonengine.com/fusion/v2/addons/advanced-kcc/features)
- Relooted: [Gematsu announcement](https://www.gematsu.com/2025/06/africanfuturist-heist-game-relooted-announced-for-xbox-series-pc), [Epic Games Store](https://store.epicgames.com/p/relooted-89fdd7)
