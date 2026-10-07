using System;
using System.Collections.Generic;
using UnityEngine;

namespace ScrapYardKing.Core
{
    /// <summary>
    /// Something with state worth keeping between sessions. It owns its own format: <see cref="CaptureState"/> returns a
    /// small JSON string and <see cref="RestoreState"/> gets the same string back in a later session.
    /// </summary>
    public interface ISaveable
    {
        /// <summary>Stable, unique key ("economy", "tile/porter", "stock/storage_yard").</summary>
        string SaveKey { get; }

        /// <summary>Current state as JSON, or null to keep whatever was saved before.</summary>
        string CaptureState();

        void RestoreState(string state);
    }

    /// <summary>
    /// Meeting point between saveable systems and the save file. Systems call <see cref="Register"/> from Start; if the
    /// loaded save holds their key they are restored on the spot, so content that only appears later (a locked area's
    /// stations) restores itself when it shows up. States whose owner never registers are kept and written back untouched,
    /// which means a save never loses data for content that is locked, hidden or not built yet.
    /// </summary>
    public static class SaveRegistry
    {
        static readonly List<ISaveable> Saveables = new();
        static readonly Dictionary<string, string> Loaded = new();
        static int restoreDepth;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            Saveables.Clear();
            Loaded.Clear();
            restoreDepth = 0;
        }

        /// <summary>
        /// True while saved state is being applied. Systems use it to skip the fanfare of a live purchase (reveals,
        /// announcements, hire popups, XP) when the same level arrives from a save.
        /// </summary>
        public static bool IsRestoring => restoreDepth > 0;

        public static IReadOnlyList<ISaveable> All => Saveables;

        /// <summary>Replaces the loaded states (the save file's content). Call before anything registers.</summary>
        public static void SetLoaded(IEnumerable<KeyValuePair<string, string>> states)
        {
            Loaded.Clear();
            if (states == null) return;
            foreach (var kv in states)
                if (!string.IsNullOrEmpty(kv.Key)) Loaded[kv.Key] = kv.Value;
        }

        public static void Register(ISaveable saveable)
        {
            if (saveable == null || string.IsNullOrEmpty(saveable.SaveKey) || Saveables.Contains(saveable)) return;
            Saveables.Add(saveable);
            if (Loaded.TryGetValue(saveable.SaveKey, out string state) && !string.IsNullOrEmpty(state)) Restore(saveable, state);
        }

        public static void Unregister(ISaveable saveable) => Saveables.Remove(saveable);

        /// <summary>Marks a block that applies saved state (see <see cref="IsRestoring"/>). Dispose to end it.</summary>
        public static RestoreScope BeginRestore() => new(true);

        /// <summary>
        /// Every state to write: what was loaded, overlaid with what the live systems report now. One system failing to
        /// capture keeps its previous state instead of breaking the save.
        /// </summary>
        public static Dictionary<string, string> Capture()
        {
            var result = new Dictionary<string, string>(Loaded);
            foreach (var saveable in Saveables)
            {
                if (saveable == null || (saveable is UnityEngine.Object o && o == null)) continue;
                try
                {
                    string state = saveable.CaptureState();
                    if (state != null) result[saveable.SaveKey] = state;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Save] {saveable.SaveKey} failed to capture; keeping its previous state. {e}");
                }
            }

            return result;
        }

        static void Restore(ISaveable saveable, string state)
        {
            using var scope = BeginRestore();
            try
            {
                saveable.RestoreState(state);
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] {saveable.SaveKey} failed to restore; it starts fresh. {e}");
            }
        }

        public readonly struct RestoreScope : IDisposable
        {
            readonly bool active;

            internal RestoreScope(bool active)
            {
                this.active = active;
                if (active) restoreDepth++;
            }

            public void Dispose()
            {
                if (active) restoreDepth = Mathf.Max(0, restoreDepth - 1);
            }
        }
    }
}
