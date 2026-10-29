using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Logic.Economy
{
    /// <summary>
    /// Offline stand-in for the Nakama economy RPC, with the same rules the server module must enforce:
    /// the price is looked up from the server's own content, unknown dishes are rejected, and every receipt
    /// is settled once. Adds simulated latency so the client's pending/confirmed handling is exercised.
    /// </summary>
    public sealed class LocalEconomyServer : IEconomyServer, ILandPurchaseServer, IEntertainmentEconomyServer
    {
        private readonly ContentPack _catalog;
        private readonly float _latencySeconds;
        private readonly Queue<(float due, SaleReport sale)> _inFlight = new Queue<(float, SaleReport)>();
        private readonly HashSet<long> _seenReceipts = new HashSet<long>();
        private float _now;
        private int _ownedWidth, _ownedHeight;
        private bool _committing;
        private readonly Wallet _wallet;
        private long _balance;
        /// <summary>The offline session shares its confirmed balance with land purchases, idle income and spending.</summary>
        public long Balance
        {
            get => _wallet?.ConfirmedCoins ?? _balance;
            private set
            {
                if (_wallet != null) _wallet.SetConfirmed(value);
                else _balance = value;
            }
        }

        public event Action<SaleResult> SaleSettled;

        public LocalEconomyServer(ContentPack catalog, float latencySeconds = 0.4f,
            long startingCoins = 0, int ownedWidth = 0, int ownedHeight = 0, Wallet wallet = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _latencySeconds = Math.Max(0f, latencySeconds);
            if (startingCoins < 0) throw new ArgumentOutOfRangeException(nameof(startingCoins));
            _wallet = wallet;
            Balance = startingCoins;
            _ownedWidth = ownedWidth; _ownedHeight = ownedHeight;
        }

        public void SubmitSale(SaleReport sale) => _inFlight.Enqueue((_now + _latencySeconds, sale));
        public void SubmitEntertainment(long receiptId, string furnitureId)
        {
            if (!_seenReceipts.Add(receiptId)) return;
            int coins = 0;
            bool accepted = false;
            try
            {
                accepted = _catalog.GetFurniture(furnitureId).Kind == FurnitureKind.Entertainment;
                if (accepted) coins = Math.Max(0, _catalog.Rules.Entertainment.coinsPerUse);
            }
            catch (ContentException) { }
            if (_wallet == null) Balance += coins;
            SaleSettled?.Invoke(new SaleResult(receiptId, accepted, coins));
        }
        private Func<string, int> _recipeLevel;
        public void SetRecipeLevelSource(Func<string, int> source) => _recipeLevel = source;
        private Func<int, int> _tipFor;
        /// <summary>The ambience tip on a bill, as the backend credits it (null = no tips).</summary>
        public void SetTipSource(Func<int, int> tipFor) => _tipFor = tipFor;

        public void Tick(float dt)
        {
            _now += dt;
            while (_inFlight.Count > 0 && _inFlight.Peek().due <= _now)
            {
                var result = Validate(_inFlight.Dequeue().sale);
                SaleSettled?.Invoke(result);
            }
        }

        private SaleResult Validate(SaleReport sale)
        {
            if (!_seenReceipts.Add(sale.ReceiptId))
                return new SaleResult(sale.ReceiptId, false, 0);

            DishDef dish;
            try
            {
                dish = _catalog.GetDish(sale.DishId);
            }
            catch (ContentException)
            {
                return new SaleResult(sale.ReceiptId, false, 0);
            }
            // A session's wallet is credited by SaleSettled; standalone servers keep their own balance.
            int price = _catalog.Economy == null ? dish.Price :
                EconomyFormulas.DishPrice(_catalog.Economy.settings, dish.Price, _recipeLevel?.Invoke(dish.Id) ?? 1);
            price += Math.Max(0, _tipFor?.Invoke(price) ?? 0);
            if (_wallet == null) Balance += price;
            return new SaleResult(sale.ReceiptId, true, price);
        }

        private Dictionary<string, int> _ownedFurniture = new Dictionary<string, int>();

        private Layout.ExteriorCatalog _exteriorCatalog;
        private Func<Layout.ExteriorSave> _exteriorState;
        private Func<int> _exteriorLevel;

        public void ConfigureExterior(Layout.ExteriorCatalog catalog, Func<Layout.ExteriorSave> state, Func<int> level)
        {
            _exteriorCatalog = catalog; _exteriorState = state; _exteriorLevel = level;
        }

        public LandPurchaseResult CommitExterior(Layout.ExteriorSave draft,
            Func<Layout.ExteriorSave, long, bool> persist)
        {
            if (_committing || _exteriorCatalog == null || draft == null) return LandPurchaseResult.Unavailable;
            // Ownership and prices come from authority state, never from the draft.
            if (!_exteriorCatalog.TryQuote(_exteriorState(), draft.pieces, _exteriorLevel(), out long cost, out var owned))
                return LandPurchaseResult.Unavailable;
            if (Balance < cost) return LandPurchaseResult.InsufficientCoins;
            var accepted = draft.Copy();
            accepted.owned = owned;
            _committing = true;
            try
            {
                if (persist == null || !persist(accepted, Balance - cost)) return LandPurchaseResult.SaveFailed;
                Balance -= cost;
                return LandPurchaseResult.Success;
            }
            finally { _committing = false; }
        }

        public IReadOnlyDictionary<string, int> OwnedFurniture => _ownedFurniture;

        public void RestoreOwnedFurniture(IReadOnlyDictionary<string, int> owned) =>
            _ownedFurniture = Layout.FurnitureOwnership.AfterPurchase(owned, null);

        public LandPurchaseResult CommitExpansion(int width, int height, Func<long, bool> persist) =>
            CommitLayout(width, height, null, persist);

        private Func<int> _level;
        private int _terraceStep;

        public int OwnedTerraceStep => _terraceStep;

        public void RestoreTerraceStep(int step) => _terraceStep = Math.Max(0, Math.Min(step, _catalog.Rules.TerraceSteps.Count));

        public LandPurchaseResult CommitTerrace(int step, Func<long, bool> persist)
        {
            if (_committing) return LandPurchaseResult.Unavailable;
            var steps = _catalog.Rules.TerraceSteps;
            // Only the next size, in order; its level and price come from the server's own catalog.
            if (step != _terraceStep + 1 || step > steps.Count || !steps[step - 1].IsValid) return LandPurchaseResult.InvalidSize;
            var next = steps[step - 1];
            if ((_level?.Invoke() ?? 1) < next.level) return LandPurchaseResult.Unavailable;
            if (Balance < next.price) return LandPurchaseResult.InsufficientCoins;
            _committing = true;
            try
            {
                if (persist == null || !persist(Balance - next.price)) return LandPurchaseResult.SaveFailed;
                Balance -= next.price;
                _terraceStep = step;
                return LandPurchaseResult.Success;
            }
            finally { _committing = false; }
        }

        /// <summary>The restaurant level, for the room size it allows (<see cref="Layout.RoomGrowth"/>).</summary>
        public void SetLevelSource(Func<int> level) => _level = level;

        public LandPurchaseResult CommitLayout(int width, int height, IReadOnlyDictionary<string, int> placed, Func<long, bool> persist)
        {
            if (_committing) return LandPurchaseResult.Unavailable;
            if (placed != null)
                foreach (var entry in placed)
                    if (entry.Value > Layout.FurnitureOwnership.Count(_ownedFurniture, entry.Key)
                        && (!_catalog.GetFurniture(entry.Key).Available || (_level != null && _catalog.GetFurniture(entry.Key).UnlockLevel > _level())))
                        return LandPurchaseResult.Unavailable;
            long cost = 0;
            var growth = _catalog.Rules.RoomGrowth;
            if (growth != null)
            {
                // The room grows with the level for free; it may be as big as the level allows (or already was).
                if (!growth.Allows(_ownedWidth, _ownedHeight, width, height, _level?.Invoke() ?? 1))
                    return LandPurchaseResult.InvalidSize;
            }
            else if (width != _ownedWidth || height != _ownedHeight)
                if (_catalog.Rules.Expansion == null ||
                    !_catalog.Rules.Expansion.TryQuote(_ownedWidth, _ownedHeight, width, height, out cost))
                    return LandPurchaseResult.InvalidSize;
            try
            {
                // Server prices: the client's idea of what things cost is never trusted.
                cost += Layout.FurnitureOwnership.PurchaseCost(_catalog, _ownedFurniture, placed);
            }
            catch (ContentException) { return LandPurchaseResult.Unavailable; }
            if (Balance < cost) return LandPurchaseResult.InsufficientCoins;
            _committing = true;
            try
            {
                if (persist == null || !persist(Balance - cost)) return LandPurchaseResult.SaveFailed;
                Balance -= cost;
                _ownedWidth = width; _ownedHeight = height;
                if (placed != null) _ownedFurniture = Layout.FurnitureOwnership.AfterPurchase(_ownedFurniture, placed);
                return LandPurchaseResult.Success;
            }
            finally { _committing = false; }
        }
    }
}
