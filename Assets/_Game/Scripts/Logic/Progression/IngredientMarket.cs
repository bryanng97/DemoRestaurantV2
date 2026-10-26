using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Logic.Progression
{
    /// <summary>
    /// Free ingredient baskets that restock one at a time on a timer (Restaurant City's daily ingredient habit,
    /// on a short loop).
    /// Grants are client-side for now; with Nakama the server owns the timer and the rolls.
    /// </summary>
    public sealed class IngredientMarket
    {
        private readonly IReadOnlyList<IngredientDef> _pool;
        private readonly int _totalWeight;
        private readonly Random _random;
        private float _restockTimer;

        public int Capacity { get; }
        public float RestockSeconds { get; }
        public int ReadyBaskets { get; private set; }
        /// <summary>Seconds until the next basket is ready (0 when all baskets are full).</summary>
        public float NextRestockIn => ReadyBaskets >= Capacity ? 0f : _restockTimer;

        public event Action Changed;

        /// <summary>Restores a saved state.</summary>
        public void Restore(int readyBaskets, float restockTimer)
        {
            ReadyBaskets = Math.Max(0, Math.Min(Capacity, readyBaskets));
            _restockTimer = restockTimer > 0f && restockTimer <= RestockSeconds ? restockTimer : RestockSeconds;
            Changed?.Invoke();
        }

        /// <summary>Seconds left on the current restock timer (saved with the game).</summary>
        public float RestockTimer => _restockTimer;

        public IngredientMarket(IReadOnlyList<IngredientDef> pool, int baskets, float restockSeconds, Random random, bool startFull = true)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            if (baskets < 1) throw new ArgumentOutOfRangeException(nameof(baskets));
            if (restockSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(restockSeconds));
            foreach (var i in pool) _totalWeight += i.DropWeight;
            if (_totalWeight <= 0) throw new ArgumentException("Ingredient pool needs a positive total drop weight.", nameof(pool));
            Capacity = baskets;
            RestockSeconds = restockSeconds;
            ReadyBaskets = startFull ? baskets : 0;
            _restockTimer = restockSeconds;
        }

        public void Tick(float dt)
        {
            if (ReadyBaskets >= Capacity || dt <= 0)
                return;
            _restockTimer -= dt;
            bool changed = false;
            while (_restockTimer <= 0f && ReadyBaskets < Capacity)
            {
                ReadyBaskets++;
                _restockTimer += RestockSeconds;
                changed = true;
            }
            if (ReadyBaskets >= Capacity)
                _restockTimer = RestockSeconds;
            if (changed)
                Changed?.Invoke();
        }

        /// <summary>Empties every ready basket into the inventory. Returns what was collected.</summary>
        public List<IngredientDef> Collect(Inventory inventory)
        {
            var got = new List<IngredientDef>();
            for (int i = 0; i < ReadyBaskets; i++)
            {
                var ingredient = Roll();
                inventory.Add(ingredient);
                got.Add(ingredient);
            }
            if (ReadyBaskets > 0)
            {
                ReadyBaskets = 0;
                _restockTimer = RestockSeconds;
                Changed?.Invoke();
            }
            return got;
        }

        /// <summary>Weighted random ingredient (common ones more often).</summary>
        public IngredientDef Roll()
        {
            int pick = _random.Next(_totalWeight);
            foreach (var i in _pool)
            {
                pick -= i.DropWeight;
                if (pick < 0)
                    return i;
            }
            return _pool[_pool.Count - 1];
        }
    }
}
