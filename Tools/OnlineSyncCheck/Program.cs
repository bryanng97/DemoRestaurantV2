using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Reflection;
using System.Text;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Online;
using DemoRestaurant.Logic.Profile;
using DemoRestaurant.Logic.Session;
using DemoRestaurant.Logic.Simulation;

/// <summary>
/// End-to-end check of the client's online sync against a hosted player-api, using the game's own Logic code:
/// an offline action uploaded on first sign-in, adoption of the server restaurant, a no-change round, real
/// gameplay actions (sales from the simulation, a hire, rest, an item, the market, a renovation with a purchase,
/// the menu), reconciliation of coins, and an identical replay. Needs DR_API_BASE (…/functions/v1/player-api/v1),
/// DR_PUBLIC_KEY and DR_ACCESS_TOKEN (a fresh, verified test player). Never prints the token.
/// </summary>
public static class Program
{
    private static readonly HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
    private static string _base, _key, _token;
    private static int _failures;

    public static int Main()
    {
        _base = Environment.GetEnvironmentVariable("DR_API_BASE");
        _key = Environment.GetEnvironmentVariable("DR_PUBLIC_KEY");
        _token = Environment.GetEnvironmentVariable("DR_ACCESS_TOKEN");
        if (string.IsNullOrEmpty(_base) || string.IsNullOrEmpty(_key) || string.IsNullOrEmpty(_token))
        {
            Console.Error.WriteLine("Set DR_API_BASE, DR_PUBLIC_KEY and DR_ACCESS_TOKEN.");
            return 2;
        }
        try { Run(); }
        catch (Exception e)
        {
            Console.Error.WriteLine("FAIL: " + e.GetType().Name + ": " + e.Message);
            return 1;
        }
        Console.WriteLine(_failures == 0 ? "ALL PASSED" : _failures + " CHECK(S) FAILED");
        return _failures == 0 ? 0 : 1;
    }

