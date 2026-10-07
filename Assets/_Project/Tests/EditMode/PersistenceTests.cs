using System;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ScrapYardKing.Core;
using ScrapYardKing.Persistence;
using UnityEngine;

namespace ScrapYardKing.Tests
{
    public sealed class SaveFileTests
    {
        string directory, path;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "syk_save_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            path = Path.Combine(directory, "save.json");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }

        static SaveData Sample(long cash)
        {
            var data = new SaveData { playSeconds = 123.5f, savedAtUnix = 1790000000 };
            data.SetEntries(new Dictionary<string, string> { ["economy"] = "{\"cash\":" + cash + "}", ["tile/porter"] = "{\"paid\":250}" });
            return data;
        }

        [Test]
        public void RoundTrip_KeepsEveryEntry()
        {
            SaveFile.Write(path, SaveFile.Serialize(Sample(900)));

            Assert.AreEqual(SaveReadResult.Loaded, SaveFile.Read(path, out var loaded));
            Assert.AreEqual(SaveData.CurrentVersion, loaded.version);
            Assert.AreEqual(123.5f, loaded.playSeconds, 1e-3f);
            var states = loaded.ToDictionary();
            Assert.AreEqual("{\"cash\":900}", states["economy"]);
            Assert.AreEqual("{\"paid\":250}", states["tile/porter"]);
        }

        [Test]
        public void Read_NoFile_IsANewGame()
        {
            Assert.AreEqual(SaveReadResult.Missing, SaveFile.Read(path, out var loaded));
            Assert.IsNull(loaded);
        }

        [Test]
        public void Read_CorruptMainFile_FallsBackToThePreviousSave()
        {
            SaveFile.Write(path, SaveFile.Serialize(Sample(100)));
            SaveFile.Write(path, SaveFile.Serialize(Sample(200)));
            File.WriteAllText(path, "{ \"version\": 1, \"entries\": [ { \"key\": \"econ");

            Assert.AreEqual(SaveReadResult.LoadedBackup, SaveFile.Read(path, out var loaded));
            Assert.AreEqual("{\"cash\":100}", loaded.ToDictionary()["economy"]);
        }

        [Test]
        public void Read_NothingReadable_SetsTheFilesAsideInsteadOfDeletingThem()
        {
            File.WriteAllText(path, "not a save");

            Assert.AreEqual(SaveReadResult.Corrupt, SaveFile.Read(path, out var loaded));
            Assert.IsNull(loaded);
            Assert.IsFalse(File.Exists(path));
            Assert.IsTrue(File.Exists(path + ".corrupt"));
        }

        [Test]
        public void Read_SaveFromANewerBuild_IsRefusedAndLeftUntouched()
        {
            var data = Sample(500);
            data.version = SaveData.CurrentVersion + 1;
            string json = SaveFile.Serialize(data);
            File.WriteAllText(path, json);

            Assert.AreEqual(SaveReadResult.TooNew, SaveFile.Read(path, out var loaded));
            Assert.IsNull(loaded);
            Assert.AreEqual(json, File.ReadAllText(path));
        }

        [Test]
        public void Delete_RemovesSaveAndBackup()
        {
            SaveFile.Write(path, SaveFile.Serialize(Sample(1)));
            SaveFile.Write(path, SaveFile.Serialize(Sample(2)));
            SaveFile.Delete(path);

            Assert.AreEqual(SaveReadResult.Missing, SaveFile.Read(path, out _));
        }
    }

    public sealed class SaveRegistryTests
    {
        sealed class Fake : ISaveable
        {
            public string Key, State, Restored;
            public bool WasRestoringDuringRestore, Throw;

            public string SaveKey => Key;

            public string CaptureState() => Throw ? throw new InvalidOperationException("boom") : State;

            public void RestoreState(string state)
            {
                Restored = state;
                WasRestoringDuringRestore = SaveRegistry.IsRestoring;
            }
        }

        readonly List<Fake> registered = new();

        Fake Register(string key, string state = null, bool throws = false)
        {
            var fake = new Fake { Key = key, State = state, Throw = throws };
            registered.Add(fake);
            SaveRegistry.Register(fake);
            return fake;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var fake in registered) SaveRegistry.Unregister(fake);
            registered.Clear();
            SaveRegistry.SetLoaded(null);
        }

        [Test]
        public void Register_RestoresTheLoadedStateOnTheSpot()
        {
            SaveRegistry.SetLoaded(new Dictionary<string, string> { ["a"] = "saved-a" });

            var a = Register("a");
            var b = Register("b");

            Assert.AreEqual("saved-a", a.Restored);
            Assert.IsTrue(a.WasRestoringDuringRestore);
            Assert.IsNull(b.Restored, "nothing saved for b: it starts fresh");
            Assert.IsFalse(SaveRegistry.IsRestoring);
        }

        [Test]
        public void Capture_KeepsStatesWhoseOwnerIsNotInTheScene()
        {
            SaveRegistry.SetLoaded(new Dictionary<string, string> { ["stock/locked_area"] = "old", ["a"] = "old-a" });
            Register("a", "new-a");

            var states = SaveRegistry.Capture();

            Assert.AreEqual("new-a", states["a"]);
            Assert.AreEqual("old", states["stock/locked_area"]);
        }

        [Test]
        public void Capture_ASystemThatFailsKeepsItsPreviousState()
        {
            SaveRegistry.SetLoaded(new Dictionary<string, string> { ["broken"] = "last good" });
            Register("broken", "ignored", true);
            Register("fine", "ok");

            // Capture reports the failure with Debug.LogError; muted here so a passing test leaves the console clean.
            bool logging = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            Dictionary<string, string> states;
            try
            {
                states = SaveRegistry.Capture();
            }
            finally
            {
                Debug.unityLogger.logEnabled = logging;
            }

            Assert.AreEqual("last good", states["broken"]);
            Assert.AreEqual("ok", states["fine"]);
        }

        [Test]
        public void Capture_NullStateMeansKeepWhatWasSaved()
        {
            SaveRegistry.SetLoaded(new Dictionary<string, string> { ["a"] = "old-a" });
            Register("a", null);

            Assert.AreEqual("old-a", SaveRegistry.Capture()["a"]);
        }
    }
}
