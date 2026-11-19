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

### M3 progress (code complete, not yet play-tested)

Compiles against the real Fusion and Input System APIs; the puzzle and progress arithmetic is unit-tested.
Not run in Unity. The checklist is section 7g of `PHASE0_BRINGUP.md`.

| M3 item | State |
|---------|-------|
| Quick hacks for everyone | Done in code. Hold Interact while aiming at a device; a visible tether, a prompt and a progress bar; breaks on lost line of sight or damage |
| Devices | Done in code. Cameras, doors and rival traps became hackable; new shutter gates, power junctions (blackout), lift pads and sentry turrets (hijack). Twelve are placed on Freeport |
| Deep hack | Done in code. Vaults are jacked into with one press and cracked by matching eight-mark signs on keys 1 to 4. Answers are checked on the host from a shared seed. Being jacked in also trickles progress, so a vault always opens eventually |
| Counter-hack | Done in code. A rival jacked into the same vault pushes its progress back and takes it over past zero. An abandoned crack drains instead of resetting |
| The Cipher | Done in code. The Hacker class is now the hack specialist: longer reach, half the hack time, chaining, easier signs, Remote Hack (instant, aimed, 40 m) and Net Sight (devices through walls) |
| Bots | Done in code. They jack in and answer on a timer, fight hostile turrets, and may trip devices by accident. They do not quick-hack on purpose |
| Sign visuals | Placeholder. Plain open and closed bars; the art direction for the glyph language is an M4 decision and needs the cultural review noted in 4.1 |

M3 follow-up (v0.12, code complete, not play-tested): the three items first left out of M3 are now in.
Zero-gravity zones are hackable volumes where players fly and keep momentum (two on Freeport). Any player can
tether a rival and jam their abilities and weapon for a few seconds. A second deep-hack puzzle, a timed pulse
strike, is mixed in with the sign puzzle. The checklist is section 7h of `PHASE0_BRINGUP.md`.

### M4 progress (code complete where code can do it, not yet play-tested)

Everything below is made in code, so it needs no art files. It is a stand-in that gives the game a look of its
own, not a replacement for artists, composers, voice actors or cultural consultants.

| M4 item | State |
|---------|-------|
| Setting | Done as text. The premise is on the lobby screen and on signs in the Gallery |
| Four renamed specialists | Done. Cipher, Spider, Smith and Shield, each with a code-built model whose outline differs (visor and antenna, hood and limbs, apron and pack, pauldrons and shield). The enum names in code are unchanged |
| Crew pattern identities | Done. Each crew has a palette and a generated pattern (Lagos rings, Nairobi check, Accra strips, Joburg steps) on clothing, dock banners and the HUD |
| One art-passed district | Stand-in. The Gallery has a stone floor, arch, colonnades, plinths with show pieces, name plates, signs and warm lamps, all from boxes |
| Tiered music | Stand-in. Four synthesised layers at 113 BPM that fade in with the alarm tier and the getaway |
| Voice callouts | Stand-in. Subtitled crew radio lines per crew in Pidgin, Swahili and Sheng, Twi and isiZulu, with English underneath. **Written by a non-speaker. They must be reviewed by native speakers before anyone outside the team sees them** |
| HUD reskin | Partial. Crew pattern band, crew accent colours and specialist names. The layout is unchanged |
| Twenty artefacts with story cards | Done. Twenty named items, each with a model shape, a value and a story |
| Guards | Done. Code-built models for the three guard types |
| Weapons and effects | Done by the builder. Gun pack models on the seven weapons; Cartoon FX on impact, explosion and extraction |

Still open in M4 and not solvable in code: real character and environment art, composed music, recorded
voice, the cultural review named in 4.1, and the visual language of the hack signs.

### Feel, DOTween and the look (v0.15, code complete, not seen running)

