using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Profile;
using UnityEngine;

namespace DemoRestaurant.Game.Content
{
    /// <summary>How one furniture piece is drawn.</summary>
    public sealed class FurnitureArt
    {
        public string Sprite;
        public string SpriteBack;
        public string Category;
        /// <summary>What tapping the placed piece does outside decorate mode ("menu" opens the menu), or null.</summary>
        public string TapAction;
        /// <summary>1 = sprite spans the whole footprint width; smaller for pieces that don't fill their cell.</summary>
        public float Scale = 1f;
        /// <summary>Corrects a source sprite authored along the opposite isometric axis.</summary>
        public bool MirrorSprite;
        /// <summary>Left, front, right base corners in normalized sprite pixels, Y upwards.</summary>
        public Vector2[] GroundCorners;
        /// <summary>Set by the skin: pixels per floor-cell width, for drawing at native size (0 = fit to footprint).</summary>
        public float TilePixelWidth;
        /// <summary>Set by the skin: cells the art covers in front of the footprint (drawn there, not blocked).</summary>
        public int FrontCells;
        public Color PlaceholderColor = Color.gray;
    }

    /// <summary>How one customer type is drawn. Back is optional; without it the front view is reused.</summary>
    public sealed class CharacterArt
    {
        public string Front;
        public string Back;
        public string SeatedFront;
        public string SeatedBack;
        public float SeatedHeight = 1.18f;
        public float SeatedHipY = 0.19f;
        public float SeatedHipX = 0.55f;
        /// <summary>Share of the standing sprite (from the bottom) that is legs; the walk animation cuts there.</summary>
        public float LegFraction = 0.22f;
        public Color PlaceholderColor = Color.gray;
        /// <summary>
        /// Drawn by the avatar painter from these parts instead of sprites (staff.json "avatar"), so staff without
        /// concept art (e.g. cleaners) still get their own look. Null = use the sprites.
        /// </summary>
        public AvatarLook Avatar;
        /// <summary>
        /// Prefix of baked animation frames in the skin (customers.json / staff.json "animKey"): sprites named
        /// <c>{key}_{anim}_{f|b}_00..</c>. When the skin has them they replace every other way of drawing this character.
        /// </summary>
        public string AnimKey;
    }

    [Serializable]
    public sealed class FloorArt
    {
        public string id;
        public string nameKey;
        public string sprite;
        /// <summary>Top, right, bottom, left of the flat top face in normalized sprite pixels (Y upwards).</summary>
        public Vector2[] surfaceCorners;
    }

    /// <summary>Room-level art for a country pack: floor, walls, wall decorations.</summary>
    [Serializable]
    public sealed class ThemeArt
    {
        /// <summary>Skin folders (Resources paths) tried before artFolder, most preferred first. See ArtLibrary.</summary>
        public string[] skins = System.Array.Empty<string>();
        public string artFolder = "Art/Concept";
        public string floorTile;
        public FloorArt[] floors = Array.Empty<FloorArt>();
        /// <summary>Floor id the terrace is paved with until the player lays another.</summary>
        public string terraceFloor = "stone";
        public string wall;
        public int wallCellsPerSegment = 2;
        /// <summary>Wallpapers the player can put on the back walls (sprite = right-wall art, mirrored for the left).</summary>
        public FloorArt[] wallpapers = Array.Empty<FloorArt>();
        public string window;
        public string door;
        public int entranceX, entranceY;
        public float characterHeight = 1.2f;
        public string coinIcon;
        /// <summary>Optional Resources audio path, without an extension.</summary>
        public string backgroundMusic;
        /// <summary>Per-scene music, Resources paths in order of preference (first one found plays). Empty = backgroundMusic.</summary>
        public string[] restaurantMusic = System.Array.Empty<string>();
        public string[] streetMusic = System.Array.Empty<string>();
        public string[] editorMusic = System.Array.Empty<string>();
        public float musicVolume = 0.35f;
        /// <summary>Links shown in Settings. Empty = "coming soon".</summary>
        public string supportUrl;
        public string privacyUrl;
        public string termsUrl;
    }

