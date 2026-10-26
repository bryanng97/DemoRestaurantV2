using System.Collections.Generic;

namespace DemoRestaurant.Logic.Economy
{
    /// <summary>
    /// Rolling earnings stats for the restaurant HUD: coins per minute over a sliding window,
    /// plus session totals. Time is passed in (game seconds), so it is deterministic and testable.
    /// </summary>
    public sealed class EarningsTracker
    {
        private readonly Queue<(float time, int coins)> _window = new Queue<(float, int)>();
        private readonly float _windowSeconds;
        private long _windowCoins;
        private float _firstSaleTime = -1f;

        public long SessionCoins { get; private set; }
        public int CustomersServed { get; private set; }

        public EarningsTracker(float windowSeconds = 60f)
        {
            _windowSeconds = windowSeconds > 0 ? windowSeconds : 60f;
        }

        public void Record(float time, int coins)
        {
            if (_firstSaleTime < 0)
                _firstSaleTime = time;
            _window.Enqueue((time, coins));
            _windowCoins += coins;
            SessionCoins += coins;
            CustomersServed++;
        }

        /// <summary>
        /// Coins per minute over the last window. Before a full window has passed, scales by the time
        /// since the first sale, so the rate settles quickly instead of creeping up for a minute.
        /// </summary>
        public float CoinsPerMinute(float now)
        {
            while (_window.Count > 0 && _window.Peek().time <= now - _windowSeconds)
                _windowCoins -= _window.Dequeue().coins;

            if (_firstSaleTime < 0)
                return 0f;
            float span = now - _firstSaleTime;
            if (span > _windowSeconds) span = _windowSeconds;
            if (span < 5f) span = 5f; // avoid wild numbers from the first sale or two
            return _windowCoins * 60f / span;
        }
    }
}
