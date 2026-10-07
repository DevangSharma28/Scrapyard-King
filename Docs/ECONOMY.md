# Scrap Yard King — Economy model (Revamp 7)

One page for the whole chain: what a piece of scrap is worth at each stage, what each stage can push through, what
there is to spend on, and when each beat should come. Numbers are read from the data assets on 2026-10-06; the builder
that owns each one is named in `Docs/ARCHITECTURE.md` §8. Where this page says "estimate", nothing has measured it.

## 1. What one piece of scrap is worth

Splitter mix: iron 40%, aluminum 25%, copper 20%, steel 15%.

| Stage | Product (iron / aluminum / copper / steel) | Per piece, on average | Step |
|---|---|---|---|
| Crusher | Raw Metal $8 | $8.00 | |
| Splitter | $10 / $16 / $24 / $36 | $18.20 | x2.3 |
| Furnaces | ingots $26 / $40 / $60 / $90 | $45.90 | x2.5 |
| Press | bars $40 / $60 / $90 / $135 | $69.25 | x1.5 |
| Truck (Lv.1 x1.4, order bonus about x1.08) | | about $105 | x1.5 |
| Ship (Lv.1 x1.9, same orders) | | about $142 | x1.35 |

Every stage pays at least a third more than the one before it, so each build is worth its price as soon as it runs.
Counters add their own multiplier (Sell Desk x1.0–1.6, Metal Market x1.0–1.35).

## 2. What each stage can move (pieces per minute)

| Stage | Lv.1 | Top level | Note |
|---|---|---|---|
| Crusher | 171 | 375 | |
| Splitter | 100 | 207 | one full bin stops it |
| Iron / Aluminum / Copper / Steel Furnace | 67 / 60 / 55 / 40 | 140 / 122 / 111 / 82 | each gets its share of the split: 40 / 25 / 20 / 15 per 100 |
| Press | 75 | 154 | below the Splitter at equal levels: the Press is the factory's bottleneck by design |
| Truck | about 38 bars (16 a load, about 25 s a round) | about 90 (48 a load) | the narrowest point after the Press: upgrade the dock or buy the port |
| Ship | about 100 bars (60 a load, about 35 s a round) | about 190 | |

Scrap supply is the other limit: one player with a porter and the yard crane brings about 60–90 pieces a minute; the
Heavy Yard with its crane roughly doubles that (estimate).

## 3. What there is to buy

| Group | Total |
|---|---|
| Areas and builds (Back Lot, plant, four furnaces, Press, Truck Dock, Heavy Yard, Dockyard) | $70,200 |
| Workers (11 kinds, 16 hires) | $52,450 |
| Cranes (yard and Heavy Yard, three levels each) | $34,000 |
| Machine levels (Crusher, Splitter, four furnaces, Press) | $156,950 |
| Storage levels (yard, bins, ingot rack, bars) | $30,600 |
| Counters (Sell Desk, Metal Market) | $11,150 |
| Docks (truck to Lv.4, ship to Lv.3) | $118,000 |
| Player (cutter, backpack, boots) | $23,540 |
| **Everything** | **about $497,000** |

The main task chain itself asks for about $130,000 of that; the rest is the player's choice of bottleneck.

## 4. The arc (targets from the brief, with the model's estimate)

| Session time | Beat | Price / gate | Income by then (estimate) |
|---|---|---|---|
| 0–2 min | first sale | | $300–500 / min |
| 2–5 | first worker | Porter $300, Helper $450 | |
| 5–10 | the Splitter | Back Lot $700, Claw Crane $600, plant $1,000 (Lv 5) | $500–700 / min |
| 10–18 | carriers, first furnace | Hauler $600, Runner $1,100, Iron Furnace $2,000 (Lv 6), Smelter $1,500 | $1,200 / min |
| 18–25 | second and third furnace | Copper $3,500 (Lv 7), Aluminum $6,000 (Lv 8) | $2,000–2,500 / min |
| 25–35 | all four lines | Steel $10,000 (Lv 9) | $2,500–3,000 / min |
| 35–45 | Press | Press $9,000, Loader $3,000 | $3,000 / min |
| 45–55 | truck orders | Truck Dock $8,000 (Lv 10) | $4,000–5,000 / min |
| 55–70 | Heavy Scrap Yard | Heavy Yard $10,000, its crane $5,000 | |
| 70–80 | specialists, giants | operators $3,500–6,000, sellers $5,000–7,500, dock Lv.2 $5,000 | $5,000+ / min |
| 80–90 | the port | Dockyard $20,000 (Lv 11), then the ship | |

