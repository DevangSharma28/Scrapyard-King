using System.IO;
using UnityEditor;
using UnityEngine;

// One-shot project setup for Milestone 1. Run through `unity command run_script`.
public static class M1_Setup
{
    public static string Run()
    {
        var log = new System.Text.StringBuilder();

        // Layers used by gameplay queries.
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var layers = tagManager.FindProperty("layers");
        SetLayer(layers, 6, "Ground", log);
        SetLayer(layers, 7, "Characters", log);
        SetLayer(layers, 8, "Scrap", log);
        tagManager.ApplyModifiedPropertiesWithoutUndo();

        // TextMeshPro essentials (default font asset + TMP Settings).
        if (!File.Exists("Assets/TextMesh Pro/Resources/TMP Settings.asset"))
        {
            var ugui = UnityEditor.PackageManager.PackageInfo.FindForAssetPath("Packages/com.unity.ugui");
            string package = Path.Combine(ugui.resolvedPath, "Package Resources", "TMP Essential Resources.unitypackage");
            AssetDatabase.ImportPackage(package, false);
            log.AppendLine("TMP essentials import queued: " + package);
        }
        else log.AppendLine("TMP essentials already present");

        // Mobile portrait, like the genre reference.
        PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;
        PlayerSettings.productName = "Scrap Yard King";
        log.AppendLine("Orientation: Portrait");

        AssetDatabase.SaveAssets();
        return log.ToString();
    }

    static void SetLayer(SerializedProperty layers, int index, string name, System.Text.StringBuilder log)
    {
        var p = layers.GetArrayElementAtIndex(index);
        if (!string.IsNullOrEmpty(p.stringValue) && p.stringValue != name)
        {
            log.AppendLine($"Layer {index} already '{p.stringValue}', skipping");
            return;
        }

        p.stringValue = name;
        log.AppendLine($"Layer {index} = {name}");
    }
}
