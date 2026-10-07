using System.Linq;
using System.Text;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Items;
using ScrapYardKing.Player;
using ScrapYardKing.Progression;
using UnityEditor;
using UnityEngine;

// Play-mode cheats for scripted tests. One call, several commands separated by ';':
//   unity command run_script --file AgentScripts/Tools/Cheat.cs --entry Cheat.Run --args '["level 9; cash 50000; buy recycling_plant"]'
// Commands: level N | cash N | buy ID [times] | give ITEM_ID N | tp X Z | timescale S | save | wipe | saveinfo
// "save" writes the save file now; "wipe" deletes it and stops saving for this session; "saveinfo" prints its path and state.
// "buy" goes through UpgradeManager.TryPurchase, so it pays like the player would (give cash first).
public static class Cheat
{
    public static string Run(string args)
    {
        if (!EditorApplication.isPlaying) return "not playing";
        var log = new StringBuilder();
        foreach (var raw in (args ?? "").Split(';'))
        {
            var p = raw.Trim().Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 0) continue;
            switch (p[0])
            {
                case "level":
                {
                    Services.TryGet(out ProgressionManager pm);
                    int target = int.Parse(p[1]);
                    for (int guard = 0; pm.Level < target && guard < 100; guard++) pm.AddXp(pm.XpToNext - pm.Xp);
                    log.Append($"level={pm.Level} ");
                    break;
                }
                case "cash":
                {
                    Services.TryGet(out EconomyManager eco);
                    eco.AddCash(long.Parse(p[1]));
                    log.Append($"cash={eco.Cash} ");
                    break;
                }
                case "buy":
                {
                    Services.TryGet(out UpgradeManager um);
                    int times = p.Length > 2 ? int.Parse(p[2]) : 1;
                    if (!um.TryGet(p[1], out var u))
                    {
                        log.Append($"no upgrade {p[1]} ");
                        break;
                    }

                    for (int i = 0; i < times; i++)
                        if (!um.TryPurchase(u))
                            break;
                    log.Append($"{p[1]}=Lv{u.Level} ");
                    break;
                }
                case "give":
                {
                    Services.TryGet(out PlayerCharacter pc);
                    Services.TryGet(out ItemPool pool);
                    var def = AssetDatabase.FindAssets("t:ItemDefinition").Select(g => AssetDatabase.LoadAssetAtPath<ItemDefinition>(AssetDatabase.GUIDToAssetPath(g)))
                        .FirstOrDefault(d => d.Id == p[1]);
                    if (def == null)
                    {
                        log.Append($"no item {p[1]} ");
                        break;
                    }

                    int n = int.Parse(p[2]);
                    int added = 0;
                    for (int i = 0; i < n && !pc.CarryStack.IsFull; i++)
                    {
                        var item = pool.Get(def, pc.transform.position + Vector3.up, Quaternion.identity);
                        if (pc.CarryStack.TryAdd(item, 0.2f, 0.5f)) added++;
                    }

                    log.Append($"gave {added} {p[1]} ");
                    break;
                }
                case "tp":
                {
                    Services.TryGet(out PlayerCharacter pc);
                    pc.Controller.Teleport(new Vector3(float.Parse(p[1]), 0f, float.Parse(p[2])), Quaternion.identity);
                    log.Append("tp ");
                    break;
                }
                case "save":
                {
                    Services.TryGet(out ScrapYardKing.Persistence.SaveManager sm);
                    log.Append(sm != null && sm.Save() ? "saved " : "not saved ");
                    break;
                }
                case "wipe":
                {
                    Services.TryGet(out ScrapYardKing.Persistence.SaveManager sm);
                    if (sm != null) sm.Wipe();
                    log.Append("wiped ");
                    break;
                }
                case "saveinfo":
                {
                    Services.TryGet(out ScrapYardKing.Persistence.SaveManager sm);
                    log.Append(sm == null ? "no SaveManager " : $"path={sm.FilePath} load={sm.LoadResult} exists={System.IO.File.Exists(sm.FilePath)} play={sm.PlaySeconds:0}s ");
                    break;
                }
                case "timescale":
                    Time.timeScale = float.Parse(p[1]);
                    log.Append($"ts={Time.timeScale} ");
                    break;
                default:
                    log.Append($"?{p[0]} ");
                    break;
            }
        }

        return log.ToString();
    }
}
