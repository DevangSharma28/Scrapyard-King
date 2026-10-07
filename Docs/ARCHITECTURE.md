# Scrap Yard King — Technical Architecture

Source of truth: *Scrap Yard King — Game Design + Top-Down Map Blueprint* (PDF, 10 pages).
Engine: Unity 6 (developed on 6000.4.8f1), URP (Mobile renderer), Input System (new), AI Navigation, ugui/TMP, DOTween.
State: Milestones 1–8 done, plus the UI pass. In progress: the **revamp** (fast factory, redesigned production chain),
one step at a time; the plan and the target chain are in `Docs/QUALITY_PLAN.md`. Revamp 1 (speed pass, Raw Metal, four-way Metal Splitter), Revamp 2 (the furnace battery: four furnaces, four
ingots) Revamp 3 (Industrial Press, metal bars, Bar Storage, truck contracts), Revamp 4 (Heavy Yard crane and belt, the
Excavator, metal drops from heavy wrecks) Revamp 5 (timed boosts, rewarded-video offers through a stand-in ad
service, offline income) and Revamp 6 (the port: the cargo ship as a working dock) are built. Sections below describe the game as built.

## 1. Blueprint digest (what drives code decisions)

| Blueprint rule | Architectural consequence |
|---|---|
| Cut → collect → process → sell → upgrade → hire → expand | Every stage is a separate system talking through items + events, not direct calls. |
| Player stays central; workers take over routine later | Carry / collect / pad components are **role-agnostic**: player and workers share `CarryStack`, `ItemCollector` and `TransferPad`. |
| One bottleneck at a time; Active Overdrive 2x for 10 s | Throughput numbers are `ModifiableStat`s; upgrades and overdrive are just modifiers. Full outputs block upstream visibly. |
| Every gate: level/task + cost + visible new content | `Expansion` (scene) + `ExpansionDefinition` (cost) + catalog unlock level; opening it pans the camera and reveals content. Never a menu-only unlock. |
| Tiers gate scrap; drop tables by size (4–10 / 10–25 / 25–60 / 120+) | `ScrapDefinition` carries size class, health, min cut power, drop range, rare chance, XP. |
| Crusher → Metal Splitter (four metals) → Furnaces → Press → trucks | A machine type is a `MachineDefinition` asset; splitting machines use weighted `outputMix` + per-product output ports. |
| Customers enter from the south road and request material; queue 3 (yard) / 5 (plant) | `CustomerQueue` per sell desk; orders come from `CustomerConfig`; line length from the desk level. |
| Crusher Lv.1 10 in / 8 s, Storage 40, Sell 1 / 8 s, Porter 500, Expansion 1,500 | Values live in data assets only; MonoBehaviours hold no balance numbers. |
| Always an obvious next action | `GuideDirector` turns the current task + world state into one marker. |
| Hit sparks + hit-stop, break burst + camera bump + popup; overdrive gauge | Feedback services behind the static `GameFeedback` facade, so gameplay code stays one-liners. |
| Max 3 floating prompts | `FloatingTextManager` caps concurrent popups. |

## 2. World layout (canonical, 120 × 90 units, 5 m grid)

Origin = south-west corner of the world. +X = east, +Z = north. Ground plane y = 0.

| Zone | Rect (x, z, w, d) | Status |
|---|---|---|
| South customer road | 0, 0, 120, 10 | Customer lanes: arrivals z 2.6, departures z 1.2 (yard customers enter and leave at x 15, market customers at x 77: 17 m from the stall, just off screen). Ambient traffic z 4.0 (east) and 6.5 (west). The Truck Dock's truck parks on the north shoulder (z 9.1). |
| A1 Old Scrap Yard | 0, 10, 40, 28 | Built. Back Lot expansion in its north-east corner. Gate 2 on the east edge (x 40, z 24.5). |
| A2 Recycling Plant | 40, 10, 36, 32 | Built (M5). Opens through Gate 2. |
| A3 Heavy Scrap Yard | 0, 38, 40, 36 | Built (M6). Opens through Gate 3 in the Old Yard's north wall (x 12, z 38). |
| A4 Electronics | 76, 10, 36, 34 | Future (blueprint). The built Dockyard quay stands on its west part. |
| A5 Dockyard Port | 76, 44, 36, 42 | Blueprint position. Built in M8 as a reveal directly east of the plant (x 76–98, z 11–47): the plant and the blueprint's dock only touch at a corner, the same deviation as Gate 3. Press and shipping are future. |
| A6 Mega City | 112+, east | Future gate + reveal only. East = truck road. |

As built (positions are world x, z):

| Object | Position | Notes |
|---|---|---|
| Player start | (12, 17.5) | Scrap spawn field x 2–18, z 24–36. |
| Crusher / input pad | (24, 27) / (20.9, 27) | Belt south to Storage. Boost pad (26.9, 26.6). |
| Storage (yard) / withdraw pad | (24, 19.2) / (20.9, 19.2) | |
| Sell Desk / stock pad / cash pad | (32, 12.4) / (32, 14.4) / (35.7, 14.4) | Line runs south from (32, 10.9); customers come from and leave to the west. |
| Upgrade tile (yard) | (27.6, 23) | |
| Claw Crane / tile | (19.9, 29.3) / (17.9, 21.6) | Tower crane at the work floor's north-west corner; the hopper is 4.7 m away. Reach 10 / 13.5 / 19.5 m by level. |
| Heavy Yard crane / tile | (15.6, 40.6) / (17.1, 43.7) | East of the Gate 3 lane. Reach 16 / 22 / 28 m. Its belt runs from (14, 41.6) through the gate's east side to (14, 36) and tips scrap into the pit, inside the yard crane's reach. |
| Hire tiles | Porter (15, 20.8), Helper (28, 18.6) | Yard Seller (27.4, 14.7), Back Lot (30.8, 28.4), Gate 2 (36.4, 25), Truck Dock (66.6, 14.1), Loader (66.2, 17.3): moved by `Env_Build.Layout`, which is the source for tile positions it lists. |
| Back Lot tile / area | (29.5, 28.5) / x 19–39.7, z 30.3–38 | Fenced along z 30.3 and x 19 until bought. |
| Gate 2 tile | (37.2, 24.5) | Gate bars sink when bought. |
| Metal Splitter / input pad | (50, 27.4) / (45.6, 27.5) | 5.6 m wide, four chutes 1.4 m apart; four belts fan out south. Boost pad (54.9, 28.8). |
| Iron / aluminum / copper / steel bin | x 44.9 / 48.3 / 51.7 / 55.1, z 19.6 | Belt order west → east. Withdraw pads 3.1 m south of each bin. |
| Metal Market / stock pad / cash pad | (60, 12.4) / (60, 14.4) / (63.7, 14.4) | Line runs south from (60, 10.9); customers come from and leave to the east (x 88). |
| Plant tiles | Upgrades (58.2, 25.2), Hauler (43.2, 23.1), Runner (58.2, 21.8) | Moved in Revamp 1 to clear the four bins. |
| Furnace hall tile / area | (59, 30.6) / x 61.1–75.7, z 19.8–41.8 | Plant's north-east corner, fenced (west + south) until bought. |
| Furnace battery | z 38.6, x 63.9 (iron) / 67.1 (copper) / 70.3 (aluminum) / 73.75 (steel) | Four furnaces side by side against the hall's north wall. Input pads 5 m in front (z 33.6), across the collector belt (z 36.05), which runs west and then south (x 62.2) into the rack. Boost pad for all four (70.6, 28.2). Smelter tile (73.9, 30.9). Build tiles for copper, aluminum and steel stand where their pads will be. |
| Ingot Rack / withdraw pad | (63.3, 28.4) / (66.4, 28.4) | Holds all four ingots (60 / 100 / 150 / 220). |
| Industrial Press / input pad | (69, 23.2) / (64.6, 23.2) | Hall's south end. Build tile on the pad's spot. Belt south (x 69) to the Bar Storage. Boost pad (65.6, 20). |
| Bar Storage / withdraw pad | (69, 17.5) / (72.1, 17.5) | Between the hall and the Truck Dock; the truck pad is 5 m south-west of its pad. Holds all four bars (48 / 72 / 110 / 160). |
| Gate 3 tile / gate | (12, 35.2) / (12, 38) | Opening x 9.4–14.6 in the north wall, straight out of the scrap field. |
| Heavy Scrap Yard | x 0–40, z 38–74 | 14 heavy spawn points (tractors, trucks, garbage trucks, mixers, excavators) parked in three rows of bays (z 45.3, 56, 64.8; fixed yaw, nose in / nose out) plus quick targets in the gaps. Lanes: gate 3 north to z 49, a cross lane z 49–52, a spur x 16–19 up to the giant's zone. Crane + containers along the north wall. |
| Operator consoles / hire tiles | Crusher (21.6, 23, west of the belt), Splitter (54.9, 26.3, east of the machine), Furnaces (68.7, 30.9, in front of the battery; one operator runs all four) | The tile sits where the operator will stand; the console is 0.75 m in front, facing the machine. |
| Seller posts / hire tiles | Yard (30.75, 10.95) / (28.4, 13.7); Market (58.75, 10.95) / (56.4, 13.2) | Sellers stand at the head of the customer line, on the street side (the awning hides the back of the counter). |
| Truck Dock | kerb line (70.8, 10.5), apron x 66.3–75.3, z 10.8–15 | Dock tile (66.6, 14.1). Pad (70, 12.5), cash pad (73.9, 13.9). The Loader tile (63, 18.6) appears with the Press. The truck backs in from x 112.7 and leaves the same way. |
| Giant Scrap landing zone | (17.5, 64.5), giant's long axis east–west | Under the tower crane. Counter sign at (24.8, 68.4). Keep the zone clear of decor. |
| Gate 4 / Dockyard tile | (76, 24) / (73.7, 24) | Opening z 21.4–26.6 in the plant's east wall, inside the Furnace hall (so the hall comes first). |
| Ship Dock (Dockyard) | pad (95.2, 24), cash pallet (92.9, 27.9), cash pad (95.2, 27.9) | At the east end of the lane from Gate 4. The ship moors at (103.8, 36) and sails to (135, 100); bars pile on its stern deck. |
| Dockyard quay | x 76–98, z 11–47; water from x 98 | A lane runs from the gate east to the water (z 21.4–26.6). Ship at (103.8, 36), cranes at (93.5, 31.6) and (93.5, 41.4): everything tall stands north of the lane, because the camera looks north and would otherwise look through it. |

