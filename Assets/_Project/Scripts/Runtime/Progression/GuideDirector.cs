using System;
using ScrapYardKing.Core;
using ScrapYardKing.Factory;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using ScrapYardKing.Player;
using ScrapYardKing.Tiles;
using ScrapYardKing.UI;
using UnityEngine;

namespace ScrapYardKing.Progression
{
    /// <summary>
    /// Blueprint rule: "the player should always have an obvious next action". Reads the current main task plus the
    /// player's stack and station states, then points the world marker at the one thing to do next. Affordable
    /// purchases point at their tile (hires, expansions) or at the upgrade tile, whose panel card then pulses.
    /// </summary>
    [DefaultExecutionOrder(-300)]
    public sealed class GuideDirector : ServiceBehaviour<GuideDirector>
    {
        struct Hint
        {
            public Vector3? Position;
            public float Lift;
            public string UpgradeId;
            public ScrapObject Scrap;
        }

        const string RoleIn = "in", RoleOut = "out", RoleCash = "cash", RoleBoost = "boost";
        const string UpgradeTileAnchor = "tile/upgrades";
        const string ChainsawUpgrade = "chainsaw";
        const int MinRoom = 4;

        [SerializeField] GuideMarker marker;
        [SerializeField, Min(0.05f)] float refreshInterval = 0.2f;
        [Tooltip("The marker hides when the player is this close to a pad target (they are already doing it).")]
        [SerializeField, Min(0f)] float arriveRadius = 1.4f;
        [SerializeField, Min(1f)] float searchRadius = 45f;

        PlayerCharacter player;
        TaskManager tasks;
        ScrapManager scrap;
        HarvestManager harvest;
        UpgradeManager upgrades;
        UpgradePanel panel;
        float nextRefresh;

        /// <summary>Upgrade id whose card should pulse, or null.</summary>
        public string HighlightedUpgrade { get; private set; }
        /// <summary>World point currently guided to (null when hidden). Drives the off-screen pointer.</summary>
        public Vector3? Target { get; private set; }

        public event Action<string> HighlightChanged;

        void Start()
        {
            Services.TryGet(out player);
            Services.TryGet(out tasks);
            Services.TryGet(out scrap);
            Services.TryGet(out harvest);
            Services.TryGet(out upgrades);
            Services.TryGet(out panel);
        }

        void Update()
        {
            if (Time.time < nextRefresh) return;
            nextRefresh = Time.time + refreshInterval;
            if (player == null || tasks == null) return;

            var hint = Resolve(tasks.Main);
            SetHighlight(hint.UpgradeId);
            ApplyMarker(hint);
        }

        void ApplyMarker(Hint hint)
        {
            if (marker == null) return;
            if (!hint.Position.HasValue || IsAlreadyThere(hint))
            {
                Target = null;
                marker.Hide();
                return;
            }

            Target = hint.Position;
            marker.Show(hint.Position.Value, hint.Lift);
        }

        bool IsAlreadyThere(Hint hint)
        {
            if (hint.Scrap != null) return player.HarvestTool != null && player.HarvestTool.CurrentTarget == hint.Scrap;
            Vector3 d = hint.Position.Value - player.transform.position;
            d.y = 0f;
            return d.magnitude <= arriveRadius;
        }

        void SetHighlight(string id)
        {
            if (HighlightedUpgrade == id) return;
            HighlightedUpgrade = id;
            HighlightChanged?.Invoke(id);
        }

        // ---------- rules ----------