    /// <summary>A loaded country pack plus engine-side data the logic layer does not need.</summary>
    public sealed class LoadedContentPack
    {
        public ContentPack Pack { get; }
        public ThemeArt Theme { get; }
        public Logic.Layout.ExteriorCatalog Exterior { get; internal set; }
        public IReadOnlyDictionary<string, FurnitureArt> FurnitureArt { get; }
        public IReadOnlyDictionary<string, CharacterArt> CharacterArt { get; }
        public IReadOnlyDictionary<string, CharacterArt> StaffArt { get; }
        public IReadOnlyDictionary<string, string> DishIcons { get; }
        /// <summary>Plated meals for the world and order bubbles, separate from menu icons.</summary>
        public IReadOnlyDictionary<string, string> DishServings { get; internal set; } = new Dictionary<string, string>();
        /// <summary>Chip colour per ingredient id, until ingredient icons exist.</summary>
        public IReadOnlyDictionary<string, Color> IngredientColors { get; internal set; } = new Dictionary<string, Color>();

        /// <summary>Art key per item id.</summary>
        public IReadOnlyDictionary<string, string> ItemIcons { get; internal set; } = new Dictionary<string, string>();

        public Color ColorForIngredient(string id) =>
            IngredientColors.TryGetValue(id, out var c) ? c : Color.gray;

        public LoadedContentPack(ContentPack pack, ThemeArt theme,
            IReadOnlyDictionary<string, FurnitureArt> furnitureArt,
            IReadOnlyDictionary<string, CharacterArt> characterArt,
            IReadOnlyDictionary<string, CharacterArt> staffArt,
            IReadOnlyDictionary<string, string> dishIcons)
        {
            Pack = pack;
            Theme = theme;
            FurnitureArt = furnitureArt;
            CharacterArt = characterArt;
            StaffArt = staffArt;
            DishIcons = dishIcons;
        }

        public FurnitureArt ArtForFurniture(string id) =>
            FurnitureArt.TryGetValue(id, out var a) ? a : new FurnitureArt { PlaceholderColor = Color.magenta };

        public CharacterArt ArtForCustomer(string id) =>
            CharacterArt.TryGetValue(id, out var a) ? a : new CharacterArt { PlaceholderColor = Color.magenta };

        public CharacterArt ArtForStaff(string id) =>
            StaffArt.TryGetValue(id, out var a) ? a : new CharacterArt { PlaceholderColor = Color.magenta };

        public string IconForDish(string id) => id != null && DishIcons.TryGetValue(id, out var icon) ? icon : null;
        public string ServingForDish(string id) => id != null && DishServings.TryGetValue(id, out var sprite) ? sprite : null;
    }

