using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Economy;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Menu;
using DemoRestaurant.Logic.Progression;
using DemoRestaurant.Logic.Save;
using DemoRestaurant.Logic.Profile;
using DemoRestaurant.Logic.Simulation;

namespace DemoRestaurant.Logic.Session
{
    public enum HireResult
    {
        Hired,
        /// <summary>Every staff slot for the restaurant level is taken.</summary>
        NoSlot,
        /// <summary>This role unlocks at a higher restaurant level.</summary>
        RoleLocked,
        NotEnoughCoins,
    }

    /// <summary>How ingredients were gained (for the gain popup).</summary>
    public enum IngredientGain
    {
        /// <summary>Timed market baskets.</summary>
        Market,
        /// <summary>Harvested from the farm.</summary>
        Garden,
    }

    /// <summary>
    /// One running restaurant: the simulation plus everything around it (guest arrivals, restaurant level, star
    /// rating, coins, ingredients, recipes, menu and staff slots), wired together in plain C#. Numbers come from
    /// the economy table (economy.json). The Unity layer creates this, calls <see cref="Tick"/> every frame and
    /// draws its state.
    /// </summary>
    public sealed partial class RestaurantSession
    {
        private readonly Random _random;
        private readonly List<CustomerTypeDef> _arriving = new List<CustomerTypeDef>();
        private readonly Func<DateTime> _utcNow;

        public ContentPack Content { get; }
        public GameRules Rules => Content.Rules;
        /// <summary>The designers' economy sheet (economy.json): levels, formulas and limits.</summary>
        public EconomyTable Tuning { get; }
        public EconomySettings Settings => Tuning.settings;
        public RestaurantLayout Layout { get; }
        public RestaurantSim Sim { get; }
        public RestaurantMenu Menu { get; }
        public RecipeBook Recipes { get; }
        public Inventory Inventory { get; }
        /// <summary>Consumable items such as energy drinks.</summary>
        public ItemBag Items { get; } = new ItemBag();
        /// <summary>Perks running now (timed staff items, Phase 4C).</summary>
        public PerkBook Perks { get; }
        public IngredientMarket Market { get; }
        /// <summary>The farm behind the restaurant (9 plots, opened by level).</summary>
        public Garden Garden { get; }
        /// <summary>Restaurant level and XP (shown on the profile card).</summary>
        public RestaurantProgress Progress { get; }
        /// <summary>Popularity: how often guests arrive; happy guests raise it, unhappy ones lower it.</summary>
        public RestaurantDemand Demand { get; }
        /// <summary>Display name, avatar, player id and linked accounts (the profile dialog).</summary>
        public PlayerProfile Profile { get; }
        public CustomerArrivals Arrivals { get; }
        public Wallet Wallet { get; }
        public EarningsTracker Earnings { get; }
        public IEconomyServer Economy { get; }
        /// <summary>Everything done while offline, to be validated by the backend on the next sync.</summary>
        public OfflineLedger Ledger { get; } = new OfflineLedger();
        public long TotalCustomersServed { get; private set; }

        /// <summary>Game seconds since the session started.</summary>
        public float Time { get; private set; }
        /// <summary>When false, customers only arrive through <see cref="RestaurantSim.SpawnCustomer"/> (stress test).</summary>
        public bool ArrivalsEnabled { get; set; } = true;

        /// <summary>A customer paid: coins (predicted) and XP gained.</summary>
        public event Action<Customer, int, int> CustomerServed;
        /// <summary>A customer left angry: demand lost (negative).</summary>
        public event Action<Customer, double> CustomerAngered;
        public event Action<IReadOnlyList<IngredientDef>, IngredientGain> IngredientsGained;
        public event Action<DishDef> RecipeDiscovered;
        /// <summary>New restaurant level and the coins it paid.</summary>
        public event Action<int, int> LevelUp;

