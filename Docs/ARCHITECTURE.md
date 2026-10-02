# Scrap Yard King — Technical Architecture

Source of truth: *Scrap Yard King — Game Design + Top-Down Map Blueprint* (PDF, 10 pages).
Engine: Unity 6 (developed on 6000.4.8f1), URP (Mobile renderer), Input System (new), AI Navigation, ugui/TMP, DOTween.
State: Milestones 1–5 done (Old Scrap Yard, Back Lot, Recycling Plant). Next: Milestone 6.

## 1. Blueprint digest (what drives code decisions)

| Blueprint rule | Architectural consequence |
|---|---|
| Cut → collect → process → sell → upgrade → hire → expand | Every stage is a separate system talking through items + events, not direct calls. |
| Player stays central; workers take over routine later | Carry / collect / pad components are **role-agnostic**: player and workers share `CarryStack`, `ItemCollector` and `TransferPad`. |
| One bottleneck at a time; Active Overdrive 2x for 10 s | Throughput numbers are `ModifiableStat`s; upgrades and overdrive are just modifiers. Full outputs block upstream visibly. |
| Every gate: level/task + cost + visible new content | `Expansion` (scene) + `ExpansionDefinition` (cost) + catalog unlock level; opening it pans the camera and reveals content. Never a menu-only unlock. |
| Tiers gate scrap; drop tables by size (4–10 / 10–25 / 25–60 / 120+) | `ScrapDefinition` carries size class, health, min cut power, drop range, rare chance, XP. |
| Crusher → Sorter → Furnace → Press; Sorter splits mixed metal | A machine type is a `MachineDefinition` asset; splitting machines use weighted `outputMix` + per-product output ports. |
| Customers enter from the south road and request material; queue 3 (yard) / 5 (plant) | `CustomerQueue` per sell desk; orders come from `CustomerConfig`; line length from the desk level. |
| Crusher Lv.1 10 in / 8 s, Storage 40, Sell 1 / 8 s, Porter 500, Expansion 1,500 | Values live in data assets only; MonoBehaviours hold no balance numbers. |
| Always an obvious next action | `GuideDirector` turns the current task + world state into one marker. |
| Hit sparks + hit-stop, break burst + camera bump + popup; overdrive gauge | Feedback services behind the static `GameFeedback` facade, so gameplay code stays one-liners. |
| Max 3 floating prompts | `FloatingTextManager` caps concurrent popups. |

## 2. World layout (canonical, 120 × 90 units, 5 m grid)

Origin = south-west corner of the world. +X = east, +Z = north. Ground plane y = 0.

| Zone | Rect (x, z, w, d) | Status |
|---|---|---|
| South customer road | 0, 0, 120, 10 | Customer lanes: arrivals z 2.6, departures z 1.2. |
| A1 Old Scrap Yard | 0, 10, 40, 28 | Built. Back Lot expansion in its north-east corner. Gate 2 on the east edge (x 40, z 24.5). |
| A2 Recycling Plant | 40, 10, 36, 32 | Built (M5). Opens through Gate 2. |
| A3 Heavy Scrap Yard | 0, 38, 40, 36 | Built (M6). Opens through Gate 3 in the Old Yard's north wall (x 12, z 38). |
| A4 Electronics | 76, 10, 36, 34 | Future. E-Sorter, dismantling line. |
| A5 Dockyard Port | 76, 44, 36, 42 | Future. Ship, cranes, Press, truck loading. |
| A6 Mega City | 112+, east | Future gate + reveal only. East = truck road. |

As built (positions are world x, z):

