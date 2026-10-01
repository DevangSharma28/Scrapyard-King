# Scrap Yard King — Technical Architecture

Source of truth: *Scrap Yard King — Game Design + Top-Down Map Blueprint* (PDF, 10 pages).
Engine: Unity 6000.4.8f1, URP (Mobile renderer), Input System (new), AI Navigation, ugui/TMP.

## 1. Blueprint digest (what drives code decisions)

| Blueprint rule | Architectural consequence |
|---|---|
| Cut → collect → process → sell → upgrade → hire → expand | Every stage is a separate system talking through items + events, not direct calls. |
| Player stays central; workers take over routine later | Harvest / carry / collect components are **role-agnostic**: player and Porter share `CarryStack` + `ItemCollector`. |
| One bottleneck at a time; Active Overdrive 2x for 10 s | All throughput numbers are `ModifiableStat`s so upgrades and overdrive are just modifiers. |
| Every gate: task + cost + visible new content | `ExpansionDefinition` owns requirement, cost and the scene objects it reveals. Never a menu-only unlock. |
| Tiers gate scrap; drop tables by size (4–10 / 10–25 / 25–60 / 120+) | `ScrapDefinition` carries size class, health, min cut power, drop range, rare chance, XP. |
| Crusher Lv.1 10 in / 8 s, Storage 40, Sell 1 / 8 s, Porter 500, Expansion 1,500 | Values live in data assets only; MonoBehaviours hold no balance numbers. |
| Hit sparks + hit-stop, break burst + camera bump + popup | Dedicated feedback services behind a static `Feedback` facade so gameplay code stays one-liners. |
| Max 3 floating prompts | `FloatingTextManager` caps concurrent popups. |

## 2. World layout (canonical, 120 × 90 units, 5 m grid)

Origin = south-west corner of the world. +X = east, +Z = north. Ground plane y = 0.

| Zone | Rect (x, z, w, d) | Notes |
|---|---|---|
| South customer road | 0, 0, 120, 10 | Customers enter/leave here. Main entrance ~x 60. |
| A1 Old Scrap Yard | 0, 10, 40, 28 | Starter. Gate 2 on east edge (x 40, z ~24). |
| A2 Recycling Plant | 40, 10, 36, 32 | Sorter, material storage, worker area. |
| A3 Heavy Scrap Yard | 0, 38, 40, 36 | North of A1. Industrial Crusher, Furnace. |
| A4 Electronics | 76, 10, 36, 34 | E-Sorter, dismantling line. |
| A5 Dockyard Port | 76, 44, 36, 42 | North water edge. Ship, cranes, Press, truck loading. |
| A6 Mega City | 112+, east | Gate + reveal only. East = truck road. |

Area 1 interior (from blueprint p.4): scrap spawn zone x 2–18 / z 24–36, player start (12, 17.5),
Crusher (24, 26.5), Storage (24, 19.5), Sell Desk (33, 15.5) touching the road, customer queue on the road edge.

As built (M4): Crusher (24, 27), Storage (24, 19.2), Sell Desk (32, 12.4) with the customer line running south from
(32, 10.9) onto the road. Tiles: Upgrades (27.6, 23), Scrap Porter (16.4, 20.6), Delivery Helper (27.6, 17.6),
Back Lot (29.5, 28.5). Back Lot expansion: x 19–39.7, z 30.3–38 (fenced along z 30.3 and x 19 until bought).

As built (M5): Gate 2 at (40, 24.5) with its tile at (37.2, 24.5). Recycling Plant (x 40–76, z 10–42): Sorter (50, 27.4)
with its input pad on the west, iron bin (48.3, 19.6) and copper bin (51.7, 19.6) fed by one belt each, Metal Market
(60, 12.4) with its line running south from (60, 10.9); market customers come from and leave to the east (x 88), Old
Yard customers from and to the west. Boost pads: crusher (26.9, 26.6), sorter (54.4, 27.6).

## 3. Runtime systems