    /// <summary>
    /// Loads a country content pack from Resources/Content/&lt;packId&gt;/.
    /// Moves to Addressables when packs become downloadable; callers only see <see cref="LoadedContentPack"/>.
    /// </summary>
    public static class ContentLoader
    {
        public static LoadedContentPack Load(string packId)
        {
            var ingredients = new List<IngredientDef>();
            var ingredientById = new Dictionary<string, IngredientDef>();
            var ingredientColors = new Dictionary<string, Color>();
            foreach (var i in Read<IngredientFile>(packId, "ingredients").ingredients)
            {
                int rarity = i.rarity > 0 ? i.rarity : 1;
                IngredientSource source;
                if (string.IsNullOrEmpty(i.source)) source = IngredientSource.Market;
                else if (!Enum.TryParse(i.source, true, out source))
                    throw new ContentException($"Ingredient '{i.id}' has unknown source '{i.source}'.");
                // Market weight follows rarity unless a file sets it explicitly.
                int weight = i.dropWeight > 0 ? i.dropWeight : IngredientDef.WeightForRarity(rarity);
                var def = new IngredientDef(i.id, i.nameKey, weight, rarity, source, i.growHours);
                ingredients.Add(def);
                ingredientById[i.id] = def;
                ingredientColors[i.id] = ParseColor(i.color, i.id);
            }

            var dishes = new List<DishDef>();
            var dishIcons = new Dictionary<string, string>();
            var dishServings = new Dictionary<string, string>();
            foreach (var d in Read<DishFile>(packId, "dishes").dishes)
            {
                var recipe = Amounts(d.recipe, ingredientById, $"dish '{d.id}'");
                dishes.Add(new DishDef(d.id, d.nameKey, d.price, d.cookSeconds, d.eatSeconds,
                    d.stars > 0 ? d.stars : 1, recipe, d.knownAtStart, ParseCategory(d.category, d.id), d.unlockLevel));
                if (!string.IsNullOrEmpty(d.icon))
                    dishIcons[d.id] = d.icon;
                if (!string.IsNullOrEmpty(d.servingSprite)) dishServings[d.id] = d.servingSprite;
            }

            var furniture = new List<FurnitureDef>();
            var furnitureArt = new Dictionary<string, FurnitureArt>();
            foreach (var f in FurnitureEntries(packId))
            {
                if (!Enum.TryParse(f.kind, out FurnitureKind kind))
                    throw new ContentException($"Furniture '{f.id}' in pack '{packId}' has unknown kind '{f.kind}'.");
                furniture.Add(new FurnitureDef(f.id, f.nameKey, kind, f.sizeX, f.sizeY, f.height, f.seatHeight, f.price, f.unlockLevel, f.breakCount, f.stackable, f.surface, f.outdoorDecor || f.category == "Outdoor"));
                furnitureArt[f.id] = new FurnitureArt
                {
                    Sprite = f.sprite,
                    SpriteBack = f.spriteBack,
                    Category = f.category,
                    TapAction = f.tapAction,
                    Scale = f.artScale > 0 ? f.artScale : 1f,
                    MirrorSprite = f.mirrorSprite,
                    GroundCorners = f.groundCorners,
                    PlaceholderColor = ParseColor(f.placeholderColor, f.id),
                };
            }

            var customers = new List<CustomerTypeDef>();
            var characterArt = new Dictionary<string, CharacterArt>();
            foreach (var c in Read<CustomerFile>(packId, "customers").customerTypes)
            {
                customers.Add(new CustomerTypeDef(c.id, c.nameKey, c.walkSpeed, c.patienceSeconds, c.spawnWeight,
                    c.foodPatienceSeconds > 0 ? c.foodPatienceSeconds : 60f,
                    c.minLevel > 0 ? c.minLevel : 1,
                    c.expectedStars > 0 ? c.expectedStars : 1));
                characterArt[c.id] = new CharacterArt
                {
                    Front = c.spriteFront,
                    Back = c.spriteBack,
                    SeatedFront = c.spriteSeatedFront,
                    SeatedBack = c.spriteSeatedBack,
                    SeatedHeight = c.seatedHeight > 0 ? c.seatedHeight : 1.18f,
                    SeatedHipY = c.seatedHipY,
                    SeatedHipX = c.seatedHipX,
                    LegFraction = c.legFraction > 0 ? c.legFraction : 0.22f,
                    PlaceholderColor = ParseColor(c.placeholderColor, c.id),
                    AnimKey = c.animKey,
                };
            }

            var avatar = LoadAvatar(packId);
            var staff = new List<StaffTypeDef>();
            var staffArt = new Dictionary<string, CharacterArt>();
            foreach (var s in Read<StaffFile>(packId, "staff").staffTypes)
            {
                if (!Enum.TryParse(s.role, out StaffRole role))
                    throw new ContentException($"Staff type '{s.id}' in pack '{packId}' has unknown role '{s.role}'.");
                staff.Add(new StaffTypeDef(s.id, s.nameKey, role, s.walkSpeed, s.cookSpeed > 0 ? s.cookSpeed : 1f, s.owner));
                staffArt[s.id] = new CharacterArt
                {
                    Front = s.spriteFront,
                    Back = s.spriteBack,
                    LegFraction = s.legFraction > 0 ? s.legFraction : 0.22f,
                    PlaceholderColor = ParseColor(s.placeholderColor, s.id),
                    Avatar = Look(avatar, s.avatar, s.id),
                    AnimKey = s.animKey,
                };
            }

            var items = new List<ItemDef>();
            var itemById = new Dictionary<string, ItemDef>();
            var itemIcons = new Dictionary<string, string>();
            foreach (var i in Read<ItemFile>(packId, "items").items)
            {
                var def = new ItemDef(i.id, i.nameKey, i.energyRestore, i.price);
                items.Add(def);
                itemById[i.id] = def;
                if (!string.IsNullOrEmpty(i.icon)) itemIcons[i.id] = i.icon;
            }

            var r = Read<RulesFile>(packId, "rules");
            var rules = new GameRules
            {
                MarketBaskets = r.marketBaskets,
                GardenHarvest = r.gardenHarvest,
                NoTableWaitSeconds = r.noTableWaitSeconds,
                WaitingSpots = System.Math.Max(0, r.waitingSpots),
                Demand = DemandRulesFrom(r.demand),
                MarketRestockSeconds = r.marketRestockSeconds,
                NameChangeCooldownHours = r.nameChangeCooldownHours,
                CleanSeconds = r.cleanSeconds,
                PlayerCleanSeconds = r.playerCleanSeconds,
                IdleCleanTripSeconds = r.idleCleanTripSeconds,
                // JsonUtility fills a missing object with zeros: no step means no land for sale.
                Expansion = r.expansion != null && r.expansion.step > 0 ? r.expansion : null,
                TerraceSteps = r.terraceSteps != null ? System.Array.FindAll(r.terraceSteps, s => s != null && s.IsValid) : System.Array.Empty<DemoRestaurant.Logic.Layout.TerraceStep>(),
                Toilets = r.toilets ?? new DemoRestaurant.Logic.Progression.ToiletRules(),
                Entertainment = r.entertainment ?? new EntertainmentRules(),
                RoomGrowth = r.roomGrowth != null && r.roomGrowth.Length > 0 ? new DemoRestaurant.Logic.Layout.RoomGrowth(r.roomGrowth) : null,
                StartingIngredients = Amounts(r.startingIngredients, ingredientById, "startingIngredients"),
                StaffEnergy = EnergyRules(r.staffEnergy),
                StartingItems = ItemAmounts(r.startingItems, itemById),
            };

            var theme = Read<ThemeArt>(packId, "theme");
            // Private prototype floors (git-ignored, never shipped; see RefProtoBuildGuard).
            var protoFloors = Resources.Load<TextAsset>($"Content/{packId}/RefProto/floors");
            if (protoFloors != null)
            {
                var extra = JsonUtility.FromJson<FloorFile>(protoFloors.text)?.floors;
                if (extra != null && extra.Length > 0)
                {
                    var all = new List<FloorArt>(theme.floors);
                    all.AddRange(extra);
                    theme.floors = all.ToArray();
                }
            }
            var protoWallpapers = Resources.Load<TextAsset>($"Content/{packId}/RefProto/wallpapers");
            if (protoWallpapers != null)
            {
                var extra = JsonUtility.FromJson<WallpaperFile>(protoWallpapers.text)?.wallpapers;
                if (extra != null && extra.Length > 0)
                {
                    var all = new List<FloorArt>(theme.wallpapers);
                    all.AddRange(extra);
                    theme.wallpapers = all.ToArray();
                }
            }
            var pack = new ContentPack(packId, dishes, furniture, customers, staff, ingredients, rules, items);
            pack.Avatar = avatar;
            pack.Economy = LoadEconomy(packId);
            return new LoadedContentPack(pack, theme, furnitureArt, characterArt, staffArt, dishIcons)
            {
                Exterior = LoadExterior(packId),
                IngredientColors = ingredientColors,
                ItemIcons = itemIcons,
                DishServings = dishServings,
            };
        }