| Request | State |
|---------|-------|
| Use Feel | Camera shake through Feel's `MMCameraShaker` and controller rumble through Nice Vibrations, both behind one adapter (`Core/Juice.cs`). No time-scale effects: freezing local time would desynchronise this peer from the network |
| Use DOTween | Doors that move (shutters roll, vault doors sink, maglocks slide), loot that bounces, breakables that flinch, and HUD and menu motion |
| A cel-shaded look of its own | Three shaders. A toon surface whose light ends in a woven band of marks, with indigo shade, warm light, ring highlights and a warm rim. A camera ink pass with brushed, wandering outlines and a colour grade. A dusk sky in flat bands with a ringed sun |
| Applied to assets and world | At run time every opaque lit material is drawn with a toon copy, including the imported gun pack; code-built models are made in the toon shader. The editor menu and the map builders bake it into the project's own materials. One setting switches it all off |

The marks in the woven edge are plain geometric shapes drawn by the shader. Like the crew patterns, they
should be looked at by someone who knows the textile traditions they nod to before this is shown publicly.

### What is missing next to The Finals

Both games are about money: you take it, you carry it, and nothing counts until it is banked. Ouroboros
banks at an exit at the end. The Finals banks in the middle of the match, in public, slowly, and lets
rivals steal the deposit. That difference is the biggest gap. In order of what I would build:

| # | Missing | Why it matters | Size |
|---|---------|----------------|------|
| 1 | **Cash-out stations.** Deposit carried loot at a terminal; it banks over about a minute; everyone is told where; a rival can hijack the deposit by hacking the terminal | Turns banking from an exit into a fight. Gives every vault a second act and uses the counter-hack the game already has | **Done in code (v0.16), not play-tested.** See below |
| 2 | **Contextual ping.** One key marks what you are looking at for your crew: enemy, loot, door, "go here" | A team shooter without voice chat needs it, and bots can read it | **Done in code (v0.17), not play-tested.** Middle mouse or T |
| 3 | **Revive, and health that comes back.** A downed crewmate can be brought back with their ID card (cards already drop); health regenerates after a few seconds out of combat | Today a death is a long wait and chip damage is permanent. Both slow the match down | **Done in code (v0.17), not play-tested.** See below |
| 4 | **Things to pick up and throw.** Explosive, gas and foam canisters lying around the map | The Finals' answer to "the room is the weapon". Breakables are the first half of this | Medium |
| 5 | **A house announcer and live money standings.** Called moments ("Lagos are cashing out in the Bank"), and a constant view of who holds what | Money only drives decisions if everyone can see it | Small. The cash-out half arrived with v0.16 (called moments, a ticker, banked and carried on the scoreboard); kills, vaults and events still need calling |
| 6 | **Passing the loot.** Throw the case or a loot item to a crewmate | Makes the carry a team play and not one player's burden | Small |
| 7 | **A cost for a crew wipe.** A wiped crew loses a share of what it has banked | Makes a wipe matter beyond the respawn timer | Small |
| 8 | **Movement gadgets.** Deployable jump pad and zipline | The Galleria has two levels and long halls; these would open it up | Medium |
| 9 | **Spectating crewmates while down** | Dead time is watch time | **Done in code (v0.20), not play-tested** |
| 10 | **Structural destruction** | The Finals' signature, and a very large engineering job. More weak walls and breakable floors get most of the feel for a fraction of the cost | Very large |

Already here in some form: several ways into every vault, gadgets, map events (their game-show events),
low gravity, destructible containers, a carried objective, loot dropped on death.

### Clean picture, vault worth, sound reach, prep and gear changes (v0.23, code complete, not seen running)

| Request | State |
|---------|-------|
| Remove the overlay on the screen | The outline pass was also grading the colour (more saturation, indigo shadows, ochre highlights) and darkening the corners toward purple. Both are off; the outlines stay. I am assuming this is the overlay you meant: it is the only thing laid over the whole picture all the time |
| One vault and a little extra hits the quota | A vault is now worth 80% of the quota ($4,800 of $6,000), made up to the same total whatever pieces it rolls |
| Sounds heard from too far | Unity's default falloff never reaches silence inside 500 m, so every shot and crate on the map was faintly audible. Every world sound now has a reach and is silent beyond it: gunfire 48 m, explosions 75 m, most things 32 m or less. Two cues that played in every player's ear are now local |
| A longer prep phase | 30 seconds |
| Change class and gear at the spawn or after respawning | At your own crew's gate, or for 15 seconds after a redeploy, B opens the class and gear screen. A class change keeps your share of health |

