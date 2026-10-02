#!/bin/bash
# Usage: state.sh — one-line game state (time, task, stack, cash, storages, desks).
source "$(dirname "$0")/_env.sh"
ueval '
ScrapYardKing.Core.Services.TryGet(out ScrapYardKing.Progression.TaskManager tm);
ScrapYardKing.Core.Services.TryGet(out ScrapYardKing.Player.PlayerCharacter pc);
ScrapYardKing.Core.Services.TryGet(out ScrapYardKing.Economy.EconomyManager eco);
ScrapYardKing.Core.Services.TryGet(out ScrapYardKing.Progression.ProgressionManager pm);
var sb=new System.Text.StringBuilder("t="+Time.time.ToString("0.0")+" lv="+pm.Level+" task="+(tm.Main!=null?tm.Main.Definition.Id+" "+tm.Main.Progress+"/"+tm.Main.Definition.Amount:"-")+" stack="+pc.CarryStack.Count+"/"+pc.CarryStack.Capacity+" cash="+eco.Cash);
foreach(var s in UnityEngine.Object.FindObjectsByType<ScrapYardKing.Factory.Storage>()) sb.Append(" "+s.StationId+"="+s.Count);
foreach(var d in UnityEngine.Object.FindObjectsByType<ScrapYardKing.Factory.SellDesk>()) sb.Append(" "+d.StationId+"="+d.Stock+"/$"+d.CashPile.StoredCash);
return sb.ToString();'
