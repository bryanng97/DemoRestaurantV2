using System;
using System.Collections.Generic;
using System.Globalization;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Online;
using DemoRestaurant.Logic.Progression;
using DemoRestaurant.Logic.Save;
using DemoRestaurant.Logic.Simulation;

namespace DemoRestaurant.Logic.Session
{
    /// <summary>
    /// The session's side of the v2 backend: every player action is recorded in <see cref="ServerLog"/> (applied
    /// locally at once, uploaded later through <c>POST /sync/ledger</c>), and server states are folded back in.
    /// The log is kept offline too, so play before signing in or without a connection is sent on the next sync.
    /// </summary>
    public sealed partial class RestaurantSession
    {
        /// <summary>Actions waiting for the server (see <see cref="ServerActionLog"/>).</summary>
        public ServerActionLog ServerLog { get; private set; }

        /// <summary>The server restaurant id, once known (layout, terrace and rename payloads need it).</summary>
        public string ServerRestaurantId { get; set; }

        /// <summary>The server instance id of a placed piece (set by the online layer), for functional uses.</summary>
        public Func<PlacedFurniture, string> ServerInstanceOf { get; set; }

        // The action whose processing is under way, so side effects (a level-up reward) are credited to it.
        private string _effectKey;

        private void InitServerLog()
        {
            ServerLog = new ServerActionLog(utcNow: _utcNow);
            Sim.FurnitureFixed += OnFurnitureFixed;
        }

        private static JsonValue Body() => JsonValue.Object();

        private string RecordAction(string type, JsonValue payload, long coins = 0, int xp = 0, float demand = 0f) =>
            ServerLog.Record(type, payload, coins, xp, demand);

        private void RecordUnhappy(string eventType, double before) =>
            ServerLog.RecordDemandEvent(eventType, (float)(Demand.Value - before));

        private void Unhappy(string eventType)
        {
            double before = Demand.Value;
            Demand.OnUnhappyGuest();
            RecordUnhappy(eventType, before);
        }

        /// <summary>The player saved a renovation that cost <paramref name="spent"/> coins (sent as the newest layout).</summary>
        public void RecordLayoutSaved(long spent) => ServerLog.RecordDeferred("layout", -Math.Max(0, spent));

        /// <summary>The player changed the menu (sent as the newest menu).</summary>
        public void RecordMenuChanged() => ServerLog.RecordDeferred("menu");

        private void RecordDuty(Staff staff, bool onDuty)
        {
            if (staff?.ServerId != null)
                RecordAction("duty", Body().Set("staffId", staff.ServerId).Set("onDuty", onDuty));
        }

        private void OnFurnitureFixed(PlacedFurniture piece)
        {
            if (piece?.Def.Kind != FurnitureKind.Entertainment) return; // toilet wear stays local for now
            string instance = ServerInstanceOf?.Invoke(piece);
            if (instance != null) RecordAction("repair", Body().Set("instanceId", instance));
        }

        private static string RoleId(StaffRole role) => role.ToString().ToLowerInvariant();

        // ---------------------------------------------------------------- server state → session

