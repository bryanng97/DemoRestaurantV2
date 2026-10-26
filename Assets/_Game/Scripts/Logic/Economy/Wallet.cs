using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Economy
{
    /// <summary>
    /// The client's view of the player's coins: a server-confirmed balance plus pending predictions.
    /// The UI shows <see cref="DisplayCoins"/> so earnings feel instant; when the server settles a
    /// receipt, the prediction is replaced by whatever the server actually awarded.
    /// </summary>
    public sealed class Wallet
    {
        private readonly Dictionary<long, int> _pending = new Dictionary<long, int>();
        private long _nextReceiptId = 1;

        public long ConfirmedCoins { get; private set; }
        public long PendingCoins { get; private set; }
        public long DisplayCoins => ConfirmedCoins + PendingCoins;
        public int PendingCount => _pending.Count;

        /// <summary>Raised whenever DisplayCoins or ConfirmedCoins change.</summary>
        public event Action Changed;
        /// <summary>Raised when the server awarded a different amount than predicted (delta = awarded - predicted).</summary>
        public event Action<long, int> Corrected;

        public Wallet(long confirmedCoins = 0)
        {
            if (confirmedCoins < 0) throw new ArgumentOutOfRangeException(nameof(confirmedCoins));
            ConfirmedCoins = confirmedCoins;
        }

        /// <summary>Records an optimistic gain and returns the receipt id to send to the server.</summary>
        public long Predict(int coins)
        {
            if (coins < 0) throw new ArgumentOutOfRangeException(nameof(coins));
            long id = _nextReceiptId++;
            _pending.Add(id, coins);
            PendingCoins += coins;
            Changed?.Invoke();
            return id;
        }

        /// <summary>Applies the server's decision. Unknown or already-settled receipts are ignored.</summary>
        public void Settle(SaleResult result)
        {
            if (!_pending.TryGetValue(result.ReceiptId, out int predicted))
                return;
            _pending.Remove(result.ReceiptId);
            PendingCoins -= predicted;

            int awarded = result.Accepted ? result.AwardedCoins : 0;
            ConfirmedCoins += awarded;
            if (awarded != predicted)
                Corrected?.Invoke(result.ReceiptId, awarded - predicted);
            Changed?.Invoke();
        }

        /// <summary>
        /// Adds coins the client awarded itself while offline (idle income). Recorded in the offline ledger so the
        /// server can check it on the next sync.
        /// </summary>
        public void CreditOffline(long coins)
        {
            if (coins <= 0)
                return;
            ConfirmedCoins += coins;
            Changed?.Invoke();
        }

        /// <summary>
        /// Spends coins the player can see (confirmed + pending). Offline the local economy is the authority;
        /// spends go to the offline ledger for the server to re-check. Returns false if the player can't afford it.
        /// </summary>
        public bool TrySpend(long coins)
        {
            if (coins < 0) throw new ArgumentOutOfRangeException(nameof(coins));
            if (coins == 0) return true;
            if (DisplayCoins < coins)
                return false;
            ConfirmedCoins -= coins; // may dip below zero while sales are pending; DisplayCoins never does
            Changed?.Invoke();
            return true;
        }

        /// <summary>Replaces the confirmed balance with a fresh server value (e.g. after login or sync).</summary>
        public void SetConfirmed(long coins)
        {
            if (coins < 0) throw new ArgumentOutOfRangeException(nameof(coins));
            ConfirmedCoins = coins;
            Changed?.Invoke();
        }
    }
}