        Hint Resolve(ActiveTask task)
        {
            if (task == null || task.IsComplete) return default;
            var d = task.Definition;
            switch (d.Type)
            {
                case TaskType.BreakScrap:
                    var target = NearestScrap(s => d.Targets(s.Definition.Id));
                    // Too tough for the current chainsaw: the next step is the upgrade, not the scrap.
                    if (target != null && player.Stats.CutPower < target.Definition.MinCutPower) return Purchase(ChainsawUpgrade);
                    return ScrapHint(target);
                case TaskType.CollectItems:
                    // A full stack cannot collect; empty it where it is useful first.
                    if (player.CarryStack.IsFull) return Unload();
                    return LooseOrScrap(item => d.Targets(item.Id));
                case TaskType.ProcessItems:
                    return Feed(StationRegistry.Find<Machine>(d.TargetId) ?? StationRegistry.Find<Machine>());
                case TaskType.DeliverItems:
                    var station = StationRegistry.Find<IStation>(d.TargetId);
                    return station switch
                    {
                        SellDesk desk => Sell(desk),
                        Machine machine => Feed(machine),
                        _ => Feed(StationRegistry.Find<Machine>())
                    };
                case TaskType.SellItems:
                    if (string.IsNullOrEmpty(d.TargetId)) return Sell(BestDesk());
                    return Sell(DeskSelling(d.TargetId) ?? BestDesk(), i => i.Id == d.TargetId);
                case TaskType.ServeCustomers:
                    return Sell(StationRegistry.Find<SellDesk>(d.TargetId) ?? BestDesk());
                case TaskType.Overdrive:
                    var boosted = StationRegistry.Find<Machine>(d.TargetId) ?? StationRegistry.Find<Machine>();
                    return boosted != null ? Anchor(boosted.StationId, RoleBoost) : default;
                case TaskType.PurchaseUpgrade:
                case TaskType.ReachUpgradeLevel:
                case TaskType.HireWorker:
                    return Purchase(d.TargetId);
                default:
                    return Earn();
            }
        }

        Hint Purchase(string id)
        {
            if (upgrades == null || !upgrades.TryGet(id, out var u) || u.IsMaxed) return Earn();

            // Hires and expansions are paid by standing on their own tile.
            if (PurchaseTile.TryGet(id, out var tile))
                return tile.IsReady ? new Hint { Position = tile.transform.position, UpgradeId = id } : Earn();

            if (!upgrades.CanPurchase(u)) return Earn();
            var hint = new Hint { UpgradeId = id };
            if (panel != null && panel.IsOpen) return hint;
            if (GuideAnchor.TryGet(UpgradeTileAnchor, out var anchor)) hint.Position = anchor.transform.position;
            return hint;
        }

        /// <summary>Collect waiting cash first (richest pile), otherwise keep the best counter stocked.</summary>
        Hint Earn()
        {
            SellDesk richest = null;
            foreach (var s in StationRegistry.All)
                if (s is SellDesk desk && desk.CashPile != null && desk.CashPile.StoredCash > 0 &&
                    (richest == null || desk.CashPile.StoredCash > richest.CashPile.StoredCash))
                    richest = desk;
            if (richest != null) return Anchor(richest.StationId, RoleCash);
            return Sell(BestDesk());
        }

        /// <summary>
        /// Stock <paramref name="desk"/>, optionally with one material only (e.g. a "sell copper" task). A customer stuck
        /// at an empty counter blocks the whole line, so what they asked for comes first, even during a "sell copper" task.
        /// </summary>
        Hint Sell(SellDesk desk, Func<ItemDefinition, bool> only = null)
        {
            if (desk == null) return Feed(StationRegistry.Find<Machine>());
            if (desk.CustomersWaiting && desk.WaitingFor != null)
            {
                var asked = desk.WaitingFor;
                only = i => i == asked;
            }

            Func<ItemDefinition, bool> wanted = only == null ? desk.Sells : i => desk.Sells(i) && only(i);
            var stack = player.CarryStack;
            if (desk.CanTakeStock && stack.Contains(wanted)) return Anchor(desk.StationId, RoleIn);
            if (stack.IsFull || NeedsRoom(wanted)) return Unload();

            var storage = NearestStorage(wanted);
            if (storage != null) return Anchor(storage.StationId, RoleOut);
            // Nothing in stock anywhere: make some.
            var maker = MachineMaking(wanted);
            return maker != null ? Feed(maker, 0, InputsMaking(maker.Definition, wanted)) : Feed(StationRegistry.Find<Machine>());
        }

