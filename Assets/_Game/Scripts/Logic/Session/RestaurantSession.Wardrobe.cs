using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Profile;
using DemoRestaurant.Logic.Save;
using DemoRestaurant.Logic.Simulation;

namespace DemoRestaurant.Logic.Session
{
    public enum WardrobeResult { Success, AlreadyOwned, NotOwned, Locked, NotEnoughCoins, Equipped, OnlineUnavailable, UnknownPart }

    public sealed partial class RestaurantSession
    {
        private readonly HashSet<string> _wardrobe = new HashSet<string>(StringComparer.Ordinal);
        public IEnumerable<string> Wardrobe => _wardrobe;
        // The current player API has no wardrobe/trash reward contract. Never submit unsupported actions or
        // spend/award authoritative account currency through a local-only feature.
        public bool LocalWardrobeEconomy => string.IsNullOrEmpty(Profile.AccountId);
        public event Action<TrashPiece, int, int> TrashCollected;

        private void InitializeWardrobe()
        {
            _wardrobe.Clear();
            foreach (var slot in AvatarCatalog.AllSlots)
            {
                var part = Content.Avatar.Default(slot);
                if (part != null) _wardrobe.Add(part.OwnershipKey);
            }
        }

        private AvatarPartDef ResolvePart(AvatarPartDef part) => part == null ? null : Content.Avatar.Find(part.Slot, part.Id);

        public bool OwnsAvatarPart(AvatarPartDef part)
        {
            part = ResolvePart(part);
            return part != null && (part.Price == 0 || _wardrobe.Contains(part.OwnershipKey));
        }

        public WardrobeResult BuyAvatarPart(AvatarPartDef requested)
        {
            var part = ResolvePart(requested);
            if (part == null) return WardrobeResult.UnknownPart;
            if (OwnsAvatarPart(part)) return WardrobeResult.AlreadyOwned;
            if (part.UnlockLevel > Progress.Level) return WardrobeResult.Locked;
            if (!LocalWardrobeEconomy) return WardrobeResult.OnlineUnavailable;
            if (!Wallet.TrySpend(part.Price)) return WardrobeResult.NotEnoughCoins;
            _wardrobe.Add(part.OwnershipKey);
            Ledger.Record(LedgerKind.WardrobeBought, part.Price, part.OwnershipKey, _utcNow());
            return WardrobeResult.Success;
        }

        public WardrobeResult SellAvatarPart(AvatarPartDef requested)
        {
            var part = ResolvePart(requested);
            if (part == null) return WardrobeResult.UnknownPart;
            if (part.Price == 0 || !_wardrobe.Contains(part.OwnershipKey)) return WardrobeResult.NotOwned;
            if (Profile.Avatar[part.Slot] == part.Id) return WardrobeResult.Equipped;
            if (!LocalWardrobeEconomy) return WardrobeResult.OnlineUnavailable;
            _wardrobe.Remove(part.OwnershipKey);
            int refund = part.Price / 2;
            Wallet.CreditOffline(refund);
            Ledger.Record(LedgerKind.WardrobeSold, refund, part.OwnershipKey, _utcNow());
            return WardrobeResult.Success;
        }

        public AvatarLook RandomOwnedAvatar(Random random)
        {
            var look = Profile.Avatar.Clone();
            var choices = new List<AvatarPartDef>();
            foreach (var slot in AvatarCatalog.AllSlots)
            {
                choices.Clear();
                foreach (var part in Content.Avatar.PartsFor(slot))
                    if (part.UnlockLevel <= Progress.Level && OwnsAvatarPart(part)) choices.Add(part);
                if (choices.Count > 0) look.TrySet(Content.Avatar, slot, choices[random.Next(choices.Count)].Id, Progress.Level);
            }
            return look;
        }

        private void OnTrashCleaned(TrashPiece piece)
        {
            int coins = LocalWardrobeEconomy ? Math.Max(0, Settings.trashCoins) : 0;
            int xp = LocalWardrobeEconomy ? Math.Max(0, Rules.Trash.xpPerPiece) : 0;
            Wallet.CreditOffline(coins);
            if (LocalWardrobeEconomy) Progress.Add(xp);
            Ledger.Record(LedgerKind.TrashCleaned, coins, piece.id.ToString(), _utcNow());
            TrashCollected?.Invoke(piece, coins, xp);
        }
    }
}
