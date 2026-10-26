using System;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Save;
using DemoRestaurant.Logic.Economy;

namespace DemoRestaurant.Logic.Session
{
    public sealed partial class RestaurantSession
    {
        public ExteriorCatalog ExteriorCatalog { get; private set; }
        private ExteriorSave _exterior;
        public ExteriorSave CaptureExterior() => _exterior?.Copy();

        public void ConfigureExterior(ExteriorCatalog catalog)
        {
            ExteriorCatalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _exterior = catalog.Starter();
            (Economy as LocalEconomyServer)?.ConfigureExterior(catalog, () => _exterior, () => Progress.Level);
        }

        /// <summary>Offline authority validates catalog prices and ownership; persist before applying anything.</summary>
        public LandPurchaseResult CommitExterior(ExteriorSave draft, Func<SaveData, bool> persist)
        {
            if (!(Economy is LocalEconomyServer server) || ExteriorCatalog == null)
                return LandPurchaseResult.Unavailable;
            return server.CommitExterior(draft, (accepted, balance) =>
            {
                var save = CaptureSave();
                save.exterior = accepted.Copy();
                save.coins = balance;
                // Include the purchase in the same durable transaction as its coins and layout.
                var ledger = new OfflineLedger();
                ledger.Restore(save.ledger, save.ledgerDropped);
                ledger.Record(LedgerKind.ExteriorSaved, Wallet.ConfirmedCoins - balance, "facade", UtcNow);
                save.ledger.Clear();
                save.ledger.AddRange(ledger.Entries);
                save.ledgerDropped = ledger.Dropped;
                if (persist == null || !persist(save)) return false;
                _exterior = accepted.Copy();
                Ledger.Restore(save.ledger, save.ledgerDropped);
                return true;
            });
        }
    }
}