        /// <summary>
        /// Folds a server state into the running session. Coins, XP and demand become the server's value plus what
        /// the client applied for actions the server has not seen yet. Everything else is replaced only while no
        /// action that changes it is still waiting (otherwise the next sync does it).
        /// </summary>
        public void ApplyServerState(JsonValue state, ServerIdMap ids = null)
        {
            if (state == null || state.IsNull) return;
            ids ??= new ServerIdMap();
            var log = ServerLog;
            ServerRestaurantId = state["restaurantId"].AsString() ?? ServerRestaurantId;
            ApplyServerProfile(state);

            long coins = state["coins"].AsLong() + log.PendingCoins - Wallet.PendingCoins;
            Wallet.SetConfirmed(Math.Max(0, coins));

            if (log.PendingXp == 0 && !log.HasPending("recipe_learn"))
            {
                int level = state["level"].AsInt(Progress.Level), xp = state["levelXp"].AsInt(Progress.Xp);
                if (level != Progress.Level || xp != Progress.Xp)
                {
                    Progress.Restore(level, xp);
                    UpdateToiletChance();
                    Menu.FillFreeSlots();
                    GrowRoom();
                }
            }
            if (state["popularity"].Has("demand"))
                Demand.Restore(state["popularity"]["demand"].AsDouble() + log.PendingDemand);

            if (!log.HasPending("recipe_learn"))
            {
                var known = new List<DishDef>();
                foreach (var pair in state["recipes"].Fields)
                    if (TryGet(Content.GetDish, pair.Key, out var dish)) known.Add(dish);
                if (known.Count > 0)
                {
                    Recipes.Restore(known);
                    foreach (var pair in state["recipes"].Fields)
                        if (TryGet(Content.GetDish, pair.Key, out var dish))
                            Recipes.RestoreLevel(dish, pair.Value.AsInt(1), Settings.maxDishLevel);
                    foreach (var d in known) Menu.MakeAvailable(d);
                }
            }
            if (!log.HasPending("menu", "recipe_learn"))
            {
                var active = new List<DishDef>();
                foreach (var category in new[] { "starter", "main", "dessert", "drink" })
                    foreach (var id in state["menu"][category].Items)
                        if (TryGet(Content.GetDish, id.AsString(), out var dish)) active.Add(dish);
                if (active.Count > 0) Menu.SetActive(active);
            }
            if (!log.HasPending("recipe_learn", "market_collect", "market_collect_all", "garden"))
            {
                var stock = new List<IngredientAmount>();
                foreach (var pair in state["inventory"]["ingredients"].Fields)
                    if (pair.Value.AsInt() > 0 && TryGet(Content.GetIngredient, pair.Key, out var ingredient))
                        stock.Add(new IngredientAmount(ingredient, pair.Value.AsInt()));
                Inventory.Restore(stock);
            }
            if (!log.HasPending("item_buy", "item_use"))
                Items.Restore(ServerItems(state));
            if (Market != null && !log.HasPending("market_collect", "market_collect_all"))
            {
                MarketFrom(state, out int ready, out float timer);
                Market.Restore(ready, timer);
            }
            if (!log.HasPending("garden"))
                Garden.Restore(GardenFrom(state));
            if (!log.HasPending("hire", "dismiss", "owner_role", "duty", "item_use"))
                foreach (var s in state["staff"].Items)
                {
                    var staff = StaffWithServerId(s["id"].AsString()) ?? (s["owner"].AsBool() ? Owner : null);
                    if (staff == null) continue;
                    if (staff.ServerId == null) staff.ServerId = s["id"].AsString();
                    Sim.SetEnergy(staff, s["energy"].AsInt(100), !s["onDuty"].AsBool(true));
                }
            if (Economy is LocalEconomyServer local)
            {
                if (!log.HasPending("terrace")) local.RestoreTerraceStep(state["terraceStep"].AsInt(TerraceStep));
                if (!log.HasPending("layout")) local.RestoreOwnedFurniture(MergeOwned(local.OwnedFurniture, state, ids));
            }
        }

        /// <summary>
        /// True when the server's staff differ from the local team (another device, or a rejected hire): the online
        /// layer then adopts the whole server state with <see cref="SaveFromServer"/>.
        /// </summary>
        public bool StaffDiffersFrom(JsonValue state)
        {
            var server = new Dictionary<string, string>();
            foreach (var s in state["staff"].Items)
                if (!s["owner"].AsBool()) server[s["id"].AsString() ?? ""] = s["role"].AsString();
            int hired = 0;
            foreach (var staff in Sim.Staff)
            {
                if (staff.IsOwner) continue;
                hired++;
                if (staff.ServerId == null || !server.TryGetValue(staff.ServerId, out var role) || role != RoleId(staff.Role))
                    return true;
            }
            return hired != server.Count;
        }

        /// <summary>
        /// The account's id and, unless a rename is still waiting to be sent, its name: the same on every device.
        /// </summary>
        public void ApplyServerProfile(JsonValue state)
        {
            var profile = state?["profile"];
            string id = profile?["id"].AsString();
            if (string.IsNullOrEmpty(id)) return;
            Profile.SetAccount(id, ServerLog.HasPending("rename") ? null : profile["displayName"].AsString());
        }

