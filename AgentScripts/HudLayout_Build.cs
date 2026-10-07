using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// HUD / screen UI layout pass (2026-10-07), from the audit in AgentScripts/Tools/UiAudit.cs over every screen at three
/// phone shapes (16:9, 20:9 with a notch, 3:4 tablet):
///  - the canvas scales with Expand instead of a 0.5 width/height match: on 20:9 phones the match shrank the canvas
///    to 966 units wide (pills ran into the level badge, panels lost their close button off the right edge), on a
///    tablet panels ran off the bottom. Expand keeps the 1080 × 1920 design fully on screen at any shape;
///  - top row: the gem pill ran into the XP bar and the cash icon sat on the gem pill's "+"; the icons poked 9 units
///    off the top; the "+" buttons were 62 units (small to tap). Narrower pills, a shorter XP bar, the row 12 lower;
///  - mission rows: the diamond icon sat on the C of CLAIM;  shop bundle cards: the ribbon sat on the title;
///  - settings: the volume slider took touches only on its 46-unit track;
///  - the Giant Truck boss bar sat over the task banner: it rises from the bottom now;
///  - the upgrade sheet's bottom bleed was shorter than a gesture-bar inset; the upgrade tile's "!" covered the pad
///    north of the tile.
/// World labels and the status tag are runtime (StationLabel). Entry point: Scene. Idempotent. Run after U6_Build.
/// </summary>
public static class HudLayout_Build
{
    const string ScenePath = "Assets/_Project/Scenes/Area1_OldScrapYard.unity";
    static readonly StringBuilder Log = new();

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var canvas = scene.GetRootGameObjects().First(g => g.name == "_UI").transform.Find("Canvas");
        var hud = canvas.Find("HUD");

        // any phone shape: the reference resolution always fits
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
        EditorUtility.SetDirty(scaler);