| Object | Position | Notes |
|---|---|---|
| Player start | (12, 17.5) | Scrap spawn field x 2–18, z 24–36. |
| Crusher / input pad | (24, 27) / (20.9, 27) | Belt south to Storage. Boost pad (26.9, 26.6). |
| Storage (yard) / withdraw pad | (24, 19.2) / (20.9, 19.2) | |
| Sell Desk / stock pad / cash pad | (32, 12.4) / (32, 14.4) / (35.7, 14.4) | Line runs south from (32, 10.9); customers come from and leave to the west. |
| Upgrade tile (yard) | (27.6, 23) | |
| Hire tiles | Porter (16.4, 20.6), Helper (27.6, 17.6) | |
| Back Lot tile / area | (29.5, 28.5) / x 19–39.7, z 30.3–38 | Fenced along z 30.3 and x 19 until bought. |
| Gate 2 tile | (37.2, 24.5) | Gate bars sink when bought. |
| Sorter / input pad | (50, 27.4) / (46.6, 27.5) | Two belts south. Boost pad (54.4, 27.6). |
| Iron bin / copper bin | (48.3, 19.6) / (51.7, 19.6) | Withdraw pads 3.1 m south of each bin. |
| Metal Market / stock pad / cash pad | (60, 12.4) / (60, 14.4) / (63.7, 14.4) | Line runs south from (60, 10.9); customers come from and leave to the east (x 88). |
| Plant tiles | Upgrades (56.4, 22.6), Hauler (44.4, 21.6), Runner (56.4, 16.4) | |
| Furnace hall tile / area | (59, 30.6) / x 61.1–75.7, z 19.8–41.8 | Plant's north-east corner, fenced (west + south) until bought. |
| Furnace / input pad | (67, 31.4) / (63.25, 31.2) | Belt south to the Ingot Rack. Boost pad (71.4, 28.6). Smelter tile (63.4, 26.8). |
| Ingot Rack / withdraw pad | (67, 22.6) / (63.9, 22.6) | 9 m from the market stock pad. |
| Gate 3 tile / gate | (12, 35.2) / (12, 38) | Opening x 9.4–14.6 in the north wall, straight out of the scrap field. |
| Heavy Scrap Yard | x 0–40, z 38–74 | 8 spawn points (tractors, trucks, garbage trucks), crane + containers along the north wall. |

## 3. Runtime systems

```
Core ─────────── Services (scene registry), GameEvents (global events), ModifiableStat, GameManager, GameConfig, IUpgradeable
 │
 ├─ Player ───── PlayerCharacter (wiring) · PlayerStats · PlayerController · PlayerInputReader · PlayerVisuals
 ├─ Harvest ──── ScrapManager (targets, spawn pool) · ScrapSpawnPoint · ScrapObject · ScrapPart · HarvestTool
 │               HarvestManager (drops, loose items, drop bounds + blocked areas) · WorldHealthBar · DropMath
 ├─ Items ────── ItemDefinition · WorldItem (flying/ground/moving/held) · ItemPool · CarryStack · ItemCollector
 │               IItemReceiver / IItemSource · ItemPile (hoppers, bins, counters) · TransferPad (stand-on pads)
 ├─ Factory ──── MachineDefinition (input/output, output mix, recipes) + Machine (output ports, one input per cycle in
 │               turn) + MachineVisuals (overdrive look, heat glow, output burst) · WeightedSpread
 │               Conveyor · StorageDefinition + Storage (station id, shared level) · SellDeskDefinition + SellDesk
 │               StationRegistry · LevelVisuals · OverdriveConfig
 ├─ Economy ──── EconomyConfig · EconomyManager (cash, premium) · CashPile · CashCollector · CurrencyFormat
 ├─ Feedback ─── GameFeedback facade · AudioManager (Kenney clips, ProceduralSfx fallback) · VFXManager · HitStopController
 │               FloatingTextManager · SfxDefinition · FeedbackConfig
 ├─ Camera ───── CameraController (follow, look-ahead, shake, punch, aspect-safe framing, focus shift, Focus pans)
 ├─ Progression  ProgressionManager (XP/level) · UpgradeManager + UpgradeCatalog · PlayerUpgrades + UpgradeVisualTiers
 │               TaskManager (main chain + side tasks) · GuideDirector + GuideAnchor + GuideMarker
 ├─ Workers ──── WorkerManager (hire = IUpgradeable) · Worker (NavMeshAgent body) · WorkerSite/PorterRoute · PorterBrain
 │               (PorterRoute: loose-item zones = Scrap Porter; withdraw pads = Delivery Helper, Metal Hauler, Market Runner,
 │               Smelter)
 ├─ Tiles ────── Tile (stand-on base) · PurchaseTile (pay-by-standing: hires, expansions) · UpgradeTile (opens the panel)
 │               OverdriveTile (boost pad: charge → Speed x2 → cooldown)
 ├─ Customers ── CustomerConfig · CustomerQueue (line, material orders, serve rate from the desk level) · Customer · CustomerBubble
 ├─ World ────── ExpansionDefinition + Expansion (IUpgradeable: barriers sink, content pops in, camera pan, drop area grows)
 ├─ UI ───────── VirtualJoystick · CarryStackIndicator · StationLabel · HudController · CurrencyWidget · UIFlyer
 │               LevelBadge · TaskBanner · UpgradePanel + UpgradeCard + GridColumnsFit · Announcer · GuidePointer · Billboard
 │
 │  (later milestones)
 ├─ Factory+ ─── Press (M8) = a new MachineDefinition + boost pad
 ├─ Economy+ ─── IdleIncomeManager
 ├─ Progression+ StageManager · daily tasks (need save/load)
 ├─ AI+ ──────── Operator (Machine.Speed) / Loader / Seller brains (M7) · TruckRoute
 └─ Persistence  SaveManager (versioned JSON of stats, levels, cash, unlocked gates, task progress, tile payments)
```

