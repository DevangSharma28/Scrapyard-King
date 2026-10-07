using System;
using System.Collections.Generic;
using ScrapYardKing.Core;
using ScrapYardKing.Feedback;
using ScrapYardKing.Progression;
using UnityEngine;

namespace ScrapYardKing.Workers
{
    /// <summary>
    /// Hires and spawns workers. Each hireable kind is exposed to the upgrade system as an <see cref="IUpgradeable"/>
    /// whose "level" is the headcount, so hiring uses the same cards, costs and events as every other upgrade.
    /// </summary>
    [DefaultExecutionOrder(-400)]
    public sealed class WorkerManager : ServiceBehaviour<WorkerManager>
    {
        sealed class HireUpgrade : IUpgradeable
        {
            readonly WorkerDefinition definition;
            readonly WorkerManager manager;

            public HireUpgrade(WorkerDefinition definition, WorkerManager manager)
            {
                this.definition = definition;
                this.manager = manager;
            }

            public event Action<IUpgradeable> UpgradeChanged;

            public string UpgradeId => definition.Id;
            public string DisplayName => definition.DisplayName;
            public Sprite Icon => definition.Icon;
            public int Level => manager.CountOf(definition);
            public int MaxLevel => definition.MaxCount;
            public bool IsMaxed => Level >= MaxLevel;
            public long NextCost => IsMaxed ? 0 : definition.HireCost(Level);
            public string LevelLabel => $"{Level}/{MaxLevel}";
            public string NextEffect => IsMaxed ? string.Empty : Level == 0 ? "HIRE" : "+1 WORKER";

            // Hiring plays its own spawn feedback at the yard entrance.
            public Vector3? FeedbackPosition => null;

            public void ApplyLevel(int level)
            {
                while (manager.CountOf(definition) < Mathf.Min(level, MaxLevel)) manager.Spawn(definition);
                UpgradeChanged?.Invoke(this);
            }
        }

        [SerializeField] WorkerDefinition[] hireable;
        [SerializeField] WorkerSite[] sites;
        [Tooltip("Where new hires appear (yard entrance).")]
        [SerializeField] Transform spawnPoint;
        [SerializeField] Transform workersRoot;
        [SerializeField] ParticleSystem hireVfx;
        [SerializeField] SfxDefinition hireSfx;

        readonly Dictionary<WorkerDefinition, List<Worker>> hired = new();
        readonly List<HireUpgrade> upgrades = new();

        public event Action<Worker> WorkerSpawned;

        void Start()
        {
            if (hireable == null || !Services.TryGet(out UpgradeManager manager)) return;
            foreach (var definition in hireable)
            {
                if (definition == null) continue;
                var upgrade = new HireUpgrade(definition, this);
                upgrades.Add(upgrade);
                manager.Register(upgrade);
            }
        }

        public bool TryGetDefinition(string id, out WorkerDefinition definition)
        {
            definition = null;
            if (hireable == null) return false;
            foreach (var d in hireable)
            {
                if (d == null || d.Id != id) continue;
                definition = d;
                return true;
            }

            return false;
        }

        /// <summary>True when a hired carrier's route drops off into <paramref name="receiver"/> (that link of the chain is staffed).</summary>
        public bool Serves(Items.IItemReceiver receiver)
        {
            if (receiver == null) return false;
            foreach (var list in hired.Values)
                foreach (var worker in list)
                    if (worker != null && worker.Site is PorterRoute route && route.DropsAt(receiver))
                        return true;
            return false;
        }

        public int CountOf(WorkerDefinition definition) => hired.TryGetValue(definition, out var list) ? list.Count : 0;

        public IReadOnlyList<Worker> WorkersOf(WorkerDefinition definition) =>
            hired.TryGetValue(definition, out var list) ? list : (IReadOnlyList<Worker>)Array.Empty<Worker>();

        Worker Spawn(WorkerDefinition definition)
        {
            var site = FindSite(definition);
            var origin = spawnPoint != null ? spawnPoint : transform;
            // Workers from a save are already on the job: they appear at their site, without the hiring fanfare.
            bool restoring = SaveRegistry.IsRestoring;
            Vector3 position = (definition.SpawnAtSite || restoring) && site != null ? site.IdlePosition : origin.position;
            var worker = Instantiate(definition.Prefab, position, origin.rotation, workersRoot != null ? workersRoot : transform);
            if (!worker.Agent.isOnNavMesh) worker.Agent.Warp(position);
            worker.Initialize(definition, site);

            if (!hired.TryGetValue(definition, out var list))
            {
                list = new List<Worker>();
                hired[definition] = list;
            }

            list.Add(worker);
            WorkerSpawned?.Invoke(worker);
            if (restoring) return worker;

            GameFeedback.Vfx(hireVfx, position, Quaternion.identity);
            GameFeedback.Sfx(hireSfx);
            var config = GameFeedback.Config;
            GameFeedback.Popup($"{definition.DisplayName.ToUpperInvariant()} HIRED!", position + Vector3.up * 2.6f,
                config != null ? config.PositivePopupColor : Color.yellow, 1.2f);

            GameEvents.RaiseWorkerHired(definition.Id, list.Count);
            return worker;
        }

        WorkerSite FindSite(WorkerDefinition definition)
        {
            if (sites == null) return null;
            foreach (var s in sites)
            {
                if (s == null || s.Role != definition.Role) continue;
                if (string.IsNullOrEmpty(definition.SiteId) || s.SiteId == definition.SiteId) return s;
            }

            return null;
        }
    }
}
