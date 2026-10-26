using System;
using System.Collections.Generic;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;

namespace DemoRestaurant.Logic.Progression
{
    public enum PlotState
    {
        /// <summary>The restaurant level hasn't opened this plot yet.</summary>
        Locked,
        /// <summary>Open and bare: buy a seed to plant.</summary>
        Empty,
        /// <summary>Planted and watered: growing.</summary>
        Growing,
        /// <summary>Planted but the water ran out: growth is paused (plants never die).</summary>
        Dry,
        /// <summary>Fully grown: harvest it.</summary>
        Ready,
    }

    /// <summary>Saved state of one plot (plain fields for the device save).</summary>
    [Serializable]
    public sealed class GardenPlotSave
    {
        public int index;
        public string crop;
        /// <summary>Watered growing time so far, in seconds.</summary>
        public double grownSeconds;
        /// <summary>ISO 8601 UTC.</summary>
        public string waterUntilUtc;
        public string lastUpdateUtc;
    }

    /// <summary>
    /// The farm behind the restaurant: 9 plots (3 x 3 plots of 3 x 3 cells, a 9 x 9 field). Plots open at the levels
    /// in the economy sheet. A seed costs coins and grows a random garden ingredient (rarer ones less often).
    /// Planting waters it once; each watering lasts the sheet's watering hours. Growth only counts watered time, so
    /// a dry plant pauses and never dies. Everything runs on the wall clock, so it grows while the game is closed.
    /// </summary>
    public sealed class Garden
    {
        public const int PlotCount = 9;
        /// <summary>Plots per row in the 3 x 3 field, and cells per plot side.</summary>
        public const int PlotsPerRow = 3;
        public const int PlotSize = 2;

        private sealed class Plot
        {
            public IngredientDef Crop;
            public double Grown;
            public DateTime WaterUntil;
            public DateTime Last;
        }

        private readonly Plot[] _plots = new Plot[PlotCount];
        private readonly IReadOnlyList<IngredientDef> _crops;
        private readonly EconomySettings _settings;
        private readonly Random _random;

        /// <summary>Ingredients a harvest gives.</summary>
        public int HarvestAmount { get; }
        public int SeedCost => _settings.seedCost;
        public double WateringSeconds => _settings.wateringHours * 3600.0;

        public Garden(IReadOnlyList<IngredientDef> ingredients, EconomySettings settings, int harvestAmount, Random random)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _random = random ?? throw new ArgumentNullException(nameof(random));
            HarvestAmount = Math.Max(1, harvestAmount);
            var crops = new List<IngredientDef>();
            if (ingredients != null)
                foreach (var i in ingredients)
                    if (i.Source == IngredientSource.Garden) crops.Add(i);
            _crops = crops;
        }

        public bool HasCrops => _crops.Count > 0;

        // ---------------------------------------------------------------- where the farm is
        // Right behind the back-right wall (y < 0), a few cells in from the back corner, with lawn between the fence and the wall:
        // a 6 x 6 field of 2 x 2 plots inside a fence (as in the reference game). Plot 0 is the far-left corner.

        /// <summary>Side of the whole field in cells (6).</summary>
        public const int FieldSize = PlotsPerRow * PlotSize;

        /// <summary>Top corner of the field for a room <paramref name="roomWidth"/> cells wide.</summary>
        public static GridPos FieldOrigin(int roomWidth) =>
            new GridPos(Math.Max(0, Math.Min(FieldInset, roomWidth - FieldSize)), -FieldSize - FieldGapFromWall);

        /// <summary>Cells between the back corner and the field along the wall.</summary>
        public const int FieldInset = 3;

        /// <summary>Lawn rows between the field and the back-right wall (the fence sits in the last one).</summary>
        public const int FieldGapFromWall = 3;

        public static GridPos PlotOrigin(int index, int roomWidth)
        {
            var o = FieldOrigin(roomWidth);
            return new GridPos(o.X + PlotSize * (index % PlotsPerRow), o.Y + PlotSize * (index / PlotsPerRow));
        }

        /// <summary>The plot covering <paramref name="cell"/>, or -1 when the cell isn't in the field.</summary>
        public static int PlotAt(GridPos cell, int roomWidth)
        {
            var o = FieldOrigin(roomWidth);
            int dx = cell.X - o.X, dy = cell.Y - o.Y;
            if (dx < 0 || dy < 0 || dx >= FieldSize || dy >= FieldSize) return -1;
            return (dy / PlotSize) * PlotsPerRow + dx / PlotSize;
        }

        /// <summary>Restaurant level at which plot <paramref name="index"/> opens (int.MaxValue if never).</summary>
        public int UnlockLevel(int index)
        {
            var levels = _settings.gardenPlotLevels;
            return levels != null && index >= 0 && index < levels.Length ? Math.Max(1, levels[index]) : int.MaxValue;
        }

        public bool IsUnlocked(int index, int level) => level >= UnlockLevel(index);

        public PlotState StateOf(int index, int level, DateTime nowUtc)
        {
            if (!IsUnlocked(index, level)) return PlotState.Locked;
            var p = _plots[index];
            if (p == null) return PlotState.Empty;
            double grown = GrownAt(p, nowUtc);
            if (grown >= NeededSeconds(p.Crop)) return PlotState.Ready;
            return nowUtc < p.WaterUntil ? PlotState.Growing : PlotState.Dry;
        }