Rules:
- **Services** register in `Awake` (managers run at execution order -500/-1000); consumers resolve in `Start` or lazily.
- **GameEvents** are the only coupling between gameplay and progression (tasks/XP listen, never get called). Events:
  ScrapBroken, ItemsCollected, ItemsProcessed, ItemsDelivered, ItemsSold, CashEarned, UpgradePurchased, WorkerHired,
  CustomerServed, ExpansionOpened, MachineOverdrive, LevelUp, TaskCompleted.
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
- **Multi-input machines are data.** A `MachineDefinition` with `recipes` (input → product pairs) takes every listed input;
  the Furnace turns iron into iron ingots and copper into copper ingots. The hopper holds a mix; each cycle runs one
  input, taking turns so neither material starves. `Takes`, `OutputFor` and `ProducesAny` answer the guide's and the
  customers' questions for every machine type.
- **Splitting and boosting are data.** A Sorter is a `MachineDefinition` whose `outputMix` weights are handed out by a
  smooth weighted round-robin (`WeightedSpread`, 7:3 gives an even I I C I I C … pattern), each product leaving
  through its own `OutputPort` (one belt per bin). Active Overdrive is a `Multiply` modifier on `Machine.Speed` from a
  boost pad; its numbers are an `OverdriveConfig`.
- **Locked areas are physical too.** An `Expansion` keeps its barriers (NavMesh-carving, ignored by the bake) and
  registers its footprint as a `HarvestManager` blocked area so drops bounce off it. Opening it pans the camera, sinks
  the barriers, swaps the ground material and pops the content in piece by piece with dust and sound.
- **Always an obvious next action.** `GuideDirector` reads the current main task plus the player's stack and station
  stock, then points one world marker. It walks the production chain: to feed a machine it looks for loose input, then a
  storage holding it, then the machine that makes it, then scrap that drops it; to sell it picks the desk worth stocking
  and the storage holding what that desk (or the task) wants. Scrap that is too tough for the chainsaw turns into
  "upgrade the chainsaw"; affordable panel upgrades point at the upgrade tile and pulse their card.
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
- **DOTween** drives presentation only (pops, punches, jumps, counters, UI fly-ins). Item flight into stacks and
  hoppers stays in `WorldItem.MoveTo` so it can follow moving parents.

## 4. Production chain and economy (as built)

```
 Scrap objects ──cut──► Scrap pieces ──► Crusher ──belt──► Yard Storage ─┬─► Sell Desk (mixed metal $6, queue 3)
 (A1 field, Back Lot)    (player, Porter)  boost pad                     │
                                                                          └─► Sorter ─┬─belt─► Iron bin ───┐
                                                                (player, Hauler)  boost│                    ├─► Metal Market
                                                                                        └─belt─► Copper bin ─┘   (queue 5)
                                                                                                 (player, Runner)
                                                       Iron / copper bins ──(player, Smelter)──► Furnace ──belt──► Ingot Rack
                                                                              boost pad        (one machine,     (player, Runner)
                                                                                                two recipes)  ──► Metal Market
 Heavy vehicles (A3, cut power 30-45) ──► scrap pieces (25-60) + rare iron/copper chunks ──► same Crusher / Furnace
```

| Material | Value | Source |
|---|---|---|
| Scrap | 0 | Cutting scrap objects. |
| Mixed metal | $6 | Crusher (1 scrap → 1 bale). Sold at the Sell Desk. |
| Iron | $12 | Sorter, 70% of bales. Sold at the Metal Market. |
| Copper | $26 | Sorter, 30% of bales. Sold at the Metal Market. |
| Iron ingot | $30 | Furnace (1 iron → 1 ingot, 1.8 s at Lv.1). Sold at the Metal Market. |
| Copper ingot | $64 | Furnace (1 copper → 1 ingot). Sold at the Metal Market. |