        /// <param name="utcNow">Wall clock for the ledger and idle progress; injectable for tests.</param>
        public RestaurantSession(ContentPack content, RestaurantLayout layout, int seed,
            long startingCoins = 0, IEconomyServer economy = null, Func<DateTime> utcNow = null,
            string playerName = null)
        {
            Content = content ?? throw new ArgumentNullException(nameof(content));
            Tuning = content.Economy ?? throw new ContentException(
                $"Content pack '{content.Id}' has no economy.json (DemoRestaurant > Content > Import Economy Sheet).");
            Layout = layout ?? throw new ArgumentNullException(nameof(layout));
            _random = new Random(seed);
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
            Profile = new PlayerProfile(null, string.IsNullOrWhiteSpace(playerName) ? "Player" : playerName,
                AvatarLook.Default(content.Avatar));

            Inventory = new Inventory();
            Inventory.AddAll(Rules.StartingIngredients);
            foreach (var pair in Rules.StartingItems)
                Items.Add(pair.Key, pair.Value);
            Progress = new RestaurantProgress(Tuning);
            Demand = new RestaurantDemand(Rules.Demand) { CapBonus = () => EffectTotal(e => e.DemandCapBonus) };
            Perks = new PerkBook(() => Rules.Perks);
            Progress.LevelUp += OnLevelUp;

            Recipes = new RecipeBook(content.Dishes);
            if (Recipes.Known.Count == 0)
                throw new ContentException($"Content pack '{content.Id}' needs at least one dish known at the start.");
            Menu = new RestaurantMenu(Recipes.Known, category => Tuning.MenuSlots(Progress.Level, category));
            Recipes.Discovered += OnRecipeDiscovered;
            Recipes.Upgraded += (dish, level) =>
            {
                int gained = Progress.XpForRecipeLevel(level);
                _effectKey = RecordAction("recipe_learn", Body().Set("dishId", dish.Id), xp: gained);
                Progress.Add(gained);
                _effectKey = null;
                Ledger.Record(LedgerKind.RecipeUpgraded, level, dish.Id, _utcNow());
            };

            Arrivals = new CustomerArrivals(content.CustomerTypes, () => Progress.Level, () => Demand.GuestsPerMinute, _random,
                () => Rules.Demand.DailyGuestCap, () => GuestDay(_utcNow()));
            if (content.Ingredients.Count > 0)
                Market = new IngredientMarket(content.Ingredients, Rules.MarketBaskets, Rules.MarketRestockSeconds, _random);
            Garden = new Garden(content.Ingredients, Settings, Rules.GardenHarvest, _random, Rules.GardenWatering);

            Wallet = new Wallet(startingCoins);
            Economy = economy ?? new LocalEconomyServer(content, startingCoins: startingCoins,
                ownedWidth: layout.RoomWidth, ownedHeight: layout.RoomHeight, wallet: Wallet);
            Economy.SaleSettled += Wallet.Settle;
            (Economy as LocalEconomyServer)?.SetRecipeLevelSource(id => Recipes.LevelOf(Content.GetDish(id)));
            (Economy as LocalEconomyServer)?.SetLevelSource(() => Progress.Level);
            (Economy as LocalEconomyServer)?.SetTipSource(TipFor);
            RestoreTerraceFromLayout();
            Earnings = new EarningsTracker();

            Sim = new RestaurantSim(layout, Menu, seed, new SimSettings
            {
                StaffEnergy = Rules.StaffEnergy,
                FoodPatience = Rules.FoodPatience,
                Ambience = () => Ambience,
                PatienceBonusSeconds = () => EffectTotal(e => e.PatienceSeconds),
                CleanSeconds = Math.Max(0f, Rules.CleanSeconds),
                PlayerCleanSeconds = Math.Max(.1f, Rules.PlayerCleanSeconds),
                StreetWalk = true,
                NoTableWaitSeconds = Math.Max(0f, Rules.NoTableWaitSeconds),
                ToiletSeconds = Math.Max(0f, Rules.Toilets.toiletSeconds),
                SinkSeconds = Math.Max(0f, Rules.Toilets.sinkSeconds),
                FixSeconds = Math.Max(0f, Rules.Toilets.fixSeconds),
                EntertainmentSeconds = Math.Max(0f, Rules.Entertainment.playSeconds),
                Trash = Rules.Trash,
            });
            InitServerLog();
            Sim.CustomerPaid += OnCustomerPaid;
            Sim.EntertainmentCompleted += OnEntertainmentCompleted;
            Sim.CustomerAngry += OnCustomerAngry;
            // Every guest who leaves unhappy costs demand: no room even to queue, gave up queuing (angry is handled
            // in OnCustomerAngry).
            Sim.CustomerTurnedAway += _ => Unhappy("turned_away");
            Sim.CustomerNoToilet += _ => Unhappy("no_toilet"); // as in the reference game: no toilet, unhappy guest
            UpdateToiletChance();
            Sim.CustomerGaveUpWaiting += _ => Unhappy("gave_up");
            Sim.CustomerNoTable += _ => Unhappy("no_table");
            Sim.TrashCleaned += OnTrashCleaned;
            InitializeWardrobe();
        }

