using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Online
{
    public enum LayoutSyncState
    {
        /// <summary>No server snapshot loaded yet.</summary>
        Unloaded,
        /// <summary>Known server version; safe to save.</summary>
        Synced,
        /// <summary>A save is in flight.</summary>
        Saving,
        /// <summary>A save's outcome is unknown (lost response); reload and call <see cref="LayoutSync.Recover"/>.</summary>
        Uncertain,
        /// <summary>The server has a newer layout than ours; the player must choose before saving again.</summary>
        Conflict,
    }

    /// <summary>
    /// Optimistic-concurrency bookkeeping for PUT /restaurants/{id}/layout: never retries blindly with a newer
    /// version; a lost response is resolved by comparing normalized content after a reload.
    /// </summary>
    public sealed class LayoutSync
    {
        private string _pendingNormalized;

        public LayoutSyncState State { get; private set; } = LayoutSyncState.Unloaded;
        public int Version { get; private set; }
        /// <summary>The last layout the server confirmed (used to keep instance ids stable).</summary>
        public ServerLayout Confirmed { get; private set; }
        /// <summary>The layout we tried to save; kept so a conflict or loss can be retried by the player.</summary>
        public ServerLayout Pending { get; private set; }

        public void Loaded(int version, ServerLayout layout)
        {
            Version = version;
            Confirmed = layout;
            State = LayoutSyncState.Synced;
        }

        /// <summary>Returns the expectedVersion to send.</summary>
        public int BeginSave(ServerLayout layout)
        {
            if (State != LayoutSyncState.Synced)
                throw new InvalidOperationException("Layout can only be saved from a synced state, not " + State);
            Pending = layout ?? throw new ArgumentNullException(nameof(layout));
            _pendingNormalized = ServerLayoutMapper.Normalize(layout);
            State = LayoutSyncState.Saving;
            return Version;
        }

        public void Saved(int newVersion)
        {
            RequireSaving();
            Version = newVersion;
            Confirmed = Pending;
            Pending = null;
            State = LayoutSyncState.Synced;
        }

        public void SaveConflicted() { RequireSaving(); State = LayoutSyncState.Conflict; }

        /// <summary>Network failure or 5xx: the server may or may not have stored the layout.</summary>
        public void SaveLost() { RequireSaving(); State = LayoutSyncState.Uncertain; }

        /// <summary>A definite rejection (validation, not owned): nothing changed on the server.</summary>
        public void SaveRejected() { RequireSaving(); Pending = null; State = LayoutSyncState.Synced; }

        /// <summary>
        /// After reloading the server snapshot. If it matches what we sent, adopt its version (the save landed);
        /// otherwise the server holds other content and the player must resolve it.
        /// Returns true when the save is confirmed.
        /// </summary>
        public bool Recover(int serverVersion, ServerLayout serverLayout)
        {
            if (State != LayoutSyncState.Uncertain && State != LayoutSyncState.Conflict)
                throw new InvalidOperationException("Nothing to recover in state " + State);
            bool landed = Pending != null && ServerLayoutMapper.Normalize(serverLayout) == _pendingNormalized;
            Version = serverVersion;
            Confirmed = serverLayout;
            if (landed) Pending = null;
            State = landed ? LayoutSyncState.Synced : LayoutSyncState.Conflict;
            return landed;
        }

        /// <summary>The player chose to keep the server's layout (reload it into the editor).</summary>
        public void AcceptServer()
        {
            if (State != LayoutSyncState.Conflict) throw new InvalidOperationException("No conflict to resolve");
            Pending = null;
            State = LayoutSyncState.Synced;
        }

        /// <summary>The player chose to overwrite with their layout; call <see cref="BeginSave"/> next.</summary>
        public void KeepMine()
        {
            if (State != LayoutSyncState.Conflict) throw new InvalidOperationException("No conflict to resolve");
            State = LayoutSyncState.Synced;
        }

        private void RequireSaving()
        {
            if (State != LayoutSyncState.Saving) throw new InvalidOperationException("No save in flight");
        }
    }

    /// <summary>
    /// Furniture the server must sell before a layout save: placed copies beyond what the inventory owns.
    /// The server owns prices; the client only lists item ids (one POST /purchases per copy).
    /// </summary>
    public static class PurchasePlan
    {
        public static List<string> MissingCopies(ServerLayout layout, IReadOnlyDictionary<string, int> ownedByServerId)
        {
            var placed = new SortedDictionary<string, int>(StringComparer.Ordinal);
            if (layout?.furniture != null)
                foreach (var p in layout.furniture)
                {
                    placed.TryGetValue(p.itemId, out int n);
                    placed[p.itemId] = n + 1;
                }
            var buy = new List<string>();
            foreach (var pair in placed)
            {
                int owned = 0;
                ownedByServerId?.TryGetValue(pair.Key, out owned);
                for (int i = owned; i < pair.Value; i++) buy.Add(pair.Key);
            }
            return buy;
        }
    }

    /// <summary>
    /// Coins shown while online (option A): the server wallet is the truth; coins earned locally by systems the
    /// server can't validate yet (sales, tips, market…) are shown separately as "unsynced" and kept in the ledger.
    /// </summary>
    public sealed class OnlineCoins
    {
        public long ServerCoins { get; private set; }
        public long UnsyncedCoins { get; private set; }
        public bool HasServerValue { get; private set; }
        public string ServerUpdatedAt { get; private set; }

        public event Action Changed;

        /// <summary>Applies a wallet from the server. Older timestamps (stale replays) are ignored.</summary>
        public void ApplyServer(long softCurrency, string updatedAtUtc)
        {
            if (HasServerValue && updatedAtUtc != null && ServerUpdatedAt != null &&
                string.CompareOrdinal(updatedAtUtc, ServerUpdatedAt) < 0)
                return;
            ServerCoins = softCurrency;
            ServerUpdatedAt = updatedAtUtc;
            HasServerValue = true;
            Changed?.Invoke();
        }

        public void AddUnsynced(long delta)
        {
            if (delta == 0) return;
            UnsyncedCoins += delta;
            Changed?.Invoke();
        }

        /// <summary>After a ledger upload the server folded the unsynced coins into the wallet.</summary>
        public void ClearUnsynced()
        {
            UnsyncedCoins = 0;
            Changed?.Invoke();
        }

        /// <summary>Only server coins can be spent online; unsynced coins are display-only.</summary>
        public bool CanSpend(long cost) => HasServerValue && cost <= ServerCoins;
    }
}