Yard levels come from sales (0.08 XP per $ sold, plus task rewards). With the curve at 60 XP x 1.55 per level, Lv 9
needs about $44,000 sold, Lv 10 about $69,000 and Lv 11 about $108,000: by the income column that is roughly minute
32, 45 and 60, so the level gates sit just ahead of the prices instead of far behind them (at x1.45 the R2 run reached
Lv 10 at minute 24 and no gate held anything).

Measured at 60 fps (three R7 runs, see `Docs/Pacing/`): Porter 2:14–2:37, Back Lot 4:26–5:53, plant 8:09–8:38,
Iron Furnace 17:13–17:46 in the two runs without an early stall, Smelter 1:46–2:06 after the furnace. Those four rows
of the table hold. Nothing after the first furnace has been measured: every figure from "18–25" down is the model's,
and no late price should move before one clean full run.

## 5. Diamonds (premium currency, U1)

Diamonds buy **time and convenience, never power**: anything a diamond does, playing does too. All numbers are in
`Data/Economy/PremiumEconomy.asset` (`PremiumEconomyConfig`).

- **Time.** Skipping a wait of *m* minutes costs `ceil(3 × m^0.85)` diamonds, at least 2, at most 400: 1 min = 3,
  10 min = 22, 1 h = 98. Long skips are cheaper per minute.
- **Cash.** A premium action that creates cash (a special order) costs `cash / CashPerDiamond(level)` diamonds, where
  `CashPerDiamond` is $60 × 1.3 per level up to Lv 12 and × 1.08 per level after it (Lv 3: 1 diamond ≈ $101; Lv 10:
  ≈ $636; Lv 20: ≈ $1,990). That keeps a diamond's cash at about 0.2 minutes of income at every level. With × 1.3 all
  the way, a Lv 25 diamond bought 2.7 minutes of income and cash crates were worth eight boosts (found by the U6
  simulation).
- **Confirmation.** Spends above 20 diamonds ask first and show what they save; smaller ones happen on the tap.
- **Free sources (first session).** A gift per new part of the yard: Recycling Plant 25 (the first diamond moment,
  about minute 9, with the line "DIAMONDS SPEED THINGS UP"), Furnace hall 15, Press 15, Truck Dock 20, Heavy Yard 25,
  Dockyard 50; 5 diamonds on levels 6, 8, 10, 12...; two late tasks (35). About 200 in a full first session: enough
  for a handful of meaningful skips, not enough to skip the game.
- Daily rewards, missions, achievements, rewarded video and IAP add sources in later UI steps (U2–U4); each will be
  listed here with its rate so the 1-day / 7-day / 30-day totals can be checked (U6).

### Shop (U2)

| Pack | Price | Diamonds | Per $ | Note |
|---|---|---|---|---|
| Starter pack | $1.99 | 500 + cash (150 diamonds' worth) + 2X FACTORY 30 min | 251+ | once per account |
| Handful | $0.99 | 100 | 101 | |
| Pouch | $4.99 | 500 + 50 | 110 | |
| Bag | $9.99 | 1,200 + 300 | 150 | POPULAR |
| Sack | $19.99 | 2,500 + 750 | 163 | |
| Chest | $49.99 | 6,500 + 2,500 | 180 | |
| Vault | $99.99 | 15,000 + 7,000 | 220 | BEST VALUE |
| Factory Boost Pack | $4.99 | 300 + 2X FACTORY and 2X CASH 30 min | | |

Rule: diamonds per dollar never fall as the pack grows (checked by `ShopCatalogTests`). Diamond items: a boost costs
`SkipCost(minutes × (multiplier − 1))` (the time it gains you), a cash crate its diamonds' cash value (+10% / +25% for
the bigger crates). Free: 5 diamonds per video, every 4 h, at most 3 a day (≤ 15 a day).

### Rewarded video (U3)

| Offer | Where | Reward | Cooldown | Per day |
|---|---|---|---|---|
| 2X CASH / 2X FACTORY | HUD button | boost 120 / 90 s | 10 min | 6 |
| FAST BOOTS | HUD button | boost 120 s | 10 min | 4 |
| SCRAP RUSH | HUD button (first pick) | boost 90 s | 15 min | 6 |
| TRUCK RUSH | HUD button | boost 120 s | 15 min | 4 |
| FREE CASH | HUD button | $250 × yard level | 10 min | 8 |
| 2X ORDER PAY | ORDER COMPLETE card | the order's pay again | 3 min | 20 |
| FINISH ORDER | under the order card, once loading is 25–75% done | the rest of the order, paid | 10 min | 5 |
| 2X AWAY CASH | WELCOME BACK card | the offline cash again | once per return | 6 |
| FREE DIAMONDS | shop | 5 diamonds | 4 h | 3 |

