# Scrap Yard King — Quality Plan (post-M5)

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

## Tools
- `AgentScripts/Tools/*.sh`: play-test helpers (focus-safe wait, captures, teleport, state dump).
- `AgentScripts/Tools/ContactSheet.cs`: tiles N frames of game time into one PNG to judge motion and feedback.
- `AgentScripts/Tools/GuideBot.cs`: plays the main chain by following the guide and writes a pacing report.
- `AgentScripts/Tools/Cheat.cs`: level / cash / buy / give / teleport shortcuts for testing later content.