Design choices worth knowing: sound does not yet pass less easily through walls or floors, only over
distance; a revive in the field does not open the gear window, only a redeploy at the gate; a gamepad has no
button for the gear screen, for the same reason it has none for the ping.

### Class select, loot models and cameras (v0.22, code complete, not seen running)

| Request | State |
|---------|-------|
| Players can move during class select | Fixed. Nobody moves, shoots or uses anything until the match is live. It is enforced on the host and predicted on the client, and it holds bots too |
| Load, 10 seconds to choose with the helper on screen, then play | The countdown is 10 seconds and starts by itself. The briefing is on the class-select screen, under the cards, with the countdown in large type. It no longer appears after the start |
| Pink balls and boxes | Loose cash, ID cards and the loot case were still the prefab's own shape. Each now builds a model: notes and coins by value, a card on a lanyard in crew colour, a brass-cornered case. The one named item that shared a model has its own. The player's capsule is never shown either |
| Cameras announce and mark | A feed line for every crew names the player and the zone. A marker sits over them for every crew while the camera has them. This puts back, for cameras only, what v0.18 took away |
| Cameras send guards | The five nearest guards are sent to the spot and keep coming after the player leaves view. Snipers and a vault's own guards stay put |

Why things were pink, as far as I can tell without the project open: Unity draws a missing material pink, and
a prefab can only keep a material that was saved as an asset. Before v0.13 the builder gave prefab placeholders
materials that existed only in memory, so prefabs made then lost them, while scene objects kept theirs because a
scene saves its own. That fits what you saw (only spawned things were pink) but I have not confirmed it.
Either way the fix does not depend on the cause: anything spawned from a prefab now hides the prefab's shape
and builds its own model.

### Grand Vaults (v0.21, code complete, not seen running)

The vault was a box you hacked. It is now what you described from Hyenas: a room behind a locked door, the
main thing a crew is in the building for, with the small boxes kept as safes on the way.

| Part | State |
|------|-------|
| A room, not a box | Four Grand Vaults in the Galleria, one per wing, using the four largest strongrooms. Each has a real vault door (a steel slab with a hatch and wheel that sinks into the floor) and a lamp bar that shows shut, drilling or open |
| Three ways in | **Key**: the vault's own guards hold it; the last one down drops it; carry it to the door. **Drill**: set it at the door, then hold the doorway for 50 seconds while every crew knows and guards come; it runs back if you leave, and a rival can pull the plug. **Lock**: crack a console beside the door with the sign and pulse puzzles; quiet |
| Which vault takes which | Main Vault: key. Cash Cage: drill. Auction Strongroom: lock. Cold Store: any of the three. One field per vault (`methods`) changes it, so every vault can take all three if you prefer |
| The best loot | Legendary and rare pieces and cash, laid out in the room when it opens. Since v0.23 a vault is worth 80% of the quota ($4,800 of $6,000): one vault and a little extra calls the getaway |
| Crew perks | One per vault, for the first crew to reach the pedestal, held by the whole crew to the end of the match: Iron Skin, Hot Shots, Second Wind, Quick Hands |
| Small vaults stay | Safe Deposit, Stockroom, Server Vault and the four hall safes are unchanged, and are what the objective points at once the Grand Vaults are open |
| It never stalls | The Main Vault's time lock still opens it for everyone at eight minutes, and two vaults have a weak wall for a Breaching Kit |

Design choices worth knowing: a vault's guards are whichever guards stand within 16 m of its door when the
match starts, so adding a guard post near a door adds to its key; a key in a pocket has no marker, a key on the
floor does; the drill needs one crew member near, not a player holding a button, so the crew can fight; loot
appears only when the vault opens, so nothing can be picked up through a wall. The Freeport still has the old
vaults; converting it is a few lines per vault in its builder.

### Feel and clarity next to The Finals and Hyenas (v0.19)

