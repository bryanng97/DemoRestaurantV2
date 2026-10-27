using System;
using System.Collections.Generic;
using System.Globalization;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Save;
using DemoRestaurant.Logic.Profile;

namespace DemoRestaurant.Logic.Session
{
    /// <summary>Saving, loading and idle ("while you were away") progress.</summary>
    public sealed partial class RestaurantSession
    {
        public DateTime UtcNow => _utcNow();

        /// <summary>Snapshot of everything worth keeping. Customers and open orders are not saved.</summary>
        public SaveData CaptureSave()
        {
            var save = new SaveData
            {
                savedAtUtc = _utcNow().ToString("o", CultureInfo.InvariantCulture),
                contentPack = Content.Id,
                // Offline the local economy server is the authority, so pending predictions count as earned.
                coins = Wallet.DisplayCoins,
                totalCustomersServed = TotalCustomersServed,
                level = Progress.Level,
                xp = Progress.Xp,
                demand = (float)Demand.Value,
                marketReadyBaskets = Market?.ReadyBaskets ?? 0,
                marketRestockTimer = Market?.RestockTimer ?? 0f,
                ledgerDropped = Ledger.Dropped,
                exterior = CaptureExterior(),
                customAvatar = Profile.HasCustomAvatar,
                trashSpawnTimer = Sim.TrashSpawnTimer,
            };
            foreach (var d in Recipes.Known) save.knownDishes.Add(d.Id);
            foreach (var d in Recipes.Known) save.recipeLevels.Add(new RecipeLevelSave { dishId = d.Id, level = Recipes.LevelOf(d) });
            foreach (var d in Menu.Active) save.menuActive.Add(d.Id);
            foreach (var a in Inventory.Contents()) save.inventory.Add(new IngredientCount { id = a.Ingredient.Id, count = a.Count });
            foreach (var s in Sim.Staff)
                save.staff.Add(new StaffSave
                {
                    typeId = s.Type.Id, homeX = s.Home.X, homeY = s.Home.Y,
                    energy = s.Energy, resting = s.IsResting || s.RestRequested, serverId = s.ServerId,
                });
            foreach (var pair in Items.Contents()) save.items.Add(new ItemCount { id = pair.Key.Id, count = pair.Value });
            save.playerId = Profile.GuestId;
            save.accountId = Profile.AccountId ?? "";
            save.displayName = Profile.DisplayName;
            save.lastRenameUtc = Profile.LastRenameUtc?.ToString("o", CultureInfo.InvariantCulture) ?? "";
            foreach (var part in Profile.Avatar.Parts)
                save.avatar.Add(new AvatarPartSave { slot = part.Key.ToString(), id = part.Value });
            save.wardrobe.AddRange(Wardrobe);
            foreach (var piece in Sim.Trash)
                save.trash.Add(new Simulation.TrashPiece { id = piece.id, x = piece.x, y = piece.y, variant = piece.variant });
            foreach (var provider in Profile.LinkedAccounts)
                save.linkedAccounts.Add(provider.ToString());
            save.garden.AddRange(Garden.Capture());
            foreach (var f in Layout.Furniture)
                if ((f.Def.Kind == FurnitureKind.Toilet || f.Def.Kind == FurnitureKind.Entertainment) && Sim.UsesOf(f) > 0)
                    save.toiletUses.Add(new ToiletUseSave { x = f.Origin.X, y = f.Origin.Y, uses = Sim.UsesOf(f) });
            save.ledger.AddRange(Ledger.Entries);
            return save;
        }

