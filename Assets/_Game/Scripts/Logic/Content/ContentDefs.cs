using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Layout;

namespace DemoRestaurant.Logic.Content
{
    public sealed class DishDef
    {
        public string Id { get; }
        /// <summary>Translation key, e.g. "dish.nasi_lemak".</summary>
        public string NameKey { get; internal set; }
        public int Price { get; internal set; }
        /// <summary>Preparation time: how long a chef (at CookSpeed 1) works at a stove to make this dish.</summary>
        public float CookSeconds { get; internal set; }
        public float EatSeconds { get; internal set; }
        /// <summary>Quality, 1-5. Customers expect a minimum; meeting it earns full popularity.</summary>
        public int Stars { get; }
        /// <summary>Ingredients that discover this dish in the recipe lab. Empty = cannot be discovered.</summary>
        public IReadOnlyList<IngredientAmount> Recipe { get; internal set; }
        /// <summary>Known from the start, without discovering it.</summary>
        public bool IsStarter { get; }
        /// <summary>Menu category (starter, main, dessert, drink); menu slots are per category.</summary>
        public DishCategory Category { get; internal set; }
        /// <summary>Restaurant level needed before the recipe can be discovered.</summary>
        public int UnlockLevel { get; internal set; }
        public bool Enabled { get; internal set; } = true;
        public string AssetKey { get; internal set; }

        internal void ApplyPublished(string nameKey, int price, float cookSeconds, float eatSeconds,
            int unlockLevel, bool enabled, IReadOnlyList<IngredientAmount> ingredients, DishCategory category)
        {
            NameKey = nameKey; Price = Math.Max(1, price); CookSeconds = Math.Max(1, cookSeconds);
            EatSeconds = Math.Max(1, eatSeconds); UnlockLevel = Math.Max(1, unlockLevel);
            Enabled = enabled; Recipe = ingredients; Category = category;
        }