## 3. Runtime systems

```
Core ─────────── Services (scene registry), GameEvents (global events), ModifiableStat, GameManager, GameConfig, IUpgradeable
 │               ISaveable + SaveRegistry (who has state to keep; restores late arrivals on registration)
 │
 ├─ Player ───── PlayerCharacter (wiring) · PlayerStats · PlayerController · PlayerInputReader · PlayerVisuals
 ├─ Harvest ──── ScrapManager (targets, spawn pool) · ScrapSpawnPoint · ScrapObject · ScrapPart · HarvestTool
 │               HarvestManager (drops, loose items, drop bounds + blocked areas + no-rest areas) · DropBlocker
 │               WorldHealthBar · DropMath
 ├─ Items ────── ItemDefinition · WorldItem (flying/ground/moving/held) · ItemPool · CarryStack · ItemCollector
 │               IItemReceiver / IItemSource · ItemPile (hoppers, bins, counters) · TransferPad (stand-on pads)
 ├─ Factory ──── MachineDefinition (input/output, output mix, recipes) + Machine (output ports, one input per cycle in
 │               turn) + MachineVisuals (overdrive look, heat glow, output burst) · WeightedSpread
 │               Conveyor · StorageDefinition + Storage (station id, shared level) · SellDeskDefinition + SellDesk
 │               (ServiceSpeed stat) · TruckBayDefinition + TruckBay (truck that takes a load and pays when full)
 │               StationRegistry · LevelVisuals · OverdriveConfig
 │               ClawCraneDefinition + ClawCrane (feeds a machine or a belt from loose items inside its reach; IUpgradeable)
 │               ItemSpill (Items: a belt end that tips its load back onto the ground)
 ├─ Economy ──── EconomyConfig · EconomyManager (cash, premium) · CashPile · CashCollector · CurrencyFormat
 ├─ Feedback ─── GameFeedback facade · AudioManager (Kenney clips, ProceduralSfx fallback) · VFXManager · HitStopController
 │               FloatingTextManager · SfxDefinition · FeedbackConfig
 ├─ Camera ───── CameraController (follow, look-ahead, shake, punch, aspect-safe framing, focus shift, Focus pans,
 │               SetPullBack for boss fights)
 ├─ Progression  ProgressionManager (XP/level) · UpgradeManager + UpgradeCatalog · PlayerUpgrades + UpgradeVisualTiers
 │               TaskManager (main chain + side tasks) · GuideDirector + GuideAnchor + GuideMarker
 ├─ Workers ──── WorkerManager (hire = IUpgradeable) · Worker (NavMeshAgent body) · WorkerSite/PorterRoute · PorterBrain
 │               (PorterRoute: loose-item zones = Scrap Porter; withdraw pads = Delivery Helper, Metal Hauler, Market Runner,
 │               Smelter, Loader) · WorkPost + PostBrain (Operator at a console, Seller at a customer line)
 ├─ Tiles ────── Tile (stand-on base) · PurchaseTile (pay-by-standing: hires, expansions) · UpgradeTile (opens the panel)
 │               OverdriveTile (boost pad: charge → Speed x2 → cooldown)
 ├─ Customers ── CustomerConfig · CustomerQueue (line, material orders, serve rate from the desk level) · Customer · CustomerBubble
 ├─ World ────── ExpansionDefinition + Expansion (IUpgradeable: barriers sink, content pops in, camera pan, drop area grows)
 │               GiantEventConfig + GiantScrapEvent (heavy scrap charges it, a giant drops in, bonus on defeat)
 ├─ UI ───────── VirtualJoystick · CarryStackIndicator · StationLabel · HudController · CurrencyWidget · UIFlyer
 │               LevelBadge · TaskBanner · UpgradePanel + UpgradeCard + GridColumnsFit · Announcer · GuidePointer · Billboard
 │               BoostBar (chips for running boosts) · OfferButton (the one rewarded-video button) · OfflinePanel
 │               GiantScrapBar (HUD boss bar) · ContractWidget (HUD card for the truck's order) · LoadingScreen (title card
 │               over the first moments, own canvas)
 │
 ├─ Boosts ───── BoostDefinition + BoostManager (timed multipliers: cash, production, move speed, scrap respawn, truck tempo)
 │               AdService (the one door to rewarded video; IRewardedAdProvider, built-in stand-in) · AdOfferDefinition +
 │               OfferDirector (which offer is on the HUD) · IdleIncomeManager (income rate, cash for time away)
 ├─ Persistence  SaveManager (loads before everything, autosaves) · SaveData + SaveFile (versioned JSON, atomic write,
 │               backup, migration hook)
 │
 │  (later)
 ├─ Factory+ ─── Press = a new MachineDefinition + boost pad
 ├─ Economy+ ─── IdleIncomeManager (the save already stores when it was written)
 └─ Progression+ StageManager · daily tasks (each is one more ISaveable)
```

Rules:
- **Services** register in `Awake` (managers run at execution order -500/-1000); consumers resolve in `Start` or lazily.
- **GameEvents** are the only coupling between gameplay and progression (tasks/XP listen, never get called). Events:
  ScrapBroken, ItemsCollected, ItemsProcessed, ItemsDelivered, ItemsSold, CashEarned, UpgradePurchased, WorkerHired,
  CustomerServed, ExpansionOpened, MachineOverdrive, GiantScrapArrived, GiantScrapDefeated, ContractChanged, LevelUp, TaskCompleted.
- **Items are physical.** Every resource is a `WorldItem` that flies, lands, gets carried and gets fed into machines,
  so the player can visually follow material through the chain (blueprint p.7).
- **Stats are modifiable.** Base from data asset, upgrades add `StatModifier`s keyed by source; overdrive is a timed
  `Multiply` modifier.
- **No hard-coded balance.** Tuning in ScriptableObjects; MonoBehaviours hold presentation/feel values only.
- **Items move through receivers.** Anything that takes items implements `IItemReceiver`, anything that gives them
  `IItemSource`. Pads, machines, conveyors, storage, counters and carry stacks all plug together through these two,
  so every worker (and the Loader in M7) reuses the exact same pads and stacks as the player.
- **Bottlenecks are physical.** A full storage stops the conveyor, which blocks the machine (red light), which fills the
  hopper, which stops the deposit pad. A splitting machine's outputs leave in order, so one full belt holds it too.
  Station labels say it in words: a status tag shows JAMMED, FULL, x2 BOOST or NEEDS STOCK (customers waiting at an
  empty counter).
- **No deadlocks.** The one dead end the physical chain had (storage full → crusher jammed → player holding a full stack
  of raw scrap → can't pick up goods to sell) is broken by spilling: a deposit pad with `spillAfter` throws a few of a
  full, spill-allowed stack's items onto the ground after a short wait (`TransferPad`, `CarryStack.canSpill`, player
  only). The guide sends the player to drain a jammed machine's goods before feeding it more.
- **One purchase path.** Stations, player stats, worker hires and expansions all implement `IUpgradeable` and register
  with `UpgradeManager`; cards, tiles, tasks and the guide only talk to that. A hire is an upgrade whose level is the
  headcount; an expansion is a one-level upgrade. Bins that share an upgrade follow a leader's level (`levelSource`).
- **Buying happens in the world.** Station and player upgrades are bought in the `UpgradePanel`, which opens while the
  player stands on an upgrade tile. Catalog entries flagged `inPanel` are grouped (YOU, OLD YARD, RECYCLING PLANT); a
  group appears once one of its upgrades exists in the world, so locked areas add their cards when they open. Hires and
  expansions have their own `PurchaseTile`: standing on it drains cash into the tile (partial payments are kept) and
  `UpgradeManager.CompletePrepaid` applies the level. One purchase per visit.
- **Customers are physical.** `CustomerQueue` replaces the desk's walk-in timer: buyers walk in from the road, the front
  one rolls an order (material from `CustomerConfig.orderOptions`, favouring what is on the counter), gets it handed
  over unit by unit (`SellDesk.TryHandOver`), pays once (`CompleteSale`) and walks off. A desk sells only its
  definition's `sells` list. The desk level sets serve rate, order size and line length; an empty counter stalls the line.
- **One furnace per metal.** Each furnace is a plain one-input `MachineDefinition` (`furnace`, `furnace_copper`,
  `furnace_aluminum`, `furnace_steel`) on its own prefab. The Iron Furnace comes with the hall; the other three are
  `Expansion`s nested in the hall's content (plot marker and build tile until bought, then a camera pan and the furnace
  pops in). All collector-belt segments belong to the hall, so a furnace bought out of order still has an outlet. One
  rack holds all four ingots: a full rack backs up the belt and stops the whole battery.
- **A carrier can serve several machines.** `PorterRoute.extraDropoffs`: the worker loads only stock that a built
  drop-off takes (by item type) and walks to the pad that takes what it carries; a full hopper keeps it waiting at
  that pad. One Smelter does all four furnaces. Routes with one drop-off behave as before.
- **A boost pad or an operator can run a group.** `OverdriveTile.alsoBoosts` and `WorkPost.alsoRuns` add the same
  modifier to more machines (unbuilt ones are skipped): one pad and one operator for the battery.
- **Multi-input machines are data** (supported, not in use since R2). A `MachineDefinition` with `recipes` (input → product pairs) takes every listed input;
  the M6 Furnace turned iron into iron ingots and copper into copper ingots that way. The hopper holds a mix; each cycle runs one
  input, taking turns so neither material starves. `Takes`, `OutputFor` and `ProducesAny` answer the guide's and the
  customers' questions for every machine type.
