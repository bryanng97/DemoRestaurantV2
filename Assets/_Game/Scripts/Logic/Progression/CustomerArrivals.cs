using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Logic.Progression
{
    /// <summary>
    /// Decides when guests walk in: at the rate the restaurant's demand sets (<see cref="RestaurantDemand"/>), so a
    /// popular restaurant gets busier. An optional daily limit (BO game rules) stops arrivals for the rest of the day.
    /// Guest types unlock by restaurant level.
    /// </summary>
    public sealed class CustomerArrivals
    {
        private readonly IReadOnlyList<CustomerTypeDef> _types;
        private readonly Func<int> _level;
        private readonly Func<double> _guestsPerMinute;
        private readonly Random _random;
        private readonly Func<int> _dailyCap;
        private readonly Func<long> _day;
        private float _arrivalBudget;

        /// <param name="dailyCap">Most guests per day (0 or less = no limit); null = no limit.</param>
        /// <param name="day">The current day number the limit counts in; null = one day forever.</param>
        public CustomerArrivals(IReadOnlyList<CustomerTypeDef> types, Func<int> level, Func<double> guestsPerMinute, Random random,
            Func<int> dailyCap = null, Func<long> day = null)
        {
            _types = types ?? throw new ArgumentNullException(nameof(types));
            _level = level ?? throw new ArgumentNullException(nameof(level));
            _guestsPerMinute = guestsPerMinute ?? throw new ArgumentNullException(nameof(guestsPerMinute));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            _dailyCap = dailyCap;
            _day = day;
        }

        /// <summary>The day <see cref="ArrivedToday"/> counts in.</summary>
        public long Day { get; private set; }
        /// <summary>Guests who arrived on <see cref="Day"/>.</summary>
        public int ArrivedToday { get; private set; }

        private int DailyCap => Math.Max(0, _dailyCap?.Invoke() ?? 0);

        /// <summary>True while today's guest limit is used up.</summary>
        public bool DailyCapReached
        {
            get
            {
                RollDay();
                return DailyCap > 0 && ArrivedToday >= DailyCap;
            }
        }

        /// <summary>Restores the saved count (ignored when it belongs to another day).</summary>
        public void Restore(long day, int arrived)
        {
            Day = day;
            ArrivedToday = Math.Max(0, arrived);
            RollDay();
        }

        private void RollDay()
        {
            long today = _day?.Invoke() ?? 0;
            if (today == Day) return;
            Day = today;
            ArrivedToday = 0;
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
                if (DailyCapReached)
                {
                    _arrivalBudget = 0f;
                    break;
                }
                var type = PickType();
                if (type == null)
                {
                    _arrivalBudget = 0f;
                    break;
                }
                arrivingOut.Add(type);
                ArrivedToday++;
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