        /// <summary>Day number for the daily guest limit: days since 0001-01-01 in Malaysia time (UTC+8).</summary>
        public static long GuestDay(DateTime utc) => utc.AddHours(8).Ticks / TimeSpan.TicksPerDay;

        public void Tick(float dt)
        {
            if (dt <= 0)
                return;
            Time += dt;

            if (ArrivalsEnabled)
            {
                Arrivals.Tick(dt, _arriving);
                foreach (var type in _arriving)
                    Sim.SpawnCustomer(type);
            }

            ApplyPerks();
            Sim.Tick(dt);
            if (ArrivalsEnabled && !IsOpen)
                Demand.OnClosed(TimeSpan.FromSeconds(dt));
            TickEconomy(dt);
            Market?.Tick(dt);
        }

        public void TickEconomy(float dt) => (Economy as LocalEconomyServer)?.Tick(dt);

        // ---------------------------------------------------------------- staff

        /// <summary>Staff slots shared by every role at the current level (the owner doesn't take one).</summary>
        public int StaffSlots => Progress.Row.staffSlots;

        /// <summary>Hired staff of every role (not the owner).</summary>
        public int HiredCount
        {
            get
            {
                int n = 0;
                foreach (var s in Sim.Staff)
                    if (!s.IsOwner) n++;
                return n;
            }
        }

        /// <summary>Cost of the next hire: base + per member x current team size.</summary>
        public int NextHireCost => EconomyFormulas.HireCost(Settings, HiredCount);

        /// <summary>Restaurant level a role can first be hired at.</summary>
        public int RoleUnlockLevel(StaffRole role) => Tuning.RoleUnlockLevel(role.ToString());

        /// <summary>Hired staff of a role. The owner (the player's avatar) doesn't count.</summary>
        public int StaffCount(StaffRole role)
        {
            int n = 0;
            foreach (var s in Sim.Staff)
                if (s.Role == role && !s.IsOwner) n++;
            return n;
        }

        /// <summary>The player's own avatar working in the restaurant, if placed.</summary>
        public Staff Owner
        {
            get
            {
                foreach (var s in Sim.Staff)
                    if (s.IsOwner) return s;
                return null;
            }
        }

        /// <summary>
        /// Puts the player's avatar to work as <paramref name="role"/> (free, outside the caps), or moves them to that
        /// job if already working. False if the pack has no owner type for the role.
        /// </summary>
        public bool AssignOwner(StaffRole role, GridPos home)
        {
            var type = Content.OwnerType(role);
            if (type == null)
                return false;
            var owner = Owner;
            if (owner == null)
                Sim.Hire(type, home);
            else if (owner.Type == type)
                return true;
            else
                Sim.ChangeType(owner, type, home);
            // Placing the owner on a new game is local setup (the server creates its own); a job change is an action.
            if (owner != null) RecordAction("owner_role", Body().Set("role", RoleId(role)));
            Ledger.Record(LedgerKind.OwnerJobChanged, 0, type.Id, _utcNow());
            return true;
        }

        public bool HasFreeSlot => HiredCount < StaffSlots;

        /// <summary>Why a staff type can or can't be hired right now.</summary>
        public HireResult CheckHire(StaffTypeDef type, bool free = false)
        {
            if (type.IsOwner || !HasFreeSlot) return HireResult.NoSlot; // the owner is placed, not hired
            if (Progress.Level < RoleUnlockLevel(type.Role)) return HireResult.RoleLocked;
            if (!free && Wallet.DisplayCoins < NextHireCost) return HireResult.NotEnoughCoins;
            return HireResult.Hired;
        }

