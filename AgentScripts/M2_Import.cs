using System;
using System.IO;
using UnityEditor;

// Imports third-party packages for Milestone 2 from the local Asset Store cache (the user's own licensed downloads).
public static class M2_Import
{
    public static string Run()
    {
        string cache = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal), "Library/Unity/Asset Store-5.x");
        var log = new System.Text.StringBuilder();
        Import(Path.Combine(cache, "Demigiant/Editor ExtensionsAnimation/DOTween HOTween v2.unitypackage"), "Assets/Plugins/Demigiant", log);
        Import(Path.Combine(cache, "300Mind/Textures MaterialsGUI Skins/2D Mobile Game UI Kit.unitypackage"), "Assets/300Mind", log);
        AssetDatabase.Refresh();
        return log.ToString();
    }

    static void Import(string package, string expectedFolder, System.Text.StringBuilder log)
    {
        if (Directory.Exists(expectedFolder))
        {
            log.AppendLine("already present: " + expectedFolder);
            return;
        }

        if (!File.Exists(package))
        {
            log.AppendLine("MISSING package: " + package);
            return;
        }

        AssetDatabase.ImportPackage(package, false);
        log.AppendLine("import queued: " + Path.GetFileName(package));
    }
}