        /// <summary>
        /// Bring <paramref name="machine"/> its input. <paramref name="inputs"/> narrows a multi-input machine to the inputs
        /// that make what is wanted (a "sell copper ingots" task should not send the player for iron).
        /// </summary>
        Hint Feed(Machine machine, int depth = 0, Func<ItemDefinition, bool> inputs = null)
        {
            if (machine == null) return LooseOrScrap(null);
            var d = machine.Definition;
            Func<ItemDefinition, bool> input = inputs ?? d.Takes;
            var stack = player.CarryStack;
            // A jammed machine (full output, full hopper) needs its goods moved on before it needs more input.
            if (depth == 0 && Jammed(machine) && !stack.IsFull)
            {
                var drain = DrainOutputs(machine);
                if (drain.Position.HasValue) return drain;
            }

            if (stack.Contains(input) && (stack.IsFull || !NearbyLoose(input))) return Anchor(machine.StationId, RoleIn);
            if (stack.IsFull || NeedsRoom(input)) return Unload();

            // Get the input: loose on the ground, waiting in a storage, made by another machine, or cut from scrap.
            if (harvest != null && harvest.TryFindNearestCollectable(player.transform.position, searchRadius, input, out var pos))
                return new Hint { Position = pos };
            var storage = NearestStorage(input);
            if (storage != null) return Anchor(storage.StationId, RoleOut);
            var maker = MachineMaking(input);
            if (maker != null && maker != machine && depth < 3) return Feed(maker, depth + 1, InputsMaking(maker.Definition, input));
            return ScrapHint(NearestScrap(s => input(s.Definition.DropItem)));
        }

        /// <summary>Inputs of <paramref name="d"/> whose product is wanted (all inputs for single-input machines).</summary>
        static Func<ItemDefinition, bool> InputsMaking(MachineDefinition d, Func<ItemDefinition, bool> wanted) =>
            d.HasRecipes ? i => d.Takes(i) && wanted(d.OutputFor(i)) : d.Takes;

