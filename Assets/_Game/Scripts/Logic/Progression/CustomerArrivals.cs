using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Logic.Progression
{
    /// <summary>
    /// Decides when guests walk in: at the rate the restaurant's demand sets (<see cref="RestaurantDemand"/>), so a
    /// popular restaurant gets busier. There is no daily limit. Guest types unlock by restaurant level.
    /// </summary>
    public sealed class CustomerArrivals
    {
        private readonly IReadOnlyList<CustomerTypeDef> _types;
        private readonly Func<int> _level;
        private readonly Func<double> _guestsPerMinute;
        private readonly Random _random;
        private float _arrivalBudget;

        public CustomerArrivals(IReadOnlyList<CustomerTypeDef> types, Func<int> level, Func<double> guestsPerMinute, Random random)
        {
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _level = level ?? throw new ArgumentNullException(nameof(level));
            _guestsPerMinute = guestsPerMinute ?? throw new ArgumentNullException(nameof(guestsPerMinute));
            _random = random ?? throw new ArgumentNullException(nameof(random));
        }

        public double GuestsPerMinute => Math.Max(0.0, _guestsPerMinute());

        /// <summary>Seconds between guests right now (infinite when nobody comes).</summary>
        public double IntervalSeconds => GuestsPerMinute > 0 ? 60.0 / GuestsPerMinute : double.PositiveInfinity;

        /// <summary>Advances time and returns the guest types arriving now (usually none or one).</summary>
        public void Tick(float dt, List<CustomerTypeDef> arrivingOut)
        {
            arrivingOut.Clear();
            if (dt <= 0)
                return;
            _arrivalBudget += (float)(dt * GuestsPerMinute / 60.0);
            while (_arrivalBudget >= 1f)
            {
                _arrivalBudget -= 1f;
                var type = PickType();
                if (type == null)
                {
                    _arrivalBudget = 0f;
                    break;
                }
                arrivingOut.Add(type);
            }
        }

        /// <summary>Weighted pick among guest types unlocked at the current level.</summary>
        public CustomerTypeDef PickType()
        {
            int level = _level(), total = 0;
            foreach (var t in _types)
                if (t.MinLevel <= level) total += t.SpawnWeight;
            if (total <= 0)
                return null;
            int pick = _random.Next(total);
            foreach (var t in _types)
            {
                if (t.MinLevel > level) continue;
                pick -= t.SpawnWeight;
                if (pick < 0) return t;
            }
            return null;
        }
    }
}
