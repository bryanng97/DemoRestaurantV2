using System;
using DemoRestaurant.Logic.Content;

namespace DemoRestaurant.Logic.Progression
{
    /// <summary>
    /// The restaurant level (economy sheet: one level replaces the old popularity and player levels). XP comes
    /// from serving dishes and learning or upgrading recipes. XP to the next level = base + per level x level.
    /// The level never goes down and stops at the playable cap (Phase 1: 10). Each level-up pays its coin reward.
    /// Level unlocks dishes, furniture, menu slots, staff slots and garden plots (the Levels tab).
    /// </summary>
    public sealed class RestaurantProgress
    {
        private readonly EconomyTable _economy;

        public int Level { get; private set; }
        public int Xp { get; private set; }
        public int MaxLevel => IgnorePlayableCap ? _economy.MaxTableLevel : _economy.MaxPlayableLevel;
        /// <summary>Internal testing only: levels may go past the playable cap, up to the end of the Levels tab.</summary>
        public bool IgnorePlayableCap { get; set; }
        public bool IsMaxLevel => Level >= MaxLevel;
        /// <summary>XP needed for the next level; 0 at the cap.</summary>
        public int XpToNext => IsMaxLevel ? 0 : EconomyFormulas.XpToNext(_economy.settings, Level);
        /// <summary>0..1 fill of the XP bar (full at the cap).</summary>
        public float Progress => IsMaxLevel ? 1f : (float)Xp / XpToNext;
        /// <summary>The Levels tab row for the current level (menu, staff and garden limits).</summary>
        public EconomyLevel Row => _economy.Level(Level);

        public event Action<int> XpGained;
        /// <summary>New level and the coins it rewards.</summary>
        public event Action<int, int> LevelUp;

        public RestaurantProgress(EconomyTable economy, int level = 1, int xp = 0)
        {
            _economy = economy ?? throw new ArgumentNullException(nameof(economy));
            Restore(level, xp);
        }

        /// <summary>Restores a saved state (no events). Saves above the cap come back at the cap.</summary>
        public void Restore(int level, int xp)
        {
            Level = Math.Max(1, Math.Min(level, MaxLevel));
            Xp = IsMaxLevel ? 0 : Math.Max(0, Math.Min(xp, XpToNext - 1));
        }

        public int XpForServe(int dishLevel) => EconomyFormulas.XpPerServe(_economy.settings, dishLevel);
        public int XpForRecipeLevel(int newLevel) => EconomyFormulas.XpForUpgrade(_economy.settings, newLevel);

        /// <summary>Coins paid for reaching <paramref name="level"/> (Levels tab, or the flat level-up reward).</summary>
        public int CoinsForReaching(int level)
        {
            int fromTable = _economy.Level(level).coinsReward;
            return fromTable > 0 ? fromTable : _economy.settings.levelUpCoins;
        }

        public void Add(int xp)
        {
            if (xp <= 0 || IsMaxLevel)
                return;
            Xp += xp;
            XpGained?.Invoke(xp);
            while (!IsMaxLevel && Xp >= XpToNext)
            {
                Xp -= XpToNext;
                Level++;
                LevelUp?.Invoke(Level, CoinsForReaching(Level));
            }
            if (IsMaxLevel)
                Xp = 0;
        }
    }

}