        /// <summary>Hires one staff member: a free slot, role unlocked, hire cost paid (unless free).</summary>
        public HireResult Hire(StaffTypeDef type, GridPos home, out Staff staff, bool free = false)
        {
            staff = null;
            if (type == null) throw new ArgumentNullException(nameof(type));
            var check = CheckHire(type, free);
            if (check != HireResult.Hired)
                return check;
            int cost = free ? 0 : NextHireCost;
            if (cost > 0 && !Wallet.TrySpend(cost))
                return HireResult.NotEnoughCoins;
            Ledger.Record(LedgerKind.StaffHired, cost, type.Id, _utcNow());
            staff = Sim.Hire(type, home);
            // Free hires are the local starter team (the server bootstrap creates its own) and stress-test staff.
            if (!free)
            {
                staff.ServerId = Guid.NewGuid().ToString();
                RecordAction("hire", Body().Set("role", RoleId(type.Role)).Set("staffId", staff.ServerId), coins: -cost);
            }
            return HireResult.Hired;
        }

        /// <summary>Hire for a cost; null if not allowed (see <see cref="CheckHire"/>).</summary>
        public Staff TryHire(StaffTypeDef type, GridPos home) =>
            type != null && Hire(type, home, out var staff) == HireResult.Hired ? staff : null;

        // ---------------------------------------------------------------- profile

        public TimeSpan RenameCooldown => TimeSpan.FromHours(Math.Max(0f, Rules.NameChangeCooldownHours));

        public NameCheck Rename(string name)
        {
            var result = Profile.TryRename(name, _utcNow(), RenameCooldown);
            if (result == NameCheck.Ok)
            {
                RecordAction("rename", Body().Set("restaurantId", ServerRestaurantId ?? "").Set("name", Profile.DisplayName));
                Ledger.Record(LedgerKind.Renamed, 0, Profile.DisplayName, _utcNow());
            }
            return result;
        }

        /// <summary>Saves an edited avatar. Locked or unknown parts are replaced with valid ones first.</summary>
        public void SaveAvatar(AvatarLook look)
        {
            if (look == null) return;
            var clean = look.Clone();
            clean.Sanitize(Content.Avatar);
            foreach (var slot in AvatarCatalog.AllSlots)
            {
                var part = Content.Avatar.Find(slot, clean[slot]);
                if (part != null && (part.UnlockLevel > Progress.Level || !OwnsAvatarPart(part)))
                    clean.TrySet(Content.Avatar, slot, Profile.Avatar[slot] ?? Content.Avatar.Default(slot)?.Id, int.MaxValue);
            }
            Profile.HasCustomAvatar = true;
            Profile.SetAvatar(clean);
            ServerLog.RecordDeferred("wardrobe_look");
            Ledger.Record(LedgerKind.AvatarChanged, 0, clean.Key, _utcNow());
        }

        public void SendToRest(Staff staff)
        {
            if (staff == null || staff.IsResting || staff.RestRequested) return;
            Sim.SendToRest(staff);
            RecordDuty(staff, false);
        }

        public void ReturnToWork(Staff staff)
        {
            if (staff == null || (!staff.IsResting && !staff.RestRequested)) return;
            Sim.ReturnToWork(staff);
            RecordDuty(staff, true);
        }

        /// <summary>True when the item is on sale now: published as available, unlocked at this level, in its sale window.</summary>
        public bool CanBuyItem(ItemDef item) => item != null && item.Price > 0 && item.Available && Progress.Level >= item.UnlockLevel
            && item.IsOnSale(_utcNow());

        /// <summary>Buys one item with coins.</summary>
        public bool BuyItem(ItemDef item)
        {
            if (!CanBuyItem(item) || !Wallet.TrySpend(item.Price))
                return false;
            Items.Add(item);
            RecordAction("item_buy", Body().Set("itemId", item.Id).Set("count", 1), coins: -item.Price);
            Ledger.Record(LedgerKind.ItemBought, item.Price, item.Id, _utcNow());
            return true;
        }

        /// <summary>
        /// Uses one item on a staff member: restores its energy and starts its perk, if it has one. False if none
        /// left, the item is for the restaurant, a plain energy item meets full energy, or the perk can't start
        /// (see <see cref="CheckPerk"/>).
        /// </summary>
        public bool UseItem(ItemDef item, Staff staff)
        {
            if (item == null || staff == null || item.Target != ItemTarget.Employee) return false;
            var now = _utcNow();
            if (!item.IsPerk && staff.Energy >= StaffEnergyRules.Max) return false;
            if (Perks.Check(item, staff, now) != PerkCheck.Ok || !Items.TryUse(item)) return false;
            if (item.EnergyRestore > 0) Sim.RestoreEnergy(staff, item.EnergyRestore);
            if (item.IsPerk) Perks.Start(item, staff, now);
            ApplyPerks();
            if (staff.ServerId != null)
                RecordAction("item_use", Body().Set("itemId", item.Id).Set("staffId", staff.ServerId));
            Ledger.Record(LedgerKind.ItemUsed, 1, item.Id, now);
            return true;
        }

