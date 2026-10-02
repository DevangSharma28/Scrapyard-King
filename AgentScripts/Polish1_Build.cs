using System.Text;
using ScrapYardKing.Feedback;
using ScrapYardKing.Harvest;
using ScrapYardKing.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

// Quality pass 1 (gameplay feel), between M5 and M6: chainsaw motor loop, hit flash, loot fountains, stack landing
// squash, and hiding UI that does nothing yet. Entry points: Assets, Prefabs, Scene (or All). Incremental and idempotent.
public static class Polish1_Build
{
    const string P = "Assets/_Project";
    const string DataDir = P + "/Data";
    const string PrefabDir = P + "/Prefabs";
    const string ScenePath = P + "/Scenes/Area1_OldScrapYard.unity";

    static readonly StringBuilder Log = new();

    public static string All() => Assets() + Prefabs() + Scene();

    public static string Assets()
    {
        Log.Clear();
        var engine = LoadOrCreate<SfxDefinition>(DataDir + "/Audio/Sfx_ChainsawEngine.asset");
        SetMany(engine, ("fallback", (int)ProceduralSfxPreset.EngineLoop), ("volume", 0.75f), ("pitchRange", new Vector2(1f, 1f)), ("minInterval", 0f));

        SetMany(Load<FeedbackConfig>(DataDir + "/Config/FeedbackConfig.asset"), ("hitFlashColor", new Color(1.9f, 1.85f, 1.7f, 1f)),
            ("hitFlashDuration", 0.06f), ("stackLandPunch", 0.35f));

        // Loot fountains: big objects keep erupting pieces for a moment; small ones pop at once. Launch speeds keep the
        // pile around 3 m out (measured: launch 7 landed at a 4 m median / 6 m max, too far to vacuum in one pass).
        foreach (var (asset, seconds, launch) in new[]
                 {
                     ("Scrap_CarWreck", 0.3f, 6f), ("Scrap_Fridge", 0.35f, 6f), ("Scrap_Kart", 0.25f, 6f), ("Scrap_Stove", 0.2f, 5.5f),
                     ("Scrap_Washer", 0.2f, 5.5f), ("Scrap_Barrel", 0f, 5f), ("Scrap_TireStack", 0f, 5f),
                 })
            SetMany(Load<ScrapDefinition>($"{DataDir}/Scrap/{asset}.asset"), ("dropBurstDuration", seconds), ("dropLaunchSpeed", launch));

        // Levelling follows the business: XP per $ sold on top of the flat XP per sale (guide-bot run: XP stalled at Lv 5).
        SetMany(Load<Object>(DataDir + "/Progression/ProgressionConfig.asset"), ("xpPerCashSold", 0.08f));

        // A slightly wider vacuum so walking through a pile clears it in one pass.
        SetMany(Load<Object>(DataDir + "/Config/PlayerConfig.asset"), ("pickupRadius", 2.5f));

        AssetDatabase.SaveAssets();
        return Log.ToString();
    }