        /// <summary>The player's stack is full: send it to the nearest machine or counter that takes what they carry.</summary>
        Hint Unload()
        {
            var stack = player.CarryStack;
            Hint best = default;
            float bestSqr = float.MaxValue;
            Hint jammed = default;
            foreach (var s in StationRegistry.All)
            {
                bool takes = s switch
                {
                    Machine m => stack.Contains(m.CanAccept),
                    SellDesk desk => desk.CanTakeStock && stack.Contains(desk.Sells),
                    _ => false
                };
                // Remember a machine that would take our load if it weren't jammed: its pad spills a full stack.
                if (!takes && !jammed.Position.HasValue && s is Machine jm && stack.Contains(jm.Definition.Takes)) jammed = Anchor(jm.StationId, RoleIn);
                if (!takes) continue;
                var hint = Anchor(s.StationId, RoleIn);
                if (!hint.Position.HasValue) continue;
                float sqr = (hint.Position.Value - player.transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = hint;
            }

            if (best.Position.HasValue) return best;
            return jammed.Position.HasValue ? jammed : Anchor(StationRegistry.Find<Machine>()?.StationId, RoleIn);
        }

        /// <summary>
        /// The stack is nearly full of things that are not <paramref name="wanted"/> (leftover scrap while fetching bales):
        /// drop them where they belong first, or every trip carries one or two items.
        /// </summary>
        bool NeedsRoom(Func<ItemDefinition, bool> wanted)
        {
            var stack = player.CarryStack;
            if (stack.Count == 0 || stack.FreeSpace >= Mathf.Min(MinRoom, stack.Capacity / 2)) return false;
            if (stack.Contains(wanted)) return false;
            foreach (var s in StationRegistry.All)
                if ((s is Machine m && stack.Contains(m.CanAccept)) || (s is SellDesk desk && desk.CanTakeStock && stack.Contains(desk.Sells)))
                    return true;
            return false;
        }

        static bool Jammed(Machine m) => m.State == Machine.MachineState.Blocked || m.IsHopperFull;

        /// <summary>Where to take a jammed machine's goods: the storage holding them, then the desk that sells them.</summary>
        Hint DrainOutputs(Machine machine)
        {
            Func<ItemDefinition, bool> made = machine.Definition.Produces;
            var storage = NearestStorage(made);
            if (storage == null) return default;
            var stack = player.CarryStack;
            foreach (var s in StationRegistry.All)
                if (s is SellDesk desk && desk.CanTakeStock && stack.Contains(i => made(i) && desk.Sells(i)))
                    return Anchor(desk.StationId, RoleIn);
            return Anchor(storage.StationId, RoleOut);
        }

        /// <summary>
        /// The counter worth stocking now: one the player can already supply from their stack, else the one with stock
        /// waiting in a storage (highest-value goods first), else the first desk.
        /// </summary>
        SellDesk BestDesk()
        {
            SellDesk first = null, supplied = null;
            int bestValue = -1;
            var stack = player.CarryStack;
            foreach (var s in StationRegistry.All)
            {
                if (s is not SellDesk desk) continue;
                first ??= desk;
                if (desk.CanTakeStock && stack.Contains(desk.Sells)) return desk;
                int value = BestStoredValue(desk);
                if (value <= bestValue) continue;
                bestValue = value;
                supplied = desk;
            }

            return supplied ?? first;
        }

        static int BestStoredValue(SellDesk desk)
        {
            int best = -1;
            foreach (var s in StationRegistry.All)
                if (s is Storage storage)
                    best = Mathf.Max(best, storage.HighestValueOf(desk.Sells));
            return best;
        }

        SellDesk DeskSelling(string itemId)
        {
            foreach (var s in StationRegistry.All)
                if (s is SellDesk desk && desk.Definition != null && desk.Definition.SellsId(itemId)) return desk;
            return null;
        }

        Storage NearestStorage(Func<ItemDefinition, bool> filter)
        {
            Storage best = null;
            float bestSqr = float.MaxValue;
            foreach (var s in StationRegistry.All)
            {
                if (s is not Storage storage || storage.CountOf(filter) == 0) continue;
                float sqr = (storage.transform.position - player.transform.position).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = storage;
            }

            return best;
        }

        static Machine MachineMaking(Func<ItemDefinition, bool> filter)
        {
            foreach (var s in StationRegistry.All)
            {
                if (s is Machine m && m.Definition != null && m.Definition.ProducesAny(filter)) return m;
            }

            return null;
        }

        Hint LooseOrScrap(Func<ItemDefinition, bool> itemFilter, Func<ScrapObject, bool> scrapFilter = null)
        {
            if (harvest != null && harvest.TryFindNearestCollectable(player.transform.position, searchRadius, itemFilter, out var pos))
                return new Hint { Position = pos };
            return ScrapHint(NearestScrap(scrapFilter));
        }

        bool NearbyLoose(Func<ItemDefinition, bool> item) =>
            harvest != null && harvest.TryFindNearestCollectable(player.transform.position, 8f, item, out _);

        Hint ScrapHint(ScrapObject s) => s == null ? default : new Hint { Position = s.Center, Lift = s.TopHeight, Scrap = s };

        Hint Anchor(string stationId, string role) =>
            stationId != null && GuideAnchor.TryGet(GuideAnchor.IdFor(stationId, role), out var a) ? new Hint { Position = a.transform.position } : default;

        ScrapObject NearestScrap(Func<ScrapObject, bool> filter)
        {
            if (scrap == null) return null;
            ScrapObject best = null;
            float bestSqr = searchRadius * searchRadius;
            Vector3 origin = player.transform.position;
            foreach (var s in scrap.Registered)
            {
                if (s == null || !s.IsTargetable || (filter != null && !filter(s))) continue;
                float sqr = (s.transform.position - origin).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = s;
            }

            return best;
        }
    }
}
