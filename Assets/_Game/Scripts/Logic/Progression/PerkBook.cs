using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Simulation;

namespace DemoRestaurant.Logic.Progression
{
    /// <summary>A perk running now: one item's timed effects on one employee, or on the restaurant (Staff null).</summary>
    public sealed class ActivePerk
    {
        public ItemDef Item { get; }
        public Staff Staff { get; }
        public DateTime StartedUtc { get; }
        public DateTime ExpiresUtc { get; }
        public PerkEffects Effects => Item.Effects;

        public ActivePerk(ItemDef item, Staff staff, DateTime startedUtc, DateTime expiresUtc)
        {
            Item = item ?? throw new ArgumentNullException(nameof(item));
            Staff = staff;
            StartedUtc = startedUtc;
            ExpiresUtc = expiresUtc;
        }

        public TimeSpan Remaining(DateTime utc) => ExpiresUtc > utc ? ExpiresUtc - utc : TimeSpan.Zero;
    }

    public enum PerkCheck
    {
        Ok,
        /// <summary>BO → Game Rules → Perks is off.</summary>
        Disabled,
        /// <summary>As many perks are active on that employee (or the restaurant) as the rules allow.</summary>
        SlotsFull,
        /// <summary>A restaurant perk used on an employee, or the other way round.</summary>
        WrongTarget,
    }

    /// <summary>
    /// Perks, as the reference game's snacks: a staff item with timed effects starts a perk on one employee or on the
    /// whole restaurant for its duration (wall clock, so it also runs while the game is closed). Up to the rules'
    /// slots run at once on each employee and on the restaurant; effects of the same kind add up. The server keeps
    /// the same perks and applies the XP, popularity and energy-drain effects itself.
    /// </summary>
    public sealed class PerkBook
    {
        private readonly Func<PerkRules> _rules;
        private readonly List<ActivePerk> _active = new List<ActivePerk>();

        public PerkBook(Func<PerkRules> rules)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
        }

        /// <summary>Perks not yet expired at the last <see cref="Prune"/>, soonest to end first.</summary>
        public IReadOnlyList<ActivePerk> Active => _active;

        public void Prune(DateTime utc) => _active.RemoveAll(p => p.ExpiresUtc <= utc);

        /// <summary>Perks running on this employee (or, for null, on the restaurant).</summary>
        public int CountOn(Staff staff, DateTime utc)
        {
            int n = 0;
            foreach (var p in _active)
                if (p.Staff == staff && p.ExpiresUtc > utc) n++;
            return n;
        }

        public PerkCheck Check(ItemDef item, Staff staff, DateTime utc)
        {
            if (item == null || !item.IsPerk) return PerkCheck.Ok;
            if ((item.Target == ItemTarget.Restaurant) != (staff == null)) return PerkCheck.WrongTarget;
            var rules = _rules();
            if (!rules.Enabled) return PerkCheck.Disabled;
            int slots = staff == null ? rules.SlotsRestaurant : rules.SlotsPerEmployee;
            return CountOn(staff, utc) >= slots ? PerkCheck.SlotsFull : PerkCheck.Ok;
        }

        /// <summary>Starts the item's perk now; call after <see cref="Check"/> returned Ok.</summary>
        public ActivePerk Start(ItemDef item, Staff staff, DateTime utc)
        {
            var perk = new ActivePerk(item, staff, utc, utc.AddMinutes(item.DurationMinutes));
            Add(perk);
            return perk;
        }

        /// <summary>Restoring a save or a server state.</summary>
        public void Add(ActivePerk perk)
        {
            if (perk == null) return;
            int i = 0;
            while (i < _active.Count && _active[i].ExpiresUtc <= perk.ExpiresUtc) i++;
            _active.Insert(i, perk);
        }

        public void Clear() => _active.Clear();

        /// <summary>A dismissed employee's perks end with them.</summary>
        public void RemoveStaff(Staff staff) => _active.RemoveAll(p => staff != null && p.Staff == staff);

        /// <summary>One effect on an employee right now: their own perks plus the restaurant's.</summary>
        public int StaffTotal(Staff staff, DateTime utc, Func<PerkEffects, int> field)
        {
            int total = 0;
            foreach (var p in _active)
                if ((p.Staff == null || p.Staff == staff) && p.StartedUtc <= utc && p.ExpiresUtc > utc) total += field(p.Effects);
            return total;
        }

        /// <summary>One effect of the restaurant's own perks right now (XP, popularity).</summary>
        public int RestaurantTotal(DateTime utc, Func<PerkEffects, int> field)
        {
            int total = 0;
            foreach (var p in _active)
                if (p.Staff == null && p.StartedUtc <= utc && p.ExpiresUtc > utc) total += field(p.Effects);
            return total;
        }
    }
}