        /// <summary>Uses a restaurant perk (target restaurant): it applies to every employee and the restaurant.</summary>
        public bool UseRestaurantItem(ItemDef item)
        {
            if (item == null || item.Target != ItemTarget.Restaurant || !item.IsPerk) return false;
            var now = _utcNow();
            if (Perks.Check(item, null, now) != PerkCheck.Ok || !Items.TryUse(item)) return false;
            Perks.Start(item, null, now);
            ApplyPerks();
            RecordAction("item_use", Body().Set("itemId", item.Id));
            Ledger.Record(LedgerKind.ItemUsed, 1, item.Id, now);
            return true;
        }

        /// <summary>Why the item's perk could or couldn't start now on this employee (null = the restaurant).</summary>
        public PerkCheck CheckPerk(ItemDef item, Staff staff) => Perks.Check(item, staff, _utcNow());

        /// <summary>Drops ended perks and hands each employee their work speed and drain factors.</summary>
        private void ApplyPerks()
        {
            var now = _utcNow();
            Perks.Prune(now);
            foreach (var s in Sim.Staff)
            {
                s.PerkSpeed = 1f + Perks.StaffTotal(s, now, e => e.WorkSpeedPercent) / 100f;
                s.PerkDrain = Math.Max(0f, 1f - Perks.StaffTotal(s, now, e => e.EnergyDrainPercent) / 100f);
            }
        }

        /// <summary>
        /// One tap refill: uses the item on whoever is most tired, again and again, until everyone is full or the items
        /// run out. Returns how many were used. Items without instant energy (perks only) are not used this way.
        /// </summary>
        public int RefillAll(ItemDef item)
        {
            if (item == null || item.EnergyRestore <= 0 || item.Target != ItemTarget.Employee) return 0;
            int used = 0;
            while (true)
            {
                Staff tiredest = null;
                foreach (var s in Sim.Staff)
                    if (s.Energy < StaffEnergyRules.Max && (tiredest == null || s.Energy < tiredest.Energy)) tiredest = s;
                if (tiredest == null || !UseItem(item, tiredest)) return used;
                used++;
            }
        }

        /// <summary>Dismisses hired staff. The owner can change jobs or rest, but never leaves.</summary>
        public void Dismiss(Staff staff)
        {
            if (staff == null || staff.IsOwner)
                return;
            Ledger.Record(LedgerKind.StaffDismissed, 1, staff.Type.Id, _utcNow());
            if (staff.ServerId != null) RecordAction("dismiss", Body().Set("staffId", staff.ServerId));
            Perks.RemoveStaff(staff);
            Sim.Dismiss(staff);
        }

        // ---------------------------------------------------------------- ingredients & recipes

        public IReadOnlyList<IngredientDef> CollectMarket()
        {
            if (Market == null)
                return Array.Empty<IngredientDef>();
            var got = Market.Collect(Inventory);
            if (got.Count > 0) RecordAction("market_collect_all", Body());
            foreach (var i in got)
                Ledger.Record(LedgerKind.MarketCollect, 1, i.Id, _utcNow());
            if (got.Count > 0)
                IngredientsGained?.Invoke(got, IngredientGain.Market);
            return got;
        }

        public enum GardenResult { Done, Locked, NotEmpty, NotEnoughCoins, NothingToWater, NotReady }

        /// <summary>Buys a seed (economy sheet price) and plants it in an open, empty plot.</summary>
        public GardenResult BuySeed(int plot)
        {
            var now = _utcNow();
            var state = Garden.StateOf(plot, Progress.Level, now);
            if (state == PlotState.Locked) return GardenResult.Locked;
            if (state != PlotState.Empty) return GardenResult.NotEmpty;
            if (Wallet.DisplayCoins < Garden.SeedCost || !Wallet.TrySpend(Garden.SeedCost)) return GardenResult.NotEnoughCoins;
            Garden.Plant(plot, Progress.Level, now);
            RecordAction("garden", Body().Set("plot", plot).Set("action", "plant"), coins: -Garden.SeedCost);
            Ledger.Record(LedgerKind.GardenSeed, Garden.SeedCost, plot.ToString(System.Globalization.CultureInfo.InvariantCulture), now);
            return GardenResult.Done;
        }

