using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Online;
using DemoRestaurant.Logic.Profile;
using DemoRestaurant.Logic.Save;
using DemoRestaurant.Logic.Simulation;

namespace DemoRestaurant.Logic.Session
{
    public enum WardrobeResult { Success, AlreadyOwned, NotOwned, Locked, NotEnoughCoins, Equipped, OnlineUnavailable, UnknownPart }

    public sealed partial class RestaurantSession
    {
        private readonly HashSet<string> _wardrobe = new HashSet<string>(StringComparer.Ordinal);
        // The device's look was offered to an account whose server look is still empty (once per run).
        private bool _lookOffered;
        public IEnumerable<string> Wardrobe => _wardrobe;

        /// <summary>
        /// Whether the server's rules include the wardrobe and trash rewards (ruleset v3). Set by the online layer from
        /// <c>/config</c>; true until it says otherwise, so offline and guest play works as before.
        /// </summary>
        public bool ServerWardrobe { get; set; } = true;

        /// <summary>
        /// Clothes can be bought and sold and litter pays: always while playing as a guest, and on an account once its
        /// server rules include the wardrobe. Every trade is recorded in <see cref="ServerLog"/>, so the server re-checks
        /// it with its own prices (and a signed-out purchase can't outlive the next sync).
        /// </summary>
        public bool WardrobeTradingOpen => string.IsNullOrEmpty(Profile.AccountId) || ServerWardrobe;

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

        private static JsonValue PartBody(AvatarPartDef part) =>
            Body().Set("slot", WardrobeIds.SlotId(part.Slot)).Set("id", part.Id);

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
            if (!WardrobeTradingOpen) return WardrobeResult.OnlineUnavailable;
            if (!Wallet.TrySpend(part.Price)) return WardrobeResult.NotEnoughCoins;
            _wardrobe.Add(part.OwnershipKey);
            RecordAction("wardrobe_buy", PartBody(part), coins: -part.Price);
            Ledger.Record(LedgerKind.WardrobeBought, part.Price, part.OwnershipKey, _utcNow());
            return WardrobeResult.Success;
        }

        public WardrobeResult SellAvatarPart(AvatarPartDef requested)
        {
            var part = ResolvePart(requested);
            if (part == null) return WardrobeResult.UnknownPart;
            if (part.Price == 0 || !_wardrobe.Contains(part.OwnershipKey)) return WardrobeResult.NotOwned;
            if (Profile.Avatar[part.Slot] == part.Id) return WardrobeResult.Equipped;
            if (!WardrobeTradingOpen) return WardrobeResult.OnlineUnavailable;
            _wardrobe.Remove(part.OwnershipKey);
            int refund = part.Price / 2;
            Wallet.CreditOffline(refund);
            RecordAction("wardrobe_sell", PartBody(part), coins: refund);
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
            int coins = WardrobeTradingOpen ? Math.Max(0, Settings.trashCoins) : 0;
            int xp = WardrobeTradingOpen ? Math.Max(0, Rules.Trash.xpPerPiece) : 0;
            if (WardrobeTradingOpen)
            {
                Wallet.CreditOffline(coins);
                // Level-up rewards from this XP are credited to the same entry, as for sales.
                _effectKey = RecordAction("trash_clean", Body(), coins, xp);
                Progress.Add(xp);
                _effectKey = null;
            }
            Ledger.Record(LedgerKind.TrashCleaned, coins, piece.id.ToString(), _utcNow());
            TrashCollected?.Invoke(piece, coins, xp);
        }

        // ---------------------------------------------------------------- server state → wardrobe

        /// <summary>
        /// The account's clothes and look (state <c>wardrobe</c>): owned parts replace the device's, and a paid part the
        /// account doesn't own comes off. Skipped while a trade or look is still waiting to be sent. A device look the
        /// account has never saved is offered once.
        /// </summary>
        private void ApplyServerWardrobe(JsonValue state)
        {
            var wardrobe = state?["wardrobe"];
            if (wardrobe == null || wardrobe.Kind != JsonKind.Object) return;
            if (!ServerLog.HasPending("wardrobe_buy", "wardrobe_sell"))
            {
                InitializeWardrobe();
                foreach (var owned in wardrobe["owned"].Items)
                    if (WardrobeIds.TryParseSlot(owned["slot"].AsString(), out var slot))
                    {
                        var part = Content.Avatar.Find(slot, owned["id"].AsString());
                        if (part != null) _wardrobe.Add(part.OwnershipKey);
                    }
            }
            if (ServerLog.HasPending("wardrobe_buy", "wardrobe_sell", "wardrobe_look")) return;

            var look = Profile.Avatar.Clone();
            var server = wardrobe["look"];
            if (server.Kind == JsonKind.Object)
                foreach (var pair in server.Fields)
                    if (WardrobeIds.TryParseSlot(pair.Key, out var slot))
                        look.TrySet(Content.Avatar, slot, pair.Value.AsString(), int.MaxValue);
            foreach (var slot in AvatarCatalog.AllSlots)
            {
                var part = Content.Avatar.Find(slot, look[slot]);
                if (part != null && !OwnsAvatarPart(part))
                    look.TrySet(Content.Avatar, slot, Content.Avatar.Default(slot)?.Id, int.MaxValue);
            }
            if (server.Kind == JsonKind.Object) Profile.HasCustomAvatar = true;
            if (look.Key != Profile.Avatar.Key) Profile.SetAvatar(look);
            if (server.Kind != JsonKind.Object && Profile.HasCustomAvatar && !_lookOffered)
            {
                _lookOffered = true;
                ServerLog.RecordDeferred("wardrobe_look");
            }
        }
    }
}