    public static string Prefabs()
    {
        Log.Clear();
        var root = PrefabUtility.LoadPrefabContents(PrefabDir + "/Player/Player.prefab");
        try
        {
            if (!root.TryGetComponent(out ToolAudio audio)) audio = root.AddComponent<ToolAudio>();
            SetMany(audio, ("harvestTool", root.GetComponentInChildren<HarvestTool>()),
                ("engine", Load<SfxDefinition>(DataDir + "/Audio/Sfx_ChainsawEngine.asset")));
            SetMany(root.GetComponentInChildren<ScrapYardKing.Items.CarryStack>(), ("canSpill", true));
            var indicator = root.GetComponentInChildren<ScrapYardKing.UI.CarryStackIndicator>(true);
            if (indicator != null) SetMany(indicator, ("collector", root.GetComponentInChildren<ScrapYardKing.Items.ItemCollector>()));
            else Log.AppendLine("!! no CarryStackIndicator on the player");
            PrefabUtility.SaveAsPrefabAsset(root, PrefabDir + "/Player/Player.prefab");
            Log.AppendLine("edited Player.prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        // Station labels: a status pill (JAMMED / FULL / x2 BOOST / NEEDS STOCK) above the name.
        var label = PrefabUtility.LoadPrefabContents(PrefabDir + "/Stations/StationLabel.prefab");
        try
        {
            AddStatusTag(label);
            PrefabUtility.SaveAsPrefabAsset(label, PrefabDir + "/Stations/StationLabel.prefab");
            Log.AppendLine("edited StationLabel.prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(label);
        }

        // Crusher pad spills raw scrap when the chain is jammed and the player waits with a full stack (no deadlock).
        var crusher = PrefabUtility.LoadPrefabContents(PrefabDir + "/Stations/Crusher.prefab");
        try
        {
            var pad = crusher.transform.Find("InputPad").GetComponent<ScrapYardKing.Items.TransferPad>();
            SetMany(pad, ("spillAfter", 1.2f), ("spillCount", 4), ("spillCollectDelay", 6f),
                ("spillSfx", Load<SfxDefinition>(DataDir + "/Audio/Sfx_PartDetach.asset")));
            var so = new SerializedObject(pad);
            var items = so.FindProperty("spillItems");
            items.arraySize = 1;
            items.GetArrayElementAtIndex(0).objectReferenceValue = Load<Object>(DataDir + "/Items/Item_Scrap.asset");
            so.ApplyModifiedPropertiesWithoutUndo();
            PrefabUtility.SaveAsPrefabAsset(crusher, PrefabDir + "/Stations/Crusher.prefab");
            Log.AppendLine("edited Crusher.prefab");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(crusher);
        }

        return Log.ToString();
    }

    public static string Scene()
    {
        Log.Clear();
        // Never discard someone's unsaved work: save the open scene first if it has changes.
        var active = EditorSceneManager.GetActiveScene();
        if (active.isDirty && !string.IsNullOrEmpty(active.path)) EditorSceneManager.SaveScene(active);
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        // The "+" on the currency pills opened nothing (no shop yet). Dead buttons read as broken, so hide them.
        foreach (var path in new[] { "_UI/Canvas/HUD/CashPill/Plus", "_UI/Canvas/HUD/GemPill/Plus" })
        {
            var go = GameObject.Find(path);
            if (go != null) go.SetActive(false);
            else Log.AppendLine("!! missing " + path);
        }

        AddSavingsFill();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Log.AppendLine("Saved " + ScenePath);
        return Log.ToString();
    }

    static void AddStatusTag(GameObject labelRoot)
    {
        var canvas = labelRoot.transform.Find("Canvas");
        var old = canvas.Find("Status");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        var font = Load<TMPro.TMP_FontAsset>(P + "/Art/Fonts/GROBOLD SDF.asset");
        var outline = Load<Material>(P + "/Art/Fonts/GROBOLD Outline.mat");

        var root = new GameObject("Status", typeof(RectTransform));
        root.layer = LayerMask.NameToLayer("UI");
        var rt = (RectTransform)root.transform;
        rt.SetParent(canvas, false);
        rt.anchoredPosition = new Vector2(0f, 96f);
        rt.sizeDelta = new Vector2(220f, 50f);
        var bg = root.AddComponent<UnityEngine.UI.Image>();
        bg.sprite = Load<Sprite>(P + "/Art/UI/UI_Round.png");
        bg.type = UnityEngine.UI.Image.Type.Sliced;
        bg.pixelsPerUnitMultiplier = 2.6f;
        bg.color = new Color(0.9f, 0.22f, 0.18f);
        bg.raycastTarget = false;

        var textGo = new GameObject("Text", typeof(RectTransform));
        textGo.layer = root.layer;
        var trt = (RectTransform)textGo.transform;
        trt.SetParent(rt, false);
        trt.anchorMin = Vector2.zero;
        trt.anchorMax = Vector2.one;
        trt.offsetMin = trt.offsetMax = Vector2.zero;
        var text = textGo.AddComponent<TMPro.TextMeshProUGUI>();
        text.font = font;
        if (outline != null) text.fontSharedMaterial = outline;
        text.fontSize = 28f;
        text.enableAutoSizing = true;
        text.fontSizeMin = 16f;
        text.fontSizeMax = 28f;
        text.alignment = TMPro.TextAlignmentOptions.Center;
        text.color = Color.white;
        text.text = "FULL";
        text.raycastTarget = false;
        root.SetActive(false);

        SetMany(labelRoot.GetComponent<ScrapYardKing.UI.StationLabel>(), ("statusRoot", rt), ("statusBackground", bg), ("statusText", text));
    }

    /// <summary>
    /// Upgrade cards: a green fill inside the buy button that grows with your cash while the upgrade is unaffordable.
    /// Edits the card template in the panel (re-run after M4_Build.Scene, which rebuilds the panel).
    /// </summary>
    static void AddSavingsFill()
    {
        var card = GameObject.Find("_UI/Canvas/HUD/UpgradePanel/Sheet/Templates/CardTemplate");
        if (card == null)
        {
            Log.AppendLine("!! card template missing");
            return;
        }

        var buy = card.transform.Find("Buy");
        var existing = buy.Find("Savings");
        if (existing != null) Object.DestroyImmediate(existing.gameObject);
        var go = new GameObject("Savings", typeof(RectTransform));
        go.layer = LayerMask.NameToLayer("UI");
        var rt = (RectTransform)go.transform;
        rt.SetParent(buy, false);
        rt.SetSiblingIndex(0);
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<UnityEngine.UI.Image>();
        img.sprite = buy.GetComponent<UnityEngine.UI.Image>().sprite;
        img.color = new Color(1f, 1f, 1f, 0.9f);
        img.type = UnityEngine.UI.Image.Type.Filled;
        img.fillMethod = UnityEngine.UI.Image.FillMethod.Horizontal;
        img.fillOrigin = 0;
        img.fillAmount = 0.4f;
        img.raycastTarget = false;
        SetMany(card.GetComponent<ScrapYardKing.UI.UpgradeCard>(), ("savingsFill", img));
    }

    // ---------- helpers (same conventions as the milestone builders) ----------

    static T Load<T>(string path) where T : Object
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a == null) Log.AppendLine("!! missing " + path);
        return a;
    }

    static T LoadOrCreate<T>(string path) where T : ScriptableObject
    {
        var a = AssetDatabase.LoadAssetAtPath<T>(path);
        if (a != null) return a;
        a = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(a, path);
        return a;
    }

    static void SetMany(Object target, params (string field, object value)[] values)
    {
        if (target == null) return;
        var so = new SerializedObject(target);
        foreach (var (field, value) in values)
        {
            var p = so.FindProperty(field);
            if (p == null)
            {
                Log.AppendLine($"!! {target.GetType().Name}.{field} not found");
                continue;
            }

            switch (value)
            {
                case Object o: p.objectReferenceValue = o; break;
                case float f: p.floatValue = f; break;
                case int i when p.propertyType == SerializedPropertyType.Enum: p.enumValueIndex = i; break;
                case int i: p.intValue = i; break;
                case Color c: p.colorValue = c; break;
                case Vector2 v: p.vector2Value = v; break;
                case bool b: p.boolValue = b; break;
                default: Log.AppendLine($"!! unsupported value for {field}"); break;
            }
        }

        so.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(target);
    }
}