        public IngredientDef CropOf(int index) => _plots[index]?.Crop;

        /// <summary>0..1 grown (1 when ready, 0 for an empty plot).</summary>
        public double Progress(int index, DateTime nowUtc)
        {
            var p = _plots[index];
            if (p == null) return 0;
            return Math.Min(1.0, GrownAt(p, nowUtc) / NeededSeconds(p.Crop));
        }

        /// <summary>Watered time still needed; zero when ready or empty.</summary>
        public TimeSpan TimeToHarvest(int index, DateTime nowUtc)
        {
            var p = _plots[index];
            if (p == null) return TimeSpan.Zero;
            return TimeSpan.FromSeconds(Math.Max(0, NeededSeconds(p.Crop) - GrownAt(p, nowUtc)));
        }

        /// <summary>Water left; zero when dry or empty.</summary>
        public TimeSpan WaterLeft(int index, DateTime nowUtc)
        {
            var p = _plots[index];
            return p == null || nowUtc >= p.WaterUntil ? TimeSpan.Zero : p.WaterUntil - nowUtc;
        }

        /// <summary>Plants a random garden ingredient in an open, empty plot (the caller pays). Planting waters it.</summary>
        public IngredientDef Plant(int index, int level, DateTime nowUtc)
        {
            if (!HasCrops || StateOf(index, level, nowUtc) != PlotState.Empty) return null;
            var crop = PickCrop();
            _plots[index] = new Plot { Crop = crop, Grown = 0, WaterUntil = nowUtc.AddSeconds(WateringSeconds), Last = nowUtc };
            return crop;
        }

        /// <summary>Refills the water (a fresh watering period, not added on top). Only for growing or dry plants.</summary>
        public bool Water(int index, int level, DateTime nowUtc)
        {
            var state = StateOf(index, level, nowUtc);
            if (state != PlotState.Growing && state != PlotState.Dry) return false;
            var p = _plots[index];
            Settle(p, nowUtc);
            p.WaterUntil = nowUtc.AddSeconds(WateringSeconds);
            return true;
        }

        /// <summary>Picks a ready crop and empties the plot. Returns the crop, or null if not ready.</summary>
        public IngredientDef Harvest(int index, int level, DateTime nowUtc)
        {
            if (StateOf(index, level, nowUtc) != PlotState.Ready) return null;
            var crop = _plots[index].Crop;
            _plots[index] = null;
            return crop;
        }

        /// <summary>Debug: adds watered growing time to every planted plot (and keeps it watered).</summary>
        public void DebugGrow(TimeSpan time, DateTime nowUtc)
        {
            foreach (var p in _plots)
            {
                if (p == null) continue;
                Settle(p, nowUtc);
                p.Grown += time.TotalSeconds;
                if (p.WaterUntil < nowUtc) p.WaterUntil = nowUtc.AddSeconds(WateringSeconds);
            }
        }

        public List<GardenPlotSave> Capture()
        {
            var list = new List<GardenPlotSave>();
            for (int i = 0; i < PlotCount; i++)
            {
                var p = _plots[i];
                if (p == null) continue;
                list.Add(new GardenPlotSave
                {
                    index = i, crop = p.Crop.Id, grownSeconds = p.Grown,
                    waterUntilUtc = p.WaterUntil.ToString("o"), lastUpdateUtc = p.Last.ToString("o"),
                });
            }
            return list;
        }

        /// <summary>Restores saved plots; unknown crops and bad entries are skipped.</summary>
        public void Restore(IEnumerable<GardenPlotSave> saved)
        {
            Array.Clear(_plots, 0, _plots.Length);
            if (saved == null) return;
            foreach (var s in saved)
            {
                if (s == null || s.index < 0 || s.index >= PlotCount) continue;
                IngredientDef crop = null;
                foreach (var c in _crops) if (c.Id == s.crop) crop = c;
                if (crop == null || !TryParse(s.waterUntilUtc, out var water) || !TryParse(s.lastUpdateUtc, out var last)) continue;
                _plots[s.index] = new Plot { Crop = crop, Grown = Math.Max(0, s.grownSeconds), WaterUntil = water, Last = last };
            }
        }

        // ---------------------------------------------------------------------------------------------------------

        private static double NeededSeconds(IngredientDef crop) => Math.Max(1.0, crop.GrowHours * 3600.0);

        /// <summary>Growth so far plus the watered part of the time since the last update. A clock set backwards adds nothing.</summary>
        private static double GrownAt(Plot p, DateTime now)
        {
            var end = now < p.WaterUntil ? now : p.WaterUntil;
            double watered = (end - p.Last).TotalSeconds;
            return p.Grown + Math.Max(0, watered);
        }

        private static void Settle(Plot p, DateTime now)
        {
            if (now <= p.Last) return;
            p.Grown = GrownAt(p, now);
            p.Last = now;
        }

        private IngredientDef PickCrop()
        {
            int total = 0;
            foreach (var c in _crops) total += IngredientDef.WeightForRarity(c.Rarity);
            int pick = _random.Next(total);
            foreach (var c in _crops)
            {
                pick -= IngredientDef.WeightForRarity(c.Rarity);
                if (pick < 0) return c;
            }
            return _crops[_crops.Count - 1];
        }

        private static bool TryParse(string s, out DateTime utc)
        {
            if (!DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.RoundtripKind, out utc))
                return false;
            utc = utc.ToUniversalTime();
            return true;
        }
    }
}
