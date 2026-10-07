using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ScrapYardKing.Persistence
{
    [Serializable]
    public sealed class SaveEntry
    {
        public string key;
        public string state;
    }

    /// <summary>The save file: a version, some bookkeeping and one opaque state string per saveable system.</summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>Format version written by this build. Raise it with a step in <see cref="SaveFile.Migrate"/>.</summary>
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string appVersion;
        /// <summary>UTC seconds since 1970 when the file was written (for offline time later).</summary>
        public long savedAtUnix;
        public float playSeconds;
        public List<SaveEntry> entries = new();

        public Dictionary<string, string> ToDictionary()
        {
            var d = new Dictionary<string, string>();
            if (entries == null) return d;
            foreach (var e in entries)
                if (e != null && !string.IsNullOrEmpty(e.key)) d[e.key] = e.state;
            return d;
        }

        public void SetEntries(Dictionary<string, string> states)
        {
            entries = new List<SaveEntry>(states.Count);
            foreach (var kv in states) entries.Add(new SaveEntry { key = kv.Key, state = kv.Value });
            // Stable order keeps files diffable.
            entries.Sort((a, b) => string.CompareOrdinal(a.key, b.key));
        }
    }

    public enum SaveReadResult
    {
        /// <summary>No file: a new game.</summary>
        Missing,
        Loaded,
        /// <summary>The main file was unreadable; the backup was used.</summary>
        LoadedBackup,
        /// <summary>Nothing readable. The broken files were set aside; a new game starts.</summary>
        Corrupt,
        /// <summary>Written by a newer build. Not loaded and never overwritten.</summary>
        TooNew
    }

    /// <summary>
    /// File format and disk handling for saves, kept free of scene objects so it can be unit-tested. Writes go to a
    /// temporary file first and the previous save becomes the backup, so a crash mid-write cannot destroy progress.
    /// </summary>
    public static class SaveFile
    {
        const string BackupSuffix = ".bak", TempSuffix = ".tmp";

        public static string Serialize(SaveData data) => JsonUtility.ToJson(data, true);

        /// <summary>Parses and migrates. False when the text is not a save or comes from a newer build.</summary>
        public static bool TryParse(string json, out SaveData data, out SaveReadResult problem)
        {
            data = null;
            problem = SaveReadResult.Corrupt;
            if (string.IsNullOrWhiteSpace(json)) return false;
            try
            {
                data = JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception)
            {
                data = null;
            }

            if (data == null || data.version <= 0 || data.entries == null)
            {
                data = null;
                return false;
            }

            if (data.version > SaveData.CurrentVersion)
            {
                data = null;
                problem = SaveReadResult.TooNew;
                return false;
            }

            Migrate(data);
            return true;
        }

        /// <summary>Upgrades older saves step by step. Add "if (data.version == n) { ...; data.version = n + 1; }" per format change.</summary>
        public static void Migrate(SaveData data)
        {
            data.version = SaveData.CurrentVersion;
        }

        public static SaveReadResult Read(string path, out SaveData data)
        {
            data = null;
            bool hadFile = File.Exists(path), hadBackup = File.Exists(path + BackupSuffix);
            if (!hadFile && !hadBackup) return SaveReadResult.Missing;

            var problem = SaveReadResult.Corrupt;
            if (hadFile && TryParse(SafeRead(path), out data, out problem)) return SaveReadResult.Loaded;
            if (problem == SaveReadResult.TooNew) return SaveReadResult.TooNew;
            if (hadBackup && TryParse(SafeRead(path + BackupSuffix), out data, out _)) return SaveReadResult.LoadedBackup;

            // Keep the evidence instead of silently overwriting it with a fresh game.
            SetAside(path);
            SetAside(path + BackupSuffix);
            return SaveReadResult.Corrupt;
        }

        /// <summary>Writes atomically: temp file, then swap; the previous good save becomes the backup.</summary>
        public static void Write(string path, string json)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            string temp = path + TempSuffix;
            File.WriteAllText(temp, json);
            if (File.Exists(path))
            {
                string backup = path + BackupSuffix;
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(path, backup);
            }

            File.Move(temp, path);
        }

        public static void Delete(string path)
        {
            foreach (var p in new[] { path, path + BackupSuffix, path + TempSuffix })
                if (File.Exists(p)) File.Delete(p);
        }

        static string SafeRead(string path)
        {
            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception)
            {
                return null;
            }
        }

        static void SetAside(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                string target = path + ".corrupt";
                if (File.Exists(target)) File.Delete(target);
                File.Move(path, target);
            }
            catch (Exception)
            {
                // Best effort: an unreadable, unmovable file is left where it is.
            }
        }
    }
}
