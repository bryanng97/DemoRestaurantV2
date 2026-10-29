using System;

namespace DemoRestaurant.Logic.Progression
{
    /// <summary>Tuning for <see cref="RestaurantDemand"/> (rules.json → demand).</summary>
    public sealed class DemandRules
    {
        public double Start = 120;
        public double Min = 40;
        public double Max = 500;
        /// <summary>Added when a guest leaves happy (paid for their meal).</summary>
        public double PerHappyGuest = 1;
        /// <summary>Removed when a guest leaves unhappy (no seat, gave up queuing, food too slow, too much trash…).</summary>
        public double PerUnhappyGuest = 1;
        /// <summary>Lost per hour while nobody can serve (no chef or no waiter on duty).</summary>
        public double ClosedDecayPerHour = 10;
        /// <summary>Guests per minute for each point of demand (120 demand x 0.05 = 6 a minute, one every 10 s).</summary>
        public double GuestsPerMinutePerDemand = 0.05;
        /// <summary>Most guests that arrive in one local day (0 = no limit). Published from the BO game rules.</summary>
        public int DailyGuestCap;
    }

    /// <summary>
    /// The restaurant's popularity ("demand"). It decides how often guests arrive, and the guests decide it: each
    /// happy departure raises it, each unhappy one lowers it, and a restaurant nobody is serving slowly loses it.
    /// A well-run restaurant therefore gets busier, and a struggling one slows down until it copes.
    /// </summary>
    public sealed class RestaurantDemand
    {
        private readonly DemandRules _rules;

        public double Value { get; private set; }

        /// <summary>Popularity as players see it (reference game): points ÷ 10, so 120 points is 12.0 of 50.</summary>
        public double Display => Value / 10.0;

        /// <summary>Raised with the change (positive or negative).</summary>
        public event Action<double> Changed;

        public RestaurantDemand(DemandRules rules, double? value = null)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            Value = Clamp(value ?? rules.Start);
        }

        /// <summary>Extra cap from placed furniture (Phase 3 effects); read whenever the value is clamped.</summary>
        public Func<double> CapBonus { get; set; }

        public double Max => _rules.Max + Math.Max(0.0, CapBonus?.Invoke() ?? 0.0);
        public double Min => _rules.Min;

        /// <summary>0..1 of the maximum, for a bar.</summary>
        public float Fill => (float)(Value / Math.Max(1.0, Max));

        public double GuestsPerMinute => Value * _rules.GuestsPerMinutePerDemand;

        public void Restore(double value) => Value = Clamp(value);

        public void OnHappyGuest() => Add(_rules.PerHappyGuest);
        public void OnUnhappyGuest() => Add(-_rules.PerUnhappyGuest);

        /// <summary>Time passed with nobody able to serve.</summary>
        public void OnClosed(TimeSpan time)
        {
            if (time > TimeSpan.Zero) Add(-_rules.ClosedDecayPerHour * time.TotalHours);
        }

        private void Add(double delta)
        {
            double before = Value;
            Value = Clamp(Value + delta);
            if (Value != before) Changed?.Invoke(Value - before);
        }

        private double Clamp(double v) => Math.Max(_rules.Min, Math.Min(Max, v));
    }
}
