using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Save
{
    /// <summary>
    /// Everything needed to resume a restaurant: plain serializable fields only (no engine types, no dictionaries)
    /// so the Unity layer can write it with JsonUtility and the backend can read the same shape later.
    /// Customers and in-flight orders are not saved; the dining room reopens empty.
    /// </summary>
    [Serializable]
    public sealed class SaveData
    {
        /// <summary>
        /// 2: staff energy/rest and items. 3: player profile (name, avatar, linked accounts).
        /// 4: one restaurant level (level, xp) and the star rating replace popularity and the player level.
        /// 5: facade layout/ownership and individual recipe levels.
        /// 6: demand (popularity) replaces the star rating.
        /// </summary>
        public const int CurrentVersion = 6;

        public int version = CurrentVersion;
        /// <summary>UTC time of the save, ISO 8601 round-trip format ("o").</summary>
        public string savedAtUtc;
        public string contentPack;

        public long coins;
        public long totalCustomersServed;
        /// <summary>Restaurant level and XP towards the next one (version 4+).</summary>
        public int level = 1;
        public int xp;
        /// <summary>Version 5: independent facade layout and owned exterior pieces. Missing in older saves.</summary>
        public Layout.ExteriorSave exterior;
        /// <summary>Versions 4-5: the old star rating (no longer used; demand starts fresh).</summary>
        public float rating;
        /// <summary>Version 6+: demand (popularity), which sets how often guests arrive.</summary>
        public float demand;
        /// <summary>Before version 4: the player level, which becomes the restaurant level.</summary>
        public int playerLevel = 1;

        public List<string> knownDishes = new List<string>();
        public List<RecipeLevelSave> recipeLevels = new List<RecipeLevelSave>();
        public List<string> menuActive = new List<string>();
        public List<IngredientCount> inventory = new List<IngredientCount>();
        public List<StaffSave> staff = new List<StaffSave>();
        public List<ItemCount> items = new List<ItemCount>();

        /// <summary>This device's guest id.</summary>
        public string playerId;
        /// <summary>The signed-in server account, empty for a guest.</summary>
        public string accountId;
        public string displayName;
        /// <summary>ISO 8601 UTC, empty if never renamed.</summary>
        public string lastRenameUtc;
        public List<AvatarPartSave> avatar = new List<AvatarPartSave>();
        public List<string> linkedAccounts = new List<string>();

        /// <summary>Planted garden plots (wall-clock times, so crops grow while the game is closed).</summary>
        public List<Progression.GardenPlotSave> garden = new List<Progression.GardenPlotSave>();

        /// <summary>Toilets' uses since they were last fixed (missing in older saves: all as new).</summary>
        public List<ToiletUseSave> toiletUses = new List<ToiletUseSave>();

        public int marketReadyBaskets;
        public float marketRestockTimer;

        /// <summary>Actions taken offline, waiting to be sent to the backend.</summary>
        public List<LedgerEntry> ledger = new List<LedgerEntry>();
        /// <summary>Ledger entries dropped because the ledger was full (the server should re-check the totals).</summary>
        public int ledgerDropped;
    }

    [Serializable]
    public sealed class ToiletUseSave { public int x, y, uses; }

    [Serializable]
    public sealed class RecipeLevelSave { public string dishId; public int level; }

    [Serializable]
    public sealed class IngredientCount
    {
        public string id;
        public int count;
    }

    [Serializable]
    public sealed class StaffSave
    {
        public string typeId;
        public int homeX;
        public int homeY;
        /// <summary>0-100; missing in version-1 saves, which load as full energy.</summary>
        public float energy = 100f;
        public bool resting;
        /// <summary>The backend's id for this staff member (empty before the first sync).</summary>
        public string serverId;
    }

    [Serializable]
    public sealed class AvatarPartSave
    {
        public string slot;
        public string id;
    }

    [Serializable]
    public sealed class ItemCount
    {
        public string id;
        public int count;
    }
}