- **Splitting and boosting are data.** The Metal Splitter is a `MachineDefinition` (id `sorter`) whose `outputMix`
  weights (iron 40, aluminum 25, copper 20, steel 15) are handed out by a smooth weighted round-robin
  (`WeightedSpread`), each product leaving through its own `OutputPort` (one colour-coded belt per bin). One full bin
  holds the whole machine, so every stream has to keep moving. Active Overdrive is a `Multiply` modifier on `Machine.Speed` from a
  boost pad; its numbers are an `OverdriveConfig`.
- **Locked areas are physical too.** An `Expansion` keeps its barriers (NavMesh-carving, ignored by the bake) and
  registers its footprint as a `HarvestManager` blocked area so drops bounce off it. Opening it pans the camera, sinks
  the barriers, swaps the ground material and pops the content in piece by piece with dust and sound.
- **A staffed station is the worker's job.** When the current task is about a station that a hired worker supplies
  (a furnace with the Smelter, a counter with the Runner, the truck with the Loader), the guide does not send the player
  to carry beside the worker. It answers `Supply()`: fetch a cash pile worth $500 or more, help a stuck customer, do a
  link nobody is hired for, otherwise cut scrap and feed the first machine. `WorkerManager.Serves` counts every
  drop-off of a route (`PorterRoute.DropsAt`). Before this the player hauled a few pieces at a time next to the Smelter,
  nobody cut scrap, and the yard's income went to zero for a quarter of an hour.
