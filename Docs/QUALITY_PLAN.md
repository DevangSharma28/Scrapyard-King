# Scrap Yard King — Quality Plan

Goal: turn the working M1–M5 slice into a polished, satisfying vertical slice, then finish M6–M8 to the same bar.
Rules: extend the existing architecture (ARCHITECTURE.md), keep tuning in data, build through incremental builders,
verify every change in Play mode. Priority order follows the owner's brief: feel → progression → UI/UX → animation →
VFX/SFX → world → balance → performance → save/load → final polish.

## Audit (2026-10-02)

Verified in the live Editor: the full M1–M5 loop runs (cut → collect → crush → store → sell / sort → bins → market),
workers, customers, tiles, expansions and overdrive work, 33/33 EditMode tests pass, no console errors.

| # | Finding | Area | Impact |
|---|---|---|---|
| A1 | No chainsaw motor sound: cutting had only per-hit impacts, so the core verb felt thin. | Feel/audio | High |
| A2 | Hits had no flash on the object; the hit was only readable from sparks. | Feel | High |
| A3 | Health bar small, thin and anchored to a corner of big objects; no sense of chunked damage. | Feel/UI | Medium |
| A4 | Car drops landed 4 m median / 6 m max from the car, with a 2.2 m pickup radius: the player chased single pieces. | Feel | High |
| A5 | Full stack + walking over scrap = nothing happens (only a MAX label). | Feel/UX | Medium |
| A6 | Progress made in the 1.6 s gap between main tasks was lost (e.g. the first customer served right after stocking). | Progression bug | High |
| A7 | Hard hats rebuilt in M5 were too large from the game camera. | Art | Low |
| A8 | "+" buttons on the currency pills did nothing (no shop). Dead UI reads as broken. | UI | Medium |
| A9 | Pacing has never been measured end to end (time to first upgrade / worker / Back Lot / Plant). | Balance | High |
| A10 | Guide correctness is only spot-checked per task; no proof the whole chain is always followable. | UX | High |
| A11 | Machines all sound the same at any speed; overdrive was visual only. | Feel/audio | Low |
| A12 | Upgrade panel/cards and HUD are functional but plain (no next-goal emphasis, little hierarchy). | UI | Medium |
| A13 | Old Yard / Back Lot / Plant look similar; areas don't escalate visually yet. | World | Medium |
| A14 | No save/load: every session restarts. | Persistence (M8) | High later |

## Plan

### Phase 1 — Core feel (done in this pass)
- Chainsaw motor loop (`ToolAudio` + `Sfx_ChainsawEngine`): revs while cutting, bogs on each hit, spins down. (A1)
- Hit flash on scrap (`FeedbackConfig.hitFlashColor/Duration`). (A2)
- Health bar centred and sized to the object, with a lagging damage trail and a pop per hit. (A3)
- Loot fountains for big objects (`ScrapDefinition.dropBurstDuration`), tighter drop launch, pickup radius 2.5. (A4)
- Full stack: nearest wanted piece hops and MAX pulses (`ItemCollector.FullBump`). (A5)
- Stack landing squash (`FeedbackConfig.stackLandPunch`).
- Task progress buffered for the next main task during the banner delay. (A6)
- Smaller, rebuilt-from-head hard hats (`M5_Build.Hats`). (A7)
- Hidden dead "+" buttons. (A8)
- Machine cycle/output SFX pitch follows machine speed (overdrive audible). (A11)

### Phase 2 — Pacing audit with a guide bot (done in this pass)
- Found and fixed a hard deadlock (storage full → crusher jammed → full stack of scrap): crusher pad spills, guide
  drains jammed machines first. Found the XP plateau (L5 → L6 took 11 min, L7 never came): XP per $ sold.
- `GuideBot` (editor tool): plays the game by following `GuideDirector` (walks to the target, buys highlighted upgrades,
  stands on tiles) at 3x speed, logs time to every task, level, purchase and gate. Proves the guide never dead-ends
  (A10) and produces real pacing numbers (A9).
- Tune `ProgressionConfig`, costs and rewards from those numbers toward the blueprint arc (first upgrade < 2 min,
  first worker 15–30 min, Back Lot ~20–25 min, Plant 30–45 min).

### Phase 3 — UI/UX pass (first part done)
- Done: upgrade buttons fill with your cash until affordable, then breathe; task banner shows the reward up front;
  station status tags (JAMMED / FULL / x2 BOOST / NEEDS STOCK).
- Next: upgrade cards "what unlocks next", HUD hierarchy and spacing pass.

### Phase 4 — Milestone 6 (Furnace + ingots + Heavy Scrap Yard) (built)
- Furnace hall in the plant's north-east corner (bought on a tile, fence + sign appear with the plant): one Furnace with
  two recipes (iron → iron ingot $30, copper → copper ingot $64), breathing heat glow, chimney smoke + embers, spark
  burst per ingot, bellows and fan; belt to an Ingot Rack 9 m from the market; boost pad; Smelter hire (bins → furnace).
- Gate 3 in the scrap field's north wall; Heavy Scrap Yard with tractors (power 30), trucks (35) and garbage trucks (45),
  25-60 pieces with loot fountains, heavy hit/break sounds, rare iron/copper chunks; the Porter's zones reach it.
- Customers order only what the yard can make now; a customer stuck at an empty counter steers the guide.
- Fixes found while testing: the level badge could stay a level behind (tween killed before its callback), the guide
  marker hid before the player was on a pad (arrive radius 1.4 > pad rim 1.1), and a "sell copper" task could stall
  behind a customer who wanted iron.
- Pacing: Gate 2 down to $3,000 and preceded by "Sell desk to Lv.3" / "Crusher to Lv.3" (the Lv.2 desk capped income
  at ~$210/min for 17 minutes in the bot run).
- Guide fix from the M6 bot run: before fetching goods, drop a near-full stack of other things where they belong
  (`GuideDirector.NeedsRoom`); it used to send the player back and forth with one bale per trip.
