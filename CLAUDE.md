# Scrap Yard King — working guide (for people and Claude Code)

Claude Code reads this file automatically at the start of every session in this folder. Humans: read it once before
your first change. Deeper design lives in `Docs/ARCHITECTURE.md`; asset credits in `Docs/THIRD_PARTY.md`.

## What this is

A mobile **action + idle tycoon** in the style of *Chainsaw Juice King*. The player cuts scrap with a chainsaw,
carries the pieces on their back, feeds machines, sells the output and upgrades. Over the first session they hand
routine work to workers and expand the yard.

Core loop: **cut → collect → process → store → sell → cash → upgrade / hire → expand**.

The source of truth for design is the *Scrap Yard King — Game Design + Top-Down Map Blueprint* PDF (90-minute first
session, 120 × 90 unit world, 6 areas, blueprint numbers such as Crusher Lv.1 = 10 scrap / 8 s, Storage = 40,
Sell Desk = 1 customer / 8 s). `Docs/ARCHITECTURE.md` summarises the parts code depends on.

## Requirements

- **Unity 6 or newer** (any `6000.x`). Developed on 6000.4; nothing depends on a specific 6.x patch.
- Render pipeline: **URP** (Mobile and PC renderer assets in `Assets/Settings`).
- Input: **Input System only** (the old Input Manager is disabled).
- Packages (in `Packages/manifest.json`): Input System, ugui (includes TextMeshPro), ProBuilder, AI Navigation,
  Test Framework, URP.
- Optional, for Claude to drive the Editor: **Unity CLI** and the **`com.unity.pipeline`** package (see below).

Opening on a different Unity 6 version:
- Let Unity upgrade the project. URP is a core package, so its version follows the Editor automatically.
- If Package Manager cannot resolve a package, remove it from `manifest.json` and re-add the version Package
  Manager offers. None of these are needed by the game: `com.unity.ai.assistant`, `com.unity.ai.inference`,
  `com.unity.multiplayer.center`, `com.unity.visualscripting`, `com.unity.collab-proxy`.
- `com.unity.ai.assistant` spams `NoSubscription` logs without a Unity AI subscription. It is safe to remove.

Third-party content already sits inside `Assets/`, so the project opens and runs without downloading anything:
- Kenney CC0 packs (`Assets/ThirdParty/Kenney`).
- DOTween (`Assets/Plugins/Demigiant`).
- 300Mind UI kit (`Assets/300Mind`).

DOTween and 300Mind come from the Unity Asset Store and fall under its per-seat licence. Each developer should add
them (both free) to their own Asset Store account.

## Play it

1. Open `Assets/_Project/Scenes/Area1_OldScrapYard.unity` (build index 0).
2. Set the Game view to a portrait resolution, for example 1080 × 1920.
3. Press Play. Move with **WASD / arrows**, or press and drag anywhere to use the floating joystick.

Loop: walk into scrap to auto-cut. Pieces fly onto your back (max 8). Then:
- **Yellow pad** at the Crusher unloads your scrap.
- **Green pad** at Storage picks up steel bales.
- **Blue pad** at the Sell stall stocks the counter.
- **"$" pad** collects the cash.

## Project layout

```
Assets/
  _Project/
    Scripts/Runtime/        ScrapYardKing.Runtime.asmdef (namespaces ScrapYardKing.*)
      Core/                 Services registry, GameEvents, ModifiableStat, GameManager, GameConfig, Easing
      Items/                ItemDefinition, WorldItem, ItemPool, CarryStack, ItemCollector, ItemPile, TransferPad
      Harvest/              ScrapDefinition, ScrapObject, ScrapPart, ScrapManager, ScrapSpawnPoint, HarvestManager, HarvestTool
      Factory/              MachineDefinition, Machine, MachineVisuals, Conveyor, Storage(+Definition), SellDesk(+Definition)
      Economy/              EconomyConfig, EconomyManager, CashPile, CashCollector, CurrencyFormat
      Player/               PlayerConfig, PlayerStats, PlayerCharacter, PlayerController, PlayerInputReader, PlayerVisuals
      Feedback/             GameFeedback facade, Audio/VFX managers, HitStop, FloatingText, ProceduralSfx
      CameraSystem/         CameraController
      UI/                   VirtualJoystick, HudController, CurrencyWidget, UIFlyer, StationLabel, CarryStackIndicator
    Tests/EditMode/         Unit tests (ScrapYardKing.Tests.EditMode.asmdef)
    Data/                   ScriptableObject assets: Config, Items, Scrap, Factory, Economy, Audio
    Prefabs/                Player, Scrap, Stations, Items, Economy, UI, VFX
    Art/                    Materials, Textures, Meshes (baked), Animation (controllers), Fonts (GROBOLD SDF)
    Scenes/                 Area1_OldScrapYard.unity
  ThirdParty/Kenney/        CC0 models + textures (one colormap palette per pack; keep packs in separate folders)
  Plugins/Demigiant/        DOTween (+ DOTween.Modules.asmdef)
  300Mind/                  UI sprites + GROBOLD font
AgentScripts/               Editor builder scripts (outside Assets, not compiled into the game)
Docs/                       ARCHITECTURE.md, THIRD_PARTY.md
```