        /// <summary>
        /// Restores a save into this freshly created session (before any ticks). Unknown ids from older content
        /// are skipped. Returns the time the save was written, for idle progress.
        /// </summary>
        public DateTime? ApplySave(SaveData save)
        {
            if (save == null)
                return null;

            Wallet.SetConfirmed(Math.Max(0, save.coins));
            if (ExteriorCatalog != null) _exterior = ExteriorCatalog.Restore(save.exterior);
            TotalCustomersServed = Math.Max(0, save.totalCustomersServed);
            if (save.version >= 4)
            {
                Progress.Restore(save.level, save.xp);
                UpdateToiletChance();
                if (save.version >= 6) Demand.Restore(save.demand); // older saves start at the default demand
            }
            else
            {
                // Older saves: the player level carries over (XP restarts on the new curve), demand starts at the default.
                Progress.Restore(save.playerLevel, 0);
            }

            var known = new List<DishDef>();
            foreach (var id in save.knownDishes)
                if (TryGet(Content.GetDish, id, out var dish)) known.Add(dish);
            Recipes.Restore(known);
            if (save.recipeLevels != null) foreach (var recipe in save.recipeLevels)
                if (recipe != null && TryGet(Content.GetDish, recipe.dishId, out var dish))
                    Recipes.RestoreLevel(dish, recipe.level, Settings.maxDishLevel);
            foreach (var d in known) Menu.MakeAvailable(d);
            var active = new List<DishDef>();
            foreach (var id in save.menuActive)
                if (TryGet(Content.GetDish, id, out var dish)) active.Add(dish);
            Menu.SetActive(active);

            var stock = new List<IngredientAmount>();
            foreach (var item in save.inventory)
                if (item.count > 0 && TryGet(Content.GetIngredient, item.id, out var ingredient))
                    stock.Add(new IngredientAmount(ingredient, item.count));
            Inventory.Restore(stock);

            var takenHomes = new HashSet<GridPos>();
            foreach (var s in save.staff)
            {
                // Saves from the tiered staff ("chef_junior", "waiter_master", ...) become the single type per role.
                if (!TryGet(Content.GetStaffType, s.typeId, out var type) && !TryGet(Content.GetStaffType, UntieredId(s.typeId), out type))
                    continue;
                if (type.IsOwner && Owner != null)
                    continue; // only one of you
                // A renovation may have put furniture on the saved spot: keep the staff member, find them a new one.
                var home = new GridPos(s.homeX, s.homeY);
                if (!Layout.Grid.IsWalkable(home) || takenHomes.Contains(home))
                {
                    var free = FindFreeStaffSpot(takenHomes);
                    if (!free.HasValue)
                        continue;
                    home = free.Value;
                }
                takenHomes.Add(home);
                var staff = Sim.Hire(type, home);
                staff.ServerId = string.IsNullOrEmpty(s.serverId) ? null : s.serverId;
                Sim.SetEnergy(staff, s.energy, s.resting);
            }

            if (save.version >= 2)
            {
                var items = new List<KeyValuePair<ItemDef, int>>();
                foreach (var item in save.items)
                    if (item.count > 0 && TryGet(Content.GetItem, item.id, out var def))
                        items.Add(new KeyValuePair<ItemDef, int>(def, item.count));
                Items.Restore(items);
            }

            Market?.Restore(save.marketReadyBaskets, save.marketRestockTimer);
            Garden.Restore(save.garden);
            if (save.toiletUses != null)
                foreach (var t in save.toiletUses)
                {
                    var toilet = t == null ? null : Layout.FindAt(new GridPos(t.x, t.y));
                    if (toilet != null && (toilet.Def.Kind == FurnitureKind.Toilet || toilet.Def.Kind == FurnitureKind.Entertainment) && toilet.Origin.X == t.x && toilet.Origin.Y == t.y)
                        Sim.SetUses(toilet, t.uses);
                }
            RestoreProfile(save);
            InitializeWardrobe();
            if (save.wardrobe != null)
                foreach (var key in save.wardrobe)
                    foreach (var slot in AvatarCatalog.AllSlots)
                        foreach (var part in Content.Avatar.PartsFor(slot))
                            if (part.OwnershipKey == key) _wardrobe.Add(key);
            // Grandfather the equipped look in old saves; never grant the entire paid catalog.
            foreach (var pair in Profile.Avatar.Parts)
            {
                var part = Content.Avatar.Find(pair.Key, pair.Value);
                if (part != null) _wardrobe.Add(part.OwnershipKey);
            }
            Profile.HasCustomAvatar = save.version >= 7 ? save.customAvatar :
                Profile.Avatar.Key != AvatarLook.Default(Content.Avatar).Key;
            Sim.RestoreTrash(save.trash, save.trashSpawnTimer);
            Ledger.Restore(save.ledger, save.ledgerDropped);

            return DateTime.TryParse(save.savedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? at.ToUniversalTime()
                : (DateTime?)null;
        }

        /// <summary>
        /// Works out and applies idle progress since <paramref name="sinceUtc"/>. A clock that moved backwards
        /// counts as no time away. Returns the report to show the player.
        /// </summary>
        public OfflineReport ApplyOfflineProgress(DateTime sinceUtc)
        {
            var away = _utcNow() - sinceUtc;
            if (away < TimeSpan.Zero)
                away = TimeSpan.Zero;
            var report = OfflineProgress.Calculate(this, away);
            if (report.CountedSeconds <= 0)
                return report;

            if (report.Coins > 0)
            {
                Wallet.CreditOffline(report.Coins);
                Ledger.Record(LedgerKind.IdleIncome, report.Coins, ((long)report.CountedSeconds).ToString(CultureInfo.InvariantCulture), _utcNow());
            }
            TotalCustomersServed += report.CustomersServed;
            Progress.Add(report.Xp);
            // Closed while away (no chef or no waiter): demand fades as it does in play.
            if (report.BlockedReason == OfflineProgress.NoChef || report.BlockedReason == OfflineProgress.NoWaiter)
                Demand.OnClosed(TimeSpan.FromSeconds(report.CountedSeconds));

            // Idle counted as working: staff come back as tired (or as rested) as the time away left them.
            foreach (var r in report.Staff)
                Sim.SetEnergy(r.Staff, r.Energy, r.Resting);

            Market?.Tick((float)report.CountedSeconds);
            Sim.TickTrash((float)report.CountedSeconds);
            return report;
        }

        /// <summary>A walkable cell reachable from the entrance that no other staff member uses (not the entrance itself).</summary>
        private GridPos? FindFreeStaffSpot(HashSet<GridPos> taken)
        {
            var grid = Layout.Grid;
            var finder = new Pathfinder(grid);
            var path = new List<GridPos>();
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = new GridPos(x, y);
                if (cell == Layout.Entrance || taken.Contains(cell) || !grid.IsWalkable(cell))
                    continue;
                if (finder.TryFindPath(Layout.Entrance, cell, path))
                    return cell;
            }
            return null;
        }