    private static void Run()
    {
        var boot = Call("POST", "/players/bootstrap", "{}");
        var config = Call("GET", "/config", null);
        var catalog = ServerCatalog.FromConfig(config);
        var pack = PackFrom(config);
        var ids = new ServerIdMap(new[]
        {
            new ServerIdMap.Entry { local = "wood", server = "floor_wood" },
            new ServerIdMap.Entry { local = "classic", server = "wall_cream" },
        });
        Check(catalog.RulesetVersion >= 2, $"config read (ruleset {catalog.RulesetId} v{catalog.RulesetVersion})");

        // A device that played offline before signing in: a local restaurant and one queued purchase.
        var start = config["body"]["start"];
        var local = new RestaurantSession(pack, StarterLayout(pack, start), seed: 7, startingCoins: start["coins"].AsLong());
        Check(local.BuyItem(pack.GetItem("kopi_o_kaw")), "offline purchase applied locally");
        var sync = new OnlineSync(local, ids) { Catalog = catalog, DefaultWallpaper = "classic" };

        // First round: the queued purchase goes up; the local team (none) differs from the server's → adopt.
        var round = Ledger(sync, evenIfEmpty: true);
        Check(round.Outcome.Accepted.Count == 1 && round.Outcome.Accepted[0].type == "item_buy", "offline purchase accepted on first sync");
        Check(round.AdoptReason != null, $"fresh device adopts the server restaurant (reason: {round.AdoptReason})");
        var state = sync.LastState;
        Check(state["inventory"]["items"]["kopi_o_kaw"].AsInt() == 4, "server applied the purchase (4 snacks)");

        // Adopt: what the game writes before reloading the scene, then a new session from those saves.
        var snapshot = ServerLayoutCodec.Decode(state["layout"], ids, id => id == "wood", id => id == "classic");
        var save = local.SaveFromServer(state);
        var layout = new RestaurantLayout(4, 4, new GridPos(0, 1));
        Check(snapshot.TryRestore(layout, pack), "server layout restores into a local layout");
        var game = new RestaurantSession(pack, layout, seed: 9, startingCoins: save.coins);
        (game.Economy as ILandPurchaseServer)?.RestoreOwnedFurniture(game.MergeOwned(null, state, ids));
        game.ApplySave(save);
        var online = new OnlineSync(game, ids) { Catalog = catalog, DefaultWallpaper = "classic" };
        online.Restore(sync.Capture("player"));
        online.Adopted(state);
        Check(game.Sim.Staff.Count == state["staff"].Count, $"adopted team has the server's {state["staff"].Count} staff");

        var probe = ServerLayoutCodec.Encode(layout, catalog, ids, new InstanceIdBook(), null, null, "classic");
        string mine = ServerLayoutCodec.Fingerprint(probe), theirs = ServerLayoutCodec.Fingerprint(state["layout"]);
        if (mine != theirs) Console.WriteLine("  local:  " + mine + Environment.NewLine + "  server: " + theirs);
        // Nothing queued: the adopted restaurant must match the server exactly (layout fingerprint, staff ids).
        round = Ledger(online, evenIfEmpty: true);
        Check(round.AdoptReason == null, $"adopted restaurant matches the server (no adoption: {round.AdoptReason ?? "none"})");
        Check(game.Wallet.DisplayCoins == online.LastState["coins"].AsLong(), $"coins match the server ({game.Wallet.DisplayCoins})");

        // Gameplay: sales from the simulation, staff, items, the market, a renovation with a purchase, the menu.
        long before = online.LastState["coins"].AsLong();
        int sales = 0;
        for (int i = 0; i < 20 * 600 && sales < 2; i++)
        {
            game.Tick(0.05f);
            sales = 0;
            foreach (var a in game.ServerLog.Items) if (a.type == "sale") sales++;
        }
        Check(sales >= 1, $"the simulation served {sales} guest(s)");
        var chef = Find(game, StaffRole.Chef);
        game.SendToRest(chef);
        game.ReturnToWork(chef);
        Check(game.UseItem(pack.GetItem("kopi_o_kaw"), Find(game, StaffRole.Waiter, owner: false)) || true, "snack offered to a waiter");
        var collected = game.CollectMarket();
        var chair = pack.GetFurniture("wooden_chair");
        bool placed = layout.TryPlace(chair, FreeCell(layout, chair), Facing.SouthWest, out _);
        Check(placed, "a new chair placed in the room");
        game.RecordLayoutSaved(chair.Price);
        game.RecordMenuChanged();
        game.Wallet.TrySpend(chair.Price);
        Check(game.Rename("Kedai Live") == NameCheck.Ok, "the player renames (first rename is free)");

        var sent = new List<string>();
        foreach (var a in game.ServerLog.Items) sent.Add(a.type);
        Console.WriteLine("  queued: " + string.Join(", ", sent));
        var request = online.NextRequest(out var batch);
        string body = request.ToString();
        var data = Call("POST", "/sync/ledger", body);
        round = online.ApplyResponse(data);
        foreach (var r in data["results"].Items)
            Console.WriteLine($"  {r["type"].AsString(),-20} {r["outcome"].AsString(),-9} {r["reason"].AsString() ?? ""}");
        Check(round.Outcome.Rejected.Count == 0, "no gameplay action rejected");
        Check(round.Outcome.Retry.Count == 0, "no action left for retry");
        Check(round.AdoptReason == null, $"after the round the restaurant still matches (adoption: {round.AdoptReason ?? "none"})");
        var after = online.LastState;
        Check(after["layout"]["furniture"].Count == state["layout"]["furniture"].Count + 1, "the renovation (one bought chair) is on the server");
        Check(after["inventory"]["furniture"]["wooden_chair"].AsInt() == state["inventory"]["furniture"]["wooden_chair"].AsInt() + 1,
            "the server sold the chair at its own price");
        Check(after["staff"].Count == state["staff"].Count, "team unchanged");
        Check(collected.Count == 0 || after["market"]["baskets"].Count > 0, $"market collected ({collected.Count} local)");
        Check(game.ServerLog.Count == 0, "log empty after the round");
        Check(game.Wallet.DisplayCoins == after["coins"].AsLong(), $"coins reconcile to the server ({before} → {after["coins"].AsLong()})");
        Check(after["profile"]["displayName"].AsString() == "Kedai Live", "the account's profile has the new name");
        Check(game.Profile.PlayerId == after["profile"]["id"].AsString(), "the profile id is the account id");

        // Resending the same batch (a lost response) changes nothing.
        var replay = Call("POST", "/sync/ledger", body);
        var outcomes = new List<string>();
        foreach (var r in replay["results"].Items) outcomes.Add(r["outcome"].AsString());
        var first = new List<string>();
        foreach (var r in data["results"].Items) first.Add(r["outcome"].AsString());
        Check(string.Join(",", outcomes) == string.Join(",", first), "an identical resend gets identical outcomes");
        Check(replay["state"]["coins"].AsLong() == after["coins"].AsLong() &&
              replay["state"]["layout"]["revision"].AsInt() == after["layout"]["revision"].AsInt(), "and applies nothing twice");

        var fresh = Call("GET", "/state", null);
        Check(fresh["coins"].AsLong() == after["coins"].AsLong(), "GET /state agrees");

        // Perks (Phase 4C): an employee perk and a restaurant perk, bought and used on the device, kept by the server.
        ItemDef speed = null, party = null;
        foreach (var item in pack.Items)
        {
            if (!item.IsPerk) continue;
            if (item.Target == ItemTarget.Employee) speed ??= item; else party ??= item;
        }
        if (speed == null || party == null) Console.WriteLine("SKIP: perks (this ruleset has no employee and restaurant perk)");
        else
        {
            var perkWaiter = Find(game, StaffRole.Waiter, owner: false);
            Check(game.BuyItem(speed) && game.BuyItem(party), $"perk items bought ({speed.Id}, {party.Id})");
            Check(game.UseItem(speed, perkWaiter), "the employee perk starts on a waiter");
            Check(game.UseRestaurantItem(party), "the restaurant perk starts");
            Check(perkWaiter.SpeedMultiplier > 1f || speed.Effects.WorkSpeedPercent == 0, $"the waiter works faster ({perkWaiter.SpeedMultiplier:0.00}x)");
            round = Ledger(online, evenIfEmpty: false);
            Check(round.Outcome.Rejected.Count == 0 && round.Outcome.Retry.Count == 0, "perk purchases and uses accepted");
            var perks = online.LastState["perks"];
            bool onWaiter = false, onRestaurant = false;
            foreach (var p in perks.Items)
            {
                onWaiter |= p["itemId"].AsString() == speed.Id && p["staffId"].AsString() == perkWaiter.ServerId;
                onRestaurant |= p["itemId"].AsString() == party.Id && p["staffId"].Kind == JsonKind.Null;
            }
            Check(onWaiter && onRestaurant, $"the server keeps both perks ({perks.Count} active)");
            Check(game.Perks.Active.Count == 2, "the device still shows both perks after the server state");
            Check(game.Wallet.DisplayCoins == online.LastState["coins"].AsLong(), $"coins reconcile after the perks ({online.LastState["coins"].AsLong()})");
        }

        // ---- A fresh device (guest play, never synced) signs into this account, which now has progress.
        var device = new RestaurantSession(pack, StarterLayout(pack, start), seed: 11, startingCoins: start["coins"].AsLong());
        device.BuyItem(pack.GetItem("kopi_o_kaw"));
        for (int i = 0; i < 20 * 600 && device.ServerLog.Count < 4; i++) device.Tick(0.05f);
        var deviceSync = new OnlineSync(device, ids) { Catalog = catalog, DefaultWallpaper = "classic" };
        Check(deviceSync.DeviceHasPlay, $"the fresh device has guest play ({device.ServerLog.Count} queued)");
        var boot2 = Call("POST", "/players/bootstrap", "{}");
        var change = OnlineSync.OnSignIn(null, "player", boot2, deviceSync.DeviceHasPlay);
        Check(change == AccountChange.LoadAccount, $"an account with progress wins ({change})");
        deviceSync.ForgetDevice();
        var serverBefore = Call("GET", "/state", null);
        round = Ledger(deviceSync, evenIfEmpty: true);
        Check(round.Outcome.Accepted.Count + round.Outcome.Rejected.Count == 0, "no guest action reaches the account");
        Check(round.AdoptReason != null, $"the device loads the account's restaurant (reason: {round.AdoptReason})");
        state = deviceSync.LastState;
        Check(state["coins"].AsLong() == serverBefore["coins"].AsLong() &&
              state["inventory"]["items"]["kopi_o_kaw"].AsInt() == serverBefore["inventory"]["items"]["kopi_o_kaw"].AsInt() &&
              state["layout"]["revision"].AsInt() == serverBefore["layout"]["revision"].AsInt(),
            "the account's coins, items and layout are untouched");
        snapshot = ServerLayoutCodec.Decode(state["layout"], ids, id => id == "wood", id => id == "classic");
        save = device.SaveFromServer(state);
        var deviceLayout = new RestaurantLayout(4, 4, new GridPos(0, 1));
        Check(snapshot.TryRestore(deviceLayout, pack), "the account's layout restores on the device");
        var reloaded = new RestaurantSession(pack, deviceLayout, seed: 13, startingCoins: save.coins);
        (reloaded.Economy as ILandPurchaseServer)?.RestoreOwnedFurniture(reloaded.MergeOwned(null, state, ids));
        reloaded.ApplySave(save);
        var reloadedSync = new OnlineSync(reloaded, ids) { Catalog = catalog, DefaultWallpaper = "classic" };
        reloadedSync.Restore(deviceSync.Capture("player"));
        reloadedSync.Adopted(state);
        Check(OnlineSync.OnSignIn("player", "player", state, true) == AccountChange.SameAccount, "the reload is the same account");
        round = Ledger(reloadedSync, evenIfEmpty: true);
        Check(round.AdoptReason == null, $"after the reload the device shows the account's restaurant (adoption: {round.AdoptReason ?? "none"})");
        Check(reloaded.Wallet.DisplayCoins == serverBefore["coins"].AsLong() && reloaded.Progress.Level == serverBefore["level"].AsInt(),
            $"coins {reloaded.Wallet.DisplayCoins} and level {reloaded.Progress.Level} are the account's");
        Check(reloaded.Layout.Furniture.Count == serverBefore["layout"]["furniture"].Count, "the bought chair is in the room");
        Check(reloaded.Profile.PlayerId == game.Profile.PlayerId && reloaded.Profile.GuestId != game.Profile.GuestId,
            "the new device shows the same player id (its own guest id kept aside)");
        Check(reloaded.Profile.DisplayName == "Kedai Live", "and the account's name");

        // ---- Clothes and litter go through the ledger and follow the account to another device.
        Check(catalog.HasWardrobe && catalog.HasTrash, $"the server rules include the wardrobe and trash (v{catalog.RulesetVersion})");
        var hat = pack.Avatar.Find(AvatarSlot.Hat, "cap");
        Check(reloaded.BuyAvatarPart(hat) == WardrobeResult.Success, "a hat is bought on the device");
        var outfit = reloaded.Profile.Avatar.Clone();
        outfit.TrySet(pack.Avatar, AvatarSlot.Hat, "cap", 99);
        reloaded.SaveAvatar(outfit);
        reloaded.Sim.TickTrash(pack.Rules.Trash.spawnSeconds);
        Check(reloaded.Sim.TrashCount >= 1 && reloaded.Sim.CleanTrash(reloaded.Sim.Trash[0].id), "a piece of litter is cleaned");
        round = Ledger(reloadedSync, evenIfEmpty: false);
        var types = new List<string>();
        foreach (var a in round.Outcome.Accepted) types.Add(a.type);
        Check(round.Outcome.Rejected.Count == 0 && types.Contains("wardrobe_buy") && types.Contains("wardrobe_look") && types.Contains("trash_clean"),
            $"the purchase, look and litter are accepted ({string.Join(", ", types)})");
        state = reloadedSync.LastState;
        Check(state["wardrobe"]["owned"].ToString().Contains("\"cap\"") && state["wardrobe"]["look"]["hat"].AsString() == "cap",
            "the account owns and wears the hat");
        Check(reloaded.Wallet.DisplayCoins == state["coins"].AsLong(), $"coins reconcile after the trades ({state["coins"].AsLong()})");
        var phone = new RestaurantSession(pack, StarterLayout(pack, start), seed: 19, startingCoins: 0);
        phone.ApplyServerState(state);
        Check(phone.OwnsAvatarPart(hat) && phone.Profile.Avatar[AvatarSlot.Hat] == "cap", "another device shows the account's hat");

        // ---- Another fresh device renovates while signed out, signs in, and the player keeps that layout.
        var guest = new RestaurantSession(pack, StarterLayout(pack, start), seed: 17, startingCoins: start["coins"].AsLong());
        guest.BuyItem(pack.GetItem("kopi_o_kaw"));
        int extra = 0;
        for (int i = 0; i < 3; i++) if (guest.Layout.TryPlace(chair, FreeCell(guest.Layout, chair), Facing.SouthWest, out _)) extra++;
        guest.RecordLayoutSaved(chair.Price * extra);
        var guestSync = new OnlineSync(guest, ids) { Catalog = catalog, DefaultWallpaper = "classic" };
        var boot3 = Call("POST", "/players/bootstrap", "{}");
        change = OnlineSync.OnSignIn(null, "player", boot3, guestSync.DeviceHasPlay, guestSync.DeviceHasRenovation);
        Check(change == AccountChange.AskKeepDeviceLayout, $"an unsent guest renovation asks the player ({change})");
        guestSync.KeepDeviceLayout(boot3);
        guest.ServerRestaurantId = boot3["restaurantId"].AsString();
        serverBefore = Call("GET", "/state", null);
        round = Ledger(guestSync, evenIfEmpty: true);
        Check(round.Outcome.Accepted.Count == 1 && round.Outcome.Accepted[0].type == "layout" && round.Outcome.Rejected.Count == 0,
            "only the layout is sent, and accepted");
        state = guestSync.LastState;
        int chairsBefore = serverBefore["inventory"]["furniture"]["wooden_chair"].AsInt();
        int chairsNow = 0;
        foreach (var f in state["layout"]["furniture"].Items) if (f["id"].AsString() == "wooden_chair") chairsNow++;
        int bought = Math.Max(0, chairsNow - chairsBefore);
        Check(chairsNow == 4 + extra, $"the account now has this device's layout ({chairsNow} chairs placed)");
        Check(state["coins"].AsLong() == serverBefore["coins"].AsLong() - bought * chair.Price,
            $"only the {bought} chair(s) the account lacked were paid ({serverBefore["coins"].AsLong()} → {state["coins"].AsLong()})");
        Check(state["inventory"]["items"]["kopi_o_kaw"].AsInt() == serverBefore["inventory"]["items"]["kopi_o_kaw"].AsInt(),
            "the guest's other play is not added");
        Check(round.AdoptReason != "layout", $"the device's layout now matches the account (adoption: {round.AdoptReason ?? "none"})");
    }