## Architecture rules (keep these)

- **Data-driven.** Balance numbers live in ScriptableObjects (`PlayerConfig`, `ScrapDefinition`, `MachineDefinition`,
  `StorageDefinition`, `SellDeskDefinition`, `EconomyConfig`, `FeedbackConfig`). MonoBehaviours only hold
  presentation and feel values. A new machine type should be a new `MachineDefinition` asset, not new code.
- **Services.** Scene systems derive from `ServiceBehaviour<T>` and register in `Awake`, with execution order -500
  to -1000. Consumers resolve with `Services.TryGet` in `Start` or lazily, never in `Awake`/`OnEnable`.
- **Events.** Gameplay raises `GameEvents` (ScrapBroken, ItemsCollected, ItemsProcessed, ItemsSold, CashEarned).
  Progression code (tasks, XP) listens to these and never gets called directly by gameplay.
- **Physical items.** Every resource is a pooled `WorldItem` that flies, lands, gets carried and gets fed into
  stations. Anything that takes items implements `IItemReceiver`; anything that gives them implements `IItemSource`.
  Pads, machines, conveyors, storage, counters and carry stacks connect only through these two interfaces, so workers
  reuse exactly what the player uses.
- **Visible bottlenecks.** A full storage backs up the conveyor, which blocks the crusher (red light), which fills
  the hopper, which stops the deposit pad. Keep that chain; don't add hidden queues.
- **Modifiable stats.** Upgrades and overdrive add `StatModifier`s to a `ModifiableStat`. Never overwrite base values.
- **Feedback.** Call `GameFeedback.Sfx/Vfx/HitStop/CameraShake/CameraPunch/Popup`. Each call no-ops when its service
  is missing, so prefabs work in empty test scenes.
- **DOTween is for presentation only:** punches, pops, jumps, counters, UI fly-ins. Items flying into stacks or
  hoppers use `WorldItem.MoveTo`, which follows moving parents. `DOTween.Init` happens in `GameManager`.
- **No placeholder architecture.** Build each system the way it will ship; placeholder *content* (primitive art,
  procedural sounds) is fine.

## How we work (process rules from the project owner)

- **One milestone at a time.** After each one, play-test the real loop, stop, and report: what was implemented,
  files/classes, how it works, what remains for the next milestone, and architectural risks.
- Don't build future areas until the current one is fun. No multiplayer, no complicated inventory, no unnecessary
  menus, no spreadsheet feel. The player character stays central.
- Every 5–10 minutes of play should produce a visible change. No mandatory idle waiting.
- Quality and feel matter. Every action gets feedback: sparks, hit-stop, pops, sounds, counters.
- Art direction: stylized, chunky, saturated mobile look. Kenney CC0 models, ProBuilder for custom station geometry,
  GROBOLD font, 300Mind UI sprites. New art should match; the reference is a screenshot the owner shared (red
  crusher, yellow sorter, conveyors, colored material bins, market stall with green cash stacks).

## Milestones

| # | Scope | Status |
|---|---|---|
| 1 | Movement, camera, cutting, drops, pickup, carry stack, feel | Done |
| 2 | Crusher → conveyor → storage → sell desk → cash pile → HUD, art pass | Done |
| 3 | Upgrades (cards, "+" buttons), tasks, XP/level, first Porter worker | **Next** |
| 4 | Customer queue (replaces the Sell Desk walk-in timer), worker automation, first expansion gate | |
| 5 | Sorter, Iron, Recycling Plant (Area 2) | |
| 6 | Furnace, Copper, heavy scrap | |
| 7 | Operator/Loader/Seller specialisation, Giant Scrap event | |
| 8 | Dockyard reveal, save/load, balance pass, polish | |

Notes for M3/M4:
- `SellDesk.WalkInDemand` should be turned off when customers arrive; customers call `SellDesk.ServeCustomer`.
- Level data and upgrade costs already exist on the Machine, Storage and Sell Desk definitions.
- `Machine.Speed` is the hook for Overdrive and Operators.

## Tests

- EditMode tests live in `Assets/_Project/Tests/EditMode`. Run them from Window → General → Test Runner, or with the
  CLI: `unity command run_tests --mode editor --filter ScrapYardKing.Tests.EditMode --filter_type assembly`.
