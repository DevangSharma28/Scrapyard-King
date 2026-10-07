# Guide bot pacing report

Status: play mode ended at -0.1 game minutes (x2). Final: level 9, cash 300, task t30_iron_ingots.

## Stalls
- 29.3 min: no task progress for 6 min on t20_plant (guide target (20.9, 0.0, 27.0))
- 52.9 min: no task progress for 6 min on t30_iron_ingots (guide target (20.9, 0.0, 27.0))

## Timeline

| Time | Event | Cash |
|---|---|---|
| 00:00 | start: task t01_cut_car, cash 0, x2 | 0 |
| 00:06 | task done: t01_cut_car "Cut the car" | 0 |
| 00:09 | task done: t02_collect "Collect 6 scrap" | 0 |
| 00:20 | yard level 2 | 50 |
| 00:20 | task done: t03_crush "Crush 8 scrap" | 50 |
| 00:26 | task done: t04_stock "Stock the sell desk" | 50 |
| 00:34 | task done: t05_first_sale "Serve your first customer" | 70 |
| 00:40 | shopping trip | 106 |
| 00:41 | bought chainsaw → Lv.2 | 46 |
| 00:42 | shopping done | 46 |
| 00:45 | task done: t06_cash "Collect $40" | 50 |
| 01:14 | yard level 3 | 145 |
| 01:18 | bought chainsaw → Lv.3 | 55 |
| 01:18 | task done: t07_chainsaw "Upgrade the chainsaw" | 55 |
| 01:54 | task done: t08_crush30 "Crush 30 scrap" | 105 |
| 01:59 | task done: t08b_boost "Boost the crusher" | 145 |
| 02:00 | shopping trip | 145 |
| 02:00 | bought backpack → Lv.2 | 65 |
| 02:01 | shopping done | 65 |
| 02:30 | yard level 4 | 165 |
| 02:34 | bought crusher → Lv.2 | 15 |
| 02:34 | task done: t09_crusher2 "Crusher to Lv.2" | 15 |
| 05:35 | task done: t10_sell40 "Sell 40 metal" | 95 |
| 05:42 | bought sell_desk → Lv.2 | 80 |
| 05:42 | task done: t11_desk2 "Sell desk to Lv.2" | 80 |
| 05:43 | yard level 5 | 205 |
| 05:43 | task done: t12_backpack "Upgrade the backpack" | 205 |
| 05:43 | bought boots → Lv.2 | 85 |
| 07:36 | side task done: s01_barrels | 413 |
| 07:48 | task done: t13_porter "Hire a scrap porter" | 4 |
| 07:48 | bought porter → 1/2 | 4 |
| 11:48 | side task done: s02_tires | 510 |
| 12:14 | yard level 6 | 740 |
| 12:23 | task done: t14_helper "Hire a delivery helper" | 73 |
| 12:23 | bought helper → 1/2 | 73 |
| 13:04 | side task done: s06_serve | 133 |
| 14:55 | task done: t15_serve20 "Serve 20 customers" | 253 |
| 14:56 | task done: t16_level5 "Reach yard Lv.5" | 403 |
| 18:01 | GATE OPEN: back_lot | 30 |
| 18:01 | bought back_lot → OPEN | 30 |
| 18:01 | task done: t17_back_lot "Open the Back Lot" | 30 |
| 18:12 | yard level 7 | 355 |
| 18:12 | task done: t18_fridges "Cut 2 fridges" | 355 |
| 18:40 | bought backpack → Lv.3 | 175 |
| 20:35 | bought sell_desk → Lv.3 | 28 |
| 20:35 | task done: t18b_desk3 "Sell desk to Lv.3" | 28 |
| 23:12 | task done: t19_crush150 "Crush 150 scrap" | 228 |
| 23:18 | bought crusher → Lv.3 | 698 |
| 23:18 | task done: t19b_crusher3 "Crusher to Lv.3" | 698 |
| 23:19 | bought storage_yard → Lv.2 | 398 |
| 23:19 | bought chainsaw → Lv.4 | 98 |
| 28:21 | bought boots → Lv.3 | 1858 |
| 28:56 | yard level 8 | 2265 |
| 29:18 | STALL on t20_plant | 2395 |
| 29:23 | bought backpack → Lv.4 | 2045 |
| 31:40 | GATE OPEN: recycling_plant | 2 |
| 31:40 | bought recycling_plant → OPEN | 2 |
| 31:40 | task done: t20_plant "Open the Recycling Plant" | 2 |
| 32:42 | task done: t21_sort "Sort 15 metal bales" | 122 |
| 34:32 | task done: t22_copper "Sell 6 copper" | 322 |
| 36:51 | task done: t23_market "Serve 8 market customers" | 294 |
| 37:04 | task done: t24_hauler "Hire a metal hauler" | 255 |
| 37:04 | bought hauler → 1/2 | 255 |
| 37:09 | task done: t25_boost_sorter "Boost the sorter" | 315 |
| 37:11 | task done: t26_level8 "Reach yard Lv.8" | 615 |
| 37:20 | shopping trip | 615 |
| 37:22 | bought metal_bins → Lv.2 | 115 |
| 37:23 | shopping done | 115 |
| 41:22 | task done: t27_sort100 "Sort 100 metal bales" | 615 |
| 42:33 | yard level 9 | 3259 |
| 46:16 | GATE OPEN: furnace_hall | 0 |
| 46:16 | bought furnace_hall → OPEN | 0 |
| 46:16 | task done: t28_furnace "Build the Furnace" | 0 |
| 46:53 | task done: t29_smelt "Smelt 12 ingots" | 300 |
| 52:53 | STALL on t30_iron_ingots | 300 |

