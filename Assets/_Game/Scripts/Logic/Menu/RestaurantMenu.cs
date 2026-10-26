using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Logic.Menu
{
    public enum MenuChange
    {
        Added,
        Removed,
        /// <summary>All slots for the dish's category are taken; remove a dish of that category first.</summary>
        MenuFull,
        /// <summary>The menu must keep at least one dish, or customers would have nothing to order.</summary>
        LastDish,
        /// <summary>The dish is not in this restaurant's catalogue.</summary>
        NotAvailable,
    }

    /// <summary>
    /// The dishes a restaurant is currently serving: a subset of the dishes it knows, never empty. Customers order
    /// only from <see cref="Active"/>.
    /// Slots are per category (starter, main, dessert, drink) and come from the restaurant level, so they grow as it
    /// levels up (<see cref="RestaurantMenu(IReadOnlyList{DishDef}, Func{DishCategory, int}, IEnumerable{DishDef})"/>).
    /// A category with no slots (drinks before they unlock) can't be served. The single-capacity constructor
    /// treats all dishes as sharing one pool (tests and the stress test).
    /// </summary>
    public sealed class RestaurantMenu
    {
        public static readonly DishCategory[] Categories =
            { DishCategory.Starter, DishCategory.Main, DishCategory.Dessert, DishCategory.Drink };

        private readonly List<DishDef> _active = new List<DishDef>();
        private readonly List<DishDef> _availableList;
        private readonly HashSet<DishDef> _available;
        private readonly Func<DishCategory, int> _slotsFor;
        private readonly int _sharedCapacity;

        /// <summary>Dishes the restaurant knows how to cook (grows as recipes are discovered).</summary>
        public IReadOnlyList<DishDef> Available => _availableList;
        public IReadOnlyList<DishDef> Active => _active;
        /// <summary>Total slots across all categories right now.</summary>
        public int Capacity
        {
            get
            {
                if (_slotsFor == null) return _sharedCapacity;
                int total = 0;
                foreach (var c in Categories) total += Math.Max(0, _slotsFor(c));
                return total;
            }
        }
        public bool IsFull => _active.Count >= Capacity;
        public bool HasCategories => _slotsFor != null;

        public event Action Changed;

        /// <summary>One shared pool of <paramref name="capacity"/> slots for every category.</summary>
        public RestaurantMenu(IReadOnlyList<DishDef> available, int capacity, IEnumerable<DishDef> initial = null)
            : this(available, null, capacity, initial)
        {
        }

        /// <summary>Per-category slots; <paramref name="slotsFor"/> is read every time, so it can follow the level.</summary>
        public RestaurantMenu(IReadOnlyList<DishDef> available, Func<DishCategory, int> slotsFor, IEnumerable<DishDef> initial = null)
            : this(available, slotsFor ?? throw new ArgumentNullException(nameof(slotsFor)), 0, initial)
        {
        }

        private RestaurantMenu(IReadOnlyList<DishDef> available, Func<DishCategory, int> slotsFor, int capacity,
            IEnumerable<DishDef> initial)
        {
            if (available == null) throw new ArgumentNullException(nameof(available));
            if (available.Count == 0)
                throw new ArgumentException("A menu needs at least one available dish.", nameof(available));
            if (slotsFor == null && capacity < 1)
                throw new ArgumentOutOfRangeException(nameof(capacity), "A menu needs at least one slot.");
            _slotsFor = slotsFor;
            _sharedCapacity = capacity;
            _availableList = new List<DishDef>(available);
            _available = new HashSet<DishDef>(available);

            foreach (var dish in initial ?? available)
                if (_available.Contains(dish) && !_active.Contains(dish) && HasRoomFor(dish))
                    _active.Add(dish);
            if (_active.Count == 0)
                _active.Add(available[0]);
        }

        public bool IsActive(DishDef dish) => _active.Contains(dish);
        public bool IsAvailable(DishDef dish) => dish != null && _available.Contains(dish);

        /// <summary>Slots for a category right now (shared capacity when the menu has no categories).</summary>
        public int SlotsFor(DishCategory category) => _slotsFor?.Invoke(category) ?? _sharedCapacity;

        /// <summary>Dishes of a category on the menu now.</summary>
        public int ActiveCount(DishCategory category)
        {
            if (_slotsFor == null) return _active.Count;
            int n = 0;
            foreach (var d in _active) if (d.Category == category) n++;
            return n;
        }

        /// <summary>True when another dish of this dish's category fits.</summary>
        public bool HasRoomFor(DishDef dish) => ActiveCount(dish.Category) < SlotsFor(dish.Category);

        /// <summary>Adds a newly learned dish to the catalogue. It goes straight onto the menu if its category has room.</summary>
        public void MakeAvailable(DishDef dish)
        {
            if (dish == null || !_available.Add(dish))
                return;
            _availableList.Add(dish);
            if (HasRoomFor(dish))
                _active.Add(dish);
            Changed?.Invoke();
        }

        /// <summary>
        /// Call after the slot counts change (level-up): known dishes fill the new slots, so a fresh slot is never
        /// left empty while a dish for it is known.
        /// </summary>
        public void FillFreeSlots()
        {
            bool changed = false;
            foreach (var d in _availableList)
                if (!_active.Contains(d) && HasRoomFor(d))
                {
                    _active.Add(d);
                    changed = true;
                }
            if (changed) Changed?.Invoke();
        }

        /// <summary>Replaces today's dishes (loading a save). Unknown dishes are skipped; never leaves the menu empty.</summary>
        public void SetActive(IEnumerable<DishDef> dishes)
        {
            var before = new List<DishDef>(_active);
            _active.Clear();
            foreach (var d in dishes)
                if (_available.Contains(d) && !_active.Contains(d) && HasRoomFor(d))
                    _active.Add(d);
            if (_active.Count == 0)
            {
                _active.AddRange(before);
                return;
            }
            Changed?.Invoke();
        }

        public MenuChange Toggle(DishDef dish) => IsActive(dish) ? TryRemove(dish) : TryAdd(dish);

        public MenuChange TryAdd(DishDef dish)
        {
            if (dish == null || !_available.Contains(dish))
                return MenuChange.NotAvailable;
            if (_active.Contains(dish))
                return MenuChange.Added;
            if (!HasRoomFor(dish))
                return MenuChange.MenuFull;
            _active.Add(dish);
            Changed?.Invoke();
            return MenuChange.Added;
        }

        public MenuChange TryRemove(DishDef dish)
        {
            if (dish == null || !_available.Contains(dish))
                return MenuChange.NotAvailable;
            if (!_active.Contains(dish))
                return MenuChange.Removed;
            if (_active.Count == 1)
                return MenuChange.LastDish;
            _active.Remove(dish);
            Changed?.Invoke();
            return MenuChange.Removed;
        }
    }
}