A pass over what the player sees, hears and feels minute to minute, set against the two games this one is
measured by. It is a read of the code, not of the game running: where a line says "have", it means the code
is there, not that it has been tuned.

**Does the player know what to do?** This was the largest gap. The Finals keeps one objective on screen at
all times and its announcers say the rest; Hyenas put its loot target and a "go here" on the HUD from the
first second. Ouroboros had the pieces (markers, banners, a feed) but nothing that said which of them mattered now.

| Area | The Finals / Hyenas | Ouroboros before | Now (v0.19) |
|------|---------------------|------------------|-------------|
| One current objective | Always on screen | None. The rule was a sentence in the timer panel | **Objective tracker**: one instruction and how, chosen from the player's situation, with a marker on the target |
| The score | Cash totals are the biggest numbers on screen | A 300 px table of 12 to 14 px text headed "TEAMS", with points and loot in separate columns | **Cash board**: one large figure per crew, a banked and carried bar toward the quota, richest first |
| First-match teaching | Tutorial and practice range (Finals), briefing (Hyenas) | A premise line on the class picker | **Briefing card** before the match and for its first seconds: take it, bank it, get out |
| Where things are | Objective icons through walls | Stations, exits, rival cash-outs, crewmates, pings | Adds the nearest vault, and since v0.20 a **compass strip** with a pip for each of them |
| Whose is what | Team colour on everything | Crew colours on players, stations, turrets | Unchanged |

**Does the game feel good in the hand?**

| Area | The Finals / Hyenas | Ouroboros | Verdict |
|------|---------------------|-----------|---------|
| Gunplay feedback | Recoil, bloom, hit markers, kill confirm, damage numbers | Have all of these, plus damage direction and a death recap | Good on paper. Needs tuning by playing: recoil per weapon, time to kill |
| Speed you can see | View widens at a sprint; slides and landings have weight | View did not react to movement at all | **Added**: sprint and slide widen the view, landings dip and thump |
| Being hurt | Screen edge reddens, heartbeat, health regenerates | A flash per hit; regeneration since v0.17 | **Added**: a red pulse under a third of health |
| Money arriving | Coins fly, numbers tick, the announcer shouts | A feed line and a pickup sound | **Added**: figures pop on the board and on the carried readout. Banking still has no sound of its own |
| Jump forgiveness | Coyote time and jump buffering, so a jump a frame late still happens | None: a jump pressed a tick after leaving a ledge is lost | **Not done.** It changes the predicted movement code and wants testing with two clients. Small, and worth doing next |
| Mantle and slide | Mantle anything waist to head height; slide keeps momentum | Have both | Tune by playing |
| Sound | Every action has one; footsteps tell you who is near | Clip slots for shots, reloads, hits, kills, pickups and match events; no footsteps at all, no announcer voice | **Added in v0.20**, synthesised in code: footsteps for players and guards, landing, slide, pickup, and every cash-out, ping, revive and lift cue. No announcer voice, by decision. All of it is placeholder until real audio exists |
| Hit stop / time effects | Brief freeze on a kill | None, on purpose: slowing local time would desynchronise a networked peer | Leave |
| Animation | Full first-person arms, reloads, inspect | Code-built view model that kicks when fired, dips to reload and centres when aiming; no sway, no arms | Needs real art |

**What I would do next for feel, in order:** coyote time and jump buffering; a short kill-cam on the death
recap; real audio in place of the synthesised placeholders. And the session that tunes numbers: time to kill,
recoil, the 60 second cash-out, the quota, how far footsteps carry. (Footsteps and cash sounds, the compass
and spectating were on this list and arrived in v0.20.)

### Markers, cash-out markers and lift exits (v0.18, code complete, not seen running)

