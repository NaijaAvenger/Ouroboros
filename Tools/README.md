# Tools: checks that run without Unity

Two checks that need only the .NET SDK (8 or newer). They exist because a compile error or a broken rule
should be caught on push, not the next time someone opens the editor.

```
dotnet test Tools/tests        # about 5 seconds
Tools/typecheck/run.sh         # about 1 minute the first time, 20 seconds after
```

Nothing in this folder is seen by Unity (it lives outside `Assets/`).

## `tests/`: unit tests

NUnit tests for the parts of the game that have no Unity or Fusion dependency:

| Source under test | What is covered |
|-------------------|-----------------|
| `Assets/Scripts/Core/MatchRules.cs` | winner and tiebreaks, alarm tiers, loot split, crack rate, carry weight |
| `Assets/Scripts/Core/GameConstants.cs` | enum and constant invariants other code relies on |
| `Assets/Scripts/AI/BehaviorTree.cs` | sequence, selector, inverter, reset and resume behaviour |

The sources are compiled straight from `Assets/Scripts`, so there is no second copy.

**Adding a rule:** put the arithmetic in `MatchRules` (plain C#, no `UnityEngine`), call it from the
`NetworkBehaviour`, and add test cases in the same commit. To test another file, add a `<Compile Include>`
line to `Ouroboros.Rules.Tests.csproj`; it must not use Unity or Fusion types.

## `typecheck/`: offline compile check

`run.sh` compiles every script Unity would put in `Assembly-CSharp` and `Assembly-CSharp-Editor`:

1. Builds the third-party code the game references, mirroring Unity's assemblies: the Input System package
   (cloned at the version in `Packages/manifest.json`), `Photon.Realtime` and `Fusion.Unity`.
2. Compiles the game's runtime scripts four ways: Input System + legacy input (the project setting), the same
   with `UNITY_EDITOR`, Input System only, and legacy input only.
3. Compiles the game's editor scripts.

Scripts are picked up by folder, so new files and folders under `Assets/` are checked automatically.
Third-party packs are skipped: add a new pack's folder to `VendorExcludes` in `typecheck/Directory.Build.props`,
or the check will try to compile it as game code.

### What a pass means

Every game script parses and type-checks against the real Fusion 2.1.3 assemblies in `Assets/Photon`, the real
Input System source, and Unity module reference assemblies. Missing members, wrong signatures, missing `using`
lines and broken `#if` branches are caught.

### What a pass does not mean

- **Reference assemblies are Unity 2021.3, the project is Unity 6000.4.** An API that exists only in Unity 6
  would be reported as missing here; an API removed in Unity 6 would be missed. Neither has come up so far.
- **The Fusion IL weaver does not run.** Rules it enforces (valid `[Networked]` property types, RPC signatures,
  capacity attributes) are only checked by Unity.
- **Nothing is executed.** Scenes, prefabs, serialized references and play-mode behaviour are not touched.
- `EditorStubs.cs` stands in for `InputSystemUIInputModule` and `Fusion.Editor.NetworkProjectConfigUtilities`;
  only the members the game's editor scripts call are declared.

A pass is necessary, not sufficient. The two-client checklist in `PHASE0_BRINGUP.md` is still the real test.

### Reference sources

| What | Where it comes from |
|------|---------------------|
| Unity engine modules | NuGet `UnityEngine.Modules` 2021.3.33 |
| `UnityEditor.dll` | NuGet `Unity3D.SDK` 2021.1.14.1 |
| `UnityEngine.UI.dll` | NuGet `Unity3D.UnityEngine.UI` 2020.3.21 |
| Input System | github.com/Unity-Technologies/InputSystem, tag from `manifest.json` |
| Fusion, Photon | the DLLs and sources already in `Assets/Photon` |

The NuGet packages are unofficial redistributions used as compile-time references only. They are downloaded
into `typecheck/.cache/` and never committed.