```
Core ─────────── Services (scene registry), GameEvents (global events), ModifiableStat, GameManager, GameConfig, IUpgradeable
 │
 ├─ Player ───── PlayerCharacter (wiring) · PlayerStats · PlayerController · PlayerInputReader · PlayerVisuals
 ├─ Harvest ──── ScrapManager (targets, spawn pool) · ScrapSpawnPoint · ScrapObject · ScrapPart · HarvestTool
 │               HarvestManager (drops, loose items, drop bounds + blocked areas) · WorldHealthBar · DropMath
 ├─ Items ────── ItemDefinition · WorldItem (flying/ground/moving/held) · ItemPool · CarryStack · ItemCollector
 │               IItemReceiver / IItemSource · ItemPile (hoppers, bins, counters) · TransferPad (stand-on pads)
 ├─ Factory ──── MachineDefinition + Machine + MachineVisuals · Conveyor · StorageDefinition + Storage
 │               SellDeskDefinition + SellDesk (sells list; TryHandOver + CompleteSale for customers)
 │               WeightedSpread (sorter split) · Machine output ports · OverdriveConfig
 ├─ Economy ──── EconomyConfig · EconomyManager (cash, premium) · CashPile · CashCollector · CurrencyFormat
 ├─ Feedback ─── GameFeedback facade · AudioManager (+ProceduralSfx) · VFXManager · HitStopController · FloatingTextManager
 ├─ Camera ───── CameraController (follow, look-ahead, shake, punch, aspect-safe framing, focus shift, Focus pans)
 ├─ Progression  ProgressionManager (XP/level) · UpgradeManager + UpgradeCatalog · PlayerUpgrades + UpgradeVisualTiers
 │               TaskManager (main chain + side tasks) · GuideDirector + GuideAnchor + GuideMarker
 ├─ Workers ──── WorkerManager (hire = IUpgradeable) · Worker (NavMeshAgent body) · WorkerSite/PorterRoute · PorterBrain
 │               (PorterRoute: loose-item zones = Scrap Porter, or a withdraw pad = Delivery Helper; site ids pick the route)
 ├─ Tiles ────── Tile (stand-on base) · PurchaseTile (pay-by-standing: hires, expansions) · UpgradeTile (opens the panel)
 │               OverdriveTile (boost pad: charge → Speed x2 → cooldown)
 ├─ Customers ── CustomerConfig · CustomerQueue (line, orders, serve rate from the desk level) · Customer · CustomerBubble
 ├─ World ────── ExpansionDefinition + Expansion (IUpgradeable: barriers sink, content pops in, camera pan, drop area grows)
 ├─ UI ───────── VirtualJoystick · CarryStackIndicator · StationLabel · HudController · CurrencyWidget · UIFlyer
 │               LevelBadge · TaskBanner · UpgradePanel + UpgradeCard + GridColumnsFit · Announcer · GuidePointer · Billboard
 │
 │  (later milestones)
 ├─ Factory+ ─── Overdrive (timed Speed modifier) · Sorter/Furnace/Press = new MachineDefinitions
 ├─ Economy+ ─── IdleIncomeManager
 ├─ Progression+ StageManager · daily tasks (need save/load)
 ├─ AI+ ──────── Operator/Loader/Seller brains · TruckRoute
 └─ Persistence  SaveManager (versioned JSON of stats, levels, cash, unlocked gates, task progress)
```

Rules:
- **Services** register in `Awake` (managers run at execution order -500/-1000); consumers resolve in `Start` or lazily.
- **GameEvents** are the only coupling between gameplay and progression (tasks/XP listen, never get called).
- **Items are physical.** Every resource is a `WorldItem` that flies, lands, gets carried and gets fed into machines,
  so the player can visually follow material through the chain (blueprint p.7).
- **Stats are modifiable.** Base from data asset, upgrades add `StatModifier`s keyed by source; overdrive is a timed modifier.
- **No hard-coded balance.** Tuning in ScriptableObjects; MonoBehaviours hold presentation/feel values only.
- **Items move through receivers.** Anything that takes items implements `IItemReceiver`, anything that gives them
  `IItemSource`. Pads, machines, conveyors, storage, counters and carry stacks all plug together through these two,
  so a Porter (M3) or Loader (M4) reuses the exact same pads and stacks as the player.
- **Bottlenecks are physical.** A full storage stops the conveyor, which blocks the crusher (red light), which fills the
  hopper, which stops the player's deposit pad. The slowest station is visible without any UI.
- **One purchase path.** Stations, player stats, worker hires and expansions all implement `IUpgradeable` and register
  with `UpgradeManager`; cards, tiles, tasks and the guide only talk to that. A hire is an upgrade whose level is the
  headcount; an expansion is a one-level upgrade.
- **Buying happens in the world.** Station and player upgrades are bought in the `UpgradePanel`, which opens while the
  player stands on the upgrade tile (catalog entries flagged `inPanel`, grouped YOU / YARD). Hires and expansions have
  their own `PurchaseTile`: standing on it drains cash into the tile (bills fly, a radial fill grows; partial payments
  are kept) and `UpgradeManager.CompletePrepaid` applies the level. One purchase per visit.