        private void RestoreProfile(SaveData save)
        {
            if (save.version < 3)
                return; // older saves keep the default profile
            DateTime? renamed = DateTime.TryParse(save.lastRenameUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? at.ToUniversalTime() : (DateTime?)null;
            var linked = new List<AccountProvider>();
            foreach (var name in save.linkedAccounts)
                if (Enum.TryParse(name, out AccountProvider p)) linked.Add(p);
            Profile.Restore(save.playerId, save.displayName, renamed, linked, save.accountId);

            var parts = new List<KeyValuePair<AvatarSlot, string>>();
            foreach (var part in save.avatar)
                if (Enum.TryParse(part.slot, out AvatarSlot slot)) parts.Add(new KeyValuePair<AvatarSlot, string>(slot, part.id));
            var look = AvatarLook.Default(Content.Avatar);
            look.Restore(parts, Content.Avatar);
            Profile.SetAvatar(look);
        }

        /// <summary>"chef_junior" → "chef" (staff tiers were merged into one type per role).</summary>
        private static string UntieredId(string id)
        {
            foreach (var suffix in new[] { "_junior", "_intermediate", "_master" })
                if (id != null && id.EndsWith(suffix, StringComparison.Ordinal))
                    return id.Substring(0, id.Length - suffix.Length);
            return id;
        }

        private static bool TryGet<T>(Func<string, T> lookup, string id, out T value)
        {
            try
            {
                value = lookup(id);
                return true;
            }
            catch (ContentException)
            {
                value = default;
                return false;
            }
        }
    }
}