        public DishDef(string id, string nameKey, int price, float cookSeconds, float eatSeconds,
            int stars = 1, IReadOnlyList<IngredientAmount> recipe = null, bool isStarter = false,
            DishCategory category = DishCategory.Main, int unlockLevel = 1)
        {
            Category = category;
            UnlockLevel = Math.Max(1, unlockLevel);
            Id = RequireId(id);
            NameKey = nameKey ?? throw new ArgumentNullException(nameof(nameKey));
            if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));
            if (cookSeconds < 0) throw new ArgumentOutOfRangeException(nameof(cookSeconds));
            if (eatSeconds < 0) throw new ArgumentOutOfRangeException(nameof(eatSeconds));
            if (stars < 1 || stars > 5) throw new ArgumentOutOfRangeException(nameof(stars), "Stars must be 1-5.");
            Price = price;
            CookSeconds = cookSeconds;
            EatSeconds = eatSeconds;
            Stars = stars;
            Recipe = recipe ?? Array.Empty<IngredientAmount>();
            IsStarter = isStarter;
        }

        internal static string RequireId(string id)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Content id must not be empty.", nameof(id));
            return id;
        }
    }

    public enum FurnitureKind
    {
        Decor,
        Table,
        Chair,
        /// <summary>A cooking station: one chef cooks one dish at a time here.</summary>
        Stove,
        Counter,
        Plant,
        /// <summary>The preparation area: each footprint cell holds one finished plate for a waiter to collect.</summary>
        ServingCounter,
        /// <summary>Hangs on a back wall (windows, pictures, the menu holder); takes no floor cells.</summary>
        WallDecor,
        /// <summary>Drinks come from here: a waiter pours them, no chef (soda dispenser, drinks fridge).</summary>
        DrinkMachine,
        /// <summary>Guests may use it after their meal (from the rules' level); it breaks after BreakCount uses.</summary>
        Toilet,
        /// <summary>Guests wash their hands here after the toilet.</summary>
        Sink,
        /// <summary>A paid activity for guests who cannot find a dining seat.</summary>
        Entertainment,
    }

    public enum StaffRole
    {
        Chef,
        Waiter,
        /// <summary>Clears the dishes customers leave behind, so the seat can be used again.</summary>
        Cleaner,
    }

    public sealed class StaffTypeDef
    {
        public string Id { get; }
        public string NameKey { get; }
        public StaffRole Role { get; }
        /// <summary>Cells per second.</summary>
        public float WalkSpeed { get; }
        /// <summary>
        /// Work speed: multiplier on dish preparation time for chefs and on cleaning time for cleaners (and for
        /// waiters clearing tables). 2 = twice as fast.
        /// </summary>
        public float CookSpeed { get; }
        /// <summary>The player's own avatar working this role. Never hired, never takes a staff slot.</summary>
        public bool IsOwner { get; }

        /// <summary>
        /// One staff type per role (the economy sheet has no tiers). Hire cost, staff slots and the level a role
        /// unlocks at come from the economy table, not from here.
        /// </summary>
        public StaffTypeDef(string id, string nameKey, StaffRole role, float walkSpeed, float cookSpeed = 1f,
            bool isOwner = false)
        {
            Id = DishDef.RequireId(id);
            NameKey = nameKey ?? throw new ArgumentNullException(nameof(nameKey));
            if (walkSpeed <= 0) throw new ArgumentOutOfRangeException(nameof(walkSpeed));
            if (cookSpeed <= 0) throw new ArgumentOutOfRangeException(nameof(cookSpeed));
            Role = role;
            WalkSpeed = walkSpeed;
            CookSpeed = cookSpeed;
            IsOwner = isOwner;
        }
    }

    /// <summary>A consumable that restores staff energy (e.g. a strong kopi). Bought with coins.</summary>
    public sealed class ItemDef
    {
        public string Id { get; }
        public string NameKey { get; }
        /// <summary>Energy points restored when used on a staff member (energy is 0-100). Published values replace it.</summary>
        public float EnergyRestore { get; internal set; }
        /// <summary>Coins to buy one; 0 = can't be bought.</summary>
        public int Price { get; internal set; }
        /// <summary>False when the BO stopped selling it; owned ones can still be used.</summary>
        public bool Available { get; internal set; } = true;
        /// <summary>Restaurant level from which it is sold.</summary>
        public int UnlockLevel { get; internal set; } = 1;
        /// <summary>For an item added in the BO: the shipped item whose icon it uses (null = its own).</summary>
        public string AssetKey { get; internal set; }
        /// <summary>Used on one employee or on the whole restaurant (restaurant items restore no energy).</summary>
        public ItemTarget Target { get; internal set; } = ItemTarget.Employee;
        /// <summary>Timed effects (a perk, as the reference game's snacks); <see cref="PerkEffects.None"/> for none.</summary>
        public PerkEffects Effects { get; internal set; } = PerkEffects.None;
        public int DurationMinutes { get; internal set; }
        /// <summary>Optional sale window (events); owned items stay usable outside it.</summary>
        public DateTime? SaleStartUtc { get; internal set; }
        public DateTime? SaleEndUtc { get; internal set; }

        public bool IsPerk => Effects.IsTimed && DurationMinutes > 0;
        public bool IsOnSale(DateTime utc) => (!SaleStartUtc.HasValue || utc >= SaleStartUtc.Value) && (!SaleEndUtc.HasValue || utc < SaleEndUtc.Value);

        public ItemDef(string id, string nameKey, float energyRestore, int price)
        {
            Id = DishDef.RequireId(id);
            NameKey = nameKey ?? throw new ArgumentNullException(nameof(nameKey));
            if (energyRestore < 0) throw new ArgumentOutOfRangeException(nameof(energyRestore));
            if (price < 0) throw new ArgumentOutOfRangeException(nameof(price));
            EnergyRestore = energyRestore;
            Price = price;
        }
    }

    public enum ItemTarget
    {
        Employee,
        Restaurant,
    }

    /// <summary>A perk's timed effects in whole percent (BO → Catalog → Staff items).</summary>
    public sealed class PerkEffects
    {
        public static readonly PerkEffects None = new PerkEffects();

        /// <summary>Chef, waiter and cleaner speed + %.</summary>
        public int WorkSpeedPercent;
        /// <summary>Energy drain − % (100 = none while it lasts).</summary>
        public int EnergyDrainPercent;
        /// <summary>Restaurant: XP from serving + %.</summary>
        public int XpPercent;
        /// <summary>Restaurant: popularity gained from happy guests + %.</summary>
        public int PopularityPercent;

        public bool IsTimed => WorkSpeedPercent > 0 || EnergyDrainPercent > 0 || XpPercent > 0 || PopularityPercent > 0;
    }

    /// <summary>BO → Game Rules → Perks: timed effects on or off, and how many can be active at once.</summary>
    public sealed class PerkRules
    {
        public bool Enabled = true;
        public int SlotsPerEmployee = 3;
        public int SlotsRestaurant = 3;
    }

    /// <summary>One energy band: at or above <see cref="MinEnergy"/>, staff work at <see cref="SpeedMultiplier"/>.</summary>
    public readonly struct EnergyBand
    {
        public readonly float MinEnergy;
        public readonly float SpeedMultiplier;

        public EnergyBand(float minEnergy, float speedMultiplier)
        {
            MinEnergy = minEnergy;
            SpeedMultiplier = speedMultiplier;
        }
    }

    /// <summary>
    /// Staff stamina: energy (0-100) drains while on duty (working or waiting for work, and while the game is
    /// closed); speed drops in bands as it falls. Resting off duty recovers it; items restore it instantly.
    /// </summary>
    public sealed class StaffEnergyRules
    {
        public const float Max = 100f;

        private EnergyBand[] _bands = Default;

        /// <summary>Default bands: 80+ full speed, 60+ x0.8, 40+ x0.5, 20+ x0.3, below x0.05.</summary>
        public static readonly EnergyBand[] Default =
        {
            new EnergyBand(80f, 1f), new EnergyBand(60f, 0.8f), new EnergyBand(40f, 0.5f),
            new EnergyBand(20f, 0.3f), new EnergyBand(0f, 0.05f),
        };

        public float DrainPerMinute = 2f;
        public float RecoverPerMinute = 12f;
        /// <summary>Resting staff go back to work by themselves at this energy.</summary>
        public float AutoReturnAt = Max;

        /// <summary>Bands, highest first. Energy below every band uses the last band's multiplier.</summary>
        public IReadOnlyList<EnergyBand> Bands
        {
            get => _bands;
            set
            {
                if (value == null || value.Count == 0)
                    throw new ContentException("Staff energy needs at least one band.");
                var sorted = new List<EnergyBand>(value);
                sorted.Sort((a, b) => b.MinEnergy.CompareTo(a.MinEnergy));
                _bands = sorted.ToArray();
            }
        }

        public float SpeedMultiplier(float energy)
        {
            foreach (var band in _bands)
                if (energy >= band.MinEnergy)
                    return band.SpeedMultiplier;
            return _bands[_bands.Length - 1].SpeedMultiplier;
        }
    }

    public sealed class FurnitureDef
    {
        public string Id { get; }
        public string NameKey { get; internal set; }
        public FurnitureKind Kind { get; }
        /// <summary>Footprint in cells when not rotated.</summary>
        public int SizeX { get; }
        public int SizeY { get; }
        /// <summary>Visual height in cell units. Used for depth sorting and placeholder art.</summary>
        public float Height { get; }
        /// <summary>Height a seated customer sits at. Only meaningful for chairs.</summary>
        public float SeatHeight { get; }
        /// <summary>Coins to buy one copy in the build shop (paid when the renovation is saved).</summary>
        public int Price { get; internal set; }
        /// <summary>Player level at which the shop sells it.</summary>
        public int UnlockLevel { get; internal set; }
        /// <summary>Published sale eligibility. Owned copies remain usable while withdrawn.</summary>
        public bool Available { get; internal set; } = true;
        /// <summary>Existing local furniture whose artwork this catalog variant uses.</summary>
        public string AssetKey { get; internal set; }
        public string ShopCategory { get; internal set; }
        public string ConfiguredTypeId { get; internal set; }
        /// <summary>Ambience points this piece adds while placed (published catalog; raises tips and food patience).</summary>
        public int Ambience { get; internal set; }
        /// <summary>Permanent effects while placed (published catalog, Phase 3).</summary>
        public FurnitureEffects Effects { get; internal set; } = FurnitureEffects.None;

        /// <summary>Uses before it breaks and needs fixing (toilets); 0 = never breaks.</summary>
        public int BreakCount { get; }
        /// <summary>Small enough to stand on a surface piece (a TV, a lamp), as well as on the floor.</summary>
        public bool Stackable { get; }
        /// <summary>Other pieces can stand on it (tables always; counters and cabinets when set).</summary>
        public bool Surface { get; }
        /// <summary>Explicit permission to stand in the designated street decoration cells.</summary>
        public bool OutdoorDecor { get; }

        public FurnitureDef(string id, string nameKey, FurnitureKind kind, int sizeX, int sizeY,
            float height, float seatHeight, int price, int unlockLevel = 1, int breakCount = 0,
            bool stackable = false, bool surface = false, bool outdoorDecor = false)
        {
            OutdoorDecor = outdoorDecor;
            Stackable = stackable;
            Surface = surface || kind == FurnitureKind.Table;
            BreakCount = Math.Max(0, breakCount);
            UnlockLevel = Math.Max(1, unlockLevel);
            Id = DishDef.RequireId(id);
            NameKey = nameKey ?? throw new ArgumentNullException(nameof(nameKey));
            if (sizeX <= 0 || sizeY <= 0) throw new ArgumentOutOfRangeException(nameof(sizeX), "Footprint must be at least 1x1.");
            if (height < 0) throw new ArgumentOutOfRangeException(nameof(height));
            Kind = kind;
            SizeX = sizeX;
            SizeY = sizeY;
            Height = height;
            SeatHeight = seatHeight;
            Price = price;
        }
    }

    public sealed class CustomerTypeDef
    {
        public string Id { get; }
        public string NameKey { get; }
        /// <summary>Cells per second.</summary>
        public float WalkSpeed { get; }
        /// <summary>How long they wait in the waiting area for a seat before giving up.</summary>
        public float PatienceSeconds { get; }
        /// <summary>How long they wait at the table for food after ordering before leaving angry.</summary>
        public float FoodPatienceSeconds { get; }
        /// <summary>Relative spawn weight.</summary>
        public int SpawnWeight { get; }
        /// <summary>Restaurant level at which this customer type starts visiting.</summary>
        public int MinLevel { get; }
        /// <summary>Minimum dish stars that fully satisfies them.</summary>
        public int ExpectedStars { get; }

        public CustomerTypeDef(string id, string nameKey, float walkSpeed, float patienceSeconds, int spawnWeight,
            float foodPatienceSeconds = 60f, int minLevel = 1, int expectedStars = 1)
        {
            Id = DishDef.RequireId(id);
            NameKey = nameKey ?? throw new ArgumentNullException(nameof(nameKey));
            if (walkSpeed <= 0) throw new ArgumentOutOfRangeException(nameof(walkSpeed));
            if (spawnWeight < 0) throw new ArgumentOutOfRangeException(nameof(spawnWeight));
            if (foodPatienceSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(foodPatienceSeconds));
            WalkSpeed = walkSpeed;
            PatienceSeconds = patienceSeconds;
            FoodPatienceSeconds = foodPatienceSeconds;
            SpawnWeight = spawnWeight;
            MinLevel = Math.Max(1, minLevel);
            ExpectedStars = Math.Max(1, Math.Min(5, expectedStars));
        }
    }

    /// <summary>Where an ingredient normally comes from.</summary>
    public enum IngredientSource { Market, Garden }

    /// <summary>A base ingredient collected from the market or garden and used in the recipe lab.</summary>
    public sealed class IngredientDef
    {
        public string Id { get; }
        public string NameKey { get; }
        /// <summary>Relative chance of appearing in a market basket (common ingredients are higher).</summary>
        public int DropWeight { get; }
        /// <summary>1 (common) to 5 (rare) stars.</summary>
        public int Rarity { get; }
        public IngredientSource Source { get; }
        /// <summary>Garden growing time; 0 for ingredients that only come from the market.</summary>
        public float GrowHours { get; }

        public IngredientDef(string id, string nameKey, int dropWeight, int rarity = 1,
            IngredientSource source = IngredientSource.Market, float growHours = 0)
        {
            Id = DishDef.RequireId(id);
            NameKey = nameKey ?? throw new ArgumentNullException(nameof(nameKey));
            if (dropWeight < 0) throw new ArgumentOutOfRangeException(nameof(dropWeight));
            if (rarity < 1 || rarity > 5) throw new ArgumentOutOfRangeException(nameof(rarity), "Rarity must be 1-5.");
            if (growHours < 0) throw new ArgumentOutOfRangeException(nameof(growHours));
            if (source == IngredientSource.Garden && growHours <= 0)
                throw new ContentException($"Garden ingredient '{id}' needs growHours.");
            DropWeight = dropWeight;
            Rarity = rarity;
            Source = source;
            GrowHours = growHours;
        }

        /// <summary>Market weight from rarity (economy sheet): 12 - 2 x stars, at least 1.</summary>
        public static int WeightForRarity(int rarity) => Math.Max(1, 12 - 2 * rarity);
    }

    public readonly struct IngredientAmount
    {
        public readonly IngredientDef Ingredient;
        public readonly int Count;

        public IngredientAmount(IngredientDef ingredient, int count)
        {
            Ingredient = ingredient ?? throw new ArgumentNullException(nameof(ingredient));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));
            Count = count;
        }
    }

    /// <summary>
    /// Pack-specific tuning that is not in the designers' economy sheet (rules.json). Progression, rating, guests,
    /// menu slots, staff slots, hire costs and offline income come from <see cref="EconomyTable"/>.
    /// </summary>
    [Serializable]
    public sealed class EntertainmentRules
    {
        public float playSeconds = 30f;
        public int coinsPerUse = 1;
    }

    /// <summary>
    /// Food patience from the BO game rules: every guest type keeps its own waiting time, scaled by
    /// <see cref="baseSeconds"/> over this pack's own base (rules.json → foodPatience).
    /// </summary>
    [Serializable]
    public sealed class FoodPatienceRules
    {
        public float baseSeconds = 40f;
        /// <summary>Extra seconds per ambience point (applied once ambience exists, Phase 2C).</summary>
        public float perAmbienceSeconds = 1.5f;
        /// <summary>The pack's own base, which the guest types' times are written against.</summary>
        [NonSerialized] public float ReferenceBaseSeconds = 40f;

        public float Scale => ReferenceBaseSeconds > 0f && baseSeconds > 0f ? baseSeconds / ReferenceBaseSeconds : 1f;
    }

    /// <summary>How restaurant-wide bonuses of one asset combine (BO asset form → effects.globalStacking).</summary>
    public enum EffectStacking { UniqueAsset, Sum, Highest }

    /// <summary>
    /// A piece's permanent effects (Phase 3), counted only while placed in the saved layout. The stove's
    /// <see cref="OperationTimePercent"/> scales cooking on that stove; <see cref="DemandCapBonus"/> (popularity
    /// points) and <see cref="PatienceSeconds"/> (food patience) are restaurant-wide and combine by
    /// <see cref="Stacking"/>.
    /// </summary>
    public sealed class FurnitureEffects
    {
        public static readonly FurnitureEffects None = new FurnitureEffects();

        public int OperationTimePercent = 100;
        public int DemandCapBonus;
        public int PatienceSeconds;
        public EffectStacking Stacking = EffectStacking.UniqueAsset;
        public int MaxCopies = 1;

        public bool IsNeutral => OperationTimePercent == 100 && DemandCapBonus == 0 && PatienceSeconds == 0;

        /// <summary>
        /// The restaurant-wide total of one effect over placed pieces, as the server counts it: once per asset
        /// (UniqueAsset), per copy up to MaxCopies (Sum), plus only the strongest Highest value.
        /// </summary>
        public static int Total(IEnumerable<FurnitureDef> placed, Func<FurnitureEffects, int> field)
        {
            var copies = new Dictionary<FurnitureDef, int>();
            foreach (var def in placed)
                if (def != null) copies[def] = copies.TryGetValue(def, out int n) ? n + 1 : 1;
            int total = 0, highest = 0;
            foreach (var pair in copies)
            {
                var e = pair.Key.Effects;
                int v = Math.Max(0, field(e));
                if (v == 0) continue;
                switch (e.Stacking)
                {
                    case EffectStacking.Sum: total += v * Math.Min(pair.Value, Math.Max(1, e.MaxCopies)); break;
                    case EffectStacking.Highest: highest = Math.Max(highest, v); break;
                    default: total += v; break;
                }
            }
            return total + highest;
        }
    }

    /// <summary>Tips from ambience (BO game rules → tips): a paying guest adds bill × min(cap, rate × ambience).</summary>
    public sealed class TipRules
    {
        public bool Enabled;
        public float PerAmbience;
        public float Cap;

        public int TipFor(int price, int ambience) =>
            Enabled && price > 0 && ambience > 0 ? (int)Math.Round(price * Math.Min(Cap, PerAmbience * ambience), MidpointRounding.AwayFromZero) : 0;
    }

    /// <summary>How a garden watering counts (BO game rules → watering).</summary>
    public sealed class GardenWateringRules
    {
        /// <summary>False: each watering lasts the watering hours from now. True: it adds them, up to <see cref="CapHours"/>.</summary>
        public bool AddsUp;
        public float CapHours;
    }

    [Serializable]
    public sealed class TrashRules
    {
        public float spawnSeconds; // zero keeps older/test packs disabled
        public int maxPieces = 15;
        public float cleanSeconds = 3f;
        public float turnAwayChancePerPiece = .05f;
        public int xpPerPiece = 1;
    }

    public sealed class GameRules
    {
        public int MarketBaskets = 4;
        public PerkRules Perks = new PerkRules();
        public EntertainmentRules Entertainment = new EntertainmentRules();
        public TrashRules Trash = new TrashRules();
        /// <summary>Ingredients one garden harvest gives (not in the economy sheet).</summary>
        public int GardenHarvest = 3;
        /// <summary>Seconds a guest waits on a chair with no table before leaving unhappy (0 = chairs work alone).</summary>
        public float NoTableWaitSeconds;
        /// <summary>Cells inside the door where guests queue for a seat (the reference game has none: no seat, they leave).</summary>
        public int WaitingSpots = Layout.DoorPlanner.MinWaitingSpots;
        /// <summary>Popularity that drives guest arrivals (see <see cref="Progression.RestaurantDemand"/>).</summary>
        public Progression.DemandRules Demand = new Progression.DemandRules();
        public float MarketRestockSeconds = 60f;
        /// <summary>Hours between player renames (the first rename is free). 0 = no limit.</summary>
        public float NameChangeCooldownHours = 24f;
        /// <summary>Idle estimate: chef walking/plating time on top of a dish's prep time.</summary>
        public float IdleChefOverheadSeconds = 6f;
        /// <summary>Idle estimate: one waiter round trip (counter to table and back).</summary>
        public float IdleWaiterTripSeconds = 10f;
        /// <summary>Idle estimate: time a seat is busy besides eating (walk in, order, wait, leave).</summary>
        public float IdleSeatOverheadSeconds = 25f;
        /// <summary>Seconds to clear one table at work speed 1. 0 = customers leave no dishes.</summary>
        public float CleanSeconds = 4f;
        public float PlayerCleanSeconds = 5f;
        /// <summary>Idle estimate: walking to a dirty table and back, on top of clearing it.</summary>
        public float IdleCleanTripSeconds = 8f;
        public ExpansionRules Expansion;
        /// <summary>Room size by restaurant level (free, never shrinks). When set, land is not bought indoors.</summary>
        public RoomGrowth RoomGrowth;
        /// <summary>Toilet visits after meals (rules.json → toilets).</summary>
        public Progression.ToiletRules Toilets = new Progression.ToiletRules();
        /// <summary>Terrace sizes bought in order from the for-sale sign (empty = no terrace for sale).</summary>
        public IReadOnlyList<TerraceStep> TerraceSteps = Array.Empty<TerraceStep>();
        public IReadOnlyList<IngredientAmount> StartingIngredients = Array.Empty<IngredientAmount>();
        public StaffEnergyRules StaffEnergy = new StaffEnergyRules();
        public FoodPatienceRules FoodPatience = new FoodPatienceRules();
        public GardenWateringRules GardenWatering = new GardenWateringRules();
        public TipRules Tips = new TipRules();
        /// <summary>Items a new player starts with (item id -> count, resolved by the loader).</summary>
        public IReadOnlyList<KeyValuePair<ItemDef, int>> StartingItems = Array.Empty<KeyValuePair<ItemDef, int>>();
    }

    /// <summary>All content for one country theme (e.g. "malaysia"). Built by the content loader.</summary>
    public sealed class ShopCategoryDef
    {
        public string Code;
        public int SortOrder;
        public bool Active;
    }

    public sealed class ContentPack
    {
        private readonly Dictionary<string, DishDef> _dishes = new Dictionary<string, DishDef>();
        private readonly Dictionary<string, FurnitureDef> _furniture = new Dictionary<string, FurnitureDef>();
        private readonly Dictionary<string, CustomerTypeDef> _customerTypes = new Dictionary<string, CustomerTypeDef>();
        private readonly Dictionary<string, StaffTypeDef> _staffTypes = new Dictionary<string, StaffTypeDef>();
        private readonly Dictionary<string, IngredientDef> _ingredients = new Dictionary<string, IngredientDef>();
        private readonly Dictionary<string, ItemDef> _items = new Dictionary<string, ItemDef>();

        public string Id { get; }
        public int ConfigurationVersion { get; internal set; }
        public List<ShopCategoryDef> ShopCategories { get; } = new List<ShopCategoryDef>();
        public IReadOnlyList<DishDef> Dishes { get; }
        public IReadOnlyList<FurnitureDef> Furniture { get; }
        public IReadOnlyList<CustomerTypeDef> CustomerTypes { get; }
        public IReadOnlyList<StaffTypeDef> StaffTypes { get; }
        public IReadOnlyList<IngredientDef> Ingredients { get; }
        public IReadOnlyList<ItemDef> Items { get; }
        /// <summary>Avatar parts for the player profile (avatar.json). Empty catalog = no customisation.</summary>
        public Profile.AvatarCatalog Avatar { get; set; } = new Profile.AvatarCatalog(null);
        public GameRules Rules { get; }
        /// <summary>Numbers from the designers' economy sheet (economy.json); null until it has been imported.</summary>
        public EconomyTable Economy { get; set; }

        public ContentPack(string id, IReadOnlyList<DishDef> dishes, IReadOnlyList<FurnitureDef> furniture,
            IReadOnlyList<CustomerTypeDef> customerTypes, IReadOnlyList<StaffTypeDef> staffTypes = null,
            IReadOnlyList<IngredientDef> ingredients = null, GameRules rules = null, IReadOnlyList<ItemDef> items = null)
        {
            Id = DishDef.RequireId(id);
            Dishes = new List<DishDef>(dishes ?? throw new ArgumentNullException(nameof(dishes)));
            Furniture = new List<FurnitureDef>(furniture ?? throw new ArgumentNullException(nameof(furniture)));
            CustomerTypes = customerTypes ?? throw new ArgumentNullException(nameof(customerTypes));
            StaffTypes = staffTypes ?? Array.Empty<StaffTypeDef>();
            Ingredients = ingredients ?? Array.Empty<IngredientDef>();
            Items = new List<ItemDef>(items ?? Array.Empty<ItemDef>());
            Rules = rules ?? new GameRules();

            foreach (var d in dishes) AddUnique(_dishes, d.Id, d, "dish");
            foreach (var f in furniture) AddUnique(_furniture, f.Id, f, "furniture");
            foreach (var c in customerTypes) AddUnique(_customerTypes, c.Id, c, "customer type");
            foreach (var s in StaffTypes) AddUnique(_staffTypes, s.Id, s, "staff type");
            foreach (var i in Ingredients) AddUnique(_ingredients, i.Id, i, "ingredient");
            foreach (var i in Items) AddUnique(_items, i.Id, i, "item");
            ValidateRecipes();
        }

        /// <summary>Two dishes with the same ingredient set could never both be discovered.</summary>
        private void ValidateRecipes()
        {
            var seen = new Dictionary<string, string>();
            foreach (var d in Dishes)
            {
                if (d.Recipe.Count == 0)
                    continue;
                var parts = new List<string>();
                foreach (var a in d.Recipe)
                    parts.Add(a.Ingredient.Id + "x" + a.Count);
                parts.Sort(StringComparer.Ordinal);
                string key = string.Join("+", parts);
                if (seen.TryGetValue(key, out var other))
                    throw new ContentException($"Dishes '{other}' and '{d.Id}' in pack '{Id}' have the same recipe.");
                seen[key] = d.Id;
            }
        }

        public DishDef GetDish(string id) => Get(_dishes, id, "dish");
        public bool HasDish(string id) => id != null && _dishes.ContainsKey(id);
        public bool HasIngredient(string id) => id != null && _ingredients.ContainsKey(id);
        internal void AddCatalogDish(DishDef dish)
        {
            AddUnique(_dishes, dish.Id, dish, "dish");
            ((List<DishDef>)Dishes).Add(dish);
        }
        public IngredientDef GetIngredient(string id) => Get(_ingredients, id, "ingredient");
        public ItemDef GetItem(string id) => Get(_items, id, "item");
        public FurnitureDef GetFurniture(string id) => Get(_furniture, id, "furniture");
        public bool HasFurniture(string id) => id != null && _furniture.ContainsKey(id);
        public bool HasItem(string id) => id != null && _items.ContainsKey(id);
        /// <summary>Add a server variant backed by a supported local visual template.</summary>
        internal void AddCatalogFurniture(FurnitureDef piece)
        {
            AddUnique(_furniture, piece.Id, piece, "furniture");
            ((List<FurnitureDef>)Furniture).Add(piece);
        }
        internal void AddCatalogItem(ItemDef item)
        {
            AddUnique(_items, item.Id, item, "item");
            ((List<ItemDef>)Items).Add(item);
        }
        public CustomerTypeDef GetCustomerType(string id) => Get(_customerTypes, id, "customer type");
        public StaffTypeDef GetStaffType(string id) => Get(_staffTypes, id, "staff type");

        /// <summary>The player's own avatar working <paramref name="role"/>, or null if the pack has none.</summary>
        public StaffTypeDef OwnerType(StaffRole role)
        {
            foreach (var s in StaffTypes)
                if (s.IsOwner && s.Role == role) return s;
            return null;
        }

        private void AddUnique<T>(Dictionary<string, T> map, string id, T value, string what)
        {
            if (map.ContainsKey(id))
                throw new ContentException($"Content pack '{Id}' has duplicate {what} id '{id}'.");
            map.Add(id, value);
        }

        private T Get<T>(Dictionary<string, T> map, string id, string what)
        {
            if (id != null && map.TryGetValue(id, out var value))
                return value;
            throw new ContentException($"Content pack '{Id}' has no {what} with id '{id}'.");
        }
    }

    public sealed class ContentException : Exception
    {
        public ContentException(string message) : base(message) { }
    }
}
