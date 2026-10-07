# Scrap Yard King — working guide (for people and Claude Code)

Claude Code reads this file automatically at the start of every session in this folder. Humans: read it once before
your first change. Deeper design lives in `Docs/ARCHITECTURE.md`; asset credits in `Docs/THIRD_PARTY.md`.

## What this is

A mobile **action + idle tycoon** in the style of *Chainsaw Juice King*. The player cuts scrap with a chainsaw,
carries the pieces on their back, feeds machines, sells the output and upgrades. Over the first session they hand
routine work to workers and expand the yard.

Core loop: **cut → collect → process → store → sell → cash → upgrade / hire → expand**.

**A revamp is in progress** (owner's brief, 2026-10-06): the game must be fast, and the production chain becomes
scrap → Crusher → Raw Metal → Metal Splitter → four metals → four Furnaces → Press → bars → truck contracts. It is
built one step at a time (R1, R2, ...); the plan, targets and status are in `Docs/QUALITY_PLAN.md`. Until a step is
built, the older part of the chain (one Furnace, ingots, customers as the main sale) stays as it is.

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
- 300Mind UI kit (`Assets/300Mind`): only its GROBOLD font is still used.
- The owner's painted UI art (`Assets/UI`: three atlases, loading background, title logo, app icon).

DOTween and 300Mind come from the Unity Asset Store and fall under its per-seat licence. Each developer should add
them (both free) to their own Asset Store account.

## Play it

1. Open `Assets/_Project/Scenes/Area1_OldScrapYard.unity` (build index 0).
2. Set the Game view to a portrait resolution, for example 1080 × 1920.
3. Press Play. Move with **WASD / arrows**, or press and drag anywhere to use the floating joystick.

Progress is saved (every 20 s, after purchases, and when you stop). Play mode loads the save like a device would. To
start over: menu **Scrap Yard King → Save → Delete Save**.

Loop: walk into scrap to auto-cut. Pieces fly onto your back (max 8). Then:
- **Yellow pad** at the Crusher unloads your scrap.
- **Green pad** at Storage picks up steel bales.
- **Blue pad** at the Sell Desk stocks the counter. Customers queue on the road and take their order at once.
- **"$" pad** collects the cash.
- **Orange UPGRADES tile** opens the upgrade panel while you stand on it (walk off to close).
- **Dark tiles with a price** hire workers (Scrap Porter, Delivery Helper, Metal Hauler, Market Runner, Smelter, Loader,
  an Operator per machine, a Seller per counter) or open an area (Back Lot, Recycling Plant at the east gate, Furnace
  hall in the plant, Heavy Scrap Yard at the north gate, Truck Dock in the plant's south-east corner, Dockyard through
  the Furnace hall's east wall): stand on one and
  your cash drains into it. "LV n" = yard level too low.
- **Claw Crane** (tile beside the Scrap Porter's, from yard Lv 4, right after the Back Lot): a tower crane at the corner
  of the work floor. It picks loose scrap inside its reach and drops it into the Crusher by itself. Two more levels on
  the same tile: each adds a section of jib, a bigger grab and speed.
- **Heavy Yard crane** (tile east of Gate 3 inside the Heavy Yard, yard Lv 9): picks loose scrap in the Heavy Yard and
  drops it on a belt that runs through the gate and tips it into the scrap pit, where the Claw Crane takes over.
- **Boosts and offers**: now and then a button on the right edge offers a timed boost (2X CASH, 2X FACTORY, FAST BOOTS,
  SCRAP RUSH, TRUCK RUSH) or FREE CASH for watching a video; after a completed truck order it offers to pay the order
  again. Running boosts show as chips under the cash. Ignoring the button costs nothing. In the Editor the "video" is
  a 0.8 s pause. After at least two minutes away the game opens with a "while you were away" cash gift.
- **Round BOOST pads** next to the Crusher, Sorter and Furnace: stand on one for 1.5 s and the machine runs at 2x for 10 s.
- **Recycling Plant** (east of the yard): carry Raw Metal to the yellow Metal Splitter; it splits it into four
  colour-coded streams (iron, aluminum, copper, steel), each with its own belt and bin; stock the Metal Market from the
  bins. Market customers ask for one material each. One full bin stops the Splitter.
- **Furnaces** (plant, north-east hall): four in a row against the north wall, one per metal. The Iron Furnace comes
  with the hall; Copper, Aluminum and Steel are bought on the tile in their slot, a yard level apart. Carry a metal from
  its bin to the pad in front of its furnace (the pad shows the metal); ingots ride one collector belt to the Ingot
  Rack and sell at the Metal Market or by truck for about 2.5x. A full rack stops all four.
- **Heavy Scrap Yard** (through the gate in the north wall of the scrap field): tractors, trucks and garbage trucks need
  a stronger chainsaw (power 30 / 35 / 45) and burst into 25–60 pieces, sometimes with iron or copper chunks.
- **Operators** (tile at the console beside the Crusher, Sorter and Furnace): the hired operator stands at the console
  and that machine runs x1.5. **Sellers** (tile beside each counter) work the customer line: service x1.6.
- **Industrial Press** (south end of the Furnace hall, a build on its own tile): carry ingots from the Ingot Rack to its
  pad; each ingot is pressed into a bar that rides a belt to the Bar Storage.
- **Truck Dock** (plant, south-east corner): a truck backs in with an order (shown on the HUD card and the dock label,
  e.g. IRON ORDER 9/12). Carry bars from the Bar Storage to the blue pad. Bars the order asked for pay a bonus; the
  truck takes any bar. It leaves when the order is met or the bed is full and pays onto the dock's cash pallet. The
  Loader (hired on a tile that appears with the Press) feeds the Press and loads the truck.
- **Giant Scrap** (Heavy Scrap Yard, striped zone under the crane): every heavy vehicle you break counts on the sign
  (3 for the first, 8 after that). Then a Giant Truck drops in: cut power 40, 6,000 health, a boss bar on the HUD, 16
  parts that fly off, and a $5,000 bonus on top of 140+ pieces.
- **Dockyard** (Gate 4, east wall of the Furnace hall): the ship and the two cranes are visible over the wall from the
  start; opening the gate reveals the quay. The **cargo ship** is a dock like the truck's, four times the size: carry
  bars to the blue pad at the quay's edge, they pile up on the stern, the ship sails when its order is met or the hold
  is full, pays onto the pallet on the quay and comes back.

## Project layout

```
Assets/
  _Project/
    Scripts/Runtime/        ScrapYardKing.Runtime.asmdef (namespaces ScrapYardKing.*)
      Core/                 Services registry, GameEvents, ModifiableStat, GameManager, GameConfig, Easing, ISaveable + SaveRegistry
      Items/                ItemDefinition, WorldItem, ItemPool, CarryStack, ItemCollector, ItemPile, TransferPad
      Harvest/              ScrapDefinition, ScrapObject, ScrapPart, ScrapManager, ScrapSpawnPoint, HarvestManager, HarvestTool,
                            DropBlocker
      Factory/              MachineDefinition (+ weighted outputs), Machine (+ output ports), MachineVisuals, WeightedSpread,
                            Conveyor, Storage(+Definition), SellDesk(+Definition), TruckBay(+Definition), OverdriveConfig,
                            ClawCrane(+Definition)
      Economy/              EconomyConfig, EconomyManager, CashPile, CashCollector, CurrencyFormat
      Player/               PlayerConfig, PlayerStats, PlayerCharacter, PlayerController, PlayerInputReader, PlayerVisuals
      Progression/          XP/levels, UpgradeManager + catalog, player stat upgrades, TaskManager, GuideDirector/Anchor/Marker
      Workers/              WorkerDefinition, Worker (NavMeshAgent), WorkerSite/PorterRoute, PorterBrain, WorkPost, PostBrain,
                            WorkerManager
      Tiles/                Tile (stand-on base), PurchaseTile (pay-by-standing), UpgradeTile (opens the panel), OverdriveTile
      Customers/            CustomerConfig, CustomerQueue (on the sell desk line), Customer, CustomerBubble
      World/                ExpansionDefinition, Expansion (locked area: barriers, reveal, drop area), GiantEventConfig,
                            GiantScrapEvent, AmbientMotion, AmbientPath
      Feedback/             GameFeedback facade, Audio/VFX managers, HitStop, FloatingText, ProceduralSfx
      Persistence/          SaveManager, SaveData, SaveFile
      CameraSystem/         CameraController
      Boosts/               BoostDefinition, BoostManager, AdService, AdOfferDefinition, OfferDirector, IdleIncomeManager
      UI/                   Joystick, HUD (cash/gems, LevelBadge, TaskBanner, UpgradePanel/Card, Announcer, GuidePointer,
                            GiantScrapBar, ContractWidget, BoostBar, OfferButton, OfflinePanel), labels, Billboard,
                            LoadingScreen
    Tests/EditMode/         Unit tests (ScrapYardKing.Tests.EditMode.asmdef)
    Data/                   ScriptableObject assets: Config, Items, Scrap, Factory, Economy, Audio, Progression, Workers, Customers, World
    Prefabs/                Player, Scrap, Stations, Items, Economy, UI, VFX, Workers, Customers, Tiles, Props
    Art/                    Materials, Textures, Meshes (baked), Animation (controllers), Fonts (GROBOLD SDF), UI (shapes),
                            UI/Kit (painted sprites cut from Assets/UI), Icons (rendered)
    Scenes/                 Area1_OldScrapYard.unity
  ThirdParty/Kenney/        CC0 models + textures (one colormap palette per pack; keep packs in separate folders),
                            Audio/ (Impact, Interface, RPG, Casino, Jingles: the clips on every Sfx_* asset)
  UI/                       The owner's painted UI sources (atlases, LoadingBG, MainTitleLogo, MainAppIcon). Never
                            referenced by the game: UI_Build.Kit cuts them into _Project/Art/UI/Kit
  Plugins/Demigiant/        DOTween (+ DOTween.Modules.asmdef)
  300Mind/                  GROBOLD font (its UI sprites are no longer used)
AgentScripts/               Editor builder scripts (outside Assets, not compiled into the game)
Docs/                       ARCHITECTURE.md, THIRD_PARTY.md
```

## Architecture rules (keep these)

- **Data-driven.** Balance numbers live in ScriptableObjects (`PlayerConfig`, `ScrapDefinition`, `MachineDefinition`,
  `StorageDefinition`, `SellDeskDefinition`, `EconomyConfig`, `FeedbackConfig`). MonoBehaviours only hold
  presentation and feel values. A new machine type should be a new `MachineDefinition` asset, not new code.
- **Services.** Scene systems derive from `ServiceBehaviour<T>` and register in `Awake`, with execution order -500
  to -1000. Consumers resolve with `Services.TryGet` in `Start` or lazily, never in `Awake`/`OnEnable`.
- **Events.** Gameplay raises `GameEvents` (ScrapBroken, ItemsCollected, ItemsProcessed, ItemsDelivered, ItemsSold,
  CashEarned, UpgradePurchased, WorkerHired, CustomerServed, ExpansionOpened, MachineOverdrive, GiantScrapArrived,
  GiantScrapDefeated, LevelUp).
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
- **Purchases go through `UpgradeManager`.** Anything buyable implements `IUpgradeable` (stations, player stat
  upgrades, worker hires, expansions) and registers in `Start`. `UpgradeCatalog` holds unlock levels, which entries
  appear in the upgrade panel and under which group. Everything else is bought on its own `PurchaseTile`
  (`upgradeId` + a `GuideAnchor` named `tile/<upgradeId>`).
- **Buying is in the world.** No menu buttons: the upgrade panel only opens on the upgrade tile; hires and expansions
  are pay-by-standing tiles. Keep it that way.
- **Customers sell, not timers.** `CustomerQueue` turns `SellDesk.WalkInDemand` off and sells through
  `SellDesk.TryHandOver` + `CompleteSale`. Serve rate, order size and line length come from the desk level. A desk sells
  only its definition's `sells` list; customers pick a material from `CustomerConfig.orderOptions` (favouring stock).
- **Splitting machines are data.** The Metal Splitter (id `sorter`, asset `Machine_Sorter`) is a `MachineDefinition`
  with `outputMix` weights plus one `OutputPort` per product on the `Machine` (belt per material). Outputs leave in
  order, so one full belt holds the machine visibly. A new product = an item, a weight, a port, a belt and a bin.
- **Ids outlive names.** Raw Metal is still item id `mixed_metal` (asset `Item_MixedMetal`) and the Splitter is still
  `sorter`: saves, tasks, anchors and upgrade ids hang on them. Rename what the player reads, not the id.
- **Active Overdrive** goes through `Machine.Speed` (a `Multiply` modifier from an `OverdriveTile`); numbers live in
  `OverdriveConfig`. Every machine should get a boost pad with a `GuideAnchor` role `boost`.
- **Specialists boost through stats.** An Operator or Seller is a `Worker` with a `PostBrain` on a `WorkPost`. While they
  stand at the post they add one `Multiply` modifier (`WorkerDefinition.workBoost`) to `Machine.Speed` or
  `SellDesk.ServiceSpeed`. One hire per machine and per counter (the player chooses the bottleneck); no assignment menu.
  The Loader is a plain carrier: a `PorterRoute` with role Loader.
- **Groups, not copies.** Four furnaces share one Smelter (`PorterRoute.extraDropoffs`), one boost pad
  (`OverdriveTile.alsoBoosts`), one operator (`WorkPost.alsoRuns`), one collector belt and one rack. Add a fifth
  machine to a group by adding it to those lists, not by adding a fifth worker type and pad.
- **Nested expansions bake too.** A purchase inside a locked area (a furnace plot in the hall) is an `Expansion` inside
  that area's content. `R1_Build.Bake` switches every expansion's content on for the bake, nested ones included.
- **A belt can end on the ground.** `ItemSpill` turns what a belt delivers back into loose items (it stops at 70 loose
  items in the world). Give a crane whose target is a belt a `takes` list: a belt accepts anything.
- **Boosts go through `BoostManager`, ads through `AdService`.** A new boost is a `BoostDefinition` (and, if it should
  be offered, an `AdOfferDefinition`). Never call an ad SDK from game code and never show a video the player did not
  tap for; plug the SDK in with `AdService.SetProvider`. Balance must hold with every offer ignored.
- **The crane is a station too.** `ClawCrane` (data: `ClawCraneDefinition`) is an `IUpgradeable` that takes loose items
  from `HarvestManager` and gives them to an `IItemReceiver` (the Crusher), the same two doors the player and the
  porters use. Level 0 = only its foundation stands. Its motion is scripted (yaw, trolley, hoist). A full hopper leaves
  the claw waiting above it. Another machine gets a crane by placing another `ClawCrane` with its own definition.
- **Nothing a station reads in `Start` hangs under popped-in content.** An expansion reveals its content from scale 0;
  a child transform's world position is the parent's origin at that moment. The ship's course points are in the
  backdrop for that reason.
- **The player supplies, workers haul.** For a task on a staffed station the guide points the player at the job no worker
  does (cutting scrap, the first machine, an unstaffed link), not at carrying beside the worker. Any new carrier must
  be visible to `WorkerManager.Serves` (through `PorterRoute.DropsAt`) or the guide will double it up.
- **Orders are data, and the truck never refuses.** A `TruckContractDefinition` says what a truck wants and how much
  better it pays; `TruckBay` only counts the bed against it. Keep the rule that any bar is accepted: an order that
  refused the wrong metal would leave the player and the Loader holding bars with nowhere to put them.
- **The truck is a station.** `TruckBay` is an `IItemReceiver` behind a normal deposit pad. It leaves when full, pays the
  whole load onto its `CashPile` and raises `ItemsSold`; while it is away the pad takes nothing and carriers wait.
- **Boss scrap is still scrap.** The Giant Truck is a `ScrapDefinition` + prefab (parts, damage stages, `breakDelay`
  final sequence). `GiantScrapEvent` only decides when it arrives (heavy scrap broken, counted on a sign), drives the
  camera pull-back and pays the bonus; numbers live in `GiantEventConfig`. The HUD bar and announcer listen to events.
- **Real audio.** Sounds are Kenney CC0 clips assigned to `SfxDefinition` assets; the procedural presets remain only as
  a fallback. New sounds = new `Sfx_*` asset with clips, never code.
- **Tasks and guidance are data.** Add objectives as `TaskDefinition` assets in a `TaskChain`. The guide reads the task
  type; new station pads need a `GuideAnchor` named `<stationId>/in|out|cash`.
- **Saving is opt-in per system.** State worth keeping = implement `ISaveable` (key, `CaptureState`, `RestoreState`, own
  JSON) and call `SaveRegistry.Register(this)` in `Start`. Never teach `SaveManager` about a system. Anything buyable is
  already saved by `UpgradeManager` (level per id). While `SaveRegistry.IsRestoring` is true, skip fanfare: no reveal,
  popup, XP or purchase event for a level that comes from the save.
- **UI wears the kit.** Sprites come from `Art/UI/Kit` (`Kit_Panels`, `Kit_IconsA`, `Kit_IconsB`), never from the
  atlases in `Assets/UI` (haze and colour fringes) and no longer from 300Mind. A bar is `Kit_BarTrack` + a
  `Kit_BarFill` child (Filled); anything code tints is a white sprite (`Kit_BarFill`, `Kit_PillWhite`). Icons of things
  that exist as models stay the rendered ones in `Art/Icons`. Rules and the kit table: `Docs/ARCHITECTURE.md` 8c.
- **Loose items must stay collectable.** Decor with a footprint deep enough to bury a piece (mounds, container stacks)
  gets a `DropBlocker`. New stations lower than 0.75 m need a Not Walkable `NavMeshModifier` (see gotchas).
- **Nothing covers what the player works with.** No roof, canopy or sign above a counter, a pile or a pad: the stalls
  are open counters (the owner had the striped roof removed because it hid the stack and the stock pad).
- **Customers do not make the player wait.** A buyer at the counter takes the order at once; do not bring back a serve
  timer or a waiting bar.
- **One popup card, one motion kit.** Rewards, questions and messages go through `PopupManager.Show(PopupRequest)`;
  UI motion goes through `UIAnim`. No screen instantiates its own panel or writes its own tweens. Every `Button` carries
  `UIButtonFeel` (`U1_Build.Scene` adds it to new ones).
- **Diamonds have two doors.** In: `EconomyManager.AddPremium(amount, reason, origin)`; out: `TrySpendPremium` via
  `DiamondSpend.Request` (it confirms big spends). Prices come from `PremiumEconomyConfig`, never from a UI script.
  Diamonds buy time, never power. A reward is recorded before it is offered and paid on claim.
- **Real money goes through `StoreService`.** Never grant a purchase anywhere else. A transaction id pays once, a paid
  purchase is saved before it is shown and paid on CLAIM. Prices on screen come from the store; the USD numbers in
  `IAPProductConfig` are for value checks only. Do not sell a product that does nothing (No Ads stays off while the
  game has no forced ads).
- **Every video goes through `OfferDirector`.** Ask `Ready(offer)` before showing a video button; play with
  `Watch(offer, reward)`. Never call `AdService` directly from UI, never show two video buttons at once (a HUD element
  with its own video sets `OfferDirector.ContextualVisible`). Time-sensitive offer cards set `PopupRequest.MaxWait`.
- **Extras appear with the first diamond.** Shop, missions and daily rewards stay hidden in the first minutes
  (`Badges.ExtrasUnlocked`). New navigation gets one badge key, not its own dots.
- **Missions listen, never get called.** New mission kinds = a `MissionStat` fed from a `GameEvents` event in
  `MissionManager`, then data in `MissionConfig`.
- **Settings are device data.** `GameSettings` (PlayerPrefs) survives a progress reset; the save never holds them.
- **An effect text is also the celebration.** `IUpgradeable.NextEffect` in the form "a → b unit" becomes "+x% WORD"
  after a purchase (`UpgradeGain`); keep new upgradeables in that form, or give them a short sentence.
- **Economy changes go through the simulation.** After changing a price, reward, cooldown, cap or the offline gift,
  run the EditMode tests: `EconomySimulationTests` plays four players for 30 days on the data and fails when free
  diamonds pile up or starve, videos carry progress, or a diamond's cash value drifts. The table is in the Economy
  window (Profiles) and `Docs/ECONOMY.md`. The game's shape it assumes (`EconomySimulator.Model`) follows section 4 of
  that doc; update both together.
- **Developer tools stay in the Editor.** Balancing views (`EconomyWindow`, `EconomySimulator`) are Editor code; the
  runtime only keeps numbers (`EconomyLedger`, listening to `Analytics`). Nothing of it is ever shown to players.
- **Check UI with the audit, at three shapes.** After moving or adding screen UI, run `Tools/UiAudit.cs` in Play
  (`UiAudit.Res "1080 1920"`, `"1080 2400"` with `insets 0 0 0.055 0.0425`, `"1536 2048"`, then `UiAudit.Run`) on
  the screens you touched; `UiAudit.World` checks world labels from the current camera. It reports widgets drawn over
  each other, text over text, spilling text, off-screen / unsafe elements and small buttons. The canvas uses Expand:
  design for 1080 × 1920, it never shrinks.
- **Top of the screen, from the top:** currency row (level, gems, cash, gear) → task banner (reward inside) → bonus
  line → order card (left) / boost chips (right) → FINISH ORDER. The boss bar lives at the bottom above the nav bar.
  A new HUD element finds a free place in that order or goes to the bottom, never on top of another.
- **Labels step back from tiles.** A `StationLabel` fades while it covers a visible `Tile` on screen; new floating
  world UI over stations should do the same instead of hiding what the player can buy.
- **No dead UI.** A button that would do nothing (no store yet, no support address) is hidden, not greyed.
- **Tall things stand north of where the player walks.** The camera looks north from above; a crane or a high stack
  south of a walkway hides the player.
- **No placeholder architecture.** Build each system the way it will ship; placeholder *content* (primitive art,
  procedural sounds) is fine.

## How we work (process rules from the project owner)

- **One milestone at a time.** After each one, play-test the real loop, stop, and report: what was implemented,
  files/classes, how it works, what remains for the next milestone, and architectural risks.
- Don't build future areas until the current one is fun. No multiplayer, no complicated inventory, no unnecessary
  menus, no spreadsheet feel. The player character stays central.
- Every 5–10 minutes of play should produce a visible change. No mandatory idle waiting.
- Quality and feel matter. Every action gets feedback: sparks, hit-stop, pops, sounds, counters.
- Art direction: colourful industrial toy world, premium cartoon 3D, chunky saturated mobile look. **No Kenney visuals**
  (Kenney CC0 audio is fine): every model is custom, built with ArtKit by the `Art_*` builders. GROBOLD font; UI from
  the owner's painted kit (`Assets/UI`, cut and applied by `UI_Build`). Reference: the owner's screenshots (red crusher, yellow sorter, conveyors, coloured bins, market stall with
  green cash). See `Docs/ART_DIRECTION.md` for the pipeline, budgets and the visual audit.

## Milestones

| # | Scope | Status |
|---|---|---|
| 1 | Movement, camera, cutting, drops, pickup, carry stack, feel | Done |
| 2 | Crusher → conveyor → storage → sell desk → cash pile → HUD, art pass | Done |
| 3 | Upgrade rail, task chain + guide arrow, XP/yard level, first Porter worker | Done |
| 4 | Upgrade tile + panel, hire tiles, Delivery Helper, customer queue, Back Lot expansion (tier-2 scrap), UI restyle | Done |
| 5 | Recycling Plant (Gate 2): Sorter → iron/copper bins → Metal Market, Hauler + Runner, Active Overdrive, CC0 audio, hats | Done |
| 6 | Furnace hall (iron/copper → ingots, Ingot Rack, Smelter), Gate 3 + Heavy Scrap Yard (tractors, trucks, garbage trucks) | Done |
| 7 | Operators, Sellers, Truck Dock + Loader, Giant Scrap event | Done |
| 8 | Dockyard reveal, save/load, balance pass, polish | Done |

After the milestones: **UI pass** (2026-10-06) with the owner's painted kit: HUD, upgrade panel, tiles, labels and
bubbles restyled, loading screen with the title logo, app icon (`UI_Build`).

**HUD layout pass** (2026-10-07, `HudLayout_Build`): audited every screen at 16:9, 20:9 with a notch and 3:4 with
`Tools/UiAudit.cs`; the canvas scales with Expand, a cleaner top row, the boss bar at the bottom, station labels fade
while they cover a buy tile, status tags fit their text, plus panel fixes (CLAIM, shop ribbon, slider, sheet bleed).

**UI U6** (2026-10-07, `U6_Build`): the developer economy window (**Window → Scrap Yard King → Economy Window**: live
economy, prices with time-to-afford, simulated players), `EconomyLedger`, and guardrail tests over four simulated
players (`EconomySimulationTests`). The simulation fixed a runaway diamond cash value, front-loaded achievements, too
many HUD offers (150 s pause), a 4 h offline cap (now 2 h) and FINISH ORDER on an empty truck. The U1–U6 plan is done.

**UI U5** (2026-10-07, `U5_Build`): purchases celebrate on the station (LEVEL 8! + "+20% SPEED"), upgrade cards say
"LV 7 → 8" and "8.4K / 12.5K", the announcer shows what arrived, the order card celebrates, Overdrive shows on the HUD.

**UI U4** (2026-10-06, `U4_Build`): missions (main, three dailies, achievements), a 7-day daily reward, badges, and a
bottom bar (SHOP, MISSIONS, DAILY) that appears with the first diamond.

**UI U3** (2026-10-06, `U3_Build`): one rewarded-video gate (`OfferDirector`) with saved cooldowns and daily caps;
ORDER COMPLETE card with 2X, WELCOME BACK card with 2X, FINISH ORDER on a waiting truck (video or diamonds).

**UI U2** (2026-10-06, `U2_Build`): the shop. Starter pack, six diamond bundles, a boost pack, diamond boosts and cash
crates, free diamonds for a video; `StoreService` with an Editor stand-in until a real store is plugged in.

**UI U1** (2026-10-06, `U1_Build`): first step of the owner's UI/economy brief (plan U1–U6 in `Docs/QUALITY_PLAN.md`):
UI motion kit, tactile buttons, one popup card for every reward and question, diamonds as a real currency with
milestone gifts, settings as the pause screen, safe area, a cleaner top HUD.

**Revamp 7** (2026-10-06, `R7_Build`, partly done): economy model (`Docs/ECONOMY.md`), level curve x1.55, Heavy Yard
tasks after the truck, carriers that leave machines their input. Measured at 60 fps up to the first furnace (17:13 to
17:46) only: **no clean full-session run and no device test yet**; do not tune late-game prices before one.

**Revamp 6** (2026-10-06, `R6_Build`): the port works: the cargo ship is a second dock with bigger orders. Quay
activity, ship lights and the environment pass are still open.

**Revamp 5** (2026-10-06, `R5_Build`): five timed boosts, rewarded-video offers through a stand-in `AdService`, offline
income. No ad SDK yet. Next: R6 (port and ship, world activity, final environment pass).

**Revamp 4** (2026-10-06, `R4_Build`, `Crane_Build`): Heavy Yard crane and belt, truss jibs, the Excavator, metal drops.
New wreck models and boss types are still open. Next: R5 (boosts, rewarded video, offline income).

**Revamp 3** (2026-10-06, `R3_Build`): Industrial Press, four bars, Bar Storage, truck orders with a HUD card.

**Revamp 2** (2026-10-06, `R2_Build`): the furnace battery. Four furnaces, four ingots, one Smelter for all.

**Owner's requests of 2026-10-06 (all built):** stalls without a roof, customers that take goods at once, no road or
floor words in the Old Yard, ground under the whole world, a faster economy (`R1_Build.Balance`), the Claw Crane, and
buy tiles that belong to the yard (steel plate with a hazard rim instead of a UI card; `UI_Build.PurchaseTile`).

**Old Yard is the reference look** (owner's choice, 2026-10-06): one area is brought to the target look, the owner
approves it, then it goes to every area. `Env_Build.OldYard` holds it (paving, gravel pit, hazard kerb, floor words,
billboard). Do not roll it out to other areas before the owner has said yes; the production chain (R2 onward) follows.

**Environment and layout pass** (2026-10-06, `Env_Build`): lanes, bays and clutter in ProBuilder, Heavy Yard wrecks
parked in rows (16 heavy spawn points), tiles moved off belts and props, customers and hired workers arrive at once,
the player's arms finally hold and work the cutter (mask fix).

**Revamp 1** (2026-10-06, `R1_Build`): speed pass (first worker inside 3 minutes instead of 8), Raw Metal, the
four-way Metal Splitter with aluminum and steel. Next revamp steps are listed in `Docs/QUALITY_PLAN.md`.

The eight planned milestones are built. What a next step would pick up:
- **Dockyard content.** The quay is a reveal. Press hall = one more recipe `MachineDefinition` + boost pad + operator
  console; shipping = a second `TruckBay`-style station (the ship as the "truck"). `TruckBayDefinition.accepts` is where
  pressed goods go. The main chain ends at `t50_dockyard`; `M8_Build.IsM8Task` shows the id pattern.
- **Offline income.** The save stores `savedAtUnix`; an `IdleIncomeManager` would be one more service reading it.
- **Daily tasks / stages.** `TaskCategory.Daily` exists but has no content; each new system is one more `ISaveable`.
- **Balance is only half measured.** `Docs/Pacing/README.md` has the runs: after the revamp the first 18 minutes (to
  the Iron Furnace) are measured at 60 fps; everything later is the model in `Docs/ECONOMY.md`. A clean full guide-bot
  run is the first thing to do before touching late-game costs. The bot is a guided newcomer, not a human.
- The cash/gem "+" buttons are hidden (shop is out of scope until monetisation).

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
- `recompile_status` and `console` can show stale results; `clear_console` before checking. `recompile_status` answers
  `completed` or `up_to_date`: wait for either, or a polling loop never ends.
- The first frames after new TextMeshPro material keywords can render as cyan blocks (async shader compile). This
  is not a bug.
- DOTween's Utility Panel deletes hand-made asmdefs. Keep `createASMDEF = true` in
  `Assets/Resources/DOTweenSettings.asset`; the `DOTween.Modules.asmdef` it generates is referenced by
  `ScrapYardKing.Runtime`.
- In editor scripts, use `TryGetComponent` instead of `GetComponent<T>() ?? AddComponent<T>()` (fake-null).
  MaterialPropertyBlocks are not saved in prefabs, so use materials.
- `capture_game_view --save_path` writes inside `Assets/`, which pollutes the project. Prefer the base64 output or
  `screenshot --output`.
- Rendering icons with `Camera.Render` into a RenderTexture: keep `antiAliasing = 1` (URP rejects mismatched MSAA and
  the PNG comes out empty).
- `FindAnyObjectByType<T>(true)` does not compile; use `FindAnyObjectByType<T>(FindObjectsInactive.Include)`.
- If someone is playing in the Editor while Claude runs scripted play tests, teleports and cheats will fight their
  input. Agree who drives before a test run.
- `run_tests` on a dirty scene opens a modal "Save modified scenes?" dialog that blocks the whole Editor (every CLI
  command then times out). Save or check `SceneManager.GetActiveScene().isDirty` first; the scene tends to get dirty
  after play mode.
- Play-mode time only advances while Unity has focus, and focus can drop between commands. Wrap waits in a loop
  that calls `editor_focus` and polls `Time.time` until it has advanced, instead of counting polls. For long unattended
  runs, set `EditorPrefs.SetInt("InteractionMode", 1)` (No Throttling) and `Application.runInBackground = true` in play
  mode: the game then keeps running without stealing focus, but only at about 10 fps. At 4x that is 0.4 s of game time
  per frame: the player overshoots pads and never stands on one long enough to engage. Run the guide bot at x2 with
  `Time.maximumDeltaTime = 0.2f` instead (or keep Unity focused). Put InteractionMode back to 0 afterwards.
- Every `run_script` call compiles a separate in-memory assembly, so static state is not shared between calls (a
  `Status` call can't see a bot started by an earlier call). Talk to long-running editor tools through files.
- A compile error in the test assembly leaves the old test DLL in place, so `run_tests` silently runs the old tests.
  If the test count did not change after adding tests, read `console --level error`.
- Saving a changed baked mesh with `EditorUtility.CopySerialized` can leave the old mesh on screen; copy vertices,
  normals, UVs and submesh triangles into the existing asset instead (see `M5_Build.CombineSubmeshes`).
- Metallic materials render nearly black in the icon studio (nothing to reflect). Keep item metallic at 0.2–0.35.
- ProBuilder `GenerateTorus(pivot, rows, columns, a, b, ...)`: `a` is the ring radius and `b` the tube radius, flat in XZ.
- World-space canvases (tiles, bubbles, labels) sit at scale 0.01. Punch or shake a unit-scale child, never the canvas
  itself, or DOTween's additive punch makes it 100× bigger.
- Characters are one SkinnedMeshRenderer on a joint hierarchy (Hips/Torso/Head/ArmL...); the generated clips address
  joints by path, so keep joint names and positions when changing a character. New looks = a new `ToySpec`.
- Builders that create meshes reuse sub-assets by name in `Art/Meshes/Toy/<Model>.asset`; give every part a unique
  name per model or two parts will overwrite each other.
- `UnityStats.triangles` includes shadow cascades and the SSAO depth pass; compare like with like.
- The NavMesh agent step height is 0.75 m (Unity's default). Any collider lower than that gets walkable floor on top in
  the bake: workers walk over it and guide paths lead the player into it. Conveyors carry a `NavMeshModifier` set to
  Not Walkable for this reason (added by the bakes in `M7_Build.Scene` and `Art_World.Scene`); do the same for new low
  stations. The surface also bakes a height mesh, otherwise agents float beside tall obstacles.
- DOTween: `Kill(true)` on a `Sequence` jumps to the end without firing its `AppendCallback`s. Use `Complete(true)` first
  when a callback must run (see `LevelBadge.Refresh`).
- `Art_World.Scene` places props into roots it shares with other builders (the Heavy Yard `Decor`); it now clears its own
  `Prop_*` children first. If a prop appears twice at one spot, a builder forgot to clear.
- The save file is loaded in Play mode. Scripted tests and guide-bot runs must start from a known state: `source
  AgentScripts/Tools/_env.sh; wipesave` before `editor_play`, or the `wipe` cheat in play (deletes it and stops saving
  for that session). Exiting play mode writes a save, so a cheat session leaves one behind.
- A guide-bot stall whose target does not move is a dead end, not slow pacing: look at the target position first (a
  loose item behind a wall, a pad the path cannot reach).
- Springs integrated with one explicit step per frame (`v += a*dt; x += v*dt`) blow up to NaN on long frames (a hitch,
  5–10 fps in a background Editor at x2). Sub-step them and reset non-finite state: see `CarryStack.StepSway` and the
  camera punch spring. Symptom: thousands of "transform.localPosition assign attempt ... NaN" errors.
- Play starts under the loading cover (`LoadingScreen`: 1.8 s minimum, unscaled time, then a 0.4 s fade). A capture
  taken right after `editor_play` shows the title card, and the joystick takes no touch until it is gone; `wait.sh 3`
  first.
- Sprites cut from a painted atlas: check them over a strong colour, not over black or white. A soft haze around the
  icons (alpha up to 230 in Atlas1) is invisible in most viewers and shows up as a dark box in the game.
- A guide-bot stall right after a pacing change is often a hidden order dependency: the old run always had upgrade X
  before area Y. (The Back Lot used to open after the second chainsaw upgrade; when it opened earlier the guide sent the
  player to "earn" at a fridge they could not cut.) Check what the player owns at the stall, not only where they stand.
- `AvatarMask.AddTransformPath(root, true)` stores paths with the root's own name in front (`Model/Hips/...`). A mask
  for an Animator on that root needs paths relative to it (`Hips/...`): set them with `SetTransformPath`. With the
  prefix every bone was masked out, so the player's arm layer (hold pose, cutting stroke) did nothing for the whole
  art pass and nobody noticed until the owner did. When an animation "does not play", check the layer's mask first.
- The grass outside the walls is `_Environment/WorldGround`. Its mesh was a 1 m cube for several milestones (nobody
  looked past the walls in the Scene view): trees stood over the void. `Env_Build.WorldGround` rebuilds it at an
  explicit size, ending at the quay's edge because the dock water is lower. After a ground or backdrop change, look at
  the whole world from above in the Scene view, not only at Game-view captures.
- New tiles, props and spawn points: run `Env_Build.Check` instead of judging overlaps from screenshots. A station
  label floats 3 m up and hides the ground about 2.3 m north of it on screen; keep tiles out of that strip.
- Customers must spawn just outside the camera (about 17 m from the counter), not at the world's edge: at 40 m the
  street stayed empty for 10 s after every sale burst and it read as "customers come too late".
- While the Editor has focus, the mouse over the Game view steers the player. A scripted test with a lot of cheat cash
  then "buys" whatever tile the player wanders onto (a session showed 2 porters and a Lv.3 crane nobody asked for).
  Check `UpgradeManager.LevelOf` before blaming the game for a purchase or for missing cash.
- A builder that destroys and rebuilds a container must first take out anything another step moved into it
  (`R3_Build.Scene` rescues `Tile_Loader` from the old Press plot before deleting the plot).
- `editor_focus` (and therefore `wait.sh`) lands a click in the Game view: a popup's CLAIM button under that point
  gets pressed. Reward cards "claimed themselves" in tests; they stayed up for 20 s when nothing called focus. To look
  at a popup, run unfocused (`InteractionMode` 1 + `runInBackground`) and `sleep`, or invoke the button yourself.
- `Mathf.SmoothStep(from, to, t)` interpolates between two values; it is not GLSL `smoothstep(edge0, edge1, x)`. Using
  it as an edge left a 15% alpha floor on the generated FX sprites (sparkles drew as squares). Use `U1_Build.Edge`.
- The owner often plays in the Editor while Claude tests: popups get "claimed by themselves" and the player walks off.
  Check state through values, not only screenshots, and do not read a missing popup as a bug before ruling that out.
- `wait.sh` polls game time, so it hangs while the game is paused (settings open: `timeScale` 0). Use `sleep` there.
- Editing a `.cs` file while Play mode runs makes Unity stop Play to recompile (at the next refresh). Finish code
  changes, compile, then run one uninterrupted play session; a test that "stopped by itself" usually hit this.
- `capcam.sh` renders from 24 m by default, closer than the game camera in a boss fight (22 m + 7 m pull-back). Judge
  framing of big objects with `cap.sh` (the real Game view).

### Play-test tools (`AgentScripts/Tools/`)

Shell helpers for driving a play test from the CLI (all take care of `--project-path`):
- `wait.sh SECONDS` waits until play-mode time has advanced (re-focusing Unity, which freezes in the background);
  `keepfocus.sh SECONDS` just keeps Unity in front (run it in the background during long tests).
- `cap.sh OUT.png` captures the Game view with UI; `capcam.sh OUT.png X Z` renders the world from a game-like camera
  without moving the player (safe while someone else is playing); `tp.sh X Z` teleports the player; `state.sh` prints
  a one-line state (task, stack, cash, storages, desks). `source _env.sh` gives `ucmd` and `ueval` for one-off calls.
- `ContactSheet.cs` (`run_script ... --entry ContactSheet.Start --args '["/abs/out.png 16 0.1 4 2"]'`): tiles 16 frames
  of game time into one PNG to judge motion and feedback.
- `GuideBot.cs` (`--entry GuideBot.Start --args '["/abs/report.md 90 4"]'`): plays the game by following the guide
  (plus shopping trips) at 4x and writes a pacing timeline; reports stalls where the guide dead-ends. Each `run_script`
  is a separate assembly, so poll the report file for progress (it is rewritten every 30 s of game time).
- `UiAudit.cs` (`--entry UiAudit.Run`, `UiAudit.Res`, `UiAudit.World`): layout audit of the screen UI and the world
  labels (see the rule "Check UI with the audit").
- `Cheat.cs` (`--entry Cheat.Run --args '["level 9; cash 50000; buy furnace_hall; give iron 6; tp 63 31"]'`): play-mode
  shortcuts for testing later content (level, cash, buy through `UpgradeManager`, give items, teleport, timescale,
  `save`, `wipe`, `saveinfo`).

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
7. `M3_Build.Assets`, `M3_Build.Prefabs`, `M3_Build.Scene` (and `M3_Build.Icons` to re-render card icons): adds M3
   content by **editing the existing prefabs and scene in place**. This is the pattern to follow for new milestones.
8. `M4_Build.Assets`, `M4_Build.Prefabs`, `M4_Build.Scene` (or `M4_Build.All`; `M4_Build.Icons` re-renders the
   helper, Back Lot and mixed-metal icons): catalog groups, tasks, helper, customers, tiles, tier-2 scrap, Back Lot,
   station label restyle, upgrade panel, NavMesh rebake. Also incremental.
9. `M5_Build.Assets`, `M5_Build.Prefabs`, `M5_Build.Scene` (or `M5_Build.All`): audio import + clip mapping, iron and
   copper, Sorter, bins, Metal Market, overdrive, Hauler/Runner, catalog sections, tasks; Sorter/boost/worker prefabs,
   item meshes and icons, round hard hats; Gate 2 expansion, Area 2 walls and content, routes, market line, NavMesh
   rebake over both areas. Also incremental. The Kenney audio must already sit in `Assets/ThirdParty/Kenney/Audio`.

10. `Polish1_Build.All` (quality pass 1, after M5): chainsaw engine SFX, hit flash / stack landing / drop tuning in
    data, `ToolAudio` + full-stack bump on the player, hides the dead currency "+" buttons, savings fill on upgrade cards.
    `M5_Build.Hats` rebuilds the hard hats alone.
11. `M6_Build.Assets`, `M6_Build.Prefabs`, `M6_Build.Scene` (or `M6_Build.All`; `M6_Build.Icons` re-renders the ingot,
    Furnace, Smelter and Heavy Yard icons): ingots, Furnace recipes, Ingot Rack, Smelter, heavy scrap, both M6
    expansions, catalog, tasks (also inserts the desk/crusher Lv.3 tasks before Gate 2 and sets Gate 2 to $3,000);
    Furnace / smoke / Smelter / heavy vehicle prefabs; Furnace hall in the plant, Gate 3 + Area 3 walls and content,
    worker routes, NavMesh over Areas 1–3. Incremental; it saves the open scene first if it is dirty.
12. **Art pass (visual overhaul, replaces every Kenney model)**, in this order: `Art_Characters.Build`,
    `Art_Scrap.Build`, `Art_Machines.All`, `Art_VFX.Build`, `Art_World.All`. They use the Editor-only ArtKit
    (`Assets/_Project/Scripts/Editor/ArtKit`: MeshKit, ArtMaterials, ArtPalette, PaletteBaker, ToyRig, ArtIcons).
    `Preview(dir)` entries on the character, scrap and machine builders render review lineups without touching assets.
    Re-running any M-builder `Prefabs`/`Scene` step brings Kenney content back: run the Art builders after it.

13. **M7** (after the art pass): `M7_Build.Assets`, `M7_Build.Prefabs`, then `Art_Characters.Build` (dresses the
    Operator, Seller and Loader and renders their icons), then `M7_Build.Scene`. Data for the specialists, Truck Dock and
    Giant Scrap event, catalog entries and tasks `t38`–`t47` (added by id; nothing earlier is rewritten); worker prefabs,
    `Prop_Console`, the `TruckBay` station, `Scrap_GiantTruck` (all ArtKit); consoles, posts, hire tiles, the dock and
    its fence gap, the Loader route, the giant's landing zone, the HUD boss bar, NavMesh. Incremental, no Kenney.
    Re-run `M7_Build.Scene` after `M5_Build.Scene` or `M6_Build.Scene` (they rebuild the plant fence and content).

14. **M8**: `M8_Build.Assets`, `M8_Build.Prefabs`, `M8_Build.Scene` (or `All`). Dockyard expansion data, the balance
    changes (`M8_Build.Balance`: every number changed after the measured session, with the reason), tasks `t48`–`t50`;
    ship, quay crane and bollard prefabs; SaveManager + item catalog, Gate 4 and the quay, `DropBlocker`s, NavMesh.
    Run after M7. Re-run `M8_Build.Scene` after `M5_Build.Scene` or `M6_Build.Scene` (they rebuild the plant's east wall).

15. **UI pass**: `UI_Build.Kit`, then `UI_Build.Apply` (or `All`). `Kit` cleans and slices the atlases in `Assets/UI`
    into `Art/UI/Kit` (stable sprite names, 9-slice borders, an assembled bar track / white fill / white pill). `Apply`
    restyles the HUD, the upgrade panel and its card template, the three tile prefabs, station labels and customer
    bubbles in place, rebuilds `_UI/LoadingCanvas` and sets the app icon. Run it last: every M-builder `Prefabs` /
    `Scene` step puts the old sprites back.

16. **Revamp 1**: `R1_Build.Assets`, `Art_Machines.Prefabs`, `R1_Build.Prefabs`, `R1_Build.Scene`,
    `Art_Machines.Scene`, `R1_Build.Bake`, in that order (the Splitter's model and out points belong to
    `Art_Machines.Sorter`; data, items, ports, bins, belts and the plant layout to `R1_Build`). `R1_Build.Balance` is
    the one place for every speed-pass number. Run after M8 and the Art builders; `UI_Build.Apply` still goes last.

20. **Revamp 3 (Press, bars, contracts)**: `R3_Build.Assets`, `R3_Build.Prefabs`, `Art_Machines.Prefabs`,
    `Art_Machines.Icons`, `R3_Build.Icons`, `R3_Build.Scene`, then `Art_Machines.Scene`, `Env_Build.All`,
    `R1_Build.Bake`, `UI_Build.Apply`. Run `R3_Build.Scene` after `R2_Build.Scene` (it adds the Press plot to the hall).

19. **Revamp 2 (furnace battery)**: `R2_Build.Assets`, `R2_Build.Prefabs`, `Art_Machines.Prefabs`, `Art_Machines.Icons`,
    `R2_Build.Icons`, `R2_Build.Scene`, then `Art_Machines.Scene`, `Env_Build.All`, `R1_Build.Bake`, `UI_Build.Apply`.
    `R2_Build.Balance` and the tables in `R2_Build.Assets` hold every furnace number.

31. **HUD layout pass**: `HudLayout_Build.Scene`, after U6 (canvas Expand, top row, mission row, bundle ribbon, volume
    slider, upgrade sheet bleed, boss bar at the bottom, upgrade tile "!"). Run it last of the UI builders.

30. **UI U6**: `U6_Build.Assets`, `U6_Build.Scene` (or `All`), after U5 (ledger service, offer pause, offline cap, the
    late cash-per-diamond slope). Achievement tiers live in `U4_Build.Assets`.

29. **UI U5**: `U5_Build.Scene`, after U4 (announcer icon, fifth boost chip, panel title order, level pill size).

28. **UI U4**: `U4_Build.Assets`, `U4_Build.Scene` (or `All`), after U3. Panels and the bar are rebuilt on each run.

27. **UI U3**: `U3_Build.Assets`, `U3_Build.Scene` (or `All`), after U2. Removes the old `OfflinePanel`.

26. **UI U2**: `U2_Build.Assets`, `U2_Build.Scene` (or `All`), after U1. The shop panel is rebuilt on each run.

25. **UI U1**: `U1_Build.Assets`, `U1_Build.Scene` (or `All`). Run after `UI_Build.Apply` and `R5_Build.Scene`: both
    reset parts of the HUD this step moves (pills, XP bar, boost chips). The popup layer and settings panel are rebuilt
    on each run.

24. **Revamp 7**: `R7_Build.Assets` (final task order, balance changes from the full-session runs), `R7_Build.Scene`
    (Runner route priority, Delivery Helper buffer). Run last of the data steps. The economy model is `Docs/ECONOMY.md`.

23. **Revamp 6**: `R6_Build.Assets`, `R6_Build.Scene`, then `R1_Build.Bake`, `UI_Build.Apply`.

22. **Revamp 5**: `R5_Build.Assets`, `R5_Build.Scene` (services, boost chips, offer button, offline panel).

21. **Revamp 4**: `R4_Build.Assets` (Excavator scrap type, metal drops), then step 18.

18. **Cranes**: `Crane_Build.Assets` (both definitions, catalog, tasks), `Crane_Build.Scene` (both cranes, tiles, icons,
    the Heavy Yard belt and spill), then `Art_Machines.Scene`, `Env_Build.All` and `R1_Build.Bake`.

Order for a full rebuild of everything after M8 and the Art builders: `R1_Build.Assets` → `R2_Build.Assets` →
`R3_Build.Assets` → `R4_Build.Assets` → `R2_Build.Prefabs` → `R3_Build.Prefabs` → `Art_Machines.Prefabs` → `R1_Build.Prefabs` →
`Art_Machines.Icons` → `R2_Build.Icons` → `R3_Build.Icons` →
`R1_Build.Scene` → `R2_Build.Scene` → `R3_Build.Scene` → `Art_Machines.Scene` → `Crane_Build.Assets` → `Crane_Build.Scene` →
`Env_Build.All` → `R1_Build.Bake` → `UI_Build.Apply`.

17. **Environment and layout pass**: `Env_Build.All`, then `R1_Build.Bake`. ProBuilder geometry only (editable in the
    scene): service lanes with painted lines, parking bays, clutter islands and wall clusters (bales, pipes, barriers,
    drums, crates, tanks, floodlights), each cluster one merged ProBuilder mesh with a collider and a `DropBlocker`.
    It also parks the Heavy Yard's wrecks in rows of bays (16 heavy spawn points instead of 8), adds quick-target
    spawn points, moves tiles off belts and props, and shortens the customers' walk-in. Everything it makes is named
    `EnvPB_*` or `SP_Env_*` and rebuilt on each run (fixed seed). `Env_Build.Check` lists tiles and pads that overlap
    each other or anything standing on them; `Env_Build.Map "x0 z0 x1 z1"` prints an occupancy map. Run it after
    `R1_Build.Scene` and the Art builders (it measures what is already there and only builds where there is room).

Later builders overwrite some data that earlier ones write (catalog, task chain, station label prefab). If you re-run
an earlier builder, re-run every later one after it.

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