| Worker | Route | Hire (unlock) |
|---|---|---|
| Scrap Porter | Loose scrap in the A1 field and Back Lot → crusher pad | $500 / $1,500 (Lv 3) |
| Delivery Helper | Yard storage pad → Sell Desk stock pad | $800 / $2,000 (Lv 4) |
| Metal Hauler | Yard storage pad → Sorter input pad | $1,200 / $2,600 (Lv 6) |
| Market Runner | Fullest of the bin and ingot rack pads → Metal Market stock pad | $1,500 / $3,000 (Lv 7) |
| Smelter | Fuller bin pad → Furnace input pad | $3,000 / $6,000 (Lv 8) |

Gates: Back Lot $1,500 (Lv 5, tier-2 scrap: fridges need cut power 20; washers, stoves and go-karts 15), Recycling
Plant $3,000 (Lv 6), Furnace hall $5,000 (Lv 8), Heavy Scrap Yard $10,000 (Lv 9: tractors need cut power 30, trucks 35,
garbage trucks 45; 25-60 pieces each plus a rare iron or copper drop). Active Overdrive: stand 1.5 s on a boost pad →
2x for 10 s, 6 s cooldown.

Customers order only what the yard can make right now (`CustomerQueue.Obtainable`: a registered machine produces it, or a
bin holds it), so ingots appear in market orders once the Furnace exists. A customer waiting at an empty counter sets
`SellDesk.WaitingFor`, and the guide brings that item first, even during a "sell copper" task, because they block the line.

Task chain (`TaskChain_Area1`): 40 main tasks, from "Cut the car" to `t37_crush500` ("Crush 500 scrap", the blueprint's
completion beat). "Boost the crusher" follows "Crush 30"; "Sell desk to Lv.3" and "Crusher to Lv.3" lead into Gate 2
(the guide bot showed the Lv.2 desk capping income for 17 minutes); the plant tasks start at `t20_plant`, the M6 tasks at
`t28_furnace` (Furnace → smelt → sell iron ingots → boost → Smelter → Lv 9 → Heavy Scrap Yard → cut trucks → sell copper
ingots → crush 500). Six side tasks rotate from main task 8 on.

## 5. Data assets

| Asset | Milestone | Holds |
|---|---|---|
| `GameConfig` | 1 | Root config refs (feedback; later economy/stages). |
| `PlayerConfig` | 1 | Move speed/accel/turn, cut power/rate/range, carry capacity, pickup radius. |
| `ItemDefinition` | 1 | Id, name, colour, icon, world prefab, stack height, base value. In use: Scrap, MixedMetal, Iron, Copper, IronIngot, CopperIngot. |
| `ScrapDefinition` | 1 | Tier, size class, prefab (+ variants), health, min cut power, drop item/range, part-drop share, rare drop, XP, respawn, feedback overrides. |
| `FeedbackConfig` | 1–5 | Hit-stop, shakes, punch, hit flash, pickup fly/pitch ramp, stack landing squash, default VFX/SFX. |
| `SfxDefinition` | 1–5 | Clips (Kenney CC0), volume, pitch range, rate limit, procedural fallback. |
| `MachineDefinition` | 2–6 | Input/output items, optional weighted output mix (Sorter) or recipes (input → product pairs, Furnace), per-level input capacity, cycle time, inputs/outputs per cycle, upgrade cost. In use: Crusher, Sorter, Furnace. |
| `StorageDefinition` | 2–5 | Per-level capacity, withdraw speed, upgrade cost (blueprint: 40 units). In use: Storage_Yard, Storage_Bins (shared by both bins), Storage_IngotRack. |
| `SellDeskDefinition` | 2–5 | Items it sells, per-level sale interval (blueprint: 1 / 8 s), units per customer, price multiplier, counter size, queue size. In use: SellDesk_Yard, SellDesk_Market. |
| `EconomyConfig` | 2 | Starting cash/premium, cash per physical bundle. |
| `PlayerStatUpgradeDefinition` | 3 | Player stat, modifier type, cumulative modifier per level, costs, card text. In use: Chainsaw, Backpack, Boots. |
| `UpgradeCatalog` | 3–5 | Every purchasable id: unlock yard level, shown in the panel or not, panel group. |
| `ProgressionConfig` | 3–5 | Level curve (base, growth), XP per sale + per $ sold, per upgrade/hire, cash per level-up. |
| `TaskDefinition` / `TaskChain` | 3–5 | Type, target id, amount, rewards, category; ordered main chain + side pool. |
| `WorkerDefinition` | 3–5 | Role, site id, prefab, tagline, speed/carry/efficiency/pickup radius, loose-item collecting, hire cost per headcount. |
| `CustomerConfig` | 4–5 | Customer prefabs, walk speed, arrival interval, hand-over interval, order item / weighted order options, prefer-in-stock chance, SFX. One per desk. |
| `ExpansionDefinition` | 4–5 | Id, name, icon, cost, tile teaser, open SFX. Unlock level lives in the catalog; scene content on the `Expansion`. |
| `OverdriveConfig` | 5 | Speed multiplier (2x), duration (10 s), charge time, cooldown, SFX. |
| `StageDefinition` | later | Unlocked scrap tiers, spawn tables, milestone task. |