- **Customers are physical.** `CustomerQueue` replaces the desk's walk-in timer: buyers walk in from the road, the front
  one gets its order handed over unit by unit (`SellDesk.TryHandOver`), pays once (`CompleteSale`) and walks off. The
  desk level sets serve rate, order size and line length; an empty counter stalls the line visibly.
- **Splitting and boosting are data.** A Sorter is a `MachineDefinition` whose `outputMix` weights are handed out by a
  smooth weighted round-robin (`WeightedSpread`), each product leaving through its own `OutputPort` (one belt per bin).
  Active Overdrive is a `Multiply` modifier on `Machine.Speed` from a boost pad; its numbers are an `OverdriveConfig`.
- **Locked areas are physical too.** An `Expansion` keeps its fence (NavMesh-carving, ignored by the bake) and registers
  its footprint as a `HarvestManager` blocked area so drops bounce off it. Opening it is a camera pan plus a reveal.
- **Always an obvious next action.** `GuideDirector` reads the current main task plus the player's stack and station
  stock, then points one world marker. Pads carry `GuideAnchor`s named `<stationId>/in|out|cash`; tiles carry
  `tile/upgrades` and `tile/<upgradeId>`. Affordable panel upgrades point at the upgrade tile and pulse their card;
  scrap that is too tough for the chainsaw turns into "upgrade the chainsaw".
- **Every upgrade is visible.** `LevelVisuals` turns on extra station geometry per level; `UpgradeVisualTiers` recolours
  the chainsaw. Numbers alone never count as an upgrade.
- **DOTween** drives presentation only (pops, punches, jumps, counters, UI fly-ins). Item flight into stacks and
  hoppers stays in `WorldItem.MoveTo` so it can follow moving parents.

## 4. Data assets

| Asset | Milestone | Holds |
|---|---|---|
| `GameConfig` | 1 | Root config refs (feedback; later economy/stages). |
| `PlayerConfig` | 1 | Move speed/accel/turn, cut power/rate/range, carry capacity, pickup radius. |
| `ItemDefinition` | 1 | Id, name, colour, icon, world prefab, stack height, base value. |
| `ScrapDefinition` | 1 | Tier, size class, prefab, health, min cut power, drop item/range, part-drop share, rare drop, XP, respawn, feedback overrides. |
| `FeedbackConfig` | 1 | Hit-stop, shakes, punch, pickup fly/pitch ramp, default VFX/SFX. |
| `SfxDefinition` | 1 | Clips, volume, pitch range, rate limit, procedural fallback. |
| `MachineDefinition` | 2–5 | Input/output items, optional weighted output mix (Sorter), per-level input capacity, cycle time, inputs/outputs per cycle, upgrade cost. |
| `StorageDefinition` | 2 | Per-level capacity, withdraw speed, upgrade cost (blueprint: 40 units). |
| `SellDeskDefinition` | 2–5 | Items it sells, per-level sale interval (blueprint: 1 / 8 s), units per customer, price multiplier, counter size, queue size. |
| `EconomyConfig` | 2 | Starting cash/premium, cash per physical bundle. |
| `PlayerStatUpgradeDefinition` | 3 | Player stat, modifier type, cumulative modifier per level, costs, card text. |
| `UpgradeCatalog` | 3–4 | Every purchasable id: unlock yard level, shown in the panel or not, panel group (YOU / YARD). |
| `ProgressionConfig` | 3 | Level curve (base, growth), XP per sale/upgrade/hire, cash per level-up. |
| `TaskDefinition` / `TaskChain` | 3 | Type, target id, amount, rewards, category; ordered main chain + side pool. |
| `WorkerDefinition` | 3–4 | Role, site id, prefab, tagline, speed/carry/efficiency/pickup radius, loose-item collecting, hire cost per headcount. |
| `CustomerConfig` | 4–5 | Customer prefabs, walk speed, arrival interval, hand-over interval, order item / weighted order options, prefer-in-stock chance, SFX. |
| `OverdriveConfig` | 5 | Speed multiplier (2x), duration (10 s), charge time, cooldown, SFX. |
| `ExpansionDefinition` | 4 | Id, name, icon, cost (blueprint 1,500), tile teaser, open SFX. Unlock level lives in the catalog; scene content on the `Expansion`. |
| `StageDefinition` | 5+ | Unlocked scrap tiers, spawn tables, milestone task. |