        public GardenResult WaterPlot(int plot)
        {
            var now = _utcNow();
            if (!Garden.Water(plot, Progress.Level, now)) return GardenResult.NothingToWater;
            RecordAction("garden", Body().Set("plot", plot).Set("action", "water"));
            Ledger.Record(LedgerKind.GardenWater, 1, plot.ToString(System.Globalization.CultureInfo.InvariantCulture), now);
            return GardenResult.Done;
        }

        /// <summary>Picks a ready crop into the inventory.</summary>
        public GardenResult HarvestPlot(int plot)
        {
            var now = _utcNow();
            var crop = Garden.Harvest(plot, Progress.Level, now);
            if (crop == null) return GardenResult.NotReady;
            RecordAction("garden", Body().Set("plot", plot).Set("action", "harvest"));
            Inventory.Add(crop, Garden.HarvestAmount);
            Ledger.Record(LedgerKind.GardenHarvest, Garden.HarvestAmount, crop.Id, now);
            var gained = new List<IngredientDef>();
            for (int i = 0; i < Garden.HarvestAmount; i++) gained.Add(crop);
            IngredientsGained?.Invoke(gained, IngredientGain.Garden);
            return GardenResult.Done;
        }


        /// <summary>Internal testing only (debug overlay): adds <paramref name="count"/> of every ingredient.</summary>
        public void DebugAddIngredients(int count)
        {
            foreach (var ingredient in Content.Ingredients) Inventory.Add(ingredient, count);
        }

        /// <summary>
        /// Internal testing only (debug overlay): learns every recipe at once, whatever the level, and makes each one
        /// available on the menu. No XP, no ledger entry. Returns how many were new.
        /// </summary>
        public int DebugLearnAllRecipes()
        {
            int learned = 0;
            foreach (var dish in Recipes.AllDishes)
            {
                if (Recipes.IsKnown(dish)) continue;
                Recipes.Restore(new[] { dish });
                Menu.MakeAvailable(dish);
                learned++;
            }
            return learned;
        }

        public RecipeTrainResult TrainRecipe(DishDef dish) => Recipes.Train(dish, Inventory, Progress.Level,
            Settings.maxDishLevel, Settings.ingredientSetsPerUpgrade);

        public int PriceForDish(DishDef dish) => EconomyFormulas.DishPrice(Settings, dish.Price, Recipes.LevelOf(dish));

        // ---------------------------------------------------------------- events

        private void OnRecipeDiscovered(DishDef dish)
        {
            Menu.MakeAvailable(dish);
            int gained = Progress.XpForRecipeLevel(1);
            _effectKey = RecordAction("recipe_learn", Body().Set("dishId", dish.Id), xp: gained);
            Progress.Add(gained);
            _effectKey = null;
            Ledger.Record(LedgerKind.RecipeDiscovered, 1, dish.Id, _utcNow());
            RecipeDiscovered?.Invoke(dish);
        }

        private void OnLevelUp(int level, int coins)
        {
            if (coins > 0)
            {
                Wallet.CreditOffline(coins);
                ServerLog.AddEffect(_effectKey, coins);
                Ledger.Record(LedgerKind.LevelReward, coins, level.ToString(System.Globalization.CultureInfo.InvariantCulture), _utcNow());
            }
            Menu.FillFreeSlots(); // more menu slots at some levels
            GrowRoom();
            UpdateToiletChance();
            LevelUp?.Invoke(level, coins);
        }

        /// <summary>
        /// Grows the room to the size its level allows (<see cref="GameRules.RoomGrowth"/>): free, never smaller. Called
        /// on level-up and by the game after loading a save. Returns true when the room got bigger.
        /// </summary>
        /// <summary>From the rules' level on, some guests use the toilet after their meal.</summary>
        private void UpdateToiletChance() =>
            Sim.ToiletChance = Progress.Level >= Rules.Toilets.fromLevel ? Rules.Toilets.chance : 0f;