The HUD shows one offer at a time with 150 s of quiet between offers (70 s gave about 28 offers per hour of play).
Time away pays 30% of the measured income for at most 2 hours (it was 4: one return paid five 15-minute sessions). None is needed: the arc in section 4 assumes
every offer is ignored. FINISH ORDER for diamonds costs the missing order's cash value in diamonds.

### Missions and daily rewards (U4)

- Dailies (three a day): cash rewards are `cashPerLevel × level` ($120–200 a level), diamond ones 3 (5 for truck
  orders): about 9 diamonds a day.
- Calendar: $200 × level, 10 diamonds, 2X CASH 10 min, $400 × level, 15 diamonds, 2X FACTORY 15 min, $600 × level +
  50 diamonds. 75 diamonds a week.
- Achievements: 5–40 diamonds a tier (e.g. BREAK SCRAP 100 / 500 / 2,000 / 8,000 / 25,000 for 5 / 10 / 15 / 25 / 40).
  First tiers start later than in U4 (they paid 195 diamonds in the first hour of play).
- All free sources together, for a player who does everything: about 35 diamonds a day, about 1,000 a month plus
  achievements. A one-hour skip costs 98: free diamonds buy a few short skips a day, never the game.

### Simulated players (U6)

`EconomySimulator` (Editor code, `Scripts/Editor/Economy`) plays four players forward for 30 days on the real data:
every price, reward, cooldown and cap comes from the assets; only the game's shape (income along the arc in section 4,
when each area opens, an order every 2.5 minutes) is the model's. It runs without randomness. "Progress" is
income-equivalent time. Only sales (played time and boosts) give XP, so levels and areas follow play, not gifts.
Boosts count at 100% (2X CASH), 60% (2X FACTORY), 25% (FAST BOOTS) or 30% (the rest) of their extra speed.

| Profile | Plays | Videos watched | Pays |
|---|---|---|---|
| Free | 2 × 15 min a day | 30% of offers | nothing |
| Light | 2 × 20 min | 50% | starter pack on day 2 |
| Mid | 3 × 20 min | 50% | starter pack, Bag ($9.99) on day 3 and weekly; spare diamonds buy crates |
| High | 4 × 25 min | 20% | starter, Chest, Factory Pack on day 1, Vault weekly; spare diamonds buy crates |
| AllVideos | 3 × 20 min | every one | nothing |

