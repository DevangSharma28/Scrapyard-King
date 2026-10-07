using System.IO;
using ScrapYardKing.Persistence;
using UnityEditor;
using UnityEngine;

namespace ScrapYardKing.EditorTools
{
    /// <summary>Editor shortcuts for the save file (Play mode loads it like a device would).</summary>
    public static class SaveMenu
    {
        const string FileName = "scrapyard_save.json";

        static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

        [MenuItem("Scrap Yard King/Save/Delete Save (start a new game)")]
        public static void DeleteSave()
        {
            if (Application.isPlaying && Object.FindAnyObjectByType<SaveManager>() is { } manager) manager.Wipe();
            else SaveFile.Delete(FilePath);
            Debug.Log("[Save] Deleted " + FilePath);
        }

        [MenuItem("Scrap Yard King/Save/Reveal Save File")]
        public static void Reveal() => EditorUtility.RevealInFinder(File.Exists(FilePath) ? FilePath : Application.persistentDataPath);
    }
}
