using System;
using System.IO;
using ScrapYardKing.Core;
using UnityEngine;

namespace ScrapYardKing.Persistence
{
    /// <summary>
    /// Loads the save before any other system starts, then writes it on a timer, shortly after important moments
    /// (purchases, tasks, level-ups) and when the app is paused or closed. It knows nothing about what is saved: systems
    /// implement <see cref="ISaveable"/> and meet the file through <see cref="SaveRegistry"/>.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public sealed class SaveManager : ServiceBehaviour<SaveManager>
    {
        [SerializeField] string fileName = "scrapyard_save.json";
        [Tooltip("Seconds between routine saves.")]
        [SerializeField, Min(2f)] float autosaveInterval = 20f;
        [Tooltip("A purchase, finished task or level-up saves this many seconds later (several in a row share one write).")]
        [SerializeField, Min(0f)] float eventSaveDelay = 1.5f;
        [Tooltip("Off = always start a new game and never write (test scenes).")]
        [SerializeField] bool persist = true;

        float nextSaveAt, playSeconds;
        int warmupFrames = 3;
        bool ready, readOnly;

        public string FilePath => Path.Combine(Application.persistentDataPath, fileName);
        /// <summary>How this session started.</summary>
        public SaveReadResult LoadResult { get; private set; } = SaveReadResult.Missing;
        public float PlaySeconds => playSeconds;
        public bool HasLoadedGame => LoadResult is SaveReadResult.Loaded or SaveReadResult.LoadedBackup;

        public event Action Saved;

        protected override void Awake()
        {
            base.Awake();
            if (persist) Load();
        }

        void OnEnable()
        {
            GameEvents.UpgradePurchased += OnProgress;
            GameEvents.WorkerHired += OnProgress;
            GameEvents.TaskCompleted += OnProgress;
            GameEvents.LevelUp += OnProgress;
            GameEvents.ExpansionOpened += OnProgress;
            GameEvents.GiantScrapDefeated += OnProgress;
        }

        void OnDisable()
        {
            GameEvents.UpgradePurchased -= OnProgress;
            GameEvents.WorkerHired -= OnProgress;
            GameEvents.TaskCompleted -= OnProgress;
            GameEvents.LevelUp -= OnProgress;
            GameEvents.ExpansionOpened -= OnProgress;
            GameEvents.GiantScrapDefeated -= OnProgress;
        }

        void Load()
        {
            LoadResult = SaveFile.Read(FilePath, out var data);
            switch (LoadResult)
            {
                case SaveReadResult.Loaded:
                case SaveReadResult.LoadedBackup:
                    playSeconds = data.playSeconds;
                    SaveRegistry.SetLoaded(data.ToDictionary());
                    if (LoadResult == SaveReadResult.LoadedBackup) Debug.LogWarning("[Save] Main save unreadable; loaded the backup.");
                    break;
                case SaveReadResult.TooNew:
                    // A newer build wrote this file. Play a fresh session but never write over it.
                    readOnly = true;
                    Debug.LogWarning("[Save] Save file is from a newer version; it is left untouched and this session is not saved.");
                    break;
                case SaveReadResult.Corrupt:
                    Debug.LogWarning("[Save] Save file unreadable; it was set aside (.corrupt) and a new game starts.");
                    break;
            }
        }

        void Start()
        {
            nextSaveAt = Time.unscaledTime + autosaveInterval;
        }

        void Update()
        {
            // The first frame restores state (systems register in Start, locked content right after). Saving starts once
            // that frame is over, so a half-restored game can never be written.
            if (!ready)
            {
                ready = --warmupFrames <= 0;
                return;
            }

            playSeconds += Time.unscaledDeltaTime;
            if (Time.unscaledTime >= nextSaveAt) Save();
        }

        void OnProgress(string id, int value) => RequestSave();
        void OnProgress(string id) => RequestSave();
        void OnProgress(int level) => RequestSave();
        void OnProgress(string id, long value) => RequestSave();

        /// <summary>Saves soon (see <see cref="eventSaveDelay"/>).</summary>
        public void RequestSave() => nextSaveAt = Mathf.Min(nextSaveAt, Time.unscaledTime + eventSaveDelay);

        void OnApplicationPause(bool paused)
        {
            if (paused) Save();
        }

        void OnApplicationQuit() => Save();

        /// <summary>Writes the save now. Returns false when saving is off, not ready yet or the write failed.</summary>
        public bool Save()
        {
            nextSaveAt = Time.unscaledTime + autosaveInterval;
            if (!persist || readOnly || !ready) return false;
            try
            {
                var data = new SaveData
                {
                    appVersion = Application.version,
                    savedAtUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                    playSeconds = playSeconds
                };
                data.SetEntries(SaveRegistry.Capture());
                SaveFile.Write(FilePath, SaveFile.Serialize(data));
                Saved?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[Save] Write failed; the previous save is intact. {e}");
                return false;
            }
        }

        /// <summary>Deletes the save and stops saving for the rest of this session (the next launch is a new game).</summary>
        public void Wipe()
        {
            readOnly = true;
            SaveFile.Delete(FilePath);
            SaveRegistry.SetLoaded(null);
        }
    }
}