        /// <summary>
        /// economy.json (imported from the designers' sheet). A running restaurant needs it; editor tools that only
        /// read art and furniture can load a pack without it.
        /// </summary>
        private static EconomyTable LoadEconomy(string packId)
        {
            if (Resources.Load<TextAsset>($"Content/{packId}/economy") == null)
                return null;
            var table = Read<EconomyTable>(packId, "economy");
            var errors = table.Validate();
            if (errors.Count > 0)
                throw new ContentException($"economy.json in pack '{packId}': {string.Join("; ", errors)}");
            return table;
        }

        /// <summary>avatar.json is optional; without it the profile dialog hides the look and outfit tabs.</summary>
        private static AvatarCatalog LoadAvatar(string packId)
        {
            if (Resources.Load<TextAsset>($"Content/{packId}/avatar") == null)
                return new AvatarCatalog(null);
            var parts = new List<AvatarPartDef>();
            foreach (var a in Read<AvatarFile>(packId, "avatar").parts)
            {
                if (!Enum.TryParse(a.slot, out AvatarSlot slot))
                    throw new ContentException($"Avatar part '{a.id}' in pack '{packId}' has unknown slot '{a.slot}'.");
                if (!string.IsNullOrEmpty(a.color) && !ColorUtility.TryParseHtmlString(a.color, out _))
                    throw new ContentException($"Avatar part '{a.id}' has invalid color '{a.color}'.");
                parts.Add(new AvatarPartDef(a.id, slot, a.nameKey, a.color, a.unlockLevel, a.sprite));
            }
            return new AvatarCatalog(parts);
        }

