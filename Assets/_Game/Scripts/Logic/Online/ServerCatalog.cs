using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Online
{
    /// <summary>
    /// What the server's ruleset (<c>GET /config</c>) sells, by server id. The client only sends pieces the server
    /// knows; anything else stays local (prototype content the backend has not been given yet).
    /// </summary>
    public sealed class ServerCatalog
    {
        public sealed class Piece
        {
            public string Id;
            /// <summary><c>floor</c>, <c>wall</c>, <c>floorTile</c> or <c>wallpaper</c>.</summary>
            public string Placement;
            public int Price;
            public bool Available;
            public bool OutdoorOnly;
            public JsonValue Definition;
        }

        private readonly Dictionary<string, Piece> _furniture = new Dictionary<string, Piece>();
        private readonly HashSet<string> _exterior = new HashSet<string>();
        private readonly HashSet<string> _items = new HashSet<string>();
        private readonly HashSet<string> _streetCells = new HashSet<string>();
        private readonly HashSet<string> _wardrobe = new HashSet<string>();
        private JsonValue _body;

        public int RulesetVersion { get; private set; }
        public string RulesetId { get; private set; }
        public string ETag { get; private set; }
        /// <summary>The ruleset sells avatar parts (<c>catalog.wardrobe</c>, v3).</summary>
        public bool HasWardrobe { get; private set; }
        /// <summary>The ruleset pays for cleaned litter (<c>rules.trash</c>, v3).</summary>
        public bool HasTrash { get; private set; }

        /// <summary>Reads a <c>/config</c> body (<c>{ rulesetId, version, etag, body: { catalog, rules } }</c>).</summary>
        public static ServerCatalog FromConfig(JsonValue config)
        {
            var catalog = new ServerCatalog
            {
                RulesetId = config["rulesetId"].AsString(),
                RulesetVersion = config["version"].AsInt(),
                ETag = config["etag"].AsString(),
            };
            var body = config["body"];
            catalog._body = body;
            foreach (var f in body["catalog"]["furniture"].Items)
            {
                string id = f["id"].AsString();
                if (string.IsNullOrEmpty(id)) continue;
                catalog._furniture[id] = new Piece
                {
                    Id = id,
                    Placement = f["placement"].AsString() ?? "floor",
                    Price = f["price"].AsInt(),
                    Available = f["available"].AsBool(true),
                    OutdoorOnly = f["outdoorOnly"].AsBool(),
                    Definition = f,
                };
            }
            foreach (var e in body["catalog"]["exterior"].Items)
                if (e["id"].AsString() != null) catalog._exterior.Add(e["id"].AsString());
            foreach (var i in body["catalog"]["items"].Items)
                if (i["id"].AsString() != null) catalog._items.Add(i["id"].AsString());
            catalog.HasWardrobe = body["catalog"]["wardrobe"].Kind == JsonKind.Array;
            foreach (var w in body["catalog"]["wardrobe"].Items)
                catalog._wardrobe.Add(w["slot"].AsString() + ":" + w["id"].AsString());
            catalog.HasTrash = body["rules"]["trash"].Kind == JsonKind.Object;
            foreach (var c in body["rules"]["street"]["cells"].Items)
                catalog._streetCells.Add(Cell(c["x"].AsInt(), c["y"].AsInt()));
            return catalog;
        }

        public Piece Furniture(string id) => id != null && _furniture.TryGetValue(id, out var p) ? p : null;
        public bool HasFurniture(string id, string placement) => Furniture(id)?.Placement == placement;
        public bool HasExterior(string id) => id != null && _exterior.Contains(id);
        public bool HasItem(string id) => id != null && _items.Contains(id);
        public bool IsStreetCell(int x, int y) => _streetCells.Contains(Cell(x, y));
        /// <summary>The server knows this avatar part (server slot id, e.g. <c>hat</c>).</summary>
        public bool HasWardrobePart(string slot, string id) => _wardrobe.Contains(slot + ":" + id);

        /// <summary>Apply supported shop metadata; furniture effects are configured now and implemented in Phase 3.</summary>
        public void ApplyFurniture(Content.ContentPack pack, ServerIdMap ids = null)
        {
            ids ??= new ServerIdMap();
            foreach (var entry in _furniture.Values)
            {
                if (entry.Placement != "floor" && entry.Placement != "wall") continue;
                var data = entry.Definition;
                string localId = ids.ToLocal(entry.Id);
                if (string.IsNullOrEmpty(localId)) continue;
                Content.FurnitureDef piece;
                if (pack.HasFurniture(localId)) piece = pack.GetFurniture(localId);
                else
                {
                    string templateId = ids.ToLocal(data["assetKey"].AsString());
                    if (!pack.HasFurniture(templateId)) continue; // A newer client's artwork is not in this build.
                    var source = pack.GetFurniture(templateId);
                    if (data["kind"].AsString() != source.Kind.ToString()
                        || data["sizeX"].AsInt() != source.SizeX || data["sizeY"].AsInt() != source.SizeY) continue;
                    piece = new Content.FurnitureDef(localId, "catalog.furniture." + entry.Id, source.Kind,
                        source.SizeX, source.SizeY, source.Height, source.SeatHeight, entry.Price,
                        data["unlockLevel"].AsInt(1), source.BreakCount, source.Stackable, source.Surface, source.OutdoorDecor)
                    { AssetKey = templateId };
                    pack.AddCatalogFurniture(piece);
                }
                piece.Price = Math.Max(0, entry.Price);
                piece.UnlockLevel = Math.Max(1, data["unlockLevel"].AsInt(1));
                piece.Available = entry.Available;
                piece.ShopCategory = data["category"].AsString();
                piece.ConfiguredTypeId = data["typeId"].AsString() ?? data["kind"].AsString();
                piece.Ambience = Math.Max(0, data["ambience"].AsInt(piece.Ambience));
                piece.Effects = ParseEffects(data["effects"]);
                if (data["localizedNames"].Kind == JsonKind.Object) piece.NameKey = "catalog.furniture." + entry.Id;
            }
            ApplyConfiguration(pack, ids);
        }

        private void ApplyConfiguration(Content.ContentPack pack, ServerIdMap ids)
        {
            pack.ConfigurationVersion = RulesetVersion;
            pack.ShopCategories.Clear();
            foreach (var row in _body["catalog"]["configuration"]["categories"].Items)
            {
                string code = row["code"].AsString();
                if (string.IsNullOrEmpty(code)) continue;
                pack.ShopCategories.Add(new Content.ShopCategoryDef { Code = code, Active = row["active"].AsBool(), SortOrder = row["sortOrder"].AsInt() });
            }
            pack.ShopCategories.Sort((a,b) => a.SortOrder != b.SortOrder ? a.SortOrder.CompareTo(b.SortOrder) : string.CompareOrdinal(a.Code,b.Code));
            var economyRows = new List<Content.EconomyDish>();
            foreach (var row in _body["economy"]["dishes"].Items)
            {
                string id = ids.ToLocal(row["id"].AsString());
                if (string.IsNullOrEmpty(id)) continue;
                var ingredients = new List<Content.IngredientAmount>();
                var ingredientIds = new List<string>();
                bool supported = true;
                foreach (var ingredient in row["ingredients"].Items)
                {
                    string ingredientId = ids.ToLocal(ingredient.AsString());
                    if (!pack.HasIngredient(ingredientId)) { supported = false; break; }
                    ingredientIds.Add(ingredientId);
                    ingredients.Add(new Content.IngredientAmount(pack.GetIngredient(ingredientId), 1));
                }
                if (!supported || ingredients.Count == 0) continue;
                Content.DishDef dish;
                if (pack.HasDish(id)) dish = pack.GetDish(id);
                else
                {
                    string templateId = ids.ToLocal(row["assetKey"].AsString());
                    if (!pack.HasDish(templateId)) continue;
                    var source = pack.GetDish(templateId);
                    dish = new Content.DishDef(id, "catalog.recipe." + row["id"].AsString(), row["basePrice"].AsInt(1),
                        row["cookSeconds"].AsInt(1), row["eatSeconds"].AsInt((int)source.EatSeconds), source.Stars,
                        ingredients, false, ParseCategory(row["category"].AsString()), row["unlockLevel"].AsInt(1)) { AssetKey = templateId };
                    pack.AddCatalogDish(dish);
                }
                // Keep the same object: learned levels and menu slots reference it.
                dish.ApplyPublished("catalog.recipe." + row["id"].AsString(), row["basePrice"].AsInt(1), row["cookSeconds"].AsInt(1),
                    row["eatSeconds"].AsInt((int)dish.EatSeconds), row["unlockLevel"].AsInt(1), row["enabled"].AsBool(true), ingredients, ParseCategory(row["category"].AsString()));
                economyRows.Add(new Content.EconomyDish { id = id, cuisine = row["cuisine"].AsString(), category = row["category"].AsString(),
                    ingredients = ingredientIds.ToArray(), basePrice = dish.Price, cookSeconds = dish.CookSeconds,
                    unlockLevel = dish.UnlockLevel, inPhase1 = dish.Enabled });
            }
            ApplyRules(pack);
            ApplyItems(pack, ids);
            if (pack.Economy != null && economyRows.Count > 0)
            {
                pack.Economy.dishes = economyRows.ToArray();
                var settings = _body["economy"]["settings"];
                if (settings.Kind == JsonKind.Object)
                {
                    var target = pack.Economy.settings;
                    target.maxDishLevel = settings["maxDishLevel"].AsInt(target.maxDishLevel);
                    target.ingredientSetsPerUpgrade = settings["ingredientSetsPerUpgrade"].AsInt(target.ingredientSetsPerUpgrade);
                    target.dishPriceStepPerLevel = (float)settings["dishPriceIncreasePerLevel"].AsDouble(target.dishPriceStepPerLevel);
                    target.xpUpgradeBase = settings["xpUpgradeBase"].AsInt(target.xpUpgradeBase);
                    target.xpUpgradePerLevel = settings["xpUpgradePerLevel"].AsInt(target.xpUpgradePerLevel);
                }
            }
            // BO → Game Rules → Restaurant level: the published playable cap replaces the sheet's Phase 1 cap.
            int maxLevel = _body["rules"]["maxLevel"].AsInt(0);
            if (pack.Economy != null && maxLevel > 0) pack.Economy.settings.phase1MaxLevel = maxLevel;
        }

        /// <summary>A catalog piece's effects block; missing or neutral blocks become <see cref="Content.FurnitureEffects.None"/>.</summary>
        private static Content.FurnitureEffects ParseEffects(JsonValue e)
        {
            if (e.Kind != JsonKind.Object) return Content.FurnitureEffects.None;
            string stacking = e["globalStacking"].AsString();
            var effects = new Content.FurnitureEffects
            {
                OperationTimePercent = Math.Max(10, Math.Min(200, e["operationTimePercent"].AsInt(100))),
                DemandCapBonus = Math.Max(0, e["demandCapBonus"].AsInt(0)),
                PatienceSeconds = Math.Max(0, e["patienceSeconds"].AsInt(0)),
                Stacking = stacking == "sum" ? Content.EffectStacking.Sum
                    : stacking == "highest" ? Content.EffectStacking.Highest : Content.EffectStacking.UniqueAsset,
                MaxCopies = Math.Max(1, e["maxCopies"].AsInt(1)),
            };
            return effects.IsNeutral ? Content.FurnitureEffects.None : effects;
        }

        /// <summary>
        /// Staff items (BO → Catalog → Staff items): published price, energy, availability and unlock level replace the
        /// shipped values; items added in the BO join the pack, drawn with the first shipped item's icon.
        /// </summary>
        private void ApplyItems(Content.ContentPack pack, ServerIdMap ids)
        {
            string template = pack.Items.Count > 0 ? pack.Items[0].AssetKey ?? pack.Items[0].Id : null;
            foreach (var row in _body["catalog"]["items"].Items)
            {
                string id = ids.ToLocal(row["id"].AsString());
                int energy = row["energy"].AsInt(0), price = row["price"].AsInt(-1);
                var effects = ParsePerkEffects(row["effects"]);
                int duration = row["durationMinutes"].AsInt(0);
                bool perk = effects.IsTimed && duration > 0;
                if (string.IsNullOrEmpty(id) || (energy <= 0 && !perk) || price < 0) continue;
                Content.ItemDef item;
                if (pack.HasItem(id)) item = pack.GetItem(id);
                else
                {
                    if (template == null) continue; // Nothing to draw it with in this build.
                    item = new Content.ItemDef(id, "catalog.item." + row["id"].AsString(), energy, price) { AssetKey = template };
                    pack.AddCatalogItem(item);
                }
                item.EnergyRestore = Math.Min(Content.StaffEnergyRules.Max, Math.Max(0, energy));
                item.Price = price;
                item.Available = row["available"].AsBool(true);
                item.UnlockLevel = Math.Max(1, row["unlockLevel"].AsInt(1));
                item.Target = row["target"].AsString() == "restaurant" ? Content.ItemTarget.Restaurant : Content.ItemTarget.Employee;
                item.Effects = perk ? effects : Content.PerkEffects.None;
                item.DurationMinutes = perk ? duration : 0;
                item.SaleStartUtc = ParseUtc(row["saleStart"].AsString());
                item.SaleEndUtc = ParseUtc(row["saleEnd"].AsString());
            }
        }

        /// <summary>A staff item's timed effects (Phase 4C perks), clamped to the BO ranges.</summary>
        private static Content.PerkEffects ParsePerkEffects(JsonValue e)
        {
            if (e.Kind != JsonKind.Object) return Content.PerkEffects.None;
            var effects = new Content.PerkEffects
            {
                WorkSpeedPercent = Math.Max(0, Math.Min(500, e["workSpeedPercent"].AsInt(0))),
                EnergyDrainPercent = Math.Max(0, Math.Min(100, e["energyDrainPercent"].AsInt(0))),
                XpPercent = Math.Max(0, Math.Min(500, e["xpPercent"].AsInt(0))),
                PopularityPercent = Math.Max(0, Math.Min(500, e["popularityPercent"].AsInt(0))),
            };
            return effects.IsTimed ? effects : Content.PerkEffects.None;
        }

        private static DateTime? ParseUtc(string text) =>
            DateTime.TryParse(text, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t)
                ? t : (DateTime?)null;

        /// <summary>
        /// Copies the published game rules (BO → Game Rules) over rules.json. Objects the session already holds
        /// (demand, staff energy, toilets, entertainment) change in place; groups missing from an older ruleset keep
        /// the pack's values.
        /// </summary>
        private void ApplyRules(Content.ContentPack pack)
        {
            // Tips are a mode plus two sheet settings (BO → Game Rules → Tips).
            var tips = pack.Rules.Tips;
            var economySettings = _body["economy"]["settings"];
            tips.Enabled = _body["modes"]["tips"].AsString() == "ambience";
            tips.PerAmbience = (float)economySettings["tipPerAmbience"].AsDouble(tips.PerAmbience);
            tips.Cap = (float)economySettings["tipCap"].AsDouble(tips.Cap);
            var rules = _body["rules"];
            if (rules.Kind != JsonKind.Object) return;
            var target = pack.Rules;
            var demand = rules["demand"];
            if (demand.Kind == JsonKind.Object)
            {
                var d = target.Demand;
                d.Start = demand["start"].AsDouble(d.Start);
                d.Min = demand["min"].AsDouble(d.Min);
                d.Max = demand["max"].AsDouble(d.Max);
                d.PerHappyGuest = demand["perHappyGuest"].AsDouble(d.PerHappyGuest);
                // The server signs the unhappy change (negative); the client stores the amount removed.
                d.PerUnhappyGuest = Math.Abs(demand["perUnhappyGuest"].AsDouble(-d.PerUnhappyGuest));
                d.ClosedDecayPerHour = demand["closedDecayPerHour"].AsDouble(d.ClosedDecayPerHour);
                d.GuestsPerMinutePerDemand = demand["guestsPerMinutePerDemand"].AsDouble(d.GuestsPerMinutePerDemand);
                d.DailyGuestCap = Math.Max(0, demand["dailyGuestCap"].AsInt(0));
            }
            var energy = rules["staffEnergy"];
            if (energy.Kind == JsonKind.Object && energy["max"].AsDouble() > 0)
            {
                // The client counts energy out of 100; scale the server's numbers to that.
                float scale = Content.StaffEnergyRules.Max / (float)energy["max"].AsDouble();
                var e = target.StaffEnergy;
                e.DrainPerMinute = (float)energy["drainPerMinute"].AsDouble(e.DrainPerMinute / scale) * scale;
                e.RecoverPerMinute = (float)energy["recoverPerMinute"].AsDouble(e.RecoverPerMinute / scale) * scale;
                e.AutoReturnAt = (float)energy["autoReturnAt"].AsDouble(e.AutoReturnAt / scale) * scale;
                var bands = new List<Content.EnergyBand>();
                foreach (var band in energy["speedBands"].Items)
                    bands.Add(new Content.EnergyBand((float)band["min"].AsDouble() * scale, (float)band["speed"].AsDouble(1)));
                if (bands.Count > 0) e.Bands = bands;
            }
            var perks = rules["perks"];
            if (perks.Kind == JsonKind.Object)
            {
                var p = target.Perks;
                p.Enabled = perks["enabled"].AsBool(p.Enabled);
                p.SlotsPerEmployee = Math.Max(1, perks["slotsPerEmployee"].AsInt(p.SlotsPerEmployee));
                p.SlotsRestaurant = Math.Max(0, perks["slotsRestaurant"].AsInt(p.SlotsRestaurant));
            }

            var toilets = rules["toilets"];
            if (toilets.Kind == JsonKind.Object)
            {
                var t = target.Toilets;
                t.fromLevel = Math.Max(1, toilets["fromLevel"].AsInt(t.fromLevel));
                t.chance = (float)toilets["chance"].AsDouble(t.chance);
                t.toiletSeconds = (float)toilets["toiletSeconds"].AsDouble(t.toiletSeconds);
                t.sinkSeconds = (float)toilets["sinkSeconds"].AsDouble(t.sinkSeconds);
                t.fixSeconds = (float)toilets["fixSeconds"].AsDouble(t.fixSeconds);
            }
            target.MarketBaskets = Math.Max(1, rules["marketBaskets"].AsInt(target.MarketBaskets));
            target.MarketRestockSeconds = (float)rules["marketRestockSeconds"].AsDouble(target.MarketRestockSeconds);
            target.GardenHarvest = Math.Max(1, rules["gardenHarvest"].AsInt(target.GardenHarvest));
            var patience = rules["foodPatience"];
            if (patience.Kind == JsonKind.Object)
            {
                // Only the base moves; the pack's own reference base stays, so guest types scale against it.
                target.FoodPatience.baseSeconds = (float)patience["baseSeconds"].AsDouble(target.FoodPatience.baseSeconds);
                target.FoodPatience.perAmbienceSeconds = (float)patience["perAmbienceSeconds"].AsDouble(target.FoodPatience.perAmbienceSeconds);
            }
            var watering = rules["gardenWatering"];
            target.GardenWatering.AddsUp = watering.Kind == JsonKind.Object && watering["mode"].AsString() == "add";
            target.GardenWatering.CapHours = (float)watering["capHours"].AsDouble(0);
            var fun = rules["entertainment"];
            if (fun.Kind == JsonKind.Object)
            {
                target.Entertainment.playSeconds = (float)fun["useSeconds"].AsDouble(target.Entertainment.playSeconds);
                target.Entertainment.coinsPerUse = Math.Max(0, fun["coinsPerUse"].AsInt(target.Entertainment.coinsPerUse));
            }
            if (rules["terrace"].Kind == JsonKind.Array)
            {
                var steps = new List<Layout.TerraceStep>();
                foreach (var step in rules["terrace"].Items)
                    steps.Add(new Layout.TerraceStep { width = step["width"].AsInt(), depth = step["depth"].AsInt(),
                        level = step["level"].AsInt(1), price = step["price"].AsLong() });
                if (steps.TrueForAll(s => s.IsValid)) target.TerraceSteps = steps;
            }
            var settings = _body["economy"]["settings"];
            if (pack.Economy != null && settings.Kind == JsonKind.Object)
            {
                var s = pack.Economy.settings;
                s.wateringHours = (float)settings["wateringHours"].AsDouble(s.wateringHours);
                s.seedCost = settings["seedCost"].AsInt(s.seedCost);
                s.offlineRate = (float)settings["offlineRate"].AsDouble(s.offlineRate);
                s.offlineCapHours = (float)settings["offlineCapHours"].AsDouble(s.offlineCapHours);
                if (settings["gardenPlotLevels"].Kind == JsonKind.Array)
                {
                    var levels = new List<int>();
                    foreach (var level in settings["gardenPlotLevels"].Items) levels.Add(Math.Max(1, level.AsInt(1)));
                    s.gardenPlotLevels = levels.ToArray();
                }
            }
        }

        private static Content.DishCategory ParseCategory(string category) => category == "starter" ? Content.DishCategory.Starter :
            category == "dessert" ? Content.DishCategory.Dessert : category == "drink" ? Content.DishCategory.Drink : Content.DishCategory.Main;

        /// <summary>The first free indoor floor tile (the fallback for floors the server does not know).</summary>
        public string DefaultFloor
        {
            get
            {
                foreach (var p in _furniture.Values)
                    if (p.Placement == "floorTile" && p.Price == 0 && !p.OutdoorOnly) return p.Id;
                return null;
            }
        }

        /// <summary>The first free terrace tile, or any free floor tile.</summary>
        public string DefaultTerraceFloor
        {
            get
            {
                foreach (var p in _furniture.Values)
                    if (p.Placement == "floorTile" && p.Price == 0 && p.OutdoorOnly) return p.Id;
                return DefaultFloor;
            }
        }

        private static string Cell(int x, int y) => x + "," + y;
    }

    /// <summary>
    /// Local content id ↔ server id, for the few ids that differ (the theme's floors and wallpapers). Data-driven
    /// (<c>Content/&lt;pack&gt;/server_ids.json</c>); unlisted ids are the same on both sides. A local id mapped to an
    /// empty server id means "none" (the theme's default wallpaper is the server's bare wall).
    /// </summary>
    public sealed class ServerIdMap
    {
        [Serializable]
        public sealed class Entry
        {
            public string local;
            public string server;
        }

        [Serializable]
        public sealed class File
        {
            public Entry[] ids = new Entry[0];
        }

        private readonly Dictionary<string, string> _toServer = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _toLocal = new Dictionary<string, string>();

        public ServerIdMap(IEnumerable<Entry> entries = null)
        {
            if (entries == null) return;
            foreach (var e in entries)
            {
                if (e == null || string.IsNullOrEmpty(e.local)) continue;
                if (_toServer.ContainsKey(e.local))
                    throw new Content.ContentException($"server_ids: '{e.local}' is listed twice");
                _toServer[e.local] = e.server ?? "";
                if (!string.IsNullOrEmpty(e.server) && !_toLocal.ContainsKey(e.server)) _toLocal[e.server] = e.local;
            }
        }

        /// <summary>The server id, or null for "none".</summary>
        public string ToServer(string localId)
        {
            if (string.IsNullOrEmpty(localId)) return null;
            if (!_toServer.TryGetValue(localId, out var s)) return localId;
            return s.Length == 0 ? null : s;
        }

        public string ToLocal(string serverId)
        {
            if (string.IsNullOrEmpty(serverId)) return null;
            return _toLocal.TryGetValue(serverId, out var l) ? l : serverId;
        }
    }
}
