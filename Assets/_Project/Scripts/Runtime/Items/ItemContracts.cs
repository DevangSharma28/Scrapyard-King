using System;

namespace ScrapYardKing.Items
{
    /// <summary>Anything that physically takes <see cref="WorldItem"/>s: machine hoppers, conveyors, storage, counters, carry stacks.</summary>
    public interface IItemReceiver
    {
        /// <summary>True when this receiver takes <paramref name="item"/> and has room right now.</summary>
        bool CanAccept(ItemDefinition item);

        /// <summary>Takes ownership of <paramref name="item"/> and animates it into place. Call only after <see cref="CanAccept"/>.</summary>
        void Accept(WorldItem item);
    }

    /// <summary>Anything items can be pulled out of: storage, output trays, carry stacks.</summary>
    public interface IItemSource
    {
        int Count { get; }

        /// <summary>Removes and returns the top-most item matching <paramref name="filter"/> (null = any), or null. The caller moves it.</summary>
        WorldItem Take(Func<ItemDefinition, bool> filter);
    }
}