        /// <summary>"Slot:partId" pairs on top of the catalog's defaults, e.g. ["Hat:cap", "Shirt:apron"].</summary>
        private static AvatarLook Look(AvatarCatalog catalog, string[] parts, string owner)
        {
            if (parts == null || parts.Length == 0 || catalog.IsEmpty)
                return null;
            var look = AvatarLook.Default(catalog);
            foreach (var p in parts)
            {
                int colon = p.IndexOf(':');
                if (colon < 0 || !Enum.TryParse(p.Substring(0, colon), out AvatarSlot slot) ||
                    !look.TrySet(catalog, slot, p.Substring(colon + 1), int.MaxValue))
                    throw new ContentException($"'{owner}' has an unknown avatar part '{p}'.");
            }
            return look;
        }

        private static StaffEnergyRules EnergyRules(EnergyDto dto)
        {
            var rules = new StaffEnergyRules();
            if (dto == null)
                return rules;
            if (dto.drainPerMinute >= 0) rules.DrainPerMinute = dto.drainPerMinute;
            if (dto.recoverPerMinute > 0) rules.RecoverPerMinute = dto.recoverPerMinute;
            if (dto.autoReturnAt > 0) rules.AutoReturnAt = dto.autoReturnAt;
            if (dto.bands != null && dto.bands.Length > 0)
            {
                var bands = new List<EnergyBand>();
                foreach (var b in dto.bands) bands.Add(new EnergyBand(b.minEnergy, b.speedMultiplier));
                rules.Bands = bands;
            }
            return rules;
        }

        private static List<KeyValuePair<ItemDef, int>> ItemAmounts(AmountDto[] list, Dictionary<string, ItemDef> byId)
        {
            var result = new List<KeyValuePair<ItemDef, int>>();
            foreach (var a in list ?? Array.Empty<AmountDto>())
            {
                if (!byId.TryGetValue(a.id ?? "", out var item))
                    throw new ContentException($"startingItems uses unknown item '{a.id}'.");
                result.Add(new KeyValuePair<ItemDef, int>(item, a.count > 0 ? a.count : 1));
            }
            return result;
        }

        private static List<IngredientAmount> Amounts(AmountDto[] items, Dictionary<string, IngredientDef> byId, string owner)
        {
            var list = new List<IngredientAmount>();
            foreach (var a in items ?? Array.Empty<AmountDto>())
            {
                if (!byId.TryGetValue(a.id ?? "", out var ingredient))
                    throw new ContentException($"{owner} uses unknown ingredient '{a.id}'.");
                list.Add(new IngredientAmount(ingredient, a.count > 0 ? a.count : 1));
            }
            return list;
        }

        private static Logic.Layout.ExteriorCatalog LoadExterior(string packId)
        {
            var asset = Resources.Load<TextAsset>($"Content/{packId}/exterior");
            if (asset == null) return null;
            var file = JsonUtility.FromJson<ExteriorFile>(asset.text);
            var defs = new List<Logic.Layout.ExteriorItemDef>();
            var dtos = new List<ExteriorDto>(file.items);
            // Private prototype street-front pieces (git-ignored, never shipped; see RefProtoBuildGuard).
            var proto = Resources.Load<TextAsset>($"Content/{packId}/RefProto/exterior");
            if (proto != null) dtos.AddRange(JsonUtility.FromJson<ExteriorFile>(proto.text)?.items ?? Array.Empty<ExteriorDto>());
            foreach (var dto in dtos)
            {
                if (!Enum.TryParse(dto.kind, out Logic.Layout.ExteriorKind kind))
                    throw new ContentException($"Unknown exterior kind: {dto.kind}");
                defs.Add(new Logic.Layout.ExteriorItemDef { id = dto.id, nameKey = dto.nameKey,
                    sprite = dto.sprite, kind = kind, price = dto.price, unlockLevel = dto.unlockLevel,
                    starterOwned = dto.starterOwned, width = dto.width, height = dto.height, wallTop = dto.wallTop,
                    roofAnchorX = dto.roofAnchorX, roofAnchorY = dto.roofAnchorY });
            }
            return new Logic.Layout.ExteriorCatalog(defs, file.starter, file.buildingSteps);
        }

