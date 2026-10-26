using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Save
{
    public enum LedgerKind
    {
        Sale,
        IdleIncome,
        MarketCollect,
        /// <summary>A garden seed bought (amount: coins; detail: plot index).</summary>
        GardenSeed,
        /// <summary>A garden plot watered (detail: plot index).</summary>
        GardenWater,
        /// <summary>A crop harvested (amount: count; detail: ingredient id).</summary>
        GardenHarvest,
        RecipeDiscovered,
        StaffHired,
        StaffDismissed,
        ItemBought,
        ItemUsed,
        Renamed,
        AvatarChanged,
        /// <summary>The player's avatar took another job in the restaurant (detail: staff type id).</summary>
        OwnerJobChanged,
        /// <summary>Coins for reaching a restaurant level (detail: the level).</summary>
        LevelReward,
        ExteriorSaved,
        RecipeUpgraded,
        EntertainmentIncome,
    }

    /// <summary>One thing the player did (or earned) while playing offline.</summary>
    [Serializable]
    public sealed class LedgerEntry
    {
        public string kind;
        /// <summary>Coins, item count or seconds, depending on the kind.</summary>
        public long amount;
        /// <summary>Dish, ingredient or staff type id, if any.</summary>
        public string refId;
        public string timeUtc;
    }

    /// <summary>
    /// Append-only log of everything that changed the player's state while the game ran without the backend.
    /// The game is fully playable offline; on the next sync the backend replays and validates this log
    /// (prices from its own catalogue, idle time against its own clock) and returns the corrected state.
    /// Capped so a long offline stretch can't grow the save without bound; consecutive sales are merged.
    /// </summary>
    public sealed class OfflineLedger
    {
        public const int MaxEntries = 2000;

        private readonly List<LedgerEntry> _entries = new List<LedgerEntry>();

        public IReadOnlyList<LedgerEntry> Entries => _entries;
        public int Dropped { get; private set; }

        public void Record(LedgerKind kind, long amount, string refId, DateTime nowUtc)
        {
            string kindName = kind.ToString();
            string time = nowUtc.ToString("o");

            // Merge a run of sales of the same dish into one entry to keep the log small.
            if (_entries.Count > 0)
            {
                var last = _entries[_entries.Count - 1];
                if (kind == LedgerKind.Sale && last.kind == kindName && last.refId == refId)
                {
                    last.amount += amount;
                    last.timeUtc = time;
                    return;
                }
            }

            if (_entries.Count >= MaxEntries)
            {
                _entries.RemoveAt(0);
                Dropped++;
            }
            _entries.Add(new LedgerEntry { kind = kindName, amount = amount, refId = refId, timeUtc = time });
        }

        /// <summary>Called after a successful backend sync.</summary>
        public void Clear()
        {
            _entries.Clear();
            Dropped = 0;
        }

        public void Restore(IEnumerable<LedgerEntry> entries, int dropped)
        {
            _entries.Clear();
            if (entries != null)
                _entries.AddRange(entries);
            Dropped = Math.Max(0, dropped);
        }
    }
}