        public bool GrowRoom()
        {
            var growth = Rules.RoomGrowth;
            if (growth == null) return false;
            var (width, height) = growth.Grow(Layout.RoomWidth, Layout.RoomHeight, Progress.Level);
            if (width == Layout.RoomWidth && height == Layout.RoomHeight) return false;
            Layout.Resize(width, height);
            return true;
        }

        public event Action<PlacedFurniture, int> EntertainmentEarned;

        private void OnEntertainmentCompleted(Customer customer, PlacedFurniture item)
        {
            int coins = Math.Max(0, Rules.Entertainment.coinsPerUse);
            if (Economy is IEntertainmentEconomyServer authority)
            {
                long receipt = Wallet.Predict(coins);
                authority.SubmitEntertainment(receipt, item.Def.Id);
            }
            else Wallet.CreditOffline(coins); // Not yet validated online: stays in the unsynced ledger.
            string instance = ServerInstanceOf?.Invoke(item);
            if (instance != null) RecordAction("functional_use", Body().Set("instanceId", instance), coins: coins);
            Ledger.Record(LedgerKind.EntertainmentIncome, coins, item.Def.Id, _utcNow());
            Earnings.Record(Time, coins);
            EntertainmentEarned?.Invoke(item, coins);
        }

        /// <summary>Ambience of the saved layout: every placed floor and wall piece's points, each copy counting.</summary>
        public int Ambience
        {
            get
            {
                int total = 0;
                foreach (var p in Layout.Furniture) total += p.Def.Ambience;
                foreach (var w in Layout.WallItems) total += w.Def.Ambience;
                return total;
            }
        }

        /// <summary>Every placed floor and wall piece of the saved layout (one entry per copy).</summary>
        public IEnumerable<FurnitureDef> PlacedPieces()
        {
            foreach (var p in Layout.Furniture) yield return p.Def;
            foreach (var w in Layout.WallItems) yield return w.Def;
        }

        /// <summary>A restaurant-wide furniture effect total (Phase 3), counted as the server counts it.</summary>
        public int EffectTotal(Func<FurnitureEffects, int> field) => FurnitureEffects.Total(PlacedPieces(), field);

        /// <summary>The tip a paying guest adds to a bill of <paramref name="price"/> (0 while tips are off).</summary>
        public int TipFor(int price) => Rules.Tips.TipFor(price, Ambience);

        private void OnCustomerPaid(Customer c, int coins)
        {
            // The bill plus the ambience tip, as the server will credit it.
            coins = PriceForDish(c.Order);
            coins += TipFor(coins);
            long receipt = Wallet.Predict(coins);
            Economy.SubmitSale(new SaleReport(receipt, c.Order.Id, coins));
            Earnings.Record(Time, coins);
            TotalCustomersServed++;
            Ledger.Record(LedgerKind.Sale, coins, c.Order.Id, _utcNow());
            double demandBefore = Demand.Value;
            // Restaurant perks raise the popularity and XP of this sale, rounded as the server rounds.
            var now = _utcNow();
            Demand.OnHappyGuest(1 + Perks.RestaurantTotal(now, e => e.PopularityPercent) / 100.0);
            int xp = (int)Math.Round(Progress.XpForServe(Recipes.LevelOf(c.Order))
                * (1 + Perks.RestaurantTotal(now, e => e.XpPercent) / 100.0), MidpointRounding.AwayFromZero);
            _effectKey = RecordAction("sale", Body().Set("dishId", c.Order.Id).Set("recipeLevel", Recipes.LevelOf(c.Order))
                .Set("happy", true), coins, xp, (float)(Demand.Value - demandBefore));
            Progress.Add(xp);
            _effectKey = null;
            CustomerServed?.Invoke(c, coins, xp);
        }

        private void OnCustomerAngry(Customer c)
        {
            double before = Demand.Value;
            Demand.OnUnhappyGuest();
            RecordUnhappy("angry", before);
            CustomerAngered?.Invoke(c, Demand.Value - before);
        }

        /// <summary>Someone can cook and someone can serve (resting staff don't count). The owner counts.</summary>
        public bool IsOpen
        {
            get
            {
                bool chef = false, waiter = false;
                foreach (var s in Sim.Staff)
                {
                    if (s.IsResting) continue;
                    if (s.Role == StaffRole.Chef) chef = true;
                    else if (s.Role == StaffRole.Waiter) waiter = true;
                }
                return chef && waiter;
            }
        }
    }
}