Tier-2 scrap (M4, Back Lot): Fridge (power 20, 20–25 pieces), Washer / Stove / Go-Kart (power 15, 10–20 pieces).

Materials (M5): mixed metal $6 → Sorter (70% iron $12 / 30% copper $26). Recycling Plant gate $3,500 at yard Lv 6;
Sorter, Metal Bins and Metal Market upgrades unlock with it; Metal Hauler $1,200 (Lv 6), Market Runner $1,500 (Lv 7).

## 5. Area 1 scene hierarchy (minimum)

```
Area1_OldScrapYard
├─ _Systems        GameManager, EconomyManager, ItemPool, ScrapManager, HarvestManager, AudioManager, VFXManager,
│                  HitStopController, FloatingTextManager, ProgressionManager, UpgradeManager, TaskManager,
│                  GuideDirector, WorkerManager
├─ _Environment    Ground slabs (textured), Road, Fences (corrugated walls + low rail fence), Gate_A1_A2 (locked),
│                  JunkPiles (static; heap behind the Back Lot), Backdrop (Area 2/3 teasers, containers, trees)
├─ _Gameplay
│  ├─ PlayerStart, Player (prefab), LooseItems, ItemPool
│  ├─ ScrapSpawnZone  SP_* (car wreck variants, barrels, tire stacks)
│  ├─ Stations     Crusher (24,27) → Conveyor → Storage (24,19.2) · SellDesk (32,12.4) + CashPile
│  ├─ Workers      WorkerSpawn, Sites/PorterRoute (scrap zones + Back Lot zone → crusher pad),
│  │               Sites/DeliveryRoute (storage withdraw pad → desk stock pad)
│  ├─ Tiles        Tile_Upgrades, Tile_Porter, Tile_Helper, Tile_BackLot (GuideAnchors tile/...)
│  ├─ Customers    CustomerQueue: Slots (7, front first), Entry path, Exit path, Crowd (pooled customers)
│  ├─ BackLot      Expansion: Barriers (fences), LockedOnly (sign), Content (tier-2 spawn points), Focus
│  ├─ RecyclingPlant Expansion (barriers = Gate_A1_A2 bars + collider): Content (floors, Sorter, belts, Bin_Iron,
│  │               Bin_Copper, MetalMarket, Customers_Market, plant tiles, sorter boost pad), Focus
│  ├─ GuideMarker
│  └─ Slots        Slot_CustomerQueue, Slot_Gate_A1_A2 (legacy anchors)
├─ _Camera         Main Camera (CameraController)
├─ _Lighting       Directional Light, Global Volume (VP_Yard)
├─ _Environment/NavMesh   NavMeshSurface over Area 1 (scrap carves it at runtime)
└─ _UI             Canvas (JoystickArea, HUD: LevelBadge, cash/gem pills, TaskBanner, GuidePointer, UpgradePanel,
                   Announcer, FlyLayer), EventSystem
```

## 6. System dependencies (build order)

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
```

## 7. Blockout builders

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
   market, overdrive, workers, tasks; Sorter, boost pad, worker and item prefabs, round hats; Gate 2 + Area 2 content,
   routes, market line, NavMesh over both areas (content activated while baking so machines carve). Incremental.

Assets are updated in place so GUIDs survive re-runs. ProBuilder meshes on stations stay editable with ProBuilder;
items (pooled by the hundred) use baked mesh assets instead.

## 8. Milestones

1. **Harvest core** — movement, camera, one car (+ tier-1 small scrap), cutting, drops, pickup, carry stack, feel. ✔
2. **Production + cash** — crusher, conveyor, storage, sell desk, cash pile, HUD, art pass (Kenney/ProBuilder/DOTween). ✔
3. **Upgrades, tasks, XP, first worker** — upgrade rail, task chain + guide, yard level, Porter. ✔
4. **Tiles, customers, automation, first expansion** — upgrade tile + panel, hire tiles, Delivery Helper, customer
   queue, Back Lot with tier-2 scrap, UI restyle. ✔
5. **Recycling Plant** — Gate 2, Sorter → iron/copper bins → Metal Market (material orders, queue 5), Metal Hauler and
   Market Runner, Active Overdrive pads, CC0 audio, round hard hats. ✔
6. Furnace (ingots), heavy scrap, Area 3.
7. Worker specialization, Giant Scrap event.
8. Dockyard reveal, save/load, balance, polish.