| Profile | At | Played | Progress | Lv | ◆ earned | ◆ spent | ◆ left | Max held | from video | from IAP | Boosts / crates | Videos | RV cash | Progress from play / away / ◆ / video / IAP | $ |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Free | 1 h | 60 min | 4.7 h (x4.73) | 13 | 336 | 100 | 236 | 236 | 5 | 0 | 5 / 0 | 10 / 36 | $168K | 21% / 38% / 12% / 28% / 0% | 0 |
| Free | 7 d | 3.5 h | 19 h (x5.42) | 18 | 508 | 439 | 69 | 254 | 20 | 0 | 22 / 0 | 45 / 153 | $1.6M | 18% / 41% / 13% / 25% / 0% | 0 |
| Free | 30 d | 15 h | 78.6 h (x5.24) | 22 | 1024 | 1016 | 8 | 254 | 95 | 0 | 53 / 0 | 207 / 690 | $12.4M | 19% / 45% / 7% / 27% / 0% | 0 |
| Light | 1 h | 60 min | 4.5 h (x4.51) | 14 | 853 | 83 | 770 | 794 | 5 | 500 | 4 / 0 | 19 / 38 | $232K | 22% / 27% / 12% / 20% / 18% | 1.99 |
| Light | 7 d | 4.7 h | 19.7 h (x4.23) | 19 | 1110 | 547 | 563 | 794 | 60 | 500 | 26 / 0 | 107 / 214 | $379K | 24% / 40% / 19% / 12% / 4% | 1.99 |
| Light | 30 d | 20 h | 78.2 h (x3.91) | 23 | 1790 | 1782 | 8 | 794 | 290 | 500 | 86 / 0 | 475 / 950 | $1.2M | 26% / 45% / 15% / 11% / 1% | 1.99 |
| Mid | 1 h | 60 min | 6.9 h (x6.93) | 15 | 838 | 727 | 111 | 558 | 5 | 500 | 6 / 9 | 22 / 44 | $206K | 14% / 17% / 42% / 14% / 12% | 1.99 |
| Mid | 7 d | 7 h | 34.9 h (x4.99) | 20 | 2715 | 2611 | 104 | 1614 | 95 | 2000 | 28 / 22 | 166 / 332 | $463K | 20% / 34% / 33% / 9% / 2% | 11.98 |
| Mid | 30 d | 30 h | 138.1 h (x4.60) | 24 | 9510 | 9408 | 102 | 1655 | 440 | 8000 | 79 / 68 | 718 / 1436 | $1.7M | 22% / 39% / 29% / 9% / 1% | 51.94 |
| High | 1 h | 60 min | 46.1 h (x46.06) | 15 | 10168 | 9953 | 215 | 10059 | 0 | 9800 | 4 / 67 | 9 / 48 | $478K | 2% / 3% / 89% / 3% / 3% | 56.97 |
| High | 7 d | 11.7 h | 90.4 h (x7.75) | 21 | 10420 | 10216 | 204 | 10059 | 0 | 9800 | 18 / 67 | 111 / 559 | $10.1M | 13% / 18% / 47% / 20% / 2% | 56.97 |
| High | 30 d | 50 h | 620 h (x12.40) | 24 | 98910 | 98706 | 204 | 22210 | 0 | 97800 | 53 / 659 | 479 / 2399 | $50.9M | 8% / 12% / 67% / 13% / 0% | 456.93 |
| AllVideos | 1 h | 60 min | 4.5 h (x4.50) | 14 | 348 | 83 | 265 | 265 | 15 | 0 | 4 / 0 | 34 / 34 | $321K | 22% / 27% / 12% / 38% / 0% | 0 |
| AllVideos | 7 d | 7 h | 40.3 h (x5.76) | 20 | 725 | 720 | 5 | 285 | 105 | 0 | 35 / 0 | 268 / 268 | $7.5M | 17% / 30% / 12% / 40% / 0% | 0 |
| AllVideos | 30 d | 30 h | 165.2 h (x5.51) | 24 | 1560 | 1560 | 0 | 285 | 450 | 0 | 85 / 0 | 1165 / 1165 | $41M | 18% / 32% / 5% / 43% / 0% | 0 |

Free diamonds by source after 30 days: calendar 310, achievements 285, area gifts 150, dailies 104, free videos 95,
level gifts 45, tasks 35.

What the table says:
- **Free players see the whole game.** The first-session arc (to the dockyard) is done on day 1.5 without paying.
- **Free diamonds are a treat, not a pile.** 336 in the first hour of play (the first-session gifts), about 22 a day
  after the first week, never more than about 250 held. They give 7–13% of progress.
- **Videos help, never carry.** For a 30% watcher they give about a quarter of progress, mostly the 2X on WELCOME
  BACK and on orders. For a player who watches everything they give 43%, about 39 videos per hour of play. Every
  video is optional and the daily caps hold.
- **Paying helps in order.** The share of progress from diamonds and purchases rises Free < Light < Mid < High. A big
  spender's diamonds turn into far more cash (crates) than the level gates let them spend in the first days (89% of
  progress in the first hour). That is the known edge of a cash-for-diamonds shop; a daily crate limit would be the
  lever if it matters.
- **Playing matters.** Play is 18–26% of progress for every non-paying profile, time away 30–45% (it was 54–73% with
  the 4-hour cap).

`EconomySimulationTests` checks these as guardrails: free diamonds (≤ 400 in the first hour of play, 600–1,600 in 30
days, 10–50 a day after week one, ≤ 500 held), diamond share ≤ 20%, video share ≤ 35% (30% watcher) and ≤ 50%
(watches everything), ≤ 24 HUD offers per hour of play, play ≥ 15% of progress, spender order, 0.1–0.4 minutes of
income per diamond at every level, the biggest bundle at most 2.5 times the smallest per dollar.
The same table is in **Window → Scrap Yard King → Economy Window** (Profiles tab), next to the live economy of a
running game (Live) and today's prices with time-to-afford (Prices).

## 6. Rules the numbers follow

- A new stage multiplies the value of a piece by 1.35 or more; a build costs between two and five minutes of the
  income of the stage before it.
- The first level of the next stage is slower than the last one (Splitter 100 → Press 75 → truck 38): there is always
  one visible bottleneck, and its fix is an upgrade card or the next build.
- Nothing the main chain asks for should take more than about five minutes of saving at the income of its stage.
- Boosts and offers are extra: the table above assumes none are taken.
