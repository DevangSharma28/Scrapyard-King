using System;
using ScrapYardKing.Core;
using ScrapYardKing.Economy;
using ScrapYardKing.Factory;
using ScrapYardKing.Harvest;
using ScrapYardKing.Items;
using ScrapYardKing.Player;
using ScrapYardKing.Tiles;
using ScrapYardKing.UI;
using ScrapYardKing.Workers;
using ScrapYardKing.World;
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
        /// <summary>
        /// Cash waiting on a pallet is worth a walk once it is a quarter of the price being saved for (between these two
        /// bounds), or as soon as it covers the price. A fixed small amount sent the player across the yard every half
        /// minute late in the session (measured: income halved while saving for the Truck Dock).
        /// </summary>
        const long WorthCollectingMin = 150, WorthCollectingMax = 2500;
        // While supplying (no purchase to save for) a cash pile is worth a walk from this much.
        const long SupplyCashTrip = 500;
        /// <summary>
        /// Goods waiting in bins are worth a hand-carried trip from this many on. Below it the player would shuttle two
        /// bales at a time while nobody cuts scrap (measured: every machine and worker idle, income a third of normal).
        /// </summary>
        const int WorthCarrying = 6;

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
        EconomyManager economy;
        WorkerManager workers;
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
            Services.TryGet(out economy);
            Services.TryGet(out workers);
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
                    // The task names the scrap, so distance is no limit: it may stand in another area (heavy vehicles while
                    // the player is at the market; without this the marker simply vanished).
                    var target = NearestScrap(s => d.Targets(s.Definition.Id), float.MaxValue, true);
                    // Event scrap: go to the giant wherever it stands; while it is not here yet, break the heavy scrap that calls it in.
                    if (target == null && Services.TryGet(out GiantScrapEvent giant) && giant.Config != null && giant.Config.Giant != null &&
                        giant.Config.Giant.Id == d.TargetId)
                        target = giant.Giant != null && giant.Giant.IsTargetable ? giant.Giant : NearestScrap(s => giant.IsFeeder(s.Definition), float.MaxValue, true);
                    // Too tough for the current chainsaw: the next step is the upgrade, not the scrap.
                    if (target != null && player.Stats.CutPower < target.Definition.MinCutPower) return Purchase(ChainsawUpgrade);
                    // None standing right now (all cut, waiting to respawn): keep the yard running meanwhile.
                    return target != null ? ScrapHint(target) : Earn();
                case TaskType.CollectItems:
                    // A full stack cannot collect; empty it where it is useful first.
                    if (player.CarryStack.IsFull) return Unload();
                    return LooseOrScrap(item => d.Targets(item.Id));
                // A station that a hired worker supplies does not need the player's hands: the task moves on through the
                // worker, and the player does what keeps the whole chain fed (Supply). Without this the player hauled a few
                // pieces at a time beside the Smelter while nobody cut scrap: "Smelt 12 iron ingots" took 17 minutes
                // with the yard's income at zero (run r7_run1).
                case TaskType.ProcessItems:
                    var fed = StationRegistry.Find<Machine>(d.TargetId) ?? StationRegistry.Find<Machine>();
                    return Staffed(fed) ? Supply() : Feed(fed);
                case TaskType.DeliverItems:
                    var station = StationRegistry.Find<IStation>(d.TargetId);
                    return station switch
                    {
                        SellDesk desk => Staffed(desk) ? Supply() : Sell(desk),
                        Machine machine => Staffed(machine) ? Supply() : Feed(machine),
                        TruckBay bay => Staffed(bay) ? Supply() : Ship(bay),
                        _ => Feed(StationRegistry.Find<Machine>())
                    };
                case TaskType.SellItems:
                    if (string.IsNullOrEmpty(d.TargetId)) return SellOrSupply(BestDesk(), null);
                    return SellOrSupply(DeskSelling(d.TargetId) ?? BestDesk(), i => i.Id == d.TargetId);
                case TaskType.ServeCustomers:
                    return SellOrSupply(StationRegistry.Find<SellDesk>(d.TargetId) ?? BestDesk(), null);
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
                return tile.IsReady ? new Hint { Position = tile.transform.position, UpgradeId = id } : Earn(tile.IsUnlocked ? tile.Remaining : 0);

            if (!upgrades.CanPurchase(u)) return Earn(upgrades.IsUnlocked(u) ? u.NextCost : 0);
            var hint = new Hint { UpgradeId = id };
            if (panel != null && panel.IsOpen) return hint;
            if (GuideAnchor.TryGet(UpgradeTileAnchor, out var anchor)) hint.Position = anchor.transform.position;
            return hint;
        }

        /// <summary>
        /// The task needs cash (<paramref name="needed"/> = the price being saved for, 0 = just earn). Pick the most
        /// productive thing the player can do: fetch waiting cash once it is worth the walk or covers the price, unblock a
        /// customer stuck at an empty counter, do any link of the chain that no worker is hired for (nearest to the cash
        /// first), and otherwise do the one job no worker ever does: cut scrap and keep the first machine fed.
        /// Hand-stocking counters that a Helper already serves starved the yard (measured: about a quarter of the income).
        /// </summary>
        Hint Earn(long needed = 0)
        {
            IStation richest = null;
            long most = 0, waiting = 0;
            foreach (var s in StationRegistry.All)
            {
                var pile = s switch
                {
                    SellDesk desk => desk.CashPile,
                    TruckBay bay => bay.CashPile,
                    _ => null
                };
                if (pile == null) continue;
                waiting += pile.StoredCash;
                if (pile.StoredCash <= most) continue;
                most = pile.StoredCash;
                richest = s;
            }

            long wallet = economy != null ? economy.Cash : 0;
            bool covers = needed > 0 && wallet < needed && wallet + waiting >= needed;
            long worth = System.Math.Clamp(needed / 4, WorthCollectingMin, WorthCollectingMax);
            if (richest != null && (covers || needed <= 0 || most >= worth)) return Anchor(richest.StationId, RoleCash);

            var first = StationRegistry.Find<Machine>();
            // Counters: a stuck customer always; a half-empty counter when nobody is hired to stock it.
            foreach (var s in StationRegistry.All)
                if (s is SellDesk desk && desk.CanTakeStock && CanSupply(desk) &&
                    (desk.CustomersWaiting || (!Staffed(desk) && desk.Stock * 2 <= desk.Stats.counterCapacity)))
                    return Sell(desk);
            // Machines down the line that nobody feeds and that are running dry while their input waits in a bin.
            foreach (var s in StationRegistry.All)
                if (s is Machine m && m != first && !Staffed(m) && m.QueuedInputs < MinRoom && !Jammed(m) &&
                    (player.CarryStack.Contains(m.Definition.Takes) || Stored(m.Definition.Takes) >= WorthCarrying))
                    return Feed(m);

            var feed = Feed(first);
            if (feed.Position.HasValue) return feed;
            if (richest != null) return Anchor(richest.StationId, RoleCash);
            return Sell(BestDesk());
        }

        bool Staffed(IItemReceiver station) => station != null && workers != null && workers.Serves(station);

        /// <summary>
        /// The most useful job while workers carry the task forward: fetch cash once a pile is worth the walk, help a
        /// stuck customer, do a link nobody is hired for, otherwise cut scrap and feed the first machine.
        /// </summary>
        Hint Supply() => Earn(SupplyCashTrip * 4);

        Hint SellOrSupply(SellDesk desk, Func<ItemDefinition, bool> only) => Staffed(desk) ? Supply() : Sell(desk, only);

        /// <summary>The player carries, or the bins hold a load worth carrying of, something this desk sells (what its waiting customer asked for, if known).</summary>
        bool CanSupply(SellDesk desk)
        {
            var asked = desk.WaitingFor;
            Func<ItemDefinition, bool> wanted = asked != null ? i => i == asked : desk.Sells;
            return player.CarryStack.Contains(wanted) || Stored(wanted) >= WorthCarrying;
        }

        /// <summary>Units matching <paramref name="filter"/> in all storages.</summary>
        static int Stored(Func<ItemDefinition, bool> filter)
        {
            int total = 0;
            foreach (var s in StationRegistry.All)
                if (s is Storage storage) total += storage.CountOf(filter);
            return total;
        }

        /// <summary>Load the truck: carry what it buys to the dock pad (and wait there while the truck is on the road).</summary>
        Hint Ship(TruckBay bay)
        {
            Func<ItemDefinition, bool> wanted = bay.Takes;
            var stack = player.CarryStack;
            var storage = NearestStorage(wanted);
            // Top the stack up first when the goods are waiting; a truck is a big order.
            if (stack.Contains(wanted) && (stack.IsFull || storage == null)) return Anchor(bay.StationId, RoleIn);
            if (stack.IsFull || NeedsRoom(wanted)) return Unload();
            if (storage != null) return Anchor(storage.StationId, RoleOut);
            if (stack.Contains(wanted)) return Anchor(bay.StationId, RoleIn);
            var maker = MachineMaking(wanted);
            return maker != null ? Feed(maker, 0, InputsMaking(maker.Definition, wanted)) : Earn();
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
                    TruckBay bay => stack.Contains(bay.CanAccept),
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
            // "Nearly full" = less than half the stack free (at least MinRoom): with a big backpack, 16 leftover pieces on a
            // 20-stack still left four free slots, and the player ferried goods four at a time for minutes.
            if (stack.Count == 0 || stack.FreeSpace >= Mathf.Max(MinRoom, stack.Capacity / 2)) return false;
            if (stack.Contains(wanted)) return false;
            foreach (var s in StationRegistry.All)
                if ((s is Machine m && stack.Contains(m.CanAccept)) || (s is SellDesk desk && desk.CanTakeStock && stack.Contains(desk.Sells)) ||
                    (s is TruckBay bay && stack.Contains(bay.CanAccept)))
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

        /// <summary>
        /// Nearest scrap that passes <paramref name="filter"/>. Scrap the chainsaw cannot cut yet is skipped unless
        /// <paramref name="anyToughness"/> is set: only a task that names the object wants to hear about it (and turns it
        /// into "upgrade the chainsaw"); sending the player to earn at something they cannot cut is a dead end.
        /// </summary>
        ScrapObject NearestScrap(Func<ScrapObject, bool> filter, float maxDistance = -1f, bool anyToughness = false)
        {
            if (scrap == null) return null;
            ScrapObject best = null;
            float limit = maxDistance < 0f ? searchRadius : maxDistance;
            float bestSqr = limit >= float.MaxValue ? float.MaxValue : limit * limit;
            Vector3 origin = player.transform.position;
            float power = player.Stats.CutPower;
            foreach (var s in scrap.Registered)
            {
                if (s == null || !s.IsTargetable || (filter != null && !filter(s))) continue;
                if (!anyToughness && power < s.Definition.MinCutPower) continue;
                float sqr = (s.transform.position - origin).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = s;
            }

            return best;
        }
    }
}
