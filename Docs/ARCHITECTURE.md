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

## 3. Runtime systems

```
Core ─────────── Services (scene registry), GameEvents (global events), ModifiableStat, GameManager, GameConfig
 │
 ├─ Player ───── PlayerCharacter (wiring) · PlayerStats · PlayerController · PlayerInputReader · PlayerVisuals
 ├─ Harvest ──── ScrapManager (targets, spawn pool) · ScrapSpawnPoint · ScrapObject · ScrapPart · HarvestTool
 │               HarvestManager (drops + loose item registry) · WorldHealthBar · DropMath
 ├─ Items ────── ItemDefinition · WorldItem (flying/ground/moving/held) · ItemPool · CarryStack · ItemCollector
 │               IItemReceiver / IItemSource · ItemPile (hoppers, bins, counters) · TransferPad (stand-on pads)
 ├─ Factory ──── MachineDefinition + Machine + MachineVisuals · Conveyor · StorageDefinition + Storage
 │               SellDeskDefinition + SellDesk
 ├─ Economy ──── EconomyConfig · EconomyManager (cash, premium) · CashPile · CashCollector · CurrencyFormat
 ├─ Feedback ─── GameFeedback facade · AudioManager (+ProceduralSfx) · VFXManager · HitStopController · FloatingTextManager
 ├─ Camera ───── CameraController (follow, look-ahead, shake, punch, aspect-safe framing)
 ├─ UI ───────── VirtualJoystick · CarryStackIndicator · StationLabel · HudController · CurrencyWidget · UIFlyer
 │
 │  (later milestones)
 ├─ Factory+ ─── Overdrive (timed Speed modifier) · Sorter/Furnace/Press = new MachineDefinitions
 ├─ Economy+ ─── IdleIncomeManager
 ├─ Progression  UpgradeManager · TaskManager · XP/Level · ExpansionManager · StageManager
 ├─ AI ───────── WorkerManager + Worker (job state machine) · CustomerManager + queue · TruckRoute
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
| `MachineDefinition` | 2 | Input/output items, per-level input capacity, cycle time, inputs/outputs per cycle, upgrade cost. |
| `StorageDefinition` | 2 | Per-level capacity, withdraw speed, upgrade cost (blueprint: 40 units). |
| `SellDeskDefinition` | 2 | Per-level sale interval (blueprint: 1 / 8 s), units per customer, price multiplier, counter size, queue size. |
| `EconomyConfig` | 2 | Starting cash/premium, cash per physical bundle. |
| `UpgradeDefinition` | 3 | Target stat, modifier per level, cost curve, max level. |
| `TaskDefinition` | 3 | Type (cut/process/sell/hire/upgrade/open), target, count, reward, main/side/daily. |
| `WorkerDefinition` | 3–7 | Role (Porter/Operator/Loader/Seller), base speed/carry/efficiency, hire cost. |
| `CustomerDefinition` | 4 | Requested items, patience, pay multiplier. |
| `ExpansionDefinition` | 4 | Requirement task/level, cost, revealed scene content, camera pan target. |
| `StageDefinition` | 4+ | Unlocked scrap tiers, spawn tables, milestone task. |

## 5. Area 1 scene hierarchy (minimum)

```
Area1_OldScrapYard
├─ _Systems        GameManager, EconomyManager, ItemPool, ScrapManager, HarvestManager, AudioManager, VFXManager,
│                  HitStopController, FloatingTextManager
├─ _Environment    Ground slabs (textured), Road, Fences (corrugated walls + low rail fence), Gate_A1_A2 (locked),
│                  JunkPiles (static), Backdrop (Area 2/3 teasers, containers, trees)
├─ _Gameplay
│  ├─ PlayerStart, Player (prefab), LooseItems, ItemPool
│  ├─ ScrapSpawnZone  SP_* (car wreck variants, barrels, tire stacks)
│  ├─ Stations     Crusher (24,27) → Conveyor → Storage (24,19.2) · SellDesk (32,12.4) + CashPile
│  └─ Slots        Slot_CustomerQueue, Slot_Gate_A1_A2 (anchors for M4)
├─ _Camera         Main Camera (CameraController)
├─ _Lighting       Directional Light, Global Volume (VP_Yard)
└─ _UI             Canvas (JoystickArea, HUD: cash + premium pills, FlyLayer), EventSystem
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
M4 Workers reuse: CarryStack, ItemCollector, Machine ports; Customers depend on SellDesk + Economy.
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

Assets are updated in place so GUIDs survive re-runs. ProBuilder meshes on stations stay editable with ProBuilder;
items (pooled by the hundred) use baked mesh assets instead.

## 8. Milestones

1. **Harvest core** — movement, camera, one car (+ tier-1 small scrap), cutting, drops, pickup, carry stack, feel. ✔
2. **Production + cash** — crusher, conveyor, storage, sell desk, cash pile, HUD, art pass (Kenney/ProBuilder/DOTween). ✔
3. Upgrades, tasks, XP, first worker (Porter).
4. Customer queue, worker automation, first expansion gate.
5. Sorter, Iron, Recycling Plant.
6. Furnace, Copper, heavy scrap.
7. Worker specialization, Giant Scrap event.
8. Dockyard reveal, save/load, balance, polish.
