using System.Linq;
using System.Text;
using ScrapYardKing.Factory;
using ScrapYardKing.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>
/// UI step U5 (scene part): the announcer gets a picture of what just arrived (area, machine, worker, level star), and
/// the boost chips get a fifth slot plus the Overdrive config so a boost pad's 2X shows on the HUD. The rest of U5 is
/// runtime code (purchase celebration in <c>UpgradeManager</c>, upgrade card text, order card celebration).
/// Entry point: Scene. Idempotent. Run after U4_Build.
/// </summary>
public static class U5_Build
{
    const string Root = "Assets/_Project";
    const string KitDir = Root + "/Art/UI/Kit";
    const string ScenePath = Root + "/Scenes/Area1_OldScrapYard.unity";
    static readonly StringBuilder Log = new();

    public static string Scene()
    {
        Log.Clear();
        if (EditorApplication.isPlaying) return "!! stop Play mode first";
        var active = SceneManager.GetActiveScene();
        if (active.path == ScenePath && active.isDirty) EditorSceneManager.SaveScene(active);
        var scene = active.path == ScenePath ? active : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        var hud = scene.GetRootGameObjects().First(g => g.name == "_UI").transform.Find("Canvas/HUD");

        // announcer picture
        var announcer = hud.Find("Announcer");
        var icon = announcer.Find("Icon") as RectTransform;
        if (icon == null)
        {
            icon = new GameObject("Icon", typeof(RectTransform), typeof(Image)).GetComponent<RectTransform>();
            icon.SetParent(announcer, false);
        }

        icon.gameObject.layer = announcer.gameObject.layer;
        icon.anchorMin = icon.anchorMax = icon.pivot = new Vector2(0.5f, 0.5f);
        icon.anchoredPosition = new Vector2(0f, 215f);
        icon.sizeDelta = new Vector2(190f, 190f);
        var img = icon.GetComponent<Image>();
        img.preserveAspect = true;
        img.raycastTarget = false;
        icon.gameObject.SetActive(false);
        Set(announcer.GetComponent<Announcer>(), ("icon", img), ("levelIcon", A("Icon_Star")));

        // a fifth chip for Overdrive + boosts
        var bar = hud.Find("BoostBar");
        var last = bar.Find("Chip3");
        var five = bar.Find("Chip4");
        if (five == null)
        {
            five = Object.Instantiate(last.gameObject, bar).transform;
            five.name = "Chip4";
        }

        ((RectTransform)five).anchoredPosition = ((RectTransform)last).anchoredPosition + new Vector2(0f, -74f);
        var boostBar = bar.GetComponent<BoostBar>();
        var so = new SerializedObject(boostBar);
        var chips = so.FindProperty("chips");
        chips.arraySize = 5;
        var e = chips.GetArrayElementAtIndex(4);
        e.FindPropertyRelative("root").objectReferenceValue = five.gameObject;
        e.FindPropertyRelative("icon").objectReferenceValue = five.Find("Icon").GetComponent<Image>();
        e.FindPropertyRelative("fill").objectReferenceValue = five.Find("Fill").GetComponent<Image>();
        e.FindPropertyRelative("time").objectReferenceValue = five.Find("Time").GetComponent<TMPro.TMP_Text>();
        so.FindProperty("overdrive").objectReferenceValue = AssetDatabase.LoadAssetAtPath<OverdriveConfig>(Root + "/Data/Factory/OverdriveConfig.asset");
        so.FindProperty("overdriveIcon").objectReferenceValue = B("B_r3_c6");
        so.ApplyModifiedPropertiesWithoutUndo();
        five.gameObject.SetActive(false);

        // boost chips start below the task banner (they clipped its lower edge)
        ((RectTransform)bar).anchoredPosition = new Vector2(-30f, -262f);

        // upgrade panel: the title was drawn under its own plate; the level pill must fit "LV 12 → 13"
        var sheet = hud.Find("UpgradePanel/Sheet");
        var plate = sheet.Find("TitlePlate");
        var title = sheet.Find("Title");
        if (plate != null && title != null) title.SetSiblingIndex(plate.GetSiblingIndex() + 1);
        var pill = (RectTransform)sheet.Find("Templates/CardTemplate/LevelPill");
        pill.anchoredPosition = new Vector2(76f, -78f);
        pill.sizeDelta = new Vector2(124f, 40f);
        var pillText = pill.Find("Level").GetComponent<TMPro.TMP_Text>();
        pillText.enableAutoSizing = true;
        pillText.fontSizeMin = 14f;
        pillText.fontSizeMax = 22f;
        EditorUtility.SetDirty(pillText);

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("scene: announcer icon, fifth boost chip, overdrive on the HUD");
        return Log.ToString();
    }

    static void Set(Object target, params (string name, Object value)[] refs)
    {
        var so = new SerializedObject(target);
        foreach (var (name, value) in refs)
        {
            var p = so.FindProperty(name);
            if (p == null) { Log.AppendLine($"!! {name}"); continue; }
            p.objectReferenceValue = value;
        }

        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static Sprite Kit(string sheet, string name) =>
        AssetDatabase.LoadAllAssetsAtPath($"{KitDir}/{sheet}.png").OfType<Sprite>().FirstOrDefault(x => x.name == name);

    static Sprite A(string name) => Kit("Kit_IconsA", name);
    static Sprite B(string name) => Kit("Kit_IconsB", name);
}
