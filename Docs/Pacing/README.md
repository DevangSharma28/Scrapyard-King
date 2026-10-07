# Pacing runs (guide bot)

Raw reports from `AgentScripts/Tools/GuideBot.cs` (a bot that plays by following the guide arrow at x2).

## Revamp 7 runs (2026-10-06, economy pass)

Clean save, x2, **Editor at 60 fps or more** (the first runs whose times can be read as real play).

| Report | Length | What it showed |
|---|---|---|
| `r7_run1.md` | 44 min | Porter 2:25, plant 8:22, Iron Furnace 17:13, Smelter 18:59, then 17 minutes on "Smelt 12 iron ingots" (the player hauled beside the Smelter, nobody cut scrap) |
| `r7_run2.md` | 44 min | Furnace stall gone ("Smelt 12" 49 s after the Smelter), but "Split 15 raw metal" took from 8:09 to 16:15 (Helper drained the Raw Metal) and pushed the furnace to 24:40 |
| `r7_run3.md` | ended at about 18 min by the owner | no stall: Porter 2:14, plant 8:38, "Split 15" 9:22, Hauler 10:01, Runner 13:13, Iron Furnace 17:46 |

No run has gone past the first furnace line without a stall or a stop: the second half of the session is unmeasured.
Summary and the fixes: `Docs/QUALITY_PLAN.md`, "R7 result".

## Revamp 2 run (2026-10-06, furnace battery)

`r2_run1.md`: clean save, x2, 34 game minutes, **Editor throttled to 10 fps** (times are slower than real play; the
order of events is what it shows). Scrap Porter 2:50, Back Lot 4:51, Claw Crane 5:48, Recycling Plant 8:01, Market
Runner 14:18, Iron Furnace 17:49, then ten minutes on "Smelt 12 iron ingots". The task order was changed after it
(Smelter first) and not run again. Summary: `Docs/QUALITY_PLAN.md`, "R2 result".

## Revamp 1 runs (2026-10-06, speed pass + Metal Splitter)

`r1_run1.md` … `r1_run4.md`: each from an empty save at x2, 14–18 game minutes. Summary and what each run changed:
`Docs/QUALITY_PLAN.md`, "R1 result". Run 4 is the reference: Scrap Porter 2:58, Delivery Helper 4:40, Back Lot 6:31,
Recycling Plant 9:44, Metal Hauler 13:43 (run 5 below had 7:48, 12:23, 18:01, 31:40 and 37:04).

**These numbers are older than the game.** After run 4 the economy was sped up again (carry 14, faster Crusher, Raw
Metal $8, cheaper gates, instant customers, more scrap spawn points, the Claw Crane) at the owner's request, without a
new run. Every beat should come earlier now; nobody has measured by how much.

Read them with two cautions. Run 2 ran at 10 fps and is about 1.7x slower than the others for that reason alone: keep
the Editor in front for a pacing run (`unity command editor_focus` once after entering Play mode, then check
`1 / Time.unscaledDeltaTime`). And nothing after the Metal Hauler has been measured with the new numbers: the Furnace
half of the session still has the run-5 costs and will be rebuilt in the next revamp steps.

## Run 5 (2026-10-05, M8 balance pass)

One session, stopped and resumed from its save file each time the bot hit a dead end. `run5.md` is the first 56 game
minutes; `run5_part2.md` … `run5_part8.md` continue it (each file's clock restarts at 0). The session ended at about
200 game minutes on task `t49_ship_ingots` (15/72), before the Dockyard.

| Beat | Session time | Clean? |
|---|---|---|
| Scrap Porter / Delivery Helper | 7:48 / 12:23 | yes |
| Back Lot | 18:01 | yes |
| Recycling Plant | 31:40 | yes |
| Metal Hauler | 37:04 | yes |
| Furnace | 46:16 | yes |
| Heavy Scrap Yard | ~94 | no: about 35 minutes lost to the two market deadlocks and the starved-yard guide bug |
| Crusher Operator, first Giant Truck | ~115 / ~135 | no: the bot could not path to the giant for 18 minutes |
| Yard Seller, Truck Dock | ~144 / ~165 | no: cash-collection trips halved income |
| Loader, Furnace Operator | ~187 / ~193 | mostly (about $850 per minute in this stretch) |
| Second giant, dock Lv.2 | ~197 | yes |

What the run found (all fixed in code, see `Docs/ARCHITECTURE.md` §3 and `CLAUDE.md`):
a scrap piece buried in a mound, cash leaking into hire tiles on walk-over, two customer deadlocks at a full counter,
conveyor tops walkable in the NavMesh, a guide that restocked counters instead of cutting scrap, lost the task's scrap
in another area, carried leftover scrap around and fetched cash every $150, no task for the Market Runner, and the
carry-stack sway spring reaching NaN on long frames (2,000 console errors).

**Not done:** a clean full run with all fixes in. The first 46 minutes are trustworthy; everything after is an upper
bound. Run one before trusting late-game costs: `wipesave`, Play, `GuideBot.Start` with `["<report> 150 2"]`.