| Request | State |
|---------|-------|
| No icon over enemy players | The icon was the "revealed" diamond, and almost anything set it: a camera, a laser grid, a sniper taking aim. Those no longer mark anyone. Crewmates keep their markers |
| Enemy indicators only in certain scenarios | Two: your own crew tagged them with a reveal ability (only your crew sees it), or they carry loot for the richest crew during the getaway (everyone sees it). A crewmate's ping is a third, but it marks a place, not the player. Since v0.22 there is a fourth: a security camera has them in view (everyone sees it, while it lasts) |
| Cash-out spots easier to find | A coin-and-arrow marker turning and bobbing over every station, and a gold outline that is always on |
| Exits as lift doors | Every exit is a lift car. Doors shut and red until the getaway is called, then they slide open and everything goes green. They shut again while an exit resets |
| Nearest exit on the HUD | A line with the nearest open exit's name, distance and payout, and a marker on it through walls |

Design choices worth knowing: the lift car has no colliders and does not touch the NavMesh. Solid doors could
trap a player when they shut after another crew leaves, and solid walls would need the bots' paths rebuilt
around them. Making it solid is a small follow-up once the rule for "someone is inside when the doors shut" is
decided. More scenarios for marking a rival (the case carrier, whoever is hijacking your cash-out, whoever just
killed a crewmate) are one line each: `MarkFor`.

### After your Galleria run (v0.17, code complete, not seen running)

You ran the Galleria, so everything up to v0.15 compiles and plays. Two things you reported, and two you asked for:

| Request | State |
|---------|-------|
| Guards, turrets and cameras are hard to see | A bright outline on each: red for what shoots, amber for cameras, a crew's colour on a hijacked turret, nothing on a thing that is switched off. Hidden by walls. Wider with distance. The turret itself is rebuilt at about twice the size, with a beacon, and cameras are half as big again. One setting switches the outlines off |
| Upper floors cover the stairwells | Fixed. The coloured floor of each zone was one rectangle, which upstairs lay across the atrium, the gaps and the stairs. It is now cut to the real floor, and the generator refuses to write a layout where a tint covers an opening. **Update Galleria Map (keeps the scene)** applies it to your scene |
| Contextual ping | Middle mouse or T marks what is in your crosshair for your crew only: enemy, loot, something to use, a crewmate to revive, or a place. Bots you lead go to it |
| Revive and regeneration | Hold Interact at a downed crewmate's ID card for 3.5 s and they stand up there on 40% health. It costs no respawn and works during the getaway. Rivals taking the card denies it. Health comes back at 12 a second after 6 s without damage |

Design choices worth knowing: the outline draws a line round every body part, not one silhouette, because that
is one draw per part instead of three; pings are placed where the thing was and do not follow it, so an enemy
ping is not a tracker; a revive happens where the player fell, not at the card.

### Cash-out stations (v0.16, code complete, not seen running)

| Part | State |
|------|-------|
| Deposit | Hold Interact at a station for a second. Everything you carry, a case included, goes into the pot. The station takes your crew's colour and a column of light marks it across the hall |
| Public | A banner and a feed line tell every crew who, how much and in which zone. A ticker under the match timer lists every running cash-out. Markers point at them through walls: DEFEND on yours, STEAL on a rival's. The alarm rises |
| Slow | 60 seconds to bank. Crewmates can add more, which adds 10 seconds |
| Stealable | A rival holds Interact for 4.5 seconds (Cipher 40% faster) and the pot and the timer become theirs, with at least 18 seconds left to hold. An owner standing at the station blocks it. The countdown freezes while a hijack is under way |
| Counts | Banked money scores at once and counts toward the getaway quota with what is carried. The exits still bank whatever is left at the end |
| Bots | Bank when carrying enough, stand on their own cash-out, go for a rival's |
| Where | Galleria: beside the escalators in the North and South Concourses, and on each side of the upper level. Freeport: either side of the plaza |

Design choices worth knowing: the hijack is a held interaction and not the sign-matching hack, so any class
can do it under fire; a cash-out still running when the match ends is lost; all four numbers above are fields
on the station.

### After your first run (v0.14, code complete, not play-tested beyond the main menu)

You ran the project and the main menu came up, which confirms the code compiles in Unity and through the
Fusion weaver. Everything below was written after that and has only been through the offline checks.