        /// <summary>
        /// A local save built from a server state, keeping what only the device has (avatar, toilet wear, customers
        /// served); the profile takes the account's id and name. Coins include the actions still waiting in the log,
        /// which stays and is sent next.
        /// </summary>
        public SaveData SaveFromServer(JsonValue state)
        {
            ApplyServerProfile(state);
            var save = CaptureSave();
            save.coins = Math.Max(0, state["coins"].AsLong() + ServerLog.PendingCoins);
            save.level = state["level"].AsInt(1);
            save.xp = state["levelXp"].AsInt();
            save.demand = (float)state["popularity"]["demand"].AsDouble(save.demand);

            save.knownDishes.Clear();
            save.recipeLevels.Clear();
            foreach (var pair in state["recipes"].Fields)
            {
                save.knownDishes.Add(pair.Key);
                save.recipeLevels.Add(new RecipeLevelSave { dishId = pair.Key, level = pair.Value.AsInt(1) });
            }
            save.menuActive.Clear();
            foreach (var category in new[] { "starter", "main", "dessert", "drink" })
                foreach (var id in state["menu"][category].Items) save.menuActive.Add(id.AsString());
            save.inventory.Clear();
            foreach (var pair in state["inventory"]["ingredients"].Fields)
                if (pair.Value.AsInt() > 0) save.inventory.Add(new IngredientCount { id = pair.Key, count = pair.Value.AsInt() });
            save.items.Clear();
            foreach (var pair in ServerItems(state)) save.items.Add(new ItemCount { id = pair.Key.Id, count = pair.Value });

            save.staff.Clear();
            foreach (var s in state["staff"].Items)
            {
                if (!Enum.TryParse(s["role"].AsString() ?? "", true, out StaffRole role)) continue;
                var type = s["owner"].AsBool() ? Content.OwnerType(role) : HiredType(role);
                if (type == null) continue;
                // No saved spot: ApplySave finds a free cell for each.
                save.staff.Add(new StaffSave
                {
                    typeId = type.Id, homeX = -1, homeY = -1, serverId = s["id"].AsString(),
                    energy = s["energy"].AsInt(100), resting = !s["onDuty"].AsBool(true),
                });
            }
            MarketFrom(state, out save.marketReadyBaskets, out save.marketRestockTimer);
            save.garden.Clear();
            save.garden.AddRange(GardenFrom(state));
            save.ledger.Clear();
            save.ledgerDropped = 0;
            return save;
        }

        // ---------------------------------------------------------------- helpers

        private Staff StaffWithServerId(string id)
        {
            if (id == null) return null;
            foreach (var s in Sim.Staff)
                if (s.ServerId == id) return s;
            return null;
        }

        private StaffTypeDef HiredType(StaffRole role)
        {
            foreach (var t in Content.StaffTypes)
                if (t.Role == role && !t.IsOwner) return t;
            return null;
        }

        private List<KeyValuePair<ItemDef, int>> ServerItems(JsonValue state)
        {
            var items = new List<KeyValuePair<ItemDef, int>>();
            foreach (var pair in state["inventory"]["items"].Fields)
                if (pair.Value.AsInt() > 0 && TryGet(Content.GetItem, pair.Key, out var def))
                    items.Add(new KeyValuePair<ItemDef, int>(def, pair.Value.AsInt()));
            return items;
        }

        /// <summary>Ready baskets now and seconds until the next one (server <c>market.baskets</c>).</summary>
        private void MarketFrom(JsonValue state, out int ready, out float timer)
        {
            ready = 0;
            timer = Rules.MarketRestockSeconds;
            var now = ParseUtc(state["serverTime"].AsString()) ?? _utcNow();
            double next = double.MaxValue;
            foreach (var b in state["market"]["baskets"].Items)
            {
                if (b["ready"].AsBool()) { ready++; continue; }
                var at = ParseUtc(b["readyAt"].AsString());
                if (at.HasValue) next = Math.Min(next, Math.Max(0, (at.Value - now).TotalSeconds));
            }
            if (next < double.MaxValue) timer = (float)next;
        }

        private List<GardenPlotSave> GardenFrom(JsonValue state)
        {
            var plots = new List<GardenPlotSave>();
            string now = state["serverTime"].AsString() ?? _utcNow().ToString("o", CultureInfo.InvariantCulture);
            foreach (var p in state["garden"]["plots"].Items)
            {
                var crop = p["crop"];
                if (crop.IsNull || !TryGet(Content.GetIngredient, crop["ingredientId"].AsString(), out var ingredient)) continue;
                plots.Add(new GardenPlotSave
                {
                    index = p["index"].AsInt(),
                    crop = ingredient.Id,
                    grownSeconds = crop["progress"].AsDouble() * Math.Max(1.0, ingredient.GrowHours * 3600.0),
                    waterUntilUtc = crop["wateredUntil"].AsString() ?? now,
                    lastUpdateUtc = now,
                });
            }
            return plots;
        }

        /// <summary>Server ownership for the pieces it knows; local counts for local-only (prototype) pieces.</summary>
        public Dictionary<string, int> MergeOwned(IReadOnlyDictionary<string, int> local, JsonValue state, ServerIdMap ids)
        {
            var result = new Dictionary<string, int>();
            var server = new Dictionary<string, int>();
            foreach (var pair in state["inventory"]["furniture"].Fields)
                server[ids.ToLocal(pair.Key)] = pair.Value.AsInt();
            if (local != null)
                foreach (var pair in local)
                    if (!server.ContainsKey(pair.Key)) result[pair.Key] = pair.Value;
            foreach (var pair in server)
                if (Content.HasFurniture(pair.Key) && pair.Value > 0) result[pair.Key] = pair.Value;
            return result;
        }

        private static DateTime? ParseUtc(string text) =>
            DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
                ? t.ToUniversalTime() : (DateTime?)null;
    }
}
