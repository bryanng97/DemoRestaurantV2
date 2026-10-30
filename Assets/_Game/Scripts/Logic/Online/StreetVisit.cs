using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Online
{
    /// <summary>One restaurant on the random street (GET /streets/random, Phase 5A).</summary>
    public sealed class StreetRestaurant
    {
        public string RestaurantId;
        public string Name;
        public string OwnerName;
        public int Level;
        /// <summary>Popularity as players see it (points ÷ 10, 0–50).</summary>
        public double Popularity;
        /// <summary>The owner played within the server's activity window (they come first).</summary>
        public bool ActiveRecently;

        /// <summary>The street in the server's order; entries without an id are skipped.</summary>
        public static List<StreetRestaurant> ParseStreet(JsonValue data)
        {
            var list = new List<StreetRestaurant>();
            foreach (var item in data["items"].Items)
            {
                string id = item["restaurantId"].AsString();
                if (string.IsNullOrEmpty(id)) continue;
                list.Add(new StreetRestaurant
                {
                    RestaurantId = id,
                    Name = item["name"].AsString() ?? "",
                    OwnerName = item["ownerName"].AsString() ?? "",
                    Level = Math.Max(1, item["level"].AsInt(1)),
                    Popularity = item["popularity"].AsDouble(0),
                    ActiveRecently = item["activeRecently"].AsBool(false),
                });
            }
            return list;
        }
    }

    /// <summary>One of the visited owner's garden plots, as a visitor sees it.</summary>
    public sealed class VisitPlot
    {
        public int Index;
        public bool Open;
        /// <summary>Something is growing here (only planted plots can be watered).</summary>
        public bool Planted;
        public bool Watered;
        public bool Ready;
        public string IngredientId;

        /// <summary>A visitor can water it: open, planted and not ready to harvest.</summary>
        public bool CanWater => Open && Planted && !Ready;
    }

    /// <summary>
    /// What a visitor can help with in another restaurant (GET /restaurants/{id}/visit-state, Phase 5C):
    /// its litter and planted plots, the visitor's reward rules and the coins already earned today.
    /// </summary>
    public sealed class VisitState
    {
        public string RestaurantId;
        public int Trash;
        public readonly List<VisitPlot> Plots = new List<VisitPlot>();
        public int CoinsToday;
        /// <summary>The visitor's daily help-coin cap, or null for none.</summary>
        public int? DailyCoinCap;
        public int TrashCoinsMin, TrashCoinsMax, TrashXp, WaterCoins;
        public double WaterMinHours;

        public bool CapReached => DailyCoinCap.HasValue && CoinsToday >= DailyCoinCap.Value;

        public static VisitState Parse(JsonValue data)
        {
            var state = new VisitState
            {
                RestaurantId = data["restaurantId"].AsString(),
                Trash = Math.Max(0, data["trash"].AsInt(0)),
                CoinsToday = Math.Max(0, data["coinsToday"].AsInt(0)),
            };
            var rewards = data["rewards"];
            state.TrashCoinsMin = rewards["trashCoinsMin"].AsInt(1);
            state.TrashCoinsMax = rewards["trashCoinsMax"].AsInt(2);
            state.TrashXp = rewards["trashXp"].AsInt(1);
            state.WaterCoins = rewards["waterCoins"].AsInt(1);
            state.WaterMinHours = rewards["waterMinHours"].AsDouble(3);
            state.DailyCoinCap = rewards["dailyCoinCap"].Kind == JsonKind.Number ? rewards["dailyCoinCap"].AsInt() : (int?)null;
            foreach (var p in data["garden"]["plots"].Items)
            {
                var crop = p["crop"];
                bool planted = crop.Kind == JsonKind.Object;
                state.Plots.Add(new VisitPlot
                {
                    Index = p["index"].AsInt(),
                    Open = p["open"].AsBool(false),
                    Planted = planted,
                    Watered = planted && crop["watered"].AsBool(false),
                    Ready = planted && crop["ready"].AsBool(false),
                    IngredientId = planted ? crop["ingredientId"].AsString() : null,
                });
            }
            return state;
        }

        /// <summary>Folds a help result back: litter left, the day's coins and, after watering, the owner's plots.</summary>
        public void Apply(HelpResult result, JsonValue data)
        {
            if (result.TrashLeft.HasValue) Trash = Math.Max(0, result.TrashLeft.Value);
            CoinsToday += Math.Max(0, result.Reward);
            if (data["garden"].Kind != JsonKind.Object) return;
            var fresh = Parse(JsonValue.Object().Set("garden", data["garden"]));
            Plots.Clear();
            Plots.AddRange(fresh.Plots);
        }
    }

    /// <summary>The visitor's side of one help action (POST /restaurants/{id}/help/trash or /water).</summary>
    public readonly struct HelpResult
    {
        /// <summary>Coins paid for this help (0 when the daily cap or the watering rule allowed none).</summary>
        public readonly int Reward;
        /// <summary>The visitor's balance afterwards.</summary>
        public readonly long Coins;
        /// <summary>The daily cap cut the reward.</summary>
        public readonly bool Capped;
        public readonly int? TrashLeft;
        public readonly double AddedHours;

        public HelpResult(int reward, long coins, bool capped, int? trashLeft, double addedHours)
        {
            Reward = reward; Coins = coins; Capped = capped; TrashLeft = trashLeft; AddedHours = addedHours;
        }

        public static HelpResult Parse(JsonValue data) => new HelpResult(
            Math.Max(0, data["reward"].AsInt(0)), data["coins"].AsLong(0), data["capped"].AsBool(false),
            data["trashLeft"].Kind == JsonKind.Number ? data["trashLeft"].AsInt() : (int?)null,
            data["addedHours"].AsDouble(0));
    }

    /// <summary>The first-visit reward in a visit response (Phase 5B), or null when there is none.</summary>
    public sealed class FirstVisitReward
    {
        public string IngredientId;
        public int Quantity;

        public static FirstVisitReward Parse(JsonValue visit)
        {
            if (!visit["firstVisit"].AsBool(false)) return null;
            var reward = visit["reward"];
            string id = reward["ingredientId"].AsString();
            return string.IsNullOrEmpty(id) ? null : new FirstVisitReward { IngredientId = id, Quantity = Math.Max(1, reward["quantity"].AsInt(1)) };
        }
    }
}