| Request | State |
|---------|-------|
| Menu text fit | Fixed. The title was wider than its column; columns are now sized from the screen |
| Bigger, indoor, multi-level map | Done in code as a new map, the **Galleria**: a two-level mall around a bank, 220 by 180 m against the Freeport's 140 by 140. A central atrium, four concourses, four anchor wings (Bank, Luxe Wing, Market Hall, Services), four corner blocks of shops with staff corridors, four yards with a crew gate each, and an upper level of walkways and four secure rooms. 83 named zones. The Freeport is still there |
| Multiple vaults behind locked rooms | Eleven vaults. Main Vault (passcode, laser corridor, breachable wall), Safe Deposit (red keycard), Cash Cage (blue keycard), Auction Strongroom (lockdown override or breachable wall), Cold Store (rival's ID card), Stockroom (maglocked), Server Vault (laser doors), and a safe in each of the four halls |
| Roaming and stationary security | Two patrols (concourses and upper galleries), thirteen guard posts including elites at the vaults, snipers on the galleries, cameras, turrets, laser grids, shutters |
| Points of interest, loot drops and events | Zones are named on the HUD. A public schedule runs each match: an armoured delivery in the Truck Bay, a supply drop in the Atrium, a power failure, and the Main Vault's time lock releasing. Each is announced before it happens and marked on the HUD |
| More modelled pieces | Fifteen mall props, ten breakable kinds, a camera, a laser grid, a maglock door, an armoured truck |
| Destructibles with loot | About 120 containers: display cases, vending machines, cash machines, tills, lockers, crates, and armoured strongboxes and safes |
| Gear to solve puzzles | Every specialist carries a gadget chosen in the lobby. Each obstacle has more than one answer, and each answer is a piece of gear, a skill or a route |
| Damage indicators | Floating numbers on every hit you land, yellow for headshots, red for the final hit |
| Death summary | A recap naming the final blow and every source of damage that life, with amounts, hits and shares |

How gear opens the building:

| Obstacle | Ways through |
|----------|--------------|
| Laser grid | EMP (Cipher ability, Spider sabotage, EMP Emitter gadget), hack its control box, cut the district's power, or power-slide under the lowest beam |
| Maglocked staff door | Bypass Key gadget, or cut the district's power |
| Weak wall, strongbox, safe | Breaching Kit gadget, the Smith's breaching charge, any explosive, or a single hit of 40 or more |
| Shutter | Hack its control box, from either side |
| Vault doors | Keycard found elsewhere, passcode from a note, a rival's ID card, or a loud lockdown override. Two vaults can also be entered through a weak wall |
| Upper level without stairs | Atrium lift pads (hack), Grapple Line gadget, the Smith's grapple, the Spider's climb, or killing the Atrium's gravity |

What I could not check without Unity, in the order I would test it: the NavMesh across two levels, the frame
rate with this many objects and lights, bots finding their way through a building this size, and the
balance of about 120 loot containers against the getaway quota (set to 6000 on this map).

### M5 progress (code complete, not yet play-tested)

M5 was started at your request before the first play session. Everything here compiles and the rules are
unit-tested, but none of it has run in Unity.

| M5 item | State |
|---------|-------|
| Main menu | Done in code. Quick play, solo (offline with bots), host a private match, join with a code, name entry. Shown on Play unless `showMainMenu` is unticked on `GameSessionManager` |
| Party codes | Done in code. A five-character code is the session name of a private match; the pause menu shows it |
| Session browser | Not done. Quick play and codes cover finding a match for now |
| Pause menu | Done in code. Esc: resume, settings, controls, profile, leave match (reloads the scene to the main menu), quit |
| Settings | Done in code. Look and aim sensitivity, field of view, invert Y, master and music volume, teammate markers, crew radio, full screen. Stored per machine |
| Controls and keybinds | Done as a read-only list of every action for keyboard and mouse and for gamepad. Rebinding has code but no screen |
| Loadout screen | Unchanged: the lobby picker chooses specialist and both weapons |
| Post-match summary | Done in code. The end screen adds the XP gained and the level |
| Cosmetic progression | Done in code, local only. XP, 50 levels and eight titles. Nothing it unlocks changes a match |
| Dedicated servers or host migration | **Recommendation: stay player-hosted and add Fusion host migration before any public test.** Dedicated servers cost money every hour a match runs and need an operations pipeline this project does not have. Host migration removes the worst failure of player hosting (the host leaves, the match dies) at the cost of engineering only. Not implemented yet |

Also added with M5, at your request:

| Item | State |
|------|-------|
| Gun mechanics | Done in code. One weapon in hand; the wheel swaps, left click fires, right click aims down sights. Aiming tightens spread, cuts kick, slows the walk, blocks sprint and zooms. Draw time on swap. A first-person view model with draw, aim, reload and kick motion |
| Teammate indicators | Done in code. A crew-coloured diamond, name, health and distance over each crewmate, visible through walls, pinned to the screen edge when they are out of view. Rivals show nothing unless a reveal has tagged them |
| Every piece of gear visible | Done in code. Stand-in models for weapons without a prefab, the spare weapon on the back, worn armour, speed rig and ammunition belt, the trap, the placed charge, grenades with a trail, the recon drone, and an effect for all sixteen abilities and eight statuses |
| Environment pieces | Done in code. Eighteen kinds of prop and structure modelled from primitives |
| Map build-out | Done in code. 75 props across every district and three new areas (bonded store, courtyard, motor pool), a crane, getaway vans at the side exits, district floors, signs and lighting. No new vaults |

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

1. Run **Ouroboros > Setup > Create Galleria Map (M6)**. It refreshes the assets (gadgets, weapon handling,
   models) and builds the `Galleria` scene. Expect it to take a while: it places several thousand objects
   and bakes a two-level NavMesh.
2. With the Galleria scene open, press Play and choose **Solo**.
3. Work through sections 7s, 7r, 7q, 7p, 7o, 7n, 7m and 7l of `PHASE0_BRINGUP.md` first (the newest work), then 7j and 7k, then 7e to 7i,
   and list what breaks. The Grand Vaults (v0.21) need **Recreate Galleria Map**; "Update Galleria Map" does not add them.
4. Tell me the frame rate. If it is low, the first cuts are the ceiling lights and the number of props.
5. Find native speakers of Nigerian Pidgin, Swahili or Sheng, Twi and isiZulu to rewrite the crew radio lines
   in `Assets/Scripts/UI/CrewCallouts.cs`, and someone who can review the artefact names and stories.

You have had the Galleria running, which covers everything up to v0.15. Cash-out stations (v0.16) and all of
v0.17 have only been through the offline checks.

Notes on the repository as of the M2 work:
- `main` holds M0, M1 and your Unity project state ("Squashed local work"). M2 is on `claude/awesome-darwin-awl360`.
- `ViewMode.cs` and the Keybinds panel are not in the pushed project, so nothing blocks ADS any more.
- The pushed project references a URP global settings asset but the URP package is not in `Packages/manifest.json`.
  Either finish the switch (install Universal RP, assign a pipeline asset, upgrade materials) or remove the stray asset.
- The SciFi Gun Pack and Cartoon FX Remaster are now used by the art pass (weapon models, three effect slots).
  LaserGunPack is still unused; its turret models could replace the box turrets.

## Sources

- HYENAS match structure: [Film Stories preview](https://filmstories.co.uk/features/hyenas-preview-could-have-been-one-of-the-best-arena-shooters-in-recent-years/), [GGRecon preview](https://ggrecon.com/articles/hyenas-preview), [TheSixthAxis](https://www.thesixthaxis.com/?p=443285), [GGRecon guide](https://www.ggrecon.com/guides/hyenas-trailers-gameplay/)
- Fusion 2 character controllers: [What's New in 2.0](https://doc.photonengine.com/fusion/v2/getting-started/release-notes/whats-new-2-0), [Advanced KCC features](https://doc.photonengine.com/fusion/v2/addons/advanced-kcc/features)
- Relooted: [Gematsu announcement](https://www.gematsu.com/2025/06/africanfuturist-heist-game-relooted-announced-for-xbox-series-pc), [Epic Games Store](https://store.epicgames.com/p/relooted-89fdd7)