        [Serializable] private sealed class ExteriorFile
        {
            public ExteriorDto[] items;
            public Logic.Layout.ExteriorPiece[] starter;
            public Logic.Layout.ExteriorStep[] buildingSteps;
        }
        [Serializable] private sealed class ExteriorDto
        {
            public string id, nameKey, sprite, kind;
            public int price, unlockLevel = 1, starterOwned;
            public float width, height, wallTop;
            public float roofAnchorX = .5f, roofAnchorY = 1f;
        }

        /// <summary>furniture.json, plus the private prototype catalog when it is present (git-ignored, never shipped).</summary>
        private static IEnumerable<FurnitureDto> FurnitureEntries(string packId)
        {
            foreach (var f in Read<FurnitureFile>(packId, "furniture").furniture) yield return f;
            var proto = Resources.Load<TextAsset>($"Content/{packId}/RefProto/furniture");
            if (proto == null) yield break;
            var extra = JsonUtility.FromJson<FurnitureFile>(proto.text);
            if (extra?.furniture != null)
                foreach (var f in extra.furniture) yield return f;
        }

        [Serializable]
        private sealed class FloorFile { public FloorArt[] floors; }
        [Serializable] private sealed class WallpaperFile { public FloorArt[] wallpapers; }

        private static T Read<T>(string packId, string file)
        {
            string path = $"Content/{packId}/{file}";
            var asset = Resources.Load<TextAsset>(path);
            if (asset == null)
                throw new ContentException($"Missing content file Resources/{path}.json");
            var data = JsonUtility.FromJson<T>(asset.text);
            if (data == null)
                throw new ContentException($"Could not parse Resources/{path}.json");
            return data;
        }

        [Serializable] private class DemandDto
        {
            public double start = 120, min = 40, max = 500, perHappyGuest = 1, perUnhappyGuest = 1, closedDecayPerHour = 10,
                guestsPerMinutePerDemand = 0.05;
        }

        private static Logic.Progression.DemandRules DemandRulesFrom(DemandDto d)
        {
            if (d == null) return new Logic.Progression.DemandRules();
            if (d.min < 0 || d.max <= d.min || d.start < d.min || d.start > d.max || d.guestsPerMinutePerDemand <= 0)
                throw new ContentException("rules.json demand: need 0 <= min <= start <= max and a positive guestsPerMinutePerDemand.");
            return new Logic.Progression.DemandRules
            {
                Start = d.start, Min = d.min, Max = d.max, PerHappyGuest = d.perHappyGuest, PerUnhappyGuest = d.perUnhappyGuest,
                ClosedDecayPerHour = d.closedDecayPerHour, GuestsPerMinutePerDemand = d.guestsPerMinutePerDemand,
            };
        }

        private static DishCategory ParseCategory(string category, string dishId)
        {
            if (string.IsNullOrEmpty(category))
                throw new ContentException($"Dish '{dishId}' needs a category (starter, main, dessert or drink).");
            if (!Enum.TryParse(category, true, out DishCategory parsed))
                throw new ContentException($"Dish '{dishId}' has unknown category '{category}'.");
            return parsed;
        }

        private static Color ParseColor(string html, string id)
        {
            if (string.IsNullOrEmpty(html))
                return Color.gray;
            if (!ColorUtility.TryParseHtmlString(html, out var c))
                throw new ContentException($"'{id}' has invalid placeholderColor '{html}'.");
            return c;
        }