- **The Runner leaves what a machine wants.** `PorterRoute.leaveMachineInputs` (on the Runner's route): stock that a
  built machine takes as input is not carried to the market. Raw iron belongs to the Iron Furnace once it stands; the
  market gets ingots and the raw metals that have no furnace yet.
- **A carrier leaves a machine its buffer.** `PorterRoute.machineInputKeep` (24 on the Delivery Helper's route): that
  many pieces of machine input stay in the storage; only the surplus goes to the counter. Without it the Helper sold
  every Raw Metal bale and the Splitter starved.
- **Finished goods first.** `PorterRoute.priorityLoad` (4 on the Runner's route): the first pickup pad that holds this
  many usable pieces wins over a fuller one. List the pads in order of value (Ingot Rack before the raw bins).
- **Saving for something = do the most useful job.** When the task needs cash, `GuideDirector.Earn` picks, in order:
  fetch waiting cash once it is a quarter of the price (or covers it); serve a customer stuck at an empty counter; do
  any link no worker is hired for (`WorkerManager.Serves`) when a load worth carrying (6+) is waiting; otherwise cut
  scrap and feed the first machine, the one job no worker does. Hand-stocking staffed counters starved the whole yard.
- **Customers do not wait forever.** If their material runs out they settle after `CustomerConfig.settleAfter` seconds,
  or at once when the counter is full of other goods: they pay for what they got, or pick something in stock. An empty
  counter still stalls the line.
- **Always an obvious next action.** `GuideDirector` reads the current main task plus the player's stack and station
  stock, then points one world marker. It walks the production chain: to feed a machine it looks for loose input, then a
  storage holding it, then the machine that makes it, then scrap that drops it; to sell it picks the desk worth stocking
  and the storage holding what that desk (or the task) wants. Scrap that is too tough for the chainsaw turns into
  "upgrade the chainsaw"; affordable panel upgrades point at the upgrade tile and pulse their card. When it sends the
  player to earn, it only picks scrap the chainsaw can cut (`NearestScrap` skips tougher objects unless a task names
  them): with the faster start the Back Lot opens before the second chainsaw upgrade, and the guide used to park the
  player at a fridge.
- **Guide anchors.** Pads and tiles carry `GuideAnchor`s. A station anchor derives its id from the station plus a role
  (`<stationId>/in|out|cash|boost`), so one prefab serves every instance; tiles use `tile/upgrades` and `tile/<upgradeId>`.
- **Every upgrade is visible.** `LevelVisuals` turns on extra station geometry per level; `UpgradeVisualTiers` recolours
  the chainsaw. Numbers alone never count as an upgrade.
- **Real audio.** Every `SfxDefinition` carries Kenney CC0 clips (impact, interface, coins, jingles); `ProceduralSfx` is
  only a fallback (and synthesises the looping chainsaw motor played by `ToolAudio`). New sounds are new `Sfx_*` assets,
  never code. Machine cycle/output sounds follow `Machine.Speed` in pitch.
- **Cutting feel is data.** `FeedbackConfig` holds hit-stop, shake, hit flash, pickup ramp and stack landing squash;
  `ScrapDefinition` holds per-object drop launch, spread and `dropBurstDuration` (loot fountains for big objects).
- **XP follows the business.** `ProgressionConfig.xpPerCashSold` adds XP per $ of every sale on top of the flat XP per
  sale, so levelling keeps pace as income grows (tuned with the guide bot).
- **Automation hooks.** `PlayerInputReader.ExternalMove` lets a non-human driver steer (the guide bot); real input
  always wins. Hit-stop restores the previous time scale, so accelerated play tests survive it.
- **Specialists boost through stats.** An Operator or Seller is a `Worker` with a `PostBrain` and a `WorkPost` site. While
  the worker stands at the post, the brain adds one `Multiply` modifier (the definition's `workBoost`) to
  `Machine.Speed` or `SellDesk.ServiceSpeed`, and removes it when the worker leaves. Each machine and each desk has its
  own hire, so the player chooses which bottleneck to staff. The station label shows the result ("x1.5 BOOST",
  "x1.6 SERVICE"). A Seller's post is on the street side of the counter, so `WorkerDefinition.spawnAtSite` makes them
  appear there instead of walking in.
- **Two cranes make a chain.** The Heavy Yard crane's target is a belt (`ClawCrane.takes` limits it to scrap, because a
  belt accepts anything); the belt ends in an `ItemSpill`, an `IItemReceiver` that throws each item back onto the ground
  as a loose item (and refuses while 70 loose items already lie around, so the belt backs up instead of burying the
  pit). The spill lands inside the yard crane's reach, which feeds the Crusher. Heavy scrap crosses two areas without
  being carried. Each crane's jib is a truss in three sections (`ClawCrane.jibSections`): a level adds a section.
- **A crane automates a machine's feed.** `ClawCrane` looks for the nearest loose item its receiver takes
  (`HarvestManager.TryFindNearestCollectable`, inside the level's reach), swings the jib and trolley over it, lowers the
  claw, takes up to `grabSize` pieces around that point (`TakeNearestCollectable`), lifts, swings to the drop point
  above the hopper and hands them to the receiver. What the hopper cannot take stays in the claw until there is room.
  It is bought on a `PurchaseTile` like a hire (level 0 = foundation only, three levels); the level is saved by
  `UpgradeManager`. The Porter still exists: the crane covers what lies inside its circle, the Porter the rest.
- **The ship is a truck.** The port's Ship Dock is a second `TruckBay` (definition `TruckBay_Ship`, id `ship_dock`) whose
  carrier is the cargo ship: same pad, orders, HUD card, cash pallet, save and upgrade path. It starts moored
  (`startDocked`), holds 60 / 90 / 130 bars, pays x1.9 / 2.1 / 2.3 and is away 14 / 12 / 10 s plus the sail. Its course
  points sit in the backdrop, not under the dock: an expansion pops its content in from scale 0, and anything a station
  reads in `Start` must not hang under that. The dock label's "coming" and "away" texts are data
  (`TruckBayDefinition.arrivingText` / `awayText`). The Loader's route lists the ship's pad after the truck's.
- **The Press is the recipe machine.** One `MachineDefinition` (`press`) with four `recipes` (each ingot → its bar):
  the hopper holds a mix and each cycle presses one ingot, taking turns. Its ram is the machine's piston
  (`MachineVisuals`, 0.98 m stroke), so it slams once per bar.
- **Boosts are data on a clock.** A `BoostDefinition` says what is multiplied, by how much and for how long;
  `BoostManager` runs one boost per kind (activating again adds time, up to three durations) and saves the time left.
  Machine speed and the player's walk get an ordinary `Multiply` modifier, re-applied once a second so a machine built
  during a boost has it too. Three things have no stat to modify and ask `BoostManager.Multiplier(kind)` instead:
  `CashPile.Add` (sales income; not cash put back by a loaded save), `ScrapSpawnPoint` (respawn delay) and
  `TruckBay` (delay until the next truck).
- **Ads are optional and go through one service.** Nothing shows a video by itself. `OfferDirector` puts at most one
  offer on the HUD (`OfferButton`): the first after 90 s, then a quiet 70 s between offers, each offer on its own
  cooldown, never a boost that is already running; right after a completed truck order it offers to pay that order
  again. A tap calls `AdService.ShowRewarded`; the reward runs only if the video was earned. No ad SDK is integrated:
  `AdService` grants through a 0.8 s stand-in until `SetProvider` is given a real `IRewardedAdProvider`.
- **Offline income is a gift, not a simulation.** `IdleIncomeManager` smooths what the yard earns per second (10 s
  buckets into a 3 minute average) and saves the rate and the time. On load it credits 30% of that rate for the time
  away (2 minutes minimum, 2 hours maximum since U6) as cash waiting on the `WelcomeBack` card: claim, or double it
  with a video. The yard itself is as it was left.
- **A truck carries an order.** `TruckBay.RollContract` runs when a truck docks; `Wants(item)` says what the order still
  needs. Orders are data; the bay only counts what is on the bed against them. It raises `GameEvents.ContractChanged`
  (a `ContractStatus` snapshot) on dock, on every bar, on completion and on departure; the HUD's `ContractWidget` and
  the dock label show it. Completion sounds the horn, punches the camera and pops "ORDER COMPLETE!".
- **The truck is one big order.** `TruckBay` is an `IItemReceiver` behind an ordinary deposit pad, so the player and the
  Loader (a `PorterRoute` with role Loader and two drop-offs: Ingot Rack pad → Press pad, Bar Storage pad → dock pad) use it like any counter. The truck leaves when
  its bed is full, pays the whole load at the level's premium onto the dock's `CashPile`, raises `ItemsSold` per
  material, and the next truck backs in after `returnDelay`. While it is away the pad takes nothing and carriers wait
  on it: the dock's visible bottleneck. Ingots are contested between the Runner (market) and the Loader (truck).
- **The Giant Scrap event is earned, not timed.** `GiantScrapEvent` counts broken scrap of tier 3+ (`GiantEventConfig`:
  3 for the first giant, 8 for each repeat; a sign at the landing zone shows the count). When charged and the zone is
  clear of characters, the giant spawns through `ScrapManager`, drops from the crane, and the camera pans over. While
  the player is at the giant the camera pulls back (`CameraController.SetPullBack`) and the HUD shows `GiantScrapBar`.
  The giant itself is plain data: a `ScrapDefinition` (tier 5, Giant) and a prefab with 16 `ScrapPart`s and
  `ScrapDamageStages`. Defeat pays a cash bonus on top of the drop.
- **Boss scrap dies in a sequence.** A `ScrapDefinition` with `breakDelay` puts the object in a short Dying phase after
  the killing hit: it takes no more damage, rattles harder and fires `breakDelayBursts` explosions before the normal
  break. `hitVfxScale` sizes the sparks of each hit. Ordinary scrap leaves both at their defaults.
- **Saving is opt-in per system.** Anything with state implements `ISaveable` (a key, `CaptureState`, `RestoreState`,
  its own small JSON) and calls `SaveRegistry.Register` from `Start`. `SaveManager` only reads and writes the file.
  If the loaded file holds a state for that key, the system is restored the moment it registers, so content inside a
  locked area restores itself when the area is restored and its objects start. States whose owner never registers are
  written back unchanged: a save never loses data for content that is locked or not in the scene.
- **Restoring is quiet.** `SaveRegistry.IsRestoring` is true while saved state is applied. `UpgradeManager` raises saved
  levels through the normal `IUpgradeable.ApplyLevel`, and the few systems with fanfare check the flag: `Expansion`
  opens instantly (no pan, no reveal), `WorkerManager` puts workers at their site without popup, XP or `WorkerHired`,
  `Machine` skips the upgrade burst. No purchase events fire, so tasks and XP cannot double-count.
- **A save cannot be half-written or half-restored.** `SaveFile.Write` writes a temp file, then swaps; the previous save
  becomes `.bak`. Reading falls back to the backup, sets unreadable files aside as `.corrupt` instead of deleting them,
  and refuses a file from a newer build without ever overwriting it. `SaveManager` does not write until the restore
  frames are over. Saves happen every 20 s, 1.5 s after a purchase / task / level-up, and on pause or quit.
- **Loose items never rest where nobody can stand.** Scrap mounds and big props carry a `DropBlocker`: their footprint
  is a no-rest area in `HarvestManager`. A piece that lands there hops back toward where it was thrown from
  (`WorldItem` remembers its launch point). Blocked areas (locked expansions) stay hard walls; no-rest areas can be
  flown over.
- **DOTween** drives presentation only (pops, punches, jumps, counters, UI fly-ins). Item flight into stacks and
  hoppers stays in `WorldItem.MoveTo` so it can follow moving parents.

## 4. Production chain and economy (as built)

```
 Scrap objects ──cut──► Scrap pieces ──► Crusher ──belt──► Yard Storage ─┬─► Sell Desk (Raw Metal $8, queue 3)
 (A1 field, Back Lot)    (player, Porter,  boost pad      (Raw Metal)    │
                          Claw Crane)                                    │
                                                                          └─► Metal Splitter ─┬─belt─► Iron bin ──────┐
                                                                (player, Hauler)  boost pad    ├─belt─► Aluminum bin ──┤
                                                                                               ├─belt─► Copper bin ────┼─► Metal Market
                                                                                               └─belt─► Steel bin ─────┘   (queue 5)
                                                                                                        (player, Runner)
                              four bins ──(player, Smelter)──► Iron / Copper / Aluminum / Steel Furnace ──collector belt──► Ingot Rack
                                                                (one per metal, bought one after the other;      (four ingots)
                                                                 one boost pad, one operator)                        │
                                                                              Ingot Rack ──(player, Runner)──► Metal Market
                              Ingot Rack ──(player, Loader)──► Industrial Press ──belt──► Bar Storage ──(player, Loader)──► Truck
                                                               (four recipes: ingot → bar)  (four bars)        (an order per truck)
 Heavy vehicles (A3, cut power 30-45) ──► scrap pieces (25-60) + rare iron/copper chunks ──► same Crusher / Furnace
```

From the Press on, trucks are the main sale: the truck buys bars only, and every truck arrives with an order. Ingots
still sell at the Metal Market, so the early plant earns before the Press exists.

| Material | Value | Source |
|---|---|---|
| Scrap | 0 | Cutting scrap objects. |
| Raw Metal (id `mixed_metal`) | $8 | Crusher (1 scrap → 1). Sold at the Sell Desk. |
| Iron | $10 | Splitter, 40%. Sold at the Metal Market. |
| Aluminum | $16 | Splitter, 25%. Sold at the Metal Market. |
| Copper | $24 | Splitter, 20%. Sold at the Metal Market. |
| Steel | $36 | Splitter, 15%. Sold at the Metal Market. |
| Iron ingot | $26 | Iron Furnace (id `furnace`), 0.9 s per ingot at Lv.1. |
| Aluminum ingot | $40 | Aluminum Furnace, 1.0 s. |
| Copper ingot | $60 | Copper Furnace, 1.1 s. |
| Steel ingot | $90 | Steel Furnace, 1.5 s. Ingots sell at the Metal Market. |
| Iron / aluminum / copper / steel bar | $40 / $60 / $90 / $135 | Industrial Press (1 ingot → 1 bar, 0.8 s at Lv.1, 0.39 s at Lv.5). Sold by truck. |

One Raw Metal ($8) splits into $18.20 of material on average.

Rates after the speed passes (`R1_Build.Balance`, last changed 2026-10-06): player 6.8 m/s, 7 cuts/s, carries 14 (+3 per
backpack level); Crusher 0.35 s per piece at Lv.1 (0.16 s at Lv.5), hopper 20–50; yard storage 60 / 90 / 130; Splitter 0.6 s (0.29 s); customers arrive every 0.35–0.9 s at 5.2 m/s and take their order at once (owner's rule: no
waiting bar; `saleInterval` is 0.3 s at Lv.1 down to 0.18 s, hand-over 0.05 s per unit). Desk levels grow the order
(3–5 up to 5–9), the price, the counter and the line, no longer the serve time.

| Worker | Route | Hire (unlock) |
|---|---|---|
| Scrap Porter | Loose scrap in the A1 field and Back Lot → crusher pad | $300 / $1,000 (Lv 2) |
| Delivery Helper | Yard storage pad → Sell Desk stock pad | $450 / $1,400 (Lv 3) |
| Metal Hauler | Yard storage pad → Splitter input pad | $600 / $2,000 (Lv 5) |
| Market Runner | Fullest of the four bin pads and the ingot rack pad → Metal Market stock pad | $1,100 / $2,600 (Lv 6) |
| Smelter | Fullest bin whose metal a built furnace takes → that furnace's pad (one Smelter for all four) | $1,500 / $4,000 (Lv 6) |
| Crusher / Splitter / Furnace Operator | Stands at the console: machine speed x1.5 (the Furnace Operator runs the whole battery) | $3,500 (Lv 9) / $4,500 (Lv 9) / $6,000 (Lv 10) |
| Yard / Market Seller | Stands at the head of the customer line: service speed x1.6 | $5,000 (Lv 9) / $7,500 (Lv 10) |
| Loader | Ingot Rack pad → Press pad, and Bar Storage pad → truck pad (one worker, both hops) | $3,000 / $8,000 (Lv 9) |

Gates: Back Lot $700 (Lv 4, tier-2 scrap: fridges need cut power 20; washers, stoves and go-karts 15), Recycling
Plant $1,000 (Lv 5), Iron Furnace (the hall) $2,000 (Lv 6), Copper Furnace $3,500 (Lv 7), Aluminum Furnace $6,000 (Lv 8), Steel Furnace $10,000 (Lv 9), Heavy Scrap Yard $10,000 (Lv 9: tractors need cut power 30, trucks 35,
garbage trucks 45; 25-60 pieces each plus a rare iron or copper drop). Active Overdrive: stand 1.5 s on a boost pad →
2x for 10 s, 6 s cooldown.

Industrial Press: $9,000 build (Lv 9), upgrades $2,500 / $5,000 / $9,000 / $16,000; Bar Storage upgrades $2,000 /
$4,500 / $9,000.

Truck contracts (`TruckContractDefinition`, six orders in `Data/Factory/Contracts`): a truck docks with one order,
picked by weight among those the yard can serve now (dock level; every product in stock or makeable two machines back,
so no steel order before there is steel). An order asks for 75% of the bed (`TruckBayDefinition.contractFill`), split
by the order's shares: IRON, ALUMINUM, COPPER and STEEL ORDER (one metal; x1.0 / 1.05 / 1.1 / 1.15), BUILDER'S ORDER
(iron and steel 3:2, x1.2), MIXED ORDER (all four, x1.3, from dock Lv.2). Bars the order asked for are paid at the
dock's multiplier times the order's; any other bar on the bed at the dock's plain multiplier. The truck leaves when the
order is met or the bed is full. It never refuses a bar, so a stack cannot clog on the wrong metal. A Lv.1 iron order is
12 of 16 bars and pays $672 for the order alone; a full steel order pays about $2,600.

Truck Dock: $8,000 (Lv 10). The truck used to buy iron and copper ingots (M7); it buys bars now. Levels: 16 / 24 / 32 / 48 crates per truck, payout
x1.4 / 1.55 / 1.7 / 1.85 of base value, 8 / 7 / 6 / 5 s between trucks (upgrades $5,000 / $10,000 / $18,000, panel
group TRUCK DOCK). A full Lv.1 truck of mixed ingots pays about $1,000. The Metal Market tops out at x1.35, so the
truck is the better price but pays in lumps and needs steady supply.

Heavy wrecks (Revamp 4): tractor (700 health, power 30, 25–32 pieces, iron 60%), truck (900, power 35, 30–40, copper
60%), garbage truck or mixer (1,300, power 45, 45–60, steel 60%), excavator (2,000, power 45, 80–95, 6–10 aluminum
always; its own scrap type `excavator`, tier 4). The metal drops are loose raw metal: they go straight to a furnace.

Giant Scrap: the Giant Truck has 6,000 health, needs cut power 40 and drops 140–160 scrap plus 12–18 copper, 300 XP
and a $5,000 bonus. With the top chainsaw the fight takes about 25 seconds.

Customers order only what the yard can make right now (`CustomerQueue.Obtainable`: a registered machine produces it, or a
bin holds it), so ingots appear in market orders once the Furnace exists. A customer waiting at an empty counter sets
`SellDesk.WaitingFor`, and the guide brings that item first, even during a "sell copper" task, because they block the line.

Dockyard: $20,000 (Lv 11), the session's last purchase. Measured pacing (guide bot, `Docs/Pacing/README.md`) after
Revamp 1: Scrap Porter 2:58, Back Lot 6:31, Recycling Plant 9:44, Metal Hauler 13:43. Everything from the Furnace on
still has the pre-revamp numbers (Furnace at 46 min in the old run, about $850 per minute late in the session) and has
not been measured since; that half of the chain is rebuilt in the next revamp steps.

Task chain (`TaskChain_Area1`): 55 main tasks, from "Cut the car" to `t50_dockyard` (M8 added `t24b_runner` after the
Hauler and `t48`–`t50`: dock to Lv.2, load 72 ingots, open the Dockyard). Earlier parts: 50 main tasks to `t47_giant2`. The M6 part ends at `t37_crush500`
("Crush 500 scrap", the blueprint's completion beat); the M7 tasks follow: crusher operator → the Giant Truck → yard
seller → Lv 10 → Truck Dock → load 32 ingots → Loader → furnace operator → serve 40 customers → a second giant. "Boost the crusher" follows "Crush 30"; "Sell desk to Lv.3" and "Crusher to Lv.3" lead into Gate 2
(the guide bot showed the Lv.2 desk capping income for 17 minutes); `t17b_claw` ("Build the claw crane") follows the Back Lot; the plant tasks start at `t20_plant` (Revamp 1: split 15 → stock the market with 12 of anything (`t22_copper`, id kept) →
hire the Hauler → serve 8 market customers → Runner), the M6 tasks at
`t28_furnace` (Furnace → smelt → sell iron ingots → boost → Smelter → Lv 9 → Heavy Scrap Yard → cut trucks → sell copper
ingots → crush 500). Six side tasks rotate from main task 8 on.

## 5. Data assets

| Asset | Milestone | Holds |
|---|---|---|
| `GameConfig` | 1 | Root config refs (feedback; later economy/stages). |
| `PlayerConfig` | 1 | Move speed/accel/turn, cut power/rate/range, carry capacity, pickup radius. |
| `ItemDefinition` | 1 | Id, name, colour, icon, world prefab, stack height, base value. In use: Scrap, MixedMetal (shown as Raw Metal), Iron, Aluminum, Copper, Steel, four ingots (IronIngot ...), four bars (IronBar ...). |
| `ScrapDefinition` | 1–7 | Tier, size class, prefab (+ variants), health, min cut power, drop item/range, part-drop share, rare drop, XP, respawn, feedback overrides, hit VFX scale, final destruction (break delay, bursts). |
| `FeedbackConfig` | 1–5 | Hit-stop, shakes, punch, hit flash, pickup fly/pitch ramp, stack landing squash, default VFX/SFX. |
| `SfxDefinition` | 1–5 | Clips (Kenney CC0), volume, pitch range, rate limit, procedural fallback. |
| `MachineDefinition` | 2–6 | Input/output items, optional weighted output mix (Sorter) or recipes (input → product pairs, Furnace), per-level input capacity, cycle time, inputs/outputs per cycle, upgrade cost. In use: Crusher, Sorter (the Metal Splitter), Furnace (iron), FurnaceCopper, FurnaceAluminum, FurnaceSteel, Press (four recipes). |
| `StorageDefinition` | 2–5 | Per-level capacity, withdraw speed, upgrade cost (blueprint: 40 units). In use: Storage_Yard, Storage_Bins (shared by the four bins; the iron bin leads the level), Storage_IngotRack, Storage_Bars. |
| `SellDeskDefinition` | 2–5 | Items it sells, per-level sale interval (blueprint: 1 / 8 s), units per customer, price multiplier, counter size, queue size. In use: SellDesk_Yard, SellDesk_Market. |
| `EconomyConfig` | 2 | Starting cash/premium, cash per physical bundle. |
| `PlayerStatUpgradeDefinition` | 3 | Player stat, modifier type, cumulative modifier per level, costs, card text. In use: Chainsaw, Backpack, Boots. |
| `UpgradeCatalog` | 3–5 | Every purchasable id: unlock yard level, shown in the panel or not, panel group. |
| `ProgressionConfig` | 3–5 | Level curve (base, growth), XP per sale + per $ sold, per upgrade/hire, cash per level-up. |
| `TaskDefinition` / `TaskChain` | 3–5 | Type, target id, amount, rewards, category; ordered main chain + side pool. |
| `WorkerDefinition` | 3–7 | Role, site id, prefab, tagline, speed/carry/efficiency/pickup radius, loose-item collecting, work boost (specialists), spawn-at-site, hire cost per headcount. |
| `CustomerConfig` | 4–5 | Customer prefabs, walk speed, arrival interval, hand-over interval, order item / weighted order options, prefer-in-stock chance, SFX. One per desk. |
| `ExpansionDefinition` | 4–5 | Id, name, icon, cost, tile teaser, open SFX. Unlock level lives in the catalog; scene content on the `Expansion`. |
| `OverdriveConfig` | 5 | Speed multiplier (2x), duration (10 s), charge time, cooldown, SFX. |
| `BoostDefinition` | R5 | Id, name, icon, kind, multiplier, duration, stack limit, colour, SFX. In use: Boost_Cash (x2, 120 s), Boost_Production (x2, 90 s), Boost_Speed (x1.5, 120 s), Boost_ScrapRush (respawn x2.5, 90 s), Boost_TruckRush (truck return x3, 120 s). |
| `AdOfferDefinition` | R5 | Id, title, icon, kind (boost / free cash / double truck), boost, cash per yard level, seconds on screen, cooldown, minimum level, weight. Seven in `Data/Boosts`. |
| `ClawCraneDefinition` | Revamp | Id, name, icon, per-level reach / grab size / speed / cost, grab radius, SFX. In use: ClawCrane_Yard ($600 / $1,400 / $3,000, yard Lv 4), ClawCrane_Heavy ($5,000 / $9,000 / $15,000, yard Lv 9). |
| `TruckBayDefinition` | 7, R3 | Goods the truck buys, per-level capacity / payout multiplier / return delay / upgrade cost, truck speed, SFX, the orders it can arrive with, the share of the bed an order asks for. In use: TruckBay_Plant, TruckBay_Ship. |
| `TruckContractDefinition` | R3 | Id, short name, icon, lines (product + share), reward multiplier, minimum dock level, weight. In use: six in `Data/Factory/Contracts`. |
| `GiantEventConfig` | 7 | The giant's `ScrapDefinition`, feeder tier, charge for the first and repeat giants, cash bonus, drop-in and landing feel, fight camera, announcer and sign text. In use: GiantEvent_HeavyYard. |
| `StageDefinition` | later | Unlocked scrap tiers, spawn tables, milestone task. |

### Save file

`<persistentDataPath>/scrapyard_save.json` (plus `.bak`). `SaveData`: `version` (1), `appVersion`, `savedAtUnix`,
`playSeconds`, and `entries`: one `{ key, state }` per saveable, where `state` is that system's own JSON string.

| Key | Owner | State |
|---|---|---|
| `economy` | `EconomyManager` | cash, premium |
| `progression` | `ProgressionManager` | level, XP, fractional XP carried from cash |
| `upgrades` | `UpgradeManager` | level per upgrade id: stations, player stats, worker headcount, expansions (0/1), truck dock |
| `tasks` | `TaskManager` | current main task (by id, index as fallback) + progress, side tasks + progress, side cursor |
| `tile/<upgradeId>` | `PurchaseTile` | cash already paid into the tile |
| `stock/<stationId>` | `Storage`, `SellDesk`, `Machine`, `TruckBay` | item id + count lines (bin, counter, hopper, truck bed) and cash waiting on the pallet |
| `giant_event` | `GiantScrapEvent` | charge, giants defeated (a giant standing in the yard is saved as "fully charged") |
| `boosts` | `BoostManager` | id and seconds left of each running boost |
| `idle` | `IdleIncomeManager` | income per second, time of saving |

Not saved: loose items on the ground, the player's carry stack and position, items on belts or inside a machine,
customers in line. To change the format, raise `SaveData.CurrentVersion` and add a step to `SaveFile.Migrate`. A new
system needs no change here: implement `ISaveable` and register.

## 6. Scene hierarchy (`Area1_OldScrapYard.unity`, holds Areas 1–3)

```
Area1_OldScrapYard
├─ _Systems        BoostManager, AdService, OfferDirector, IdleIncomeManager (R5), SaveManager (first), GameManager, EconomyManager, ItemPool (item catalog), ScrapManager, HarvestManager, AudioManager, VFXManager,
│                  HitStopController, FloatingTextManager, ProgressionManager, UpgradeManager, TaskManager,
│                  GuideDirector, WorkerManager
├─ _Environment    Ground slabs (Area2_Locked / Area3_Locked swap to dirt when their gate opens), Road, Sidewalk,
│                  Fences (corrugated walls around A1 + A2 + A3, rail frontage, desk/market side blockers),
│                  Gate_A1_A2 (bars + collider = plant barriers, LockedSign), Gate_A1_A3, Gate_A2_A4, JunkPiles, Backdrop,
│                  NavMesh, DockyardBackdrop (quay slab, quay wall, water, ship, two cranes, edge blockers: always there)
├─ _Gameplay
│  ├─ PlayerStart, Player (prefab), LooseItems, ItemPool
│  ├─ ScrapSpawnZone  SP_* (car wreck variants, barrels, tire stacks)
│  ├─ Stations     Crusher (24,27) → Conveyor → Storage (24,19.2) · SellDesk (32,12.4) + CashPile · Console_Crusher
│  ├─ Workers      WorkerSpawn, Sites/PorterRoute (scrap zones + Back Lot zone → crusher pad), Sites/DeliveryRoute,
│  │               Sites/HaulerRoute (yard storage → sorter), Sites/RunnerRoute (bins → market), Sites/SmelterRoute,
│  │               Sites/LoaderRoute (rack → dock), Sites/Post_Crusher|Sorter|Furnace, Sites/Post_SellerYard|Market
│  ├─ Tiles        Tile_Upgrades, Tile_Porter, Tile_Helper, Tile_BackLot, Tile_RecyclingPlant, Tile_BoostCrusher,
│  │               Tile_HeavyYard, Tile_Operator_crusher, Tile_SellerYard
│  ├─ Customers    CustomerQueue for the Sell Desk: Slots (7, front first), Entry path, Exit path, Crowd (pooled)
│  ├─ BackLot      Expansion: Barriers (fences), LockedOnly (sign), Content (tier-2 spawn points), Focus
│  ├─ RecyclingPlant  Expansion: Content (floors, Sorter (the Splitter), Conveyor_Iron/Aluminum/Copper/Steel, Bin_Iron,
│  │               Bin_Aluminum, Bin_Copper, Bin_Steel, MetalMarket,
│  │               Customers_Market, Tile_UpgradesPlant, Tile_Hauler, Tile_Runner, Tile_BoostSorter,
│  │               FurnacePlot (the hall's fence + LockedOnly sign), Tile_FurnaceHall, Console_Sorter,
│  │               Tile_Operator_sorter, Tile_SellerMarket, TruckDockPlot (fence across the dock gap + sign),
│  │               Tile_TruckDock), Focus
│  ├─ FurnaceHall  Expansion: Content (floor, Furnace (iron), Conveyor_Collect_Iron/Copper/Aluminum/Steel/Down, IngotRack,
│  │               Copper/Aluminum/SteelFurnacePlot (each an Expansion: LockedOnly foundation + build tile, Content furnace,
│  │               Focus), PressPlot (Expansion: LockedOnly foundation + build tile; Content: Press, Conveyor_Bars,
│  │               BarStorage, Tile_BoostPress, Tile_Loader), Tile_BoostFurnace, Tile_Smelter, Console_Furnace,
│  │               Tile_Operator_furnace), Focus.
│  │               Its barriers and sign live in the plant content, so they appear only once Gate 2 is open.
│  ├─ HeavyYard    Expansion: LockedOnly (gate sign), Content (Decor: crane, containers, tank, heaps; SP_* heavy spawn
│  │               points; GiantEvent: Arena, ZoneMarkings, Sign), Focus. Barriers = Gate_A1_A3 bars + collider;
│  │               Area3_Locked ground swaps to dirt.
│  ├─ TruckDock    Expansion: Content (Floor_Dock, TruckBay prefab: kerb, Pad, CashPile, CashPad, label, Truck with
│  │               Cargo pile; Tile_Loader), Focus. Barriers and sign live in the plant content (TruckDockPlot).
│  ├─ Dockyard     Expansion: LockedOnly (gate sign), Content (lane lines, container stacks, bollards, lights, forklift,
│  │               flags, ShipDock: pad, cash pallet and pad, label), Focus. The ship, its cargo pile and its course points are
│  │               in _Environment/DockyardBackdrop. Barriers = Gate_A2_A4 bars. Tile_Dockyard is in the hall content.
│  ├─ GuideMarker
│  └─ Slots        Slot_CustomerQueue, Slot_Gate_A1_A2 (legacy anchors)
├─ _Camera         Main Camera (CameraController)
├─ _Lighting       Directional Light, Global Volume (VP_Yard)
└─ _UI             Canvas (JoystickArea, HUD: LevelBadge, cash/gem pills, TaskBanner, GiantBar, GuidePointer,
                   UpgradePanel, Announcer, FlyLayer), EventSystem,
                   LoadingCanvas (LoadingScreen; sorting order 100; Cover inactive in the scene: Art, Logo, Plate/Track/Fill)
```

The NavMeshSurface covers Areas 1–3 and the quay (centre (48, 1, 42), size 100 × 6 × 66; the quay slab is included, the rest of the dock backdrop is not). It is baked with expansion content
active so machines carve it; barriers carry a `NavMeshModifier` (ignored by the bake) plus a carving obstacle, so
nothing is left behind when they sink. Scrap carves at runtime. Since M7 the bake includes a height mesh
(`buildHeightMesh`): without it, agents beside tall obstacles stood up to 30 cm above the floor.

## 7. System dependencies (build order)

```
Services/GameEvents/ModifiableStat
   → ItemDefinition, WorldItem → CarryStack → ItemCollector
   → ScrapDefinition → ScrapObject → ScrapManager/SpawnPoint → HarvestTool
   → Feedback services (optional at runtime; every call no-ops if a service is missing)
   → PlayerStats → PlayerController/HarvestTool/Collector wiring (PlayerCharacter)
M2 Machines depend on: WorldItem, CarryStack (unload), Services, ModifiableStat.
M3 Upgrades/Tasks depend on: ModifiableStat, GameEvents, Economy.
M4 Workers reuse: CarryStack, ItemCollector, TransferPads; Customers depend on SellDesk + Economy; Tiles depend on
   UpgradeManager + Economy; Expansion depends on UpgradeManager, HarvestManager, CameraController.
M5 Sorter = Machine + WeightedSpread + output ports; bins = Storage (levelSource); OverdriveTile depends on Machine.Speed
   + MachineVisuals; the guide depends on StationRegistry (all stations) to walk the chain.
M6 Furnace = Machine + MachineDefinition recipes (no new component); heat glow and output burst are MachineVisuals
   options; heavy scrap = ScrapDefinitions + prefabs; customer orders depend on StationRegistry (what can be made).
M7 Operator/Seller = Worker + WorkPost + PostBrain on Machine.Speed / SellDesk.ServiceSpeed; Loader = PorterBrain on a
   PorterRoute with role Loader; TruckBay depends on ItemPile, CashPile, StationRegistry, UpgradeManager; GiantScrapEvent
   depends on ScrapManager, HarvestManager, EconomyManager, CameraController; GiantScrapBar and Announcer listen to
   GameEvents.GiantScrapArrived / Defeated.
M8 SaveManager depends only on Core (SaveRegistry) and GameEvents; saveable systems depend only on Core. Stock restore
   depends on ItemPool's item catalog. The Dockyard is an Expansion plus scenery (no new component).
```

## 8. Blockout builders

`AgentScripts/` (outside `Assets/`, not compiled into the game) holds idempotent editor builders run through
`unity command run_script`, in this order:

1. `M1_Setup.Run` — layers, TMP essentials, portrait.
2. `M1_Assets.Run` — M1 materials, hit/break VFX, SFX, scrap definitions.
3. `M2_Import.Run` — imports DOTween and the 300Mind UI kit from the local Asset Store cache.
4. `M2_Build.Assets` — ground textures, GROBOLD TMP font, baked item meshes, items, M2 data, SFX/VFX.
5. `M2_Build.Prefabs` — scrap (Kenney + parts), Crusher/Storage/SellDesk (ProBuilder + Kenney), pads, labels,
   player (Kenney character + Animator + ProBuilder hard hat/chainsaw).
6. `M2_Build.Scene` — Area 1 scene, **rebuilt from scratch**: stop running it once the scene is hand-edited.
7. `M3_Build.Assets` / `Prefabs` / `Icons` / `Scene` — M3 data, Porter, guide marker, rendered card icons; edits the
   existing prefabs and scene **in place** (find-or-create by name). Prefer this incremental style from now on.
8. `M4_Build.Assets` / `Prefabs` / `Icons` / `Scene` (or `All`) — catalog groups, helper, customers, tier-2 scrap,
   Back Lot, tasks; tile, customer and helper prefabs, station label restyle; places tiles, routes, the customer line
   and the Back Lot, rebakes the NavMesh and builds the upgrade panel (removes the M3 rail). Incremental.
9. `M5_Build.Assets` / `Prefabs` / `Icons` / `Scene` (or `All`) — Kenney audio mapping, iron/copper, Sorter, bins,
   market, overdrive, workers, catalog groups, tasks; Sorter, boost pad, worker and item prefabs, round hats; Gate 2 +
   Area 2 content, routes, market line, plant card icons, NavMesh over both areas. Incremental.
10. `Polish1_Build.All` — quality pass data and UI (engine sound, hit flash, loot fountains, crusher pad spill, station
   status tags, savings fill). `M5_Build.Hats` re-rounds the hard hats.
11. `M6_Build.Assets` / `Prefabs` / `Icons` / `Scene` (or `All`) — ingots, Furnace (recipes), Ingot Rack, Smelter, heavy
   scrap, Furnace hall + Heavy Scrap Yard expansions, catalog, tasks (incl. the Gate 2 pacing fix); ingot, Furnace,
   smoke, Smelter and heavy vehicle prefabs; hall in the plant, Gate 3 + Area 3 walls and content, routes, NavMesh over
   Areas 1–3. Incremental.

12. The art pass (`Art_Characters.Build`, `Art_Scrap.Build`, `Art_Machines.All`, `Art_VFX.Build`, `Art_World.All`): see 8b.
13. `M7_Build.Assets` / `Prefabs`, then `Art_Characters.Build`, then `M7_Build.Scene` — specialists, Truck Dock, Giant
   Scrap data, catalog entries and tasks (added by id, earlier entries untouched); worker skeletons copied from the
   Market Runner, the operator console, the TruckBay station and the Giant Truck (all ArtKit, no Kenney); consoles,
   posts and hire tiles, the dock (replaces part of the plant's street fence), the Loader route, the giant's landing
   zone and sign, the HUD boss bar, NavMesh with a height mesh. Incremental. It runs after the art pass and does not
   bring Kenney content back.

14. `M8_Build.Assets` / `Prefabs` / `Scene` (or `All`) — Dockyard expansion data, the balance changes from the measured
   session (`M8_Build.Balance`, one place), tasks `t48`–`t50`; ship, quay crane and bollard prefabs; the SaveManager,
   the ItemPool item catalog, Gate 4 in the plant's east wall, the quay scenery and Dockyard content, `DropBlocker`s on
   mounds and big props, NavMesh. Incremental. Re-run `M8_Build.Scene` after `M5_Build.Scene` / `M6_Build.Scene`.

15. `UI_Build.Kit` / `Apply` (or `All`) — the UI pass. `Kit` cleans and slices the owner's painted atlases
   (`Assets/UI`) into `Art/UI/Kit`; `Apply` dresses the HUD, the upgrade panel and card, tiles, station labels and
   customer bubbles in place, adds `_UI/LoadingCanvas` and sets the app icon. See 8c. Run it last: any M-builder
   `Prefabs` / `Scene` step puts the old sprites back.

16. Revamp 1: `R1_Build.Assets` (the speed pass in `Balance()`, Raw Metal, aluminum and steel, the four-way split,
   market goods and orders), then `Art_Machines.Prefabs` (the Splitter model with its four out points),
   `R1_Build.Prefabs` (item prefabs and icons, the Splitter's ports and chute icons), `R1_Build.Scene` (four bins and
   belts, ports, tiles, the operator post, the Runner's pads, the item catalog), `Art_Machines.Scene` (belt and bin
   art) and `R1_Build.Bake` (NavMesh). Re-run the last four after `M5_Build.Scene`.

20. Revamp 3: `R3_Build.Assets` (bars, Press, Bar Storage, the build, six contracts, dock, catalog, tasks),
   `R3_Build.Prefabs` (bar prefabs, Press prefab), `Art_Machines.Prefabs` (the Press model), `Art_Machines.Icons`,
   `R3_Build.Icons`, `R3_Build.Scene` (Press plot, bar belt, Bar Storage, Loader route, HUD contract card), then
   `Art_Machines.Scene`, `Env_Build.All`, `R1_Build.Bake`, `UI_Build.Apply`. After `R2_Build.Scene`.

19. Revamp 2: `R2_Build.Assets` (ingots, four furnace definitions, three builds, catalog, market, truck, tasks, numbers),
   `R2_Build.Prefabs` (ingot prefabs, four furnace prefabs), `Art_Machines.Prefabs` (the four models),
   `Art_Machines.Icons`, `R2_Build.Icons`, `R2_Build.Scene` (battery, collector belt, rack, plots, Smelter route), then
   `Art_Machines.Scene`, `Env_Build.All`, `R1_Build.Bake`, `UI_Build.Apply`.

24. Revamp 7: `R7_Build.Assets` (the task chain's final order, numbers changed after the full-session runs) and
   `R7_Build.Scene` (the Runner's route setting). Last of the data steps. The model behind the numbers: `Docs/ECONOMY.md`.

23. Revamp 6: `R6_Build.Assets` (ship dock data, catalog, task `r6_ship`), `R6_Build.Scene` (Ship Dock, the ship's course,
   Loader drop-off, NavMesh volume over the quay), then `R1_Build.Bake`, `UI_Build.Apply`. After `R3_Build`.

22. Revamp 5: `R5_Build.Assets` (five boosts, seven offers), `R5_Build.Scene` (the four services in `_Systems`, boost
   chips, offer button and offline panel on the HUD). After `UI_Build.Kit`.

21. Revamp 4: `R4_Build.Assets` (the Excavator as its own scrap type, metal drops of the heavy wrecks), then
   `Crane_Build.Assets` / `Scene`, `Art_Machines.Scene`, `Env_Build.All`, `R1_Build.Bake`, `UI_Build.Apply`.

18. `Crane_Build.Assets` / `Scene` — both claw cranes (data, catalog, tasks `t17b_claw` and `r4_heavy_crane`; cranes with
   truss jibs, tiles, icons, the Heavy Yard belt and its spill end). Then `Art_Machines.Scene`, `Env_Build.All` and
   `R1_Build.Bake`.

17. `Env_Build.All` (then `R1_Build.Bake`) — environment and layout pass in ProBuilder: lanes, Heavy Yard bays, clutter
   islands and wall clusters, extra scrap spawn points, tile and post moves, customer walk-in paths. `Env_Build.Check`
   reports overlapping tiles and pads. See `Docs/ART_DIRECTION.md`, "Environment pass".

Assets are updated in place so GUIDs survive re-runs. Later builders overwrite data written by earlier ones (catalog,
task chain, station label prefab, customer prefabs), so re-running an earlier builder means re-running every later one.
ProBuilder meshes on stations stay editable with ProBuilder; items (pooled by the hundred) use baked mesh assets.

## 8b. Art pipeline

The visual overhaul (2026-10-02) replaced every Kenney model with custom art built by `AgentScripts/Art_*.cs` on the
Editor-only ArtKit (`Assets/_Project/Scripts/Editor/ArtKit`, asmdef `ScrapYardKing.Editor`). Details, budgets and the
audit: `Docs/ART_DIRECTION.md`. Runtime additions are presentation only:

| Component | Folder | Purpose |
|---|---|---|
| `ScrapDamageStages` | Harvest | Shows/hides damage objects (smoke, cracked glass, sparks) and sags the body as `ScrapObject.Health01` drops; resets on respawn. |
| `AmbientMotion` | World | Spin / swing / bob for idle life (crane jib, wings, flags). |
| `AmbientPath` | World | Moves background traffic or a forklift along waypoints (loop or one-way respawn), spins wheels. |
| `UpgradeVisualTiers.objects` | Progression | A different model per upgrade tier (the three cutters). |
| `MachineVisuals` glow / outputBurst / idleSpin | Factory | Furnace mouth glow, a burst per output (crush puff, sorter flash), slow idle spin that stops when jammed. |

Characters are toy rigs: one SkinnedMeshRenderer with rigid weights on 12 joints and a per-character palette
material; the generated clips (`Art/Animation/Toy`) drive them through `AC_ToyPlayer` / `AC_ToyWorker` /
`AC_ToyCustomer` with the same Speed / Cutting / Happy parameters the scripts already set. Scrap, machines and props
use palette materials (`PaletteBaker`, `ArtPalette`) so a model costs one or two draw calls; textured materials (hazard
stripes, brick, belt, concrete, wood) and per-instance recolours (bins, market) stay separate.

## 8c. UI kit

The UI art is the owner's painted set in `Assets/UI` (three atlases, a portrait loading background, the title logo,
the app icon). The atlases are sources only: they sit on a soft haze with colour fringes and are never referenced by
the game. `UI_Build.Kit` writes cleaned, sliced copies:

| Texture | From | Holds | Names |
|---|---|---|---|
| `Kit_Panels` | Atlas2 | panels, title plates, buttons (3 styles × 5 colours), squares, bars, toggles | all 50 named (`Panel_Dark`, `Bar_Cream`, `Btn_Green`, `Title_Yellow`, ...), with 9-slice borders |
| `Kit_IconsA` | Atlas1 | items, vehicles, machines, props, glyphs | used ones named (`Icon_Cash`, `Icon_Gem`, `Icon_Star`, `Icon_Lock`, `Icon_Bolt`, ...), the rest `A_r<row>_c<col>` |
| `Kit_IconsB` | Atlas3 | worker actions, logistics, awards, shop, system buttons | `Icon_Upgrade`, `Btn_Check`, `Btn_Close`, ..., the rest `B_r<row>_c<col>` |
| `Kit_BarTrack`, `Kit_BarFill`, `Kit_PillWhite` | assembled from Atlas2 | an empty bar, a white fill and a white pill | the last two are white so `Image.color` tints them |

How the kit is cut: a sprite is a connected island of pixels above an alpha threshold (235 for Atlas1, whose haze is
dense; 128 for the others), small islands join the icon next to them, everything else is cleared and each island
keeps a one pixel rim in its own colour. Panel bodies are made fully opaque (the paintings are slightly see-through).
Named sprites are found by a point inside them, so the tables in `UI_Build` survive a re-export with small shifts.

Rules for new UI:
- Use kit sprites, sliced with `pixelsPerUnitMultiplier` to set the corner size (`spriteHeight / rectHeight` for bars
  and pills so the caps fit the height).
- A progress bar is `Kit_BarTrack` (sliced) with a `Kit_BarFill` child (`Image.Type.Filled`, horizontal), inset by
  10 / 9.5 / 8.5 px at a 47 px track height; the fill colour is the Image colour. Code only sets `fillAmount`.
- Anything code tints (status tags) uses a white sprite (`Kit_PillWhite`, `Kit_BarFill`); painted colours cannot be
  tinted cleanly.
- Icons of things that exist as models (machines, workers, materials) stay the rendered `Art/Icons`, so a card or a
  customer's order shows what stands in the yard. Kit icons are for currency, locks, arrows and system buttons.
- `LoadingScreen` covers the first moments (minimum 1.8 s and 5 frames, unscaled time), fills its bar, fades out and
  switches its cover off. It never waits for input and nothing waits for it.

## 8d. UI system (U1, 2026-10-06)

The owner's UI/economy brief asks for one premium UI language across HUD, popups, shop, rewards and settings. U1 is the
part everything else stands on:

| Piece | Where | Rule |
|---|---|---|
| `UIAnim` | `UI/UIAnim.cs` | The only place UI motion is written: Show/Hide (card pop and fade), Dim, Punch, Shake, Error (shake + red pulse), RewardPop, Spin, Breathe, ProgressFill, CountTo, ButtonPress/Release, UnlockReveal, Claim, Upgrade. Unscaled time, linked to the target. Normal interactions 0.15–0.45 s. |
| `UIButtonFeel` | on every `Button` (added by `U1_Build`) | Sinks to 0.94 on touch, springs back on release, click sound, light haptic; a non-interactable button shakes "no". |
| `PopupManager` | `_UI/Canvas/PopupLayer` | One card for every reward, question and message (`PopupRequest`: style Reward / Confirm / Danger / Info, title, subtitle, body, icon, amount, primary / secondary / close). Queue, one at a time, beats entry → reveal → reward → CTA → exit, a 0.45 s input guard. The card shrinks around missing parts. Danger = red plate and a two-second hold (`HoldButton`). |
| `CurrencyOrigin` | `Economy/` | Every wallet change says where it came from (world point, screen point, nowhere). The HUD flies coins or diamonds from there. |
| Diamonds | `EconomyManager.AddPremium(amount, reason, origin)`, `TrySpendPremium(amount, reason)` | The only doors in and out; both log analytics and save. UI spends only through `DiamondSpend.Request` (confirms above `PremiumEconomyConfig.ConfirmAbove`, says "not enough" plainly). |
| `PremiumEconomyConfig` | `Data/Economy/PremiumEconomy.asset` | Diamond price of time (`SkipCost`) and of cash (`CashCost`), confirm threshold, free sources (area milestones, level-ups). No UI script holds a diamond number. |
| `DiamondRewards` | `_Systems` | Turns milestones and level-ups into reward popups. A gift is recorded before it is offered and paid on CLAIM; an unclaimed gift is saved and offered again. The diamond capsule on the HUD appears with the first gift. |
| `GameSettings` + `SettingsApplier` | `Settings/` | Device settings in PlayerPrefs (survive a progress reset): sound, volume, vibration, camera shake, pop-up numbers, quality, 60 fps, battery saver. `GameFeedback` honours shake and numbers. |
| `SettingsPanel` | `_UI/Canvas/SettingsPanel` | Settings is the pause screen (gear, top right). Grouped cards; rows with no target (no store, no support address) hide. Reset = warning card + hold. |
| `Analytics` | `Core/Analytics.cs` | One static door, provider-agnostic (`IAnalyticsSink`); event names in `AnalyticsEvents`. Counts per event for the developer window. |
| `SafeArea` | on `HUD` | The HUD fits `Screen.safeArea` in Play mode; the joystick area and dimmers stay full screen. |
| `StoreService` | `_Systems`, `Shop/` | The one door to real money (`IStoreProvider`; Editor/dev stand-in; release builds without a provider sell nothing). Ledger of transaction ids (paid once), pending purchases saved before they are shown and paid on CLAIM, owned non-consumables, restore. Also the Free tab's video diamonds (wall-clock cooldown, daily cap). |
| `ShopCatalog`, `IAPProductConfig` | `Data/Shop` | What is sold: products (store id, contents, ribbon, fallback price, reference USD for value checks), diamond boosts and cash crates (priced from `PremiumEconomyConfig`), free diamonds. Never machines or workers. |
| `ShopPanel` + `ShopCard` | `_UI/Canvas/ShopPanel` | Four tabs built from the catalog; one card view for hero / bundle / row. Opened by the capsules' "+" (shown from the first diamond) and by `DiamondSpend` when diamonds run short. |
| Sorting | nested canvases | Cash and diamond capsules (10), coin layer (11) and popup layer (20) draw over panels on the main canvas, so the balance stays readable over the shop and settings. |
| `OfferDirector` | `_Systems`, `Boosts/` | The one rewarded-video gate: `Ready(offer)` (wall-clock cooldown, daily cap, network ready), `Watch(offer, reward, failed)` (reward only after completion; analytics). Runs the rotating HUD offer (priority, then weight; never under a popup or beside another video button) and the ORDER COMPLETE card. |
| `WelcomeBack` | `HUD/WelcomeBack` | The welcome-back reward card for `IdleIncomeManager.Pending`; 2X through `OfferDirector.OfflineDouble`. |
| `TruckSkipButton` | `HUD/TruckSkip` | FINISH ORDER (video or diamonds) on a truck that has waited; BRING NOW (diamonds) while the next truck is far. Thresholds in `PremiumEconomyConfig`; calls `TruckBay.FinishOrder` / `CallNow`. |
| `MissionManager` | `_Systems`, `Progression/` | Lifetime counts from `GameEvents`; three dailies per UTC day from `MissionConfig` (measured from the count at the day's start); tiered achievements. Claims recorded before paying (`MissionManager.Pay` pays any `MissionReward`). Save key `missions`. |
| `DailyRewardManager` | `_Systems`, `Progression/` | 7-day calendar (`DailyRewardConfig`), one claim per UTC day, no reset on a missed day. Save key `daily_reward`. |
| `Badges` + `BadgeDot` | `UI/` | One count per destination (`shop`, `missions`, `daily`); `Badges.ExtrasUnlocked` (first diamond) gates the shop, missions and daily rewards. |
| `NavBar`, `MissionsPanel`, `DailyPanel` | HUD bottom, `_UI/Canvas` | The bottom bar and its two panels; same frame and motion as the shop. |
| `UpgradeGain` | `Progression/` | Turns an effect text ("200 → 240/min") into the celebration line ("+20% SPEED"). `UpgradeManager.Complete` plays the purchase celebration (LEVEL n!, gain line, station squash). |
| `EconomyLedger` | `Economy/`, `_Systems/EconomyLedger` | Listens to wallet events and the `Analytics` stream (as an `IAnalyticsSink`) and keeps cash per minute, diamonds by source and sink, videos offered/watched, purchases, offline and order cash; lifetime and the last 30 days are saved ("ledger"). Never shown to players. Cash that arrives between `rv_completed` and `rv_reward_granted` counts as video cash (only the doubled half of WELCOME BACK); FINISH ORDER pays on the dock pallet and counts under orders. |
| `EconomySimulator` | `Scripts/Editor/Economy/` | Editor code: plays Free / Light / Mid / High (and any custom) player profiles over 30 days on the real data assets, deterministic. Shape of the game (`Model`: income along the arc, area minutes, order rate) from `Docs/ECONOMY.md`. Read by `EconomySimulationTests` (guardrails) and the window. |
| `StationLabel` cover fade | `UI/StationLabel`, `Tiles/Tile` | Tiles keep a `Tile.Visible` list; each station label checks 5× a second whether its drawn parts cover a visible tile on screen and fades to 30% while they do. The status tag sizes itself to one line of text. |
| `EconomyWindow` | `Scripts/Editor/Economy/` | **Window → Scrap Yard King → Economy Window**, developer only: Live (ledger, cooldowns, guardrails: cash/min, diamonds/min, time-to-afford, contract ROI, video reward ratio, IAP value, offline/day), Prices (every upgrade with time-to-afford, contracts, diamond prices, shop, offers), Profiles (the simulation table). |
| `Haptics` | `Feedback/` | `Heavy` (Unity's buzz) for big moments only; `Light` is a hook for a native haptics plugin. |

## 9. Milestones

1. **Harvest core** — movement, camera, one car (+ tier-1 small scrap), cutting, drops, pickup, carry stack, feel. ✔
2. **Production + cash** — crusher, conveyor, storage, sell desk, cash pile, HUD, art pass (Kenney/ProBuilder/DOTween). ✔
3. **Upgrades, tasks, XP, first worker** — upgrade rail, task chain + guide, yard level, Porter. ✔
4. **Tiles, customers, automation, first expansion** — upgrade tile + panel, hire tiles, Delivery Helper, customer
   queue, Back Lot with tier-2 scrap, UI restyle. ✔
5. **Recycling Plant** — Gate 2, Sorter → iron/copper bins → Metal Market (material orders, queue 5), Metal Hauler and
   Market Runner, Active Overdrive pads, CC0 audio, round hard hats. ✔
6. **Furnace + Heavy Scrap Yard** — Furnace hall in the plant (iron/copper → ingots, Ingot Rack, Smelter, boost pad),
   ingots at the Metal Market, Gate 3 + Area 3 with tractors, trucks and garbage trucks (cut power 30–45, rare metal
   drops). ✔
7. **Specialists + Giant Scrap** — Operators at machine consoles, Sellers at the customer lines, Truck Dock with a Loader,
   the Giant Truck event (drop-in, boss bar, fight camera, final destruction sequence, cash bonus). ✔
8. **Dockyard reveal, save/load, balance, polish** — Gate 4 and the quay with ship and cranes as the session's last
   beat, versioned JSON saves through `ISaveable`, a balance pass measured with full-session guide-bot runs, drop and
   NavMesh fixes found by those runs. ✔

### Revamp (after the eight milestones)

The owner's revamp brief (2026-10-06) redesigns the chain to scrap → Crusher → Raw Metal → Metal Splitter → four
metals → four Furnaces → Press → bars → truck contracts, and asks for a much faster game. It is built in steps, each
play-tested and reported like a milestone. Plan and acceptance criteria: `Docs/QUALITY_PLAN.md`.

R6. **The port works** — the cargo ship takes bars with its own bigger orders, sails, pays and returns. ✔ (partly: no
    new quay activity, no ship lights or smoke, no environment pass)

R5. **Boosts, offers, offline income** — five timed boosts, one optional rewarded-video offer on the HUD with
    cooldowns, double pay after a truck order, a cash gift for time away. ✔ (stand-in ads; no premium spending)

R4. **Heavy Yard logistics** — a second claw crane and a belt through Gate 3 that send heavy scrap to the pit, truss
    jibs that grow with the level, the Excavator as the yard's toughest wreck, a metal drop per heavy type. ✔ (partly:
    no new wreck models or boss types)

R3. **Press, bars, contracts** — Industrial Press (four recipes), four bars, Bar Storage, truck orders with a HUD card,
    the Loader on both hops. ✔

R2. **Furnace battery** — four furnaces (iron, copper, aluminum, steel), each its own model, sound pitch and speed,
    bought one after the other; four ingots; one collector belt and rack; one Smelter, boost pad and operator for all. ✔

R1. **Fast start, Raw Metal, Metal Splitter** — speed pass on cutting, crushing, selling, carriers, early hires and
    gates; the Crusher's product renamed Raw Metal; the Sorter rebuilt as a four-way splitter with aluminum and steel,
    four belts and four bins. ✔