## 6. Scene hierarchy (`Area1_OldScrapYard.unity`, holds Areas 1 and 2)

```
Area1_OldScrapYard
├─ _Systems        GameManager, EconomyManager, ItemPool, ScrapManager, HarvestManager, AudioManager, VFXManager,
│                  HitStopController, FloatingTextManager, ProgressionManager, UpgradeManager, TaskManager,
│                  GuideDirector, WorkerManager
├─ _Environment    Ground slabs (Area2_Locked / Area3_Locked swap to dirt when their gate opens), Road, Sidewalk,
│                  Fences (corrugated walls around A1 + A2 + A3, rail frontage, desk/market side blockers),
│                  Gate_A1_A2 (bars + collider = plant barriers, LockedSign), Gate_A1_A3, JunkPiles, Backdrop, NavMesh
├─ _Gameplay
│  ├─ PlayerStart, Player (prefab), LooseItems, ItemPool
│  ├─ ScrapSpawnZone  SP_* (car wreck variants, barrels, tire stacks)
│  ├─ Stations     Crusher (24,27) → Conveyor → Storage (24,19.2) · SellDesk (32,12.4) + CashPile
│  ├─ Workers      WorkerSpawn, Sites/PorterRoute (scrap zones + Back Lot zone → crusher pad), Sites/DeliveryRoute,
│  │               Sites/HaulerRoute (yard storage → sorter), Sites/RunnerRoute (bins → market)
│  ├─ Tiles        Tile_Upgrades, Tile_Porter, Tile_Helper, Tile_BackLot, Tile_RecyclingPlant, Tile_BoostCrusher
│  ├─ Customers    CustomerQueue for the Sell Desk: Slots (7, front first), Entry path, Exit path, Crowd (pooled)
│  ├─ BackLot      Expansion: Barriers (fences), LockedOnly (sign), Content (tier-2 spawn points), Focus
│  ├─ RecyclingPlant  Expansion: Content (floors, Sorter, Conveyor_Iron/Copper, Bin_Iron, Bin_Copper, MetalMarket,
│  │               Customers_Market, Tile_UpgradesPlant, Tile_Hauler, Tile_Runner, Tile_BoostSorter,
│  │               FurnacePlot (the hall's fence + LockedOnly sign), Tile_FurnaceHall), Focus
│  ├─ FurnaceHall  Expansion: Content (floor, Furnace, Conveyor_Ingots, IngotRack, Tile_BoostFurnace, Tile_Smelter), Focus.
│  │               Its barriers and sign live in the plant content, so they appear only once Gate 2 is open.
│  ├─ HeavyYard    Expansion: LockedOnly (gate sign), Content (Decor: crane, containers, tank, heaps; SP_* heavy spawn
│  │               points), Focus. Barriers = Gate_A1_A3 bars + collider; Area3_Locked ground swaps to dirt.
│  ├─ GuideMarker
│  └─ Slots        Slot_CustomerQueue, Slot_Gate_A1_A2 (legacy anchors)
├─ _Camera         Main Camera (CameraController)
├─ _Lighting       Directional Light, Global Volume (VP_Yard)
└─ _UI             Canvas (JoystickArea, HUD: LevelBadge, cash/gem pills, TaskBanner, GuidePointer, UpgradePanel,
                   Announcer, FlyLayer), EventSystem
```

The NavMeshSurface covers Areas 1–3 (centre (38, 1, 42), size 80 × 6 × 66). It is baked with expansion content
active so machines carve it; barriers carry a `NavMeshModifier` (ignored by the bake) plus a carving obstacle, so
nothing is left behind when they sink. Scrap carves at runtime.

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

Assets are updated in place so GUIDs survive re-runs. Later builders overwrite data written by earlier ones (catalog,
task chain, station label prefab, customer prefabs), so re-running an earlier builder means re-running every later one.
ProBuilder meshes on stations stay editable with ProBuilder; items (pooled by the hundred) use baked mesh assets.

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
7. Worker specialisation (Operator, Loader, Seller), Giant Scrap event.
8. Dockyard reveal, save/load, balance, polish.