- Still to measure: a full guide-bot run through M6 (Furnace and Heavy Yard timing against the blueprint's 45-75 min).
  The last run reached 14 game minutes on the new chain (no stalls) before play mode was stopped.

### Phase 5 — Milestone 7 (Operator / Loader / Seller, Giant Scrap event).

### Phase 6 — Milestone 8 (Dockyard reveal, versioned JSON save/load, balance, performance, final polish).

## Revamp: fast factory (owner's brief, 2026-10-06)

The brief: the game is too slow, too safe and too boring. Make it fast, busy and rewarding, and redesign the chain.
This replaces the M6 design goal (one Furnace, ingots, customers as the main sale).

### Target chain

```
SCRAP → CRUSHER → RAW METAL → METAL SPLITTER → iron / aluminum / copper / steel → four FURNACES (one per metal,
unlocked one after another) → four refined metals → PRESS → METAL BARS → bar storage → TRUCK LOADING → contract paid
```

Customers stay as the early sale (Raw Metal at the Sell Desk, raw materials at the Metal Market); from the Press on,
trucks and contracts are the main income.

### Pacing targets (to test against, not hard-coded)

| Session time | Beat |
|---|---|
| 0–2 min | First Crusher loop, first sale |
| 2–5 min | First upgrades, first worker |
| 5–10 min | Raw Metal flowing, the Splitter |
| 10–18 min | Several material outputs, carriers for them |
| 18–25 min | Second and third furnace |
| 25–35 min | All four material lines |
| 35–45 min | Press |
| 45–55 min | First large truck contracts |
| 55–70 min | Heavy Scrap Yard dense and busy |
| 70–80 min | Advanced workers and logistics |
| 80–90 min | Dockyard / port reveal |

Cutting feel targets: small objects 0.5–1.5 s, medium 1–2.5 s, large 2–4 s, heavy 4–8 s, giant 10–20 s.

### Steps

Each step is built, play-tested and reported like a milestone. A step is done when it is functional, fast, readable,
good-looking and within the performance budgets.

| Step | Scope | Status |
|---|---|---|
| R1 | Speed pass on the early game; Raw Metal; four-way Metal Splitter (aluminum, steel, four belts and bins) | Done |
| R2 | Four furnaces, one per metal, each with its own look, sound and speed, unlocked one after another; four refined metals; Smelter routes; the two-recipe Furnace retires | Done |
| R3 | Industrial Press → metal bars → bar storage; truck contracts (order, progress, reward) as the main sale; contract widget on the HUD and the truck-loading show (lights, horn, camera, cash flying) | Done (no truck lights, no 2x offer yet) |
| R4 | Heavy Scrap Yard: many more destructibles, vehicle variants and damage stages, excavator, rare steel / aluminum drops, a working crane cycle, more boss scrap | Partly: crane chain, truss jibs, Excavator type, metal drops done; no new wreck models, no new bosses |
| R5 | Timed boosts on `ModifiableStat` (cash, production, move speed, scrap spawn, truck loading) with a HUD indicator; rewarded-video offers behind one ad service (2x reward, instant truck, scrap rush, free cash) with cooldowns; offline income and its claim panel | Done with a stand-in ad provider; no instant truck, no premium spending |
| R6 | Port: ship cycle (arrive, dock, load, horn, leave), crane and forklift activity, dock workers; worker identities and animation set; environment, texture and world-density pass | Partly: the ship cycle as a working dock; the rest open |
| R7 | Economy model for the whole chain, clean full-session guide-bot run against the targets, device performance pass, documentation | Partly: model, task order, level curve, three carrier/guide fixes, measured to the first furnace. No full-session run, no device test |

### R1 result (2026-10-06)

Built: `R1_Build` (`Balance`, items, Splitter data, plant layout), the Splitter model in `Art_Machines.Sorter`, and a
guide fix found by the bot.

Measured with the guide bot, clean runs from an empty save (reports in `Docs/Pacing/`):

| Beat | Before (run 5) | Run 3 | Run 4 | Target |
|---|---|---|---|---|
| First sale | 0:34 | 0:40 | 0:26 | 0–2 min |
| Scrap Porter (first worker) | 7:48 | 3:21 | 2:58 | 2–5 min |
| Delivery Helper | 12:23 | 5:07 | 4:40 | |
| Back Lot | 18:01 | 7:01 | 6:31 | |
| Recycling Plant (the Splitter) | 31:40 | 11:07 | 9:44 | 5–10 min |
| First 15 Raw Metal split | 32:42 | 12:05 | 10:39 | |
| Metal Hauler | 37:04 | (not reached) | 13:43 | 10–18 min |

Run 4 has every R1 number except the last change. The bot is a guided newcomer; a player who knows the game is faster.

What the runs found and what was done:
- Run 1 stalled at 6:00: with the Back Lot open before the second chainsaw upgrade, the guide sent the player to
  "earn" at a fridge they could not cut. Fixed in `GuideDirector.NearestScrap` (skips scrap that is too tough unless a
  task names it).
- Run 2 was played at 10 fps (Editor in the background) and came out 1.7x slower than run 1 at 60 fps: the bot's
  timeline depends on the frame rate. Runs 3 and 4 were played with the Editor in front. Compare only like with like.
- Run 3: the plant at 11:07 and "Sell 6 copper" took 2:44 with flat cash. The plant now costs $1,200, the desk and
  crusher Lv.3 upgrades $500 / $300, and the task became "Stock the Metal Market" (12 of anything).
- Run 4: "Serve 8 market customers" took 2:30 with flat cash, the player doing every carry in the plant alone. The
  Hauler hire now comes before that task and costs $600. **Not re-measured**: this is the first thing to check in the
  next run.

After the owner's first look at R1 (2026-10-06: "environment needs a lot more, tiles overlap, NPCs come too late, the
hand does not animate, fewer bot runs"), one pass without bot runs (`Env_Build`, see `Docs/ART_DIRECTION.md`):
- Tiles: eight moved off belts, props and each other; `Env_Build.Check` now reports overlaps.
- NPCs: customers walked 40 m (yard) and 28 m (market) to the line; they now enter 17 m away at 5.2 m/s, arriving every
  0.35–0.9 s. Hired workers appear at their job instead of walking in from the yard's south edge; carriers walk 5 m/s.
- Cutting: the arm layer's mask excluded every bone, so the arms never held or moved the cutter. Fixed; the stroke is
  also larger (26 degrees instead of 7) and the body leans into the cut.
- Environment: lanes, Heavy Yard bays (16 heavy wrecks in rows), clutter clusters, more quick targets.
- Pacing was **not** re-measured after these changes (more scrap, faster customers and workers all make it faster).

Second round of owner requests the same day, all built, none of it measured with the bot:
- Economy again ("still slow"): carry 14 (+3 per backpack level), 7 cuts/s, 6.8 m/s, Crusher 0.35 s per piece, Raw
  Metal $8, yard storage 60, Back Lot $700, plant $1,000; customers take their order at once.
- Claw Crane after the Back Lot ($600, then $1,400 and $3,000): feeds the Crusher from loose scrap inside its reach.
  In a test 24 loose pieces went through the Crusher in about 22 s with nobody touching anything.
- Stalls without a roof, no road or floor words in the Old Yard, grass under the whole world, new buy tiles.
- The pacing table above is therefore out of date: every beat should come earlier now. Measure once before tuning.

### R7 result (2026-10-06)

Built: `R7_Build`, `Docs/ECONOMY.md`, `PorterRoute.leaveMachineInputs` / `machineInputKeep` / `priorityLoad`,
`GuideDirector.Supply`.

- **Economy model** (`Docs/ECONOMY.md`): value of one piece at each stage (8 → 18.2 → 45.9 → 69.25 → about 105 on a
  truck → about 142 on the ship), what each stage can move per minute, the list of sinks (about $497k) and the arc
  with an income estimate per beat.
- **Level curve** 60 XP x 1.55 (was 1.45): at 1.45 a run reached Lv 10 at minute 24 and no level gate held anything.
- **Task order**: the Heavy Yard block (`t34`, `t35`, `r4_heavy_crane`) now comes after the truck (`t43_ship`), so the
  session reads plant → furnaces → Press → truck → Heavy Yard → port.
- **Three fixes found by the runs** (all at 60 fps, clean save, x2):
  - `r7_run1`: "Smelt 12 iron ingots" took 17 minutes. With a Smelter hired, the guide still sent the player to haul
    iron beside them, so nobody cut scrap and income went to zero. Now a staffed station makes the guide say "supply
    the chain" (`GuideDirector.Supply`: earn, starting at the scrap), and the Runner leaves raw metal a built furnace
    wants (`leaveMachineInputs`).
  - `r7_run2`: "Split 15 raw metal" stalled 8 minutes because the Delivery Helper carried every Raw Metal bale to the counter.
    The Helper's route now leaves 24 bales for the Splitter (`machineInputKeep`).
  - `r7_run2`: the Runner kept serving the full raw bins and left ingots on the rack. The Ingot Rack pad is first on
    its route and wins from 4 pieces (`priorityLoad`).
- **Measured** (`Docs/Pacing/r7_run1..3.md`):

  | Beat | Target | run 1 | run 2 | run 3 |
  |---|---|---|---|---|
  | Scrap Porter | 2–5 min | 2:25 | 2:37 | 2:14 |
  | Back Lot | | 4:39 | 5:53 | 4:26 |
  | Recycling Plant | 5–10 | 8:22 | 8:09 | 8:38 |
  | "Split 15 raw metal" done | | 9:17 | 16:15 (stall) | 9:22 |
  | Iron Furnace | 10–18 | 17:13 | 24:40 | 17:46 |
  | Smelter | | 18:59 | 26:46 | not reached |
  | "Smelt 12 iron ingots" | | 36:01 (stall) | 27:35 (49 s after the Smelter) | not reached |

  Run 3 was ended by the owner at about minute 18 with no stall up to there. It confirms the Helper fix; the Runner's
  rack priority has **not** been seen working in a run.
- **Performance** (Editor, plant, minute 20 of run 1, `PerfProbe`): 60.0 fps, worst frame 25 ms, 29.6 KB of garbage per
  frame, 86 set-pass calls, 255k triangles (the count includes shadow cascades and the depth pass).

Not done, stated plainly:
- **No full-session run.** Nothing after the first furnace has a clean measurement: Copper / Aluminum / Steel furnaces,
  Press, truck orders, Heavy Yard, giants and the port were never reached by the bot. Every number for those beats in
  `Docs/ECONOMY.md` is the model's estimate, and late-game prices should not be tuned before one clean run.
- **No device test.** The performance line above is the Editor on a desktop. 29.6 KB of garbage per frame is high for a
  phone and has not been traced.
- The owner asked for fewer bot runs and more work on the game itself; a full run is one 45-minute job (x2) that should
  be agreed before it starts.

### R6 result (2026-10-06)

Built: `R6_Build`, `TruckBay.startDocked`, dock status texts as data.

The Dockyard is no longer a "coming soon" reveal. The cargo ship is a dock like the truck's, four times the size:
carry bars to the pad at the quay's edge, they pile up on the stern deck, the ship sails when its order is met or the
hold is full, the pay lands on a pallet on the quay and the ship comes back. A new last task asks for 60 bars on the
ship ($15,000 and 10 gems). The NavMesh now reaches the quay, so the guide and the Loader can get there.

Checked in play with cheats: 60 iron bars loaded, the ship left, $4,560 on the pallet (60 x $40 x 1.9), the ship was
back and loading again 15 s later; the bars are visible on the stern from the game camera. One bug found and fixed on
the way: the ship jumped onto the quay when the Dockyard opened (its course points were scaled to nothing during the
reveal's pop-in).

Not done from the brief's R6 list: forklifts, dock workers and trucks moving on the quay beyond the one forklift that
was there, quay cranes working in step with the loading (they still loop on their own), ship lights and smoke, worker
identities and the animation set, the environment and texture pass. Not checked: a ship order (the test had no Press,
so the ship came without one), the Loader walking to the ship, the guide's arrow to the ship pad, both docks sharing
the one HUD order card.

### R5 result (2026-10-06)

Built: `Boosts/` (BoostDefinition, BoostManager, AdService, AdOfferDefinition, OfferDirector, IdleIncomeManager), HUD
`BoostBar`, `OfferButton`, `OfflinePanel`, `R5_Build`.

Checked in play: three boosts switched on by script showed as chips with timers; cash multiplier 2, walking speed
6.8 → 10.2, Crusher speed x2. A forced offer (FREE CASH, $1,250 at yard Lv 5) appeared on the button; accepting it
paid after the stand-in's pause (cash 850 → 2,100). A simulated return after 2 h 14 min at $6.50/s showed the panel
with +15.7K; "2X WATCH" paid 31,356 and closed it. No console errors.

Not done: a real ad SDK (the stand-in always grants), "instant truck", spending premium currency on boosts, an
"emergency boost". Not checked: Scrap Rush and Truck Rush in action, the double-pay offer after a real truck order,
boosts and offline income across a real save and reload (only the restore call was exercised), how much the boosts
bend the pacing. Offers start at 90 s and are at least 70 s apart; whether that is too many is a play-test question.

### R4 result (2026-10-06)

Built: `R4_Build` (scrap data), `Crane_Build` rewritten for two cranes, `ItemSpill`, crane item filter and jib sections.

- **Heavy Yard crane** ($5,000 / $9,000 / $15,000, yard Lv 9, task after "Cut 2 trucks"): picks loose scrap inside 16 /
  22 / 28 m and drops it on a belt that runs through Gate 3 and tips it into the scrap pit, where the yard crane feeds
  the Crusher. Checked in play with cheats, twice: 20 loose pieces thrown down in the Heavy Yard became 14 Raw Metal in
  storage in about 45 s with nobody touching them, 2 pieces still lying, no console errors.
- **Crane hero pass**: both cranes have a lattice mast, a cab, counterweights and a truss jib in three sections.
- **Excavator**: its own scrap type (2,000 health, power 45, 80–95 pieces, aluminum always). Each heavy type drops one
  of the four metals. The bays hold 14 heavy wrecks (one slot went to the crane's corner).

Not done from the brief's R4 list: new wreck models and paint variants (the six heavy models and the excavator are the
ones from the art pass), a damage-stage review, more boss types (only the Giant Truck), forklifts and worker traffic in
the yard. Not checked: the belt narrows Gate 3 by 1.3 m, and neither a porter nor the guide was watched walking through
it; the Excavator was not cut in play; no frame-time measurement with two cranes working.

### R3 result (2026-10-06)

Built: `R3_Build`, the Press model in `Art_Machines.Press`, `TruckContractDefinition`, contract logic in `TruckBay`,
`GameEvents.ContractChanged`, `ContractWidget`.

Checked in play with cheats (no bot run): the Press build reveals the Press, belt and Bar Storage; ingots fed by hand
came out as bars and reached the storage; bars carried to the truck filled an IRON ORDER (9/12 on the HUD card and on
the dock label), the truck left and paid $896 (12 ordered bars and 4 extra); the next truck came with a COPPER ORDER
because copper was makeable, and paid $1,775. A hired Loader carried ingots from the rack to the Press and bars from
the storage to the truck with nobody helping.

Not done or not checked:
- No guide-bot run: the new task order (Press → Loader → press 20 bars → Lv 10 → dock → load 24 bars) and the guide's
  routing for bars are untested by a play-through.
- The truck show has the horn, the camera thump and the pop-up, but no flashing lights and no "2x reward" offer (R5).
- Mixed and Builder's orders were not seen in the test; a saved game with a truck at the dock rolls a new order on load.
- Numbers (Press $9,000, bar values, order multipliers) are first guesses for R7.

### R2 result (2026-10-06)

Built: `R2_Build` (data, prefabs, scene), four furnace models in `Art_Machines.Furnace`, carriers with several
drop-offs, group boost pad and operator, short machine labels, nested expansions in the NavMesh bake.

Checked in play with cheats: the hall opens with the Iron Furnace; buying the Copper, Aluminum and Steel builds reveals
each furnace in its slot; 8 pieces of each metal fed by hand came out as ingots and reached the rack over the collector
belt; one hired Smelter served all four furnaces from the bins. With the rack at 40 the first test filled it and the
whole battery stopped (the intended visible bottleneck), so the rack now starts at 60.

One guide-bot run from a clean save, `Docs/Pacing/r2_run1.md`, 34 game minutes. The Editor was throttled to 10 fps
for the whole run (the R1 runs showed that makes the bot about 1.7x slower), so read the order, not the clock:

| Beat | r2_run1 (10 fps) |
|---|---|
| Scrap Porter / Delivery Helper | 2:50 / 3:39 |
| Back Lot / Claw Crane | 4:51 / 5:48 |
| Recycling Plant (Splitter) | 8:01 |
| Metal Hauler / Market Runner | 10:52 / 14:18 |
| Iron Furnace | 17:49 |
| "Smelt 12 iron ingots" done | 28:09 |

No stall was reported, but "Smelt 12 iron ingots" took ten minutes with flat cash: the player carried iron alone while
the Market Runner emptied the same bin. Changed after the run and **not re-measured**: the Smelter hire now comes
straight after the furnace, and the market asks mostly for ingots. The bot never reached the Copper, Aluminum or
Steel build tasks, so those tasks and the guide's handling of four furnaces are untested by a full play-through.
Yard level 10 came at 24 minutes: with the gentler XP curve the level gates (7, 8, 9) no longer hold anything back,
which R7 has to settle.

Open after R1:
- The Furnace hall and everything after it still has the M6–M8 numbers and the single two-recipe Furnace. Aluminum and
  steel have no furnace yet; they sell raw.
- One full bin stops the Splitter. With four bins and one Market Runner that is the plant's new bottleneck by design,
  but it has only been watched for a few minutes, not tuned.
- No device test of the faster game; EditMode tests cover the logic only.

## UI + economy experience (owner's brief, 2026-10-06 evening)

The owner handed over the whole player-facing economy: HUD, popups, diamonds, rewarded video, IAP, shop, missions,
daily rewards, settings. Built in steps like the revamp, each play-checked and reported:

| Step | Scope | Status |
|---|---|---|
| U1 | Foundation: UI motion kit, tactile buttons, one popup framework (reward / confirm / danger / info), currency flights from any origin, gain tickers, diamonds as a real currency (config, earn/spend doors, milestone gifts, first diamond moment), analytics door, settings = pause screen with reset-by-hold, safe area, top HUD re-layout | Done |
| U2 | Shop + IAP: `IAPProductConfig`, store service behind an interface (stand-in like `AdService`), diamond bundles, starter pack, no-ads, restore purchases, value badges, cash/diamond "+" buttons open the shop (shown once the shop is unlocked) | Done |
| U3 | One rewarded-video system: `RVOfferConfig` (cooldown, daily cap, priority), never two offers at once; 2x truck pay on the contract-complete card, FINISH NOW on a loading truck, scrap rush, free cash, offline 2x on a new WELCOME BACK screen with animated counters; diamond sinks (instant truck, speed-up, premium boost) through `DiamondSpend` | Done (speed-up of the truck's return never shows: trucks return in 5–14 s) |
| U4 | Missions (main / daily / achievements), 7-day daily rewards, one badge system, a minimal bottom bar revealed progressively (Home, Shop, Missions; Settings stays on the gear) | Done (bar = SHOP, MISSIONS, DAILY; no events tab) |
| U5 | Upgrade panel redesign (level → level, what changes, "earn $X more"), purchase satisfaction (machine flash, floating +20% SPEED), station panel, worker overview, NEW! unlock reveal, contract card + completion sequence, boost / overdrive HUD | Done, except the station panel and worker overview (see result) |
| U6 | Developer economy window (income/min, diamond flow, RV and IAP counts), simulated player profiles (free / light / mid / high) over 1 h–30 d as EditMode tests, a clean play session against the brief's 15 acceptance questions | Done (see result) |

Conflicts with standing rules, resolved this way: buying stays in the world (tiles, upgrade tile); the shop sells
diamonds, boosts and bundles only, never a machine or a worker. The "workers" screen is an overview, not a hiring menu.
"Auto-pickup" and "music" are not in Settings: pickup is always automatic and the game has no music yet (a toggle for
either would do nothing).

### U1 result (2026-10-06)

Built: `U1_Build` (Assets, Scene); runtime `UIAnim`, `UIButtonFeel`, `UIToggle`, `HoldButton`, `SafeArea`,
`PopupManager` + `PopupRequest`, `DiamondSpend`, `DiamondRewards`, `SettingsPanel`, `HudController` (tickers, glow,
diamond reveal, screen-origin flights), `UIFlyer.FlyFromScreen`, `CurrencyOrigin`, `PremiumEconomyConfig`,
`EconomyManager` premium doors, `GameSettings` + `SettingsApplier`, `Haptics`, `Analytics`; tests
`PremiumEconomyTests` (EditMode 64/64).

Checked in Play mode (captures): the first minutes show cash, level and task only; opening the Recycling Plant (and
level 6) brings the gift card, CLAIM flies the diamonds to the new diamond capsule; the speed-up card reads "SAVE
12m 30s / SPEND 45" and spends once even on a double tap; settings pause the game, every toggle switches and is
remembered; reset shows the warning, needs a two-second hold, and reloads a clean game with no errors.

Not done in U1: nothing spends diamonds yet in normal play (the sinks arrive with U3), no shop, the "+" buttons stay
hidden, the old offline panel and offer button are not yet on the popup framework.

### U2 result (2026-10-06)

Built: `U2_Build` (Assets, Scene); runtime `Shop/IAPProductConfig`, `Shop/ShopCatalog`, `Shop/StoreService`
(`IStoreProvider`, Editor stand-in, ledger, pending grants, restore, free video diamonds), `UI/ShopPanel`, `UI/ShopCard`,
`BoostManager.Activate(definition, seconds)`; tests `ShopCatalogTests` (EditMode 69/69).

- **Products** (`Data/Shop`): Industrial Starter Pack ($1.99, one time: 500 diamonds, cash worth 150 diamonds, 2X
  FACTORY 30 min, BEST VALUE), six bundles 100 / 500+50 / 1,200+300 (POPULAR) / 2,500+750 / 6,500+2,500 /
  15,000+7,000 (BEST VALUE) at $0.99–$99.99, Factory Boost Pack ($4.99). Diamonds per dollar climb 101 → 220; the
  starter pack beats all. **No Ads exists but is switched off**: the game shows no forced ads, so it would remove
  nothing.
- **Diamond items**: five boosts priced by the time they save (2X CASH 10 min = 22, TRUCK RUSH 3X 10 min = 39) and
  three cash crates priced at the diamond's cash value (+10% / +25% for the bigger two).
- **Free**: 5 diamonds for a video, every 4 h, at most 3 a day (wall clock, saved).
- **Shop**: four tabs, hero card, bundle grid, rows; buttons show price / diamond cost / WATCH / countdown / OWNED /
  "..." while the store works. Opened by the "+" on both capsules, which appear with the first diamond. The capsules
  draw above the shop so the balance and the landing coins stay visible.
- Checked in Play mode: a double tap buys once, a double CLAIM pays once; quitting before CLAIM and restarting offers
  the purchase again and pays it once; diamond boost asks first (22 > 20) and starts 2X CASH for 10 min; a 20-diamond
  crate pays at once; the free video pays 5 and shows "3h 59m".

Not done in U2: a real store (Unity IAP or a native plugin) behind `IStoreProvider`, receipt validation on a server,
localised prices from the store (the stand-in shows the USD fallback), contextual offers after an upgrade (U3).

### U3 result (2026-10-06)

Built: `U3_Build` (Assets, Scene); `OfferDirector` rewritten as the one rewarded-video gate (`Ready`, `Watch`, saved
wall-clock cooldowns and daily caps, priorities, analytics, ORDER COMPLETE card), `AdService.ShowRewarded(..., onFail)`,
`WelcomeBack` (replaces the old offline panel), `TruckSkipButton`, `TruckBay.FinishOrder` / `CallNow` / `MissingValue`
/ `DockedFor` / `AwayLeft`, `BoostManager.Activate(definition, seconds)`, `IdleIncomeManager` keeps unclaimed cash in
the save, `PopupRequest.MaxWait` (stale offers are dropped), `OfferButton` hides under popups.

- **One gate.** Every video goes through `OfferDirector.Watch`: the reward runs only after the video completed, the
  cooldown and the day's count start then, and both survive a restart. The HUD rotation never starts while a popup or
  another video button is up. Cadence (data): free cash 10 min, scrap rush 15 min (priority), boosts 10 min, 4–8 a day;
  2X order pay every 3 min (20 a day); FINISH ORDER 10 min (5 a day); 2X away cash once per return (6 a day).
- **ORDER COMPLETE card** after a truck order (only when the 2X offer is ready and the order pays $400+): the reward
  rolls up, "NICE!" or "2X +$12.5K" with the video. Dropped if it would show more than 8 s late.
- **WELCOME BACK card**: time away, the rate the yard earned, the total rolling up, CLAIM or 2X. A failed video pays
  the plain amount. The cash stays in the save until claimed.
- **FINISH ORDER** under the order card once the truck has waited 20 s and a quarter of the order is missing: a video,
  or diamonds at the cash value of what is missing (`PremiumEconomyConfig.CashCost`).
- Checked in Play mode: 2X away cash paid $36,800 for $18,400; finish-by-video sent the truck off at once and it came
  back with a new order; finish-by-diamonds took 1 diamond for a $672 shortfall at Lv 11; a queued ORDER COMPLETE card
  behind other popups was dropped as stale; tests 69/69.
- Fixed on the way: the sparkle and ray sprites had a 15% alpha floor and showed as squares (`Mathf.SmoothStep` is
  not GLSL's `smoothstep`).

Not done in U3: BRING NOW (skip the truck's return) exists but never shows, since trucks return in 5–14 s, under the
15 s threshold; contextual premium offers after an upgrade (§39) wait for U5's upgrade flow.

### U4 result (2026-10-06)

Built: `U4_Build` (Assets, Scene); runtime `Progression/MissionConfig`, `DailyRewardConfig`, `MissionManager`,
`DailyRewardManager`; `UI/Badges`, `BadgeDot`, `NavBar`, `MissionsPanel`, `MissionRow`, `DailyPanel`; tests
`MissionTests` (EditMode 75/75).

- **Missions** (MAIN, DAILY, ACHIEVEMENTS). `MissionManager` keeps lifetime counts from `GameEvents` (scrap broken,
  pieces picked up, processed, sold, cash earned, customers, boost pads, truck orders, upgrades, hires, areas). Three
  dailies a UTC day from a pool of eight (same three for everyone that day, targets grow with the yard level, no stat
  twice); seven achievements with 3–5 tiers. MAIN shows the current chain step, which completes by itself. Each row
  shows its reward before the player starts; CLAIM pays once and flies to the HUD.
- **Daily rewards**: a 7-day calendar (cash, 10 diamonds, 2X CASH 10 min, cash, 15 diamonds, 2X FACTORY 15 min, then
  cash + 50 diamonds). A missed day does not reset the cycle. The panel opens by itself once a session when today's
  reward waits (after the welcome-back card), and from the bar.
- **Badges**: one per destination (SHOP = free diamonds ready, MISSIONS = rewards to claim, DAILY = today's reward).
- **Bottom bar**: SHOP · MISSIONS · DAILY, hidden in the first minutes, popping in one by one with the first diamond
  (the plant, about minute 9). No HOME tab (the yard is home) and no EVENTS tab (no events exist).
- Checked in Play mode: the bar is hidden on a new game and appears with the first gift; the daily panel opened by
  itself, CLAIM paid day 1 once ($1.2K at Lv 6) and turned into "NEXT IN 10H 44M"; a met daily and an achievement tier
  paid once each (+3, +10); after a restart the day stays claimed, the claimed daily stays claimed, the panel does not
  reopen.

Free diamonds a day after U4: about 9 from dailies, about 11 from the calendar, up to 15 from videos, plus achievement
tiers: roughly 35 a day for an active player (`MissionTests` holds dailies + calendar under 30).

### U5 result (2026-10-07)

Built: `U5_Build.Scene`; runtime `Progression/UpgradeGain`, purchase celebration in `UpgradeManager.Complete`,
`UpgradeCard` (level pill, savings text), `Announcer` (icon, NEW MACHINE), `ContractWidget` (completion), `BoostBar`
(Overdrive chip, last-seconds breath); fixes in `UI_Build` (panel title under its plate, wider level pill); tests
`UpgradeGainTests` (EditMode 78/78).

- **Purchase satisfaction.** Every upgrade lands on its station: sound, sparks, camera punch, a squash of the station,
  "LEVEL 8!" and then what got better, read from the effect text ("200 → 240/min" floats out as "+20% SPEED"; the sell
  desk says SALES, storage CAPACITY, cranes REACH). First levels say BUILT!, hires HIRED!.
- **Upgrade cards.** The level pill reads "LV 7 → 8" (MAX when done); a card you cannot afford yet shows
  "8.4K / 12.5K" on its button while the button fills with your cash. Fixed on the way: the panel's UPGRADES title was
  drawn under its own plate (the builder flipped the order on every re-run).
- **Unlock reveal.** The announcer shows a picture of what arrived (area, machine, worker, level star) popping in with
  the headline; a build inside an area (furnace, Press) says NEW MACHINE! instead of NEW AREA!. It plays over the
  world (no button, nothing to dismiss), so the reveal leads into the world.
- **Order card.** On completion it grows, the pay rolls up from zero, it settles and leaves; the truck driving off no
  longer cuts that short, and a new order ends it at once.
- **Boost HUD.** A boost pad's Overdrive shows as the first chip ("2X 0:08"); every chip breathes gently in its last
  10 seconds (no flashing). The chips start below the task banner (they clipped it).

Not built, on purpose: a station panel and a worker overview screen. The yard already shows both in the world
(station labels with level, stock and status; workers walking their routes), the upgrade panel lists every machine
with its level and next effect, and the owner's rules say no unnecessary menus. If the owner wants them, they are
read-only screens over `StationRegistry` and `WorkerManager`. Also not built: contextual premium offers after an
upgrade (§39): one more popup after every purchase would be the spam the brief warns against.

### U6 result (2026-10-07)
Built: `U6_Build` (Assets, Scene); runtime `Economy/EconomyLedger`; Editor `Economy/EconomySimulator` and
`Economy/EconomyWindow` (**Window → Scrap Yard King → Economy Window**); tests `EconomySimulationTests` (EditMode
89/89). Table and reasoning: `Docs/ECONOMY.md`, "Simulated players".

- **Economy window (developer only, Editor code).** Live: cash per minute, session and lifetime cash, orders, offline,
  diamonds earned/spent per day with sources and sinks, average balance, videos offered/watched/failed per session,
  video cash, per-offer cooldown and daily count, purchases (conversion per shop visit, average value), and the
  guardrails of §42 (cash/min, diamonds/min, next upgrade and time to afford, contract ROI, video reward ratio, IAP
  value, offline income per day, a diamond's worth here). Prices: every upgrade and expansion with time to afford at
  the current income, contracts, diamond prices, shop, offers. Profiles: the simulation table.
- **Simulated players.** Free / Light / Mid / High (plus "watches every video") over 1 hour of play, 1, 3, 7 and 30
  days, on the real data. All four reach the end of the first-session arc (Free on day 1.5); paying helps in order.
- **What the simulation changed** (each was a real fault, not model noise):
  - a diamond's cash value grew ×1.3 per level forever while income flattens: a Lv 25 diamond bought 2.7 minutes of
    income (boost: 0.3). Now ×1.08 after Lv 12; 0.16–0.31 minutes at every level (`PremiumEconomyConfig`);
  - achievements paid 195 diamonds in the first hour of play: early tiers start later, lifetime totals similar;
  - a HUD video offer about every two minutes of play: pause between offers 70 → 150 s;
  - one return after 4+ hours paid five 15-minute sessions of income: offline cap 4 → 2 hours;
  - FINISH ORDER paid a whole order for one video on an empty truck: now from 25% loaded (the brief: "when a truck is
    loading").

**Acceptance session** (clean save, cheats only to jump to later content; state checked by values, the owner was
playing in the Editor part of the time):

| # | Question | Answer |
|---|---|---|
| 1 | Current goal in under 2 s? | Yes: task banner with progress at the top, guide arrow in the world. |
| 2 | How much cash? | Yes: cash pill top right, ticker and glow on gains. |
| 3 | How many diamonds? | Yes, from the first diamond on: the pill appears with the first gift (25) and counts up. |
| 4 | What can I afford? | Yes: green price button vs "8.4K / 12.5K" filling; "LV n" on tiles the yard level does not reach yet. **Fixed:** the bottom bar covered the lowest row of upgrade cards; it steps aside while the panel is open. |
| 5 | Why is an upgrade valuable? | Yes: "171 → 200/min", "LV 1 → 2"; after buying "+20% SPEED" on the station. |
| 6 | What are my workers doing? | Yes in the world: workers carry visible stacks along their routes; station labels show level and stock (no screen, on purpose). |
| 7 | My current contract? | Yes: "IRON ORDER 0/12 · LOAD THE TRUCK · $672" on the HUD and above the dock. |
| 8 | What a boost does? | **Fixed:** a running boost chip showed only an icon and "9:46"; every chip now reads "2X 9:46" like the Overdrive chip. |
| 9 | An RV reward immediately? | Yes: "2X +$672" with a video icon on ORDER COMPLETE, "2X $4.8M" on WELCOME BACK, FINISH ORDER WATCH under the card. |
| 10 | An IAP bundle immediately? | Yes: contents, bonus line, price, ribbon (BEST VALUE, POPULAR, ONE TIME OFFER). |
| 11 | Change settings easily? | **Fixed:** settings could open scrolled down to the danger zone; it lays out first and opens at AUDIO. |
| 12 | Recover purchases? | Yes: RESTORE in ACCOUNT. |
| 13 | Claim offline income easily? | Yes: WELCOME BACK, "$2.4M · your yard kept working for 2 h 00 min", CLAIM / 2X. **Fixed:** the daily calendar opened in the same frame underneath; it now waits until the welcome-back cash is claimed. |
| 14 | Understand every reward? | Yes: every gift is one card with picture, amount and reason (LEVEL BONUS +5 · YARD LEVEL 6), paid once on CLAIM (25 → 90 over six cards). **Fixed:** the calendar no longer jumps in mid-play straight after the first-diamond gift (it opens at the start of a session). |
| 15 | Does every important interaction feel satisfying? | Yes for purchases, gifts, orders, boosts and returns (U1–U5 feedback). Not judged on a device: haptics are a plugin hook and frame rate on a phone is untested. |

Ledger checked in play: gifts, purchase (500 + starter), diamond spend (22), two videos, video cash = the 2X order
pay plus only the doubled half of WELCOME BACK, sessions kept across a restart.

Owner's screenshots after U6 (fixed in `U6_Build.Hud`): the level star ran into the task banner's corner, the task
reward hung under the banner on the side task's row (two texts drawn over each other), the side task crossed the order
card and the boost chips started inside the banner. Top HUD from the top now: banner (reward inside, on the bar row,
deep orange) → side task row → order card / boost chips → FINISH ORDER.

**HUD layout pass (2026-10-07, `HudLayout_Build`):** an audit tool (`AgentScripts/Tools/UiAudit.cs`) measured every
screen (start, late HUD with order card, FINISH, five chips, offer, boss bar; upgrade panel; four shop tabs; three
mission tabs; daily; settings; popups) at 1080 × 1920, 1080 × 2400 with a notch and 1536 × 2048, and swept the camera
over the map for world labels. Found and fixed: the 0.5 width/height match shrank the canvas to 966 units on 20:9
(pills over the level badge, panel close buttons off screen) and pushed panels off a tablet's bottom (now Expand);
gem pill over the XP bar, cash icon over the gem "+", icons off the top edge, 62-unit "+" buttons; the boss bar over
the task banner (now at the bottom); station labels hiding buy tiles (they fade while covering one); the dock's order
tag wrapping onto its label; diamond icon over CLAIM; shop ribbon over the title; a slider that took touches only on
its 46-unit track; the upgrade sheet's bleed shorter than a gesture bar; the upgrade tile's "!" over the boost pad; a
Boots card reading "10.2 → 7.3 speed" while FAST BOOTS ran. After the pass the audit reports nothing at all three
shapes.

Left open: a real store and ad SDK; device test (haptics, performance); a big spender can turn diamonds into more cash
than the level gates let them use in the first days (a daily crate limit would be the lever, if the owner wants one).

## Fun pass (owner's brief, 2026-10-09)

The owner wants the game more fun, more addictive and a bit faster, with heavier automation in the Heavy Yard and no
port. Built one step at a time like the revamp, each play-checked and reported:

| Step | Scope | Status |
|---|---|---|
| F1 | Remove the port (Dockyard, ship, quay, Gate 4); machine boost pads offer a 2-minute 2X by video after the free burst; a fixed 2X CASH video button (2 min); customers served 0.5 s apart; check daily reward and offline earning | Done |
| F2 | The Claw Crane grabs whole scrap (tyre, drum, car body) and drops it into the Crusher; a Scrapper worker who cuts scrap, picks the pieces up and feeds the Crusher, slower than the player | Done |
| F3 | Trucks: better truck models, loading and leaving as a mini-show (tailgate, cargo visible, horn, drive-off), and trucks as customers at the ingot market (a lane of trucks instead of walking buyers) | Done (dock truck's arrival show built but not filmed) |
| F4 | Conveyor model rework (frame, rollers, moving belt, end drums, legs) and a mesh-overlap audit with fixes across the world | Done |
| F5 | Heavy Yard automation: a Heavy Crusher fed from the Heavy Yard, dump trucks driving heavy scrap to a dump yard, excavators and cranes working the piles; higher furnace levels ask for large amounts of scrap from that line | Done (furnace numbers unchanged, see result) |

### F1 result (2026-10-09)
Built: `F1_Build` (Assets, Scene); runtime `UI/CashBoostButton`, the video extension in `Tiles/OverdriveTile`
(`OverdriveConfig.videoOffer / videoSeconds / offerEvery`, `AdOfferKind.MachineBoost`, `Offer_MachineBoost`), the HUD
chip following the running pad (`BoostBar`), the explicit customer gap (`CustomerConfig.nextCustomerGap`,
`Customer.Arriving`). Tests 89/89.

- **No port.** Dockyard expansion, ship dock, quay, water, ship, quay cranes, Gate 4 and its tile removed; the hall's
  east wall is one wall again; the grass runs on east (x 200) with the two warehouses M8 had taken away; NavMesh
  rebaked. Data: catalog entry, task t50 and the 50-diamond milestone removed; the "open areas" achievement tops at 9.
- **Machine boost by video.** After the free 10 s burst the pad shows OVERDRIVE! with OK / 2 MIN (video). Watched: the
  machine (the whole furnace battery from its pad) runs 2X for 2 minutes; the pad and the HUD chip count it down. The
  card comes at most every 90 s per pad and the offer has a 60 s cooldown and 15 a day, so standing on a pad never nags.
- **2X CASH button.** Fixed on the left edge from yard Lv 3, shown while the offer is ready and the boost is not
  running: 2 minutes of double cash. Taken out of the rotating offer (cooldown 8 min, 8 a day).
- **Customers 0.5 s apart.** The next buyer is served 0.5 s after the last leaves, as soon as they are 0.8 m from the
  counter, and the line holds 7 at every level (it used to run dry: 3–7 s while buyers walked in). Measured: one
  customer every 0.65–0.75 s with stock on the counter (gap plus handing over 3–5 items).
- **Daily reward and offline earning** were already in (U3/U4: 7-day calendar, WELCOME BACK with 2X); unchanged.

### F2 result (2026-10-09)
Built: `F2_Build` (Assets, Scene); runtime `ScrapObject.Lift / FeedInto / CanBeLifted` (a Carried phase),
`ScrapDefinition.craneLiftable`, `ClawCrane.liftWholeScrap / feedRate`, `ScrapHit.Quiet`, cutting in `PorterBrain`
(`WorkerDefinition.cutPower / cutDamage / cutsPerSecond / cutReach`). Tests 89/89.

- **Crane lifts whole scrap.** The Claw Crane swings over the nearest untouched light object in reach (tyre stack,
  drum, car, kart, stove, washer, fridge), lowers onto its top, lifts it whole and carries it over the Crusher, then
  lowers it into the hopper and feeds its pieces in at up to 18 a second as the hopper makes room; the object shrinks
  and is gone with the last piece (rare metal drops go in too). It leaves loose fragments alone and never takes scrap
  somebody has started cutting. A full hopper keeps it hanging there, so the bottleneck still shows (seen in play:
  Storage full → Crusher JAMMED → hopper FULL → the stove waits in the claw).
- **Scrap Porter cuts.** With nothing loose to carry it walks to the nearest scrap in its zone that a level-1 chainsaw
  can cut, cuts at 25 damage a second (the player: 70), collects what falls and delivers it. Its cuts make sparks and
  sound but no hit-stop or camera shake. Seen in play: Collect → Deliver → Cut cycles.
- The Heavy Yard crane still lifts loose pieces onto its belt; F5 reworks the Heavy Yard.

### F3 result (2026-10-09)
Built: `F3_Build` (Assets, Scene); runtime show fields on `TruckBay` (`sideGate`, `beacon`, `reverseLights`, `reverseSfx`,
`gateSfx`, `hornSfx`, `fullSink`), `World/VehicleWheels`; `Sfx_TruckReverse`, `Sfx_TruckGate`; prefabs
`Customer_Truck_Blue/Orange/Green`. Tests 89/89.

- **Truck model.** One ArtKit truck for the dock and the road: rounded two-tone cab, wind fairing, light bar, air horns,
  visor, wipers, mirrors on arms, chrome grille and bumper, twin stacks, fuel and air tanks, steps, fenders, mud flaps;
  plank flatbed with a ladder rack, stake sides, tail and reverse lights; a separate drop-side, roof beacon and reverse
  lights; wheels with tread, rim, hub and nuts. Same footprint as the old dock truck, so the cargo pile still fits.
- **Dock truck show.** Backs in with the beacon turning, reverse lights blinking and beeping; the side drops open; the
  body settles as the bed fills; when the order is met the side swings up, the horn sounds, and it pulls away.
- **Trucks are the market's customers.** Buyers at the Metal Market (metals and ingots) drive in along the near lane
  (z 6.4) from the east, queue in four slots 7 m apart, take their order onto the bed at the counter and turn off
  south into a new side street at x 48. Measured: never closer than 7 m (5.8 m trucks), six trucks served in about 3 s
  while stock lasted. The ambient road trucks moved to the far lanes (z 1.4 and 3.9).
- Not filmed: the dock truck's arrival (it was already docked in the test); its states and the model were checked.

### F4 result (2026-10-09)
Built: `F4_Build.Scene` (belts + tidy); runtime `Conveyor.rollers / rollerRadius` (drums turn with the belt; an empty
belt keeps running); tool `AgentScripts/Tools/MeshOverlap.cs`. Tests 89/89.

- **Conveyors.** All 12 belts rebuilt: C-channel rails (yellow web and flanges, black stripes, bolts), rubber skirt
  boards, the scrolling belt and a return belt with idlers, A-frame legs with braces and foot plates, and at each free
  end a drum that turns with the belt in bearing blocks; the head end has a blue drive motor under a yellow guard. The
  belt surface stays at 0.4 m, so items ride as before. Belts now run when empty too.
- **Overlaps fixed.** The furnace collector chain overlapped at every joint (each frame ran 0.3 m past its end with a
  doubled drum): joined ends now meet flush halfway. The Splitter's four fanned belts crossed each other's rails: they
  are narrower and start a little after the chute. MeshOverlap then listed every station, tile and prop pair whose
  bodies interpenetrate; fixed: a tank half inside a container (moved 2.7 m), pallets lying on the Smelter's hire tile
  (moved to the old gateway, free since F1), pallets wedged between crushed cars and bales (removed), two flags flying
  over the Sell Desk's stock and cash pads (removed), three trees and bushes growing through a backdrop warehouse
  (removed). What remains on the list is on purpose: tree clusters, stacked crates, belts under the furnaces' outputs,
  the crane jibs passing overhead.

### F5 result (2026-10-09)
Built: `F5_Build` (Assets, Scene); runtime `Factory/DumpRoute`, `Factory/DumpTruck`, `Factory/Excavator`; data
`Machine_HeavyCrusher`, `ClawCrane_Dump`, `Expansion_DumpYard`, task `t51_dump_yard`. Tests 89/89.

- **Dump Yard** (x 40–76, z 42–74): a fenced lot east of the Heavy Yard (its walls stand from the start), concrete with
  a painted haul lane; opened for $25,000 at yard Lv 12 on a tile north of the excavator, through a gate in the Heavy
  Yard's east wall at the cross lane. 40 diamonds when it opens; the main chain ends with "Open the Dump Yard".
- **Excavator** in the Heavy Yard (the old garbage-truck bay): turns on its tracks, reaches with a two-link arm (IK),
  scoops loose scrap within 8.5 m and drops it on the dump truck in the bay.
- **Two dump trucks** loop: back into the bay, load, drive through the gate, tip the body and pour the load on the
  dump pile, loop round and back in; one waits at the holding point while the other has the bay. Beacons, horn,
  turning wheels, the body pitches on starts and stops.
- **Dump Yard Crane** feeds the pile into the **Heavy Crusher** (1.4x the Crusher with twin drums and a caged
  top; 3–4 pieces a cycle, hopper 60–130; upgrades in the panel under DUMP YARD). Its belt runs south through a gap in
  the plant's north wall straight into the Metal Splitter.
- Seen in play: trucks alternate Loading → Driving → Tipping → Holding, the excavator cycles, the crane fed the
  crusher and Raw Metal rode the belt into the Splitter.
- **Furnace levels unchanged.** The heavy line is the supply that keeps all four furnaces busy at their higher levels;
  raising what a furnace level eats waits for the full-session run (late prices are still unmeasured).
- Also: the port's last catalog entry (`ship_dock`) removed.

Rules from the brief: every video stays a choice (nothing is blocked behind one), balance still holds with every video
ignored, and workers stay slower than the player at anything the player can do.

## Tools
- `AgentScripts/Tools/*.sh`: play-test helpers (focus-safe wait, captures, teleport, state dump).
- `AgentScripts/Tools/ContactSheet.cs`: tiles N frames of game time into one PNG to judge motion and feedback.
- `AgentScripts/Tools/GuideBot.cs`: plays the main chain by following the guide and writes a pacing report.
- `AgentScripts/Tools/Cheat.cs`: level / cash / buy / give / teleport shortcuts for testing later content.