        // JsonUtility DTOs. Field names match the JSON files.
#pragma warning disable 0649
        [Serializable] private class DishFile { public DishDto[] dishes = Array.Empty<DishDto>(); }
        [Serializable] private class DishDto
        {
            public string id; public string nameKey; public string servingSprite; public int price; public float cookSeconds; public float eatSeconds;
            public string icon; public int stars; public bool knownAtStart; public AmountDto[] recipe = Array.Empty<AmountDto>();
            public string category; public int unlockLevel = 1;
        }
        [Serializable] private class AmountDto { public string id; public int count = 1; }

        [Serializable] private class IngredientFile { public IngredientDto[] ingredients = Array.Empty<IngredientDto>(); }
        [Serializable] private class IngredientDto
        {
            public string id; public string nameKey; public int dropWeight; public string color;
            public int rarity = 1; public string source; public float growHours;
        }

        [Serializable] private class RulesFile
        {
            public DemoRestaurant.Logic.Layout.ExpansionRules expansion;
            public DemoRestaurant.Logic.Layout.RoomGrowthStep[] roomGrowth;
            public DemoRestaurant.Logic.Layout.TerraceStep[] terraceSteps;
            public DemoRestaurant.Logic.Progression.ToiletRules toilets = new DemoRestaurant.Logic.Progression.ToiletRules();
            public EntertainmentRules entertainment = new EntertainmentRules();
            public int marketBaskets = 4; public int gardenHarvest = 3; public float noTableWaitSeconds = 20f; public int waitingSpots = 3; public float marketRestockSeconds = 60f; public float nameChangeCooldownHours = 24f;
            public float cleanSeconds = 4f; public float playerCleanSeconds = 5f; public float idleCleanTripSeconds = 8f;
            public DemandDto demand;
            public EnergyDto staffEnergy; public AmountDto[] startingItems = Array.Empty<AmountDto>();
            public AmountDto[] startingIngredients = Array.Empty<AmountDto>();
        }
        [Serializable] private class EnergyDto
        {
            public float drainPerMinute = -1f; public float recoverPerMinute; public float autoReturnAt;
            public BandDto[] bands = Array.Empty<BandDto>();
        }
        [Serializable] private class BandDto { public float minEnergy; public float speedMultiplier; }

        [Serializable] private class ItemFile { public ItemDto[] items = Array.Empty<ItemDto>(); }
        [Serializable] private class ItemDto { public string id; public string nameKey; public float energyRestore; public int price; public string icon; }

        [Serializable] private class AvatarFile { public AvatarDto[] parts = Array.Empty<AvatarDto>(); }
        [Serializable] private class AvatarDto
        {
            public string id; public string slot; public string nameKey; public string color; public int unlockLevel = 1; public string sprite;
        }


        [Serializable] private class FurnitureFile { public FurnitureDto[] furniture = Array.Empty<FurnitureDto>(); }
        [Serializable] private class FurnitureDto
        {
            public string id; public string nameKey; public string kind;
            public int sizeX = 1; public int sizeY = 1; public float height; public float seatHeight; public int price; public int unlockLevel = 1; public int breakCount; public bool stackable; public bool surface;
            public string sprite; public string spriteBack; public string category; public string tapAction; public float artScale; public string placeholderColor;
            public bool mirrorSprite; public Vector2[] groundCorners;
            public bool outdoorDecor;
        }

        [Serializable] private class CustomerFile { public CustomerDto[] customerTypes = Array.Empty<CustomerDto>(); }
        [Serializable] private class CustomerDto
        {
            public string id; public string nameKey; public float walkSpeed; public float patienceSeconds; public int spawnWeight;
            public float foodPatienceSeconds; public int minLevel; public int expectedStars;
            public string spriteFront; public string spriteBack; public string placeholderColor; public float legFraction;
            public string spriteSeatedFront; public string spriteSeatedBack;
            public string animKey;
            public float seatedHeight = 1.18f; public float seatedHipY = 0.19f; public float seatedHipX = 0.55f;
        }
        [Serializable] private class StaffFile { public StaffDto[] staffTypes = Array.Empty<StaffDto>(); }
        [Serializable] private class StaffDto
        {
            public string id; public string nameKey; public string role; public float walkSpeed; public float cookSpeed;
            public bool owner;
            public string spriteFront; public string spriteBack; public string placeholderColor; public float legFraction;
            public string[] avatar = Array.Empty<string>();
            public string animKey;
        }
#pragma warning restore 0649
    }
}