- Add tests for pure logic such as stat math, drop math and economy formulas.
- Gameplay is verified in Play mode, as described below.

## Working with Claude Code

Claude can work in two ways.

**A. Code only (no extra setup).** Claude edits C# and assets as files. You press Play and report back what you see.

**B. Driving the live Editor (recommended).** Claude can compile, run tests, enter Play mode, move the player,
inspect state and take screenshots itself. Setup:

1. Install the Unity CLI (macOS/Linux):
   `curl -fsSL https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.sh | UNITY_CLI_CHANNEL=beta bash`
   On Windows (PowerShell), run
   `$env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex`.
2. Make sure `com.unity.pipeline` is in the project. It is already in `manifest.json`; otherwise run
   `unity pipeline install`.
3. Open the project in Unity, then check with `unity status`. You should see this project with state `ready`.

Useful commands (always pass `--project-path <this folder>`, because several Editors may be open):

```
unity command                       # list commands this Editor exposes
unity command recompile             # then poll: unity command recompile_status
unity command console --level warn  # read errors/warnings
unity command editor_play | editor_stop
unity command eval '<C# expression block>'                  # quick inspection / teleport / state dumps
unity command run_script --file AgentScripts/X.cs --entry X.Run  # bigger editor scripts, no domain reload
unity command capture_game_view --format json --width 1080 --height 1920   # base64 PNG incl. UI
unity command screenshot --view game --output /abs/path.png                # camera-only PNG
```

### Known gotchas (learned the hard way)

- The CLI version may reject `--caller/--skill` flags, and arguments are `--name value`, not `name=value`.
- **Play mode freezes when Unity is in the background** (the OS throttles it; `Time.time` stops advancing). Run
  `unity command editor_focus` before each timed step of a scripted play test. Note that this brings Unity to the
  front of the screen.
- Newly written `.cs` files can occasionally be left out of compilation ("type not found" although the file exists).
  Check `CompilationPipeline.GetAssemblies()` source counts. The fix is to delete the file and its `.meta`, refresh,
  recreate the file, and refresh again.
- `recompile_status` and `console` can show stale results; `clear_console` before checking.
- The first frames after new TextMeshPro material keywords can render as cyan blocks (async shader compile). This
  is not a bug.
- DOTween's Utility Panel deletes hand-made asmdefs. Keep `createASMDEF = true` in
  `Assets/Resources/DOTweenSettings.asset`; the `DOTween.Modules.asmdef` it generates is referenced by
  `ScrapYardKing.Runtime`.
- In editor scripts, use `TryGetComponent` instead of `GetComponent<T>() ?? AddComponent<T>()` (fake-null).
  MaterialPropertyBlocks are not saved in prefabs, so use materials.
- `capture_game_view --save_path` writes inside `Assets/`, which pollutes the project. Prefer the base64 output or
  `screenshot --output`.

### Builder scripts (`AgentScripts/`)

These editor scripts generated the current content. They are idempotent: re-running updates assets in place and
keeps GUIDs. Run them in this order:

1. `M1_Setup.Run`: layers (Ground 6, Characters 7, Scrap 8), TMP essentials, portrait.
2. `M1_Assets.Run`: M1 materials, hit/break VFX, SFX, scrap definitions.
3. `M2_Import.Run`: imports DOTween and 300Mind from the local Asset Store cache (macOS path
   `~/Library/Unity/Asset Store-5.x`). Only needed on a fresh project.
4. `M2_Build.Assets`: ground textures, GROBOLD font, baked meshes, items, M2 data.
5. `M2_Build.Prefabs`: scrap, stations, pads, labels, player + Animator.
6. `M2_Build.Scene`: **rebuilds the scene from scratch.**

> **Warning:** once anyone hand-edits `Area1_OldScrapYard.unity`, do not run `M2_Build.Scene` again; it discards
> those edits. For later milestones, either add stations to the scene directly (live Editor commands or by hand) or
> write a new builder that edits the existing scene instead of recreating it. `M2_Build.Prefabs` likewise regenerates
> station and player prefabs, so hand edits to those prefabs are lost if it is re-run.

## Conventions

- C#: `namespace ScrapYardKing.<Folder>`; `[SerializeField]` private fields with read-only properties; XML doc
  comments on public types and non-obvious members; no magic balance numbers in MonoBehaviours.
- Put new runtime code in the matching folder under `Scripts/Runtime`, and editor-only code in an `Editor` asmdef.
- Keep world coordinates in the blueprint's frame: origin at the south-west corner, +X east, +Z north, ground y = 0.
  Area 1 spans x 0–40, z 10–38, and the road is z 0–10.
- Update `Docs/ARCHITECTURE.md` when adding a system or data asset, and `Docs/THIRD_PARTY.md` when adding external
  content.