        TopRow(hud);
        MissionRow(canvas.Find("MissionsPanel/Root/Templates/MissionRow"));
        BundleCard(canvas.Find("ShopPanel/Root/Templates/BundleCard"));
        Slider(canvas.Find("SettingsPanel/Root/Card/Scroll/Viewport/Content/Card_AUDIO/Row_VOLUME/Slider"));
        UpgradeSheet((RectTransform)hud.Find("UpgradePanel/Sheet"));
        GiantBar(hud.Find("GiantBar"));
        UpgradeAlerts();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        return Log.ToString();
    }

    /// <summary>
    /// Level badge | gem pill | cash pill | gear, left to right with gaps (canvas units, 1080 wide). Pills are pivoted
    /// on their right edge; their icon hangs 31 units out on the left.
    /// </summary>
    static void TopRow(Transform hud)
    {
        const float rowY = -58f;
        var xp = (RectTransform)hud.Find("LevelBadge/XpBar");
        xp.sizeDelta = new Vector2(170f, xp.sizeDelta.y);                     // x 140..310
        Pill((RectTransform)hud.Find("GemPill"), -474f, 240f, rowY);         // 366..606, icon from 335
        Pill((RectTransform)hud.Find("CashPill"), -130f, 300f, rowY);        // 650..950, icon from 619
        var gear = (RectTransform)hud.Find("SettingsButton");
        gear.anchoredPosition = new Vector2(gear.anchoredPosition.x, rowY);
        Log.AppendLine("top row: XP bar 170, gem pill 240, cash pill 300, row at y -58, + buttons 72");
    }

    static void Pill(RectTransform pill, float x, float width, float y)
    {
        pill.anchoredPosition = new Vector2(x, y);
        pill.sizeDelta = new Vector2(width, pill.sizeDelta.y);
        var plus = (RectTransform)pill.Find("Plus");
        plus.sizeDelta = new Vector2(72f, 72f);
        var label = (RectTransform)pill.Find("Label");
        label.offsetMin = new Vector2(96f, label.offsetMin.y);
        label.offsetMax = new Vector2(-82f, label.offsetMax.y);              // clear of the bigger +
        var text = label.GetComponent<TMP_Text>();
        text.enableAutoSizing = true;
        text.fontSizeMin = Mathf.Min(text.fontSizeMin, 26f);
        EditorUtility.SetDirty(text);
    }

    static void MissionRow(Transform row)
    {
        var claim = row.Find("Claim");
        ((RectTransform)claim.Find("RewardIcon")).anchoredPosition = new Vector2(-68f, 2f);
        var label = (RectTransform)claim.Find("Label");
        label.anchoredPosition = new Vector2(30f, 2f);
        label.sizeDelta = new Vector2(118f, 60f);
        var text = label.GetComponent<TMP_Text>();
        text.enableAutoSizing = true;
        text.fontSizeMin = 18f;
        text.fontSizeMax = Mathf.Max(text.fontSizeMax, 34f);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        EditorUtility.SetDirty(text);
        Log.AppendLine("mission row: CLAIM label beside the icon, not under it");
    }

    static void BundleCard(Transform card)
    {
        ((RectTransform)card.Find("Badge")).anchoredPosition = new Vector2(120f, 254f);   // a ribbon over the top edge
        ((RectTransform)card.Find("Title")).anchoredPosition = new Vector2(0f, 186f);
        Log.AppendLine("bundle card: ribbon above the title");
    }

    /// <summary>
    /// The sheet bled 80 units below the screen; on a gesture-bar phone the safe area lifts the HUD by about 100, which
    /// left a strip of world under the panel. It bleeds 260 now.
    /// </summary>
    static void UpgradeSheet(RectTransform sheet)
    {
        foreach (var name in new[] { "Back", "Shadow" })
        {
            var c = (RectTransform)sheet.Find(name);
            if (c != null) c.offsetMin = new Vector2(c.offsetMin.x, -260f);
        }

        Log.AppendLine("upgrade sheet: bleeds 260 below the screen");
    }

    /// <summary>
    /// The boss bar sat over the task banner (and lower down it would land on the order card and boost chips): the
    /// top of the screen is full. During a Giant Truck fight it rises from the bottom, above the SHOP / MISSIONS / DAILY
    /// bar, where nothing else lives.
    /// </summary>
    static void GiantBar(Transform bar)
    {
        var root = (RectTransform)bar.Find("Root");
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
        root.pivot = new Vector2(0.5f, 0f);
        root.anchoredPosition = new Vector2(0f, 222f);
        var so = new SerializedObject(bar.GetComponent<ScrapYardKing.UI.GiantScrapBar>());
        so.FindProperty("hiddenOffset").floatValue = -420f;
        so.ApplyModifiedPropertiesWithoutUndo();
        Log.AppendLine("giant bar: bottom of the screen, above the nav bar");
    }

    /// <summary>
    /// The upgrade tile's "!" floated 2.1 m above the tile centre, which on screen lands on whatever lies north of it
    /// (the Crusher's boost pad). It sits on the tile's top-right corner now, like a badge on an app icon.
    /// </summary>
    static void UpgradeAlerts()
    {
        foreach (var tile in Object.FindObjectsByType<ScrapYardKing.Tiles.UpgradeTile>(FindObjectsInactive.Include))
        {
            var so = new SerializedObject(tile);
            var alert = so.FindProperty("alertBadge").objectReferenceValue as Transform;
            if (alert == null) continue;
            Undo.RecordObject(alert, "alert");
            alert.localPosition = new Vector3(1.15f, 0.9f, 0.95f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(alert);
        }

        Log.AppendLine("upgrade tiles: \"!\" on the tile's corner");
    }

    /// <summary>The whole 80-unit row height takes the drag; the track keeps its 46-unit look.</summary>
    static void Slider(Transform slider)
    {
        var rt = (RectTransform)slider;
        float track = 46f;
        rt.sizeDelta = new Vector2(rt.sizeDelta.x, 80f);
        foreach (var name in new[] { "Background", "Fill Area", "Handle Slide Area" })
        {
            var c = (RectTransform)slider.Find(name);
            var size = c.sizeDelta;
            float inset = name == "Fill Area" ? 24f : 0f;   // the fill sat 12 units inside the track top and bottom
            c.anchorMin = new Vector2(c.anchorMin.x, 0.5f);
            c.anchorMax = new Vector2(c.anchorMax.x, 0.5f);
            c.sizeDelta = new Vector2(size.x, track - inset);
        }

        if (!slider.TryGetComponent(out Image hit)) hit = slider.gameObject.AddComponent<Image>();
        hit.color = new Color(1f, 1f, 1f, 0f);
        hit.raycastTarget = true;
        EditorUtility.SetDirty(hit);
        Log.AppendLine("settings: volume slider takes touches on its whole 80-unit row");
    }
}