## Cash and level every 30 s

0.0m $0 L1, 0.5m $50 L2, 1.0m $70 L2, 1.5m $55 L3, 2.0m $145 L3, 2.5m $65 L3, 3.0m $15 L4, 3.5m $15 L4, 4.0m $15 L4, 4.5m $15 L4, 5.0m $15 L4, 5.5m $15 L4, 6.0m $85 L5, 6.5m $203 L5, 7.0m $255 L5, 7.5m $373 L5, 8.0m $4 L5, 8.5m $4 L5, 9.0m $108 L5, 9.5m $162 L5, 10.0m $249 L5, 10.5m $363 L5, 11.0m $424 L5, 11.5m $470 L5, 12.0m $570 L5, 12.5m $73 L6, 13.0m $73 L6, 13.5m $133 L6, 14.0m $133 L6, 14.5m $133 L6, 15.0m $403 L6, 15.5m $850 L6, 16.0m $981 L6, 16.5m $1061 L6, 17.0m $1214 L6, 17.5m $1323 L6, 18.0m $289 L6, 18.5m $355 L7, 19.0m $256 L7, 19.5m $358 L7, 20.0m $473 L7, 20.5m $584 L7, 21.0m $28 L7, 21.5m $28 L7, 22.0m $28 L7, 22.5m $28 L7, 23.0m $28 L7, 23.5m $276 L7, 24.0m $519 L7, 24.5m $719 L7, 25.0m $923 L7, 25.5m $1158 L7, 26.0m $1404 L7, 26.5m $1632 L7, 27.0m $1833 L7, 27.5m $1992 L7, 28.0m $2067 L7, 28.5m $1858 L7, 29.0m $2265 L8, 29.5m $2089 L8, 30.0m $2343 L8, 30.5m $2523 L8, 31.0m $2638 L8, 31.5m $2866 L8, 32.0m $2 L8, 32.5m $2 L8, 33.0m $122 L8, 33.5m $122 L8, 34.0m $122 L8, 34.5m $122 L8, 35.0m $322 L8, 35.5m $144 L8, 36.0m $144 L8, 36.5m $144 L8, 37.0m $1455 L8, 37.5m $115 L8, 38.0m $115 L8, 38.5m $115 L8, 39.0m $115 L8, 39.5m $115 L8, 40.0m $115 L8, 40.5m $115 L8, 41.0m $115 L8, 41.5m $1423 L8, 42.0m $2813 L8, 42.5m $3034 L8, 43.0m $3589 L9, 43.5m $3729 L9, 44.0m $4118 L9, 44.5m $4322 L9, 45.0m $4526 L9, 45.5m $4613 L9, 46.0m $4803 L9, 46.5m $0 L9, 47.0m $300 L9, 47.5m $300 L9, 48.0m $300 L9, 48.5m $300 L9, 49.0m $300 L9, 49.5m $300 L9, 50.0m $300 L9, 50.5m $300 L9, 51.0m $300 L9, 51.5m $300 L9, 52.0m $300 L9, 52.5m $300 L9, 53.0m $300 L9, 53.5m $300 L9, 54.0m $300 L9, 54.5m $300 L9, 55.0m $300 L9, 55.5m $300 L9, 56.0m $300 L9, 56.5m $300 L9, 57.0m $300 L9, 57.5m $300 L9, 58.0m $300 L9, 58.5m $300 L9