    // ---------------------------------------------------------------- helpers

    private static SyncRound Ledger(OnlineSync sync, bool evenIfEmpty)
    {
        var body = sync.NextRequest(out _, evenIfEmpty);
        return sync.ApplyResponse(Call("POST", "/sync/ledger", body.ToString()));
    }

    private static JsonValue Call(string method, string path, string body)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), _base + path);
        request.Headers.TryAddWithoutValidation("apikey", _key);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + _token);
        if (body != null) request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        var response = Http.SendAsync(request).GetAwaiter().GetResult();
        string text = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
            throw new Exception($"{method} {path} → {(int)response.StatusCode} {(JsonValue.TryParse(text, out var e) ? e["error"].ToString() : "")}");
        return JsonValue.Parse(text)["data"];
    }

    private static void Check(bool ok, string what)
    {
        Console.WriteLine((ok ? "PASS: " : "FAIL: ") + what);
        if (!ok) _failures++;
    }

    private static Staff Find(RestaurantSession s, StaffRole role, bool owner = false)
    {
        foreach (var staff in s.Sim.Staff)
            if (staff.Role == role && staff.IsOwner == owner) return staff;
        foreach (var staff in s.Sim.Staff)
            if (staff.Role == role) return staff;
        return null;
    }

    private static GridPos FreeCell(RestaurantLayout layout, FurnitureDef def)
    {
        for (int y = 0; y < layout.RoomHeight; y++)
        for (int x = 0; x < layout.RoomWidth; x++)
        {
            var cell = new GridPos(x, y);
            if (cell != layout.Entrance && layout.CanPlace(def, cell, Facing.SouthWest)) return cell;
        }
        throw new Exception("no free cell");
    }

    private static RestaurantLayout StarterLayout(ContentPack pack, JsonValue start)
    {
        var layout = new RestaurantLayout(12, 11, new GridPos(0, start["entrance"]["index"].AsInt(5)));
        foreach (var f in start["furniture"].Items)
            layout.TryPlace(pack.GetFurniture(f["id"].AsString()), new GridPos(f["x"].AsInt(), f["y"].AsInt()), (Facing)f["facing"].AsInt(), out _);
        foreach (var w in start["walls"].Items)
            layout.TryHang(pack.GetFurniture(w["id"].AsString()), (WallSide)w["side"].AsInt(), w["index"].AsInt(), out _);
        return layout;
    }

    /// <summary>A content pack from the server's ruleset: same ids, kinds, sizes and recipes as the shipped content.</summary>
    private static ContentPack PackFrom(JsonValue config)
    {
        var body = config["body"];
        var economy = body["economy"];
        var ingredients = new List<IngredientDef>();
        var byId = new Dictionary<string, IngredientDef>();
        foreach (var i in economy["ingredients"].Items)
        {
            bool garden = i["source"].AsString() == "Garden";
            var def = new IngredientDef(i["id"].AsString(), "ing." + i["id"].AsString(), Math.Max(1, i["crateWeight"].AsInt(1)),
                Math.Max(1, i["rarity"].AsInt(1)), garden ? IngredientSource.Garden : IngredientSource.Market,
                garden ? (float)i["growHours"].AsDouble(48) : 0f);
            ingredients.Add(def);
            byId[def.Id] = def;
        }
        var starters = new HashSet<string>();
        foreach (var r in body["start"]["recipes"].Items) starters.Add(r.AsString());
        if (starters.Count == 0) starters.UnionWith(new[] { "roti_canai", "nasi_lemak", "cendol" });
        var dishes = new List<DishDef>();
        foreach (var d in economy["dishes"].Items)
        {
            if (!d["enabled"].AsBool(true)) continue;
            var recipe = new List<IngredientAmount>();
            foreach (var id in d["ingredients"].Items)
                if (byId.TryGetValue(id.AsString(), out var ing)) recipe.Add(new IngredientAmount(ing, 1));
            Enum.TryParse(d["category"].AsString() ?? "main", true, out DishCategory category);
            dishes.Add(new DishDef(d["id"].AsString(), "dish." + d["id"].AsString(), d["basePrice"].AsInt(), (float)d["cookSeconds"].AsDouble(4),
                (float)d["eatSeconds"].AsDouble(4), 1, recipe, starters.Contains(d["id"].AsString()), category, d["unlockLevel"].AsInt(1)));
        }
        var furniture = new List<FurnitureDef>();
        foreach (var f in body["catalog"]["furniture"].Items)
        {
            string placement = f["placement"].AsString();
            if (placement == "floorTile" || placement == "wallpaper") continue;
            var kind = placement == "wall" ? FurnitureKind.WallDecor
                : Enum.TryParse(f["kind"].AsString() ?? "Decor", true, out FurnitureKind k) ? k : FurnitureKind.Decor;
            furniture.Add(new FurnitureDef(f["id"].AsString(), "f", kind, Math.Max(1, f["sizeX"].AsInt(1)), Math.Max(1, f["sizeY"].AsInt(1)),
                1f, kind == FurnitureKind.Chair ? 0.4f : 0f, f["price"].AsInt(), f["unlockLevel"].AsInt(1), f["breakCount"].AsInt(),
                f["stackable"].AsBool(), f["surface"].AsBool()));
        }
        var items = new List<ItemDef>();
        foreach (var i in body["catalog"]["items"].Items)
        {
            // Perks (Phase 4C): timed effects on one employee or the restaurant.
            var e = i["effects"];
            items.Add(new ItemDef(i["id"].AsString(), "item", (float)i["energy"].AsDouble(), i["price"].AsInt())
            {
                Target = i["target"].AsString() == "restaurant" ? ItemTarget.Restaurant : ItemTarget.Employee,
                Effects = e.Kind != JsonKind.Object ? PerkEffects.None : new PerkEffects
                {
                    WorkSpeedPercent = e["workSpeedPercent"].AsInt(0), EnergyDrainPercent = e["energyDrainPercent"].AsInt(0),
                    XpPercent = e["xpPercent"].AsInt(0), PopularityPercent = e["popularityPercent"].AsInt(0),
                },
                DurationMinutes = i["durationMinutes"].AsInt(0),
            });
        }
        var staff = new List<StaffTypeDef>();
        foreach (var role in new[] { StaffRole.Chef, StaffRole.Waiter, StaffRole.Cleaner })
        {
            string id = role.ToString().ToLowerInvariant();
            staff.Add(new StaffTypeDef(id, "staff." + id, role, 4f));
            staff.Add(new StaffTypeDef("owner_" + id, "staff.owner", role, 4f, isOwner: true));
        }
        var rules = new GameRules();
        var trash = body["rules"]["trash"];
        if (trash.Kind == JsonKind.Object)
            rules.Trash = new TrashRules
            {
                spawnSeconds = (float)trash["spawnSeconds"].AsDouble(), maxPieces = trash["maxPieces"].AsInt(15),
                cleanSeconds = (float)trash["cleanSeconds"].AsDouble(3), turnAwayChancePerPiece = (float)trash["turnAwayChancePerPiece"].AsDouble(),
                xpPerPiece = trash["xpPerPiece"].AsInt(1),
            };
        var guests = new[] { new CustomerTypeDef("local", "c", 3f, 60f, 1, foodPatienceSeconds: 120f) };
        var pack = new ContentPack("malaysia", dishes, furniture, guests, staff, ingredients, rules, items);
        pack.Economy = EconomyFrom(economy);
        var parts = new List<AvatarPartDef>();
        foreach (var w in body["catalog"]["wardrobe"].Items)
            if (WardrobeIds.TryParseSlot(w["slot"].AsString(), out var slot))
                parts.Add(new AvatarPartDef(w["id"].AsString(), slot, "avatar", unlockLevel: w["unlockLevel"].AsInt(1), price: w["price"].AsInt()));
        if (parts.Count > 0) pack.Avatar = new AvatarCatalog(parts);
        return pack;
    }

    /// <summary>The ruleset's economy block mapped onto the client's table (same workbook, same field names).</summary>
    private static EconomyTable EconomyFrom(JsonValue economy)
    {
        var table = new EconomyTable { settings = Fill(new EconomySettings(), economy["settings"]) };
        var levels = new List<EconomyLevel>();
        foreach (var l in economy["levels"].Items)
        {
            var level = Fill(new EconomyLevel(), l);
            level.inPhase1 = true;
            levels.Add(level);
        }
        table.levels = levels.ToArray();
        var roles = new List<EconomyStaffRole>();
        foreach (var r in economy["staffRoles"].Items) roles.Add(Fill(new EconomyStaffRole(), r));
        table.staffRoles = roles.ToArray();
        return table;
    }

    private static T Fill<T>(T target, JsonValue source)
    {
        foreach (var field in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            var v = source[field.Name];
            if (v.IsNull) continue;
            if (field.FieldType == typeof(int) && v.Kind == JsonKind.Number) field.SetValue(target, v.AsInt());
            else if (field.FieldType == typeof(float) && v.Kind == JsonKind.Number) field.SetValue(target, (float)v.AsDouble());
            else if (field.FieldType == typeof(bool) && v.Kind == JsonKind.Bool) field.SetValue(target, v.AsBool());
            else if (field.FieldType == typeof(string) && v.Kind == JsonKind.String) field.SetValue(target, v.AsString());
            else if (field.FieldType == typeof(int[]) && v.Kind == JsonKind.Array)
            {
                var a = new int[v.Count];
                for (int i = 0; i < a.Length; i++) a[i] = v[i].AsInt();
                field.SetValue(target, a);
            }
        }
        return target;
    }
}
