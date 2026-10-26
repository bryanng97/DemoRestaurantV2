using System;
using System.IO;
using DemoRestaurant.Game.Content;
using DemoRestaurant.Logic.Layout;
using UnityEngine;

namespace DemoRestaurant.Game.Controls
{
    /// <summary>Local renovation save, per content pack. A malformed save never replaces the starter room.</summary>
    public static class LayoutSaveStore
    {
        public static string PathFor(LoadedContentPack content) =>
            Path.Combine(Application.persistentDataPath, "restaurant-layout-" + content.Pack.Id + ".json");

        public static bool TryLoad(RestaurantLayout layout, LoadedContentPack content)
            => TryLoad(layout, content, out _);

        public static bool TryLoad(RestaurantLayout layout, LoadedContentPack content, out long? balance)
            => TryLoad(layout, content, out balance, out _);

        /// <param name="owned">Furniture owned, or null for saves from before furniture was bought (then what is placed is owned).</param>
        public static bool TryLoad(RestaurantLayout layout, LoadedContentPack content, out long? balance,
            out System.Collections.Generic.Dictionary<string, int> owned)
        {
            balance = null;
            owned = null;
            string path = PathFor(content);
            if (!File.Exists(path)) return false;
            try
            {
                var snapshot = JsonUtility.FromJson<LayoutSnapshot>(File.ReadAllText(path));
                if (snapshot == null) return Fail("unreadable file");
                LayoutMigration.UpgradeEntertainmentAxes(snapshot, content.Pack);
                if (!ValidFloors(snapshot, content)) return Fail("unknown floor tile");
                if (snapshot.hasBalance && snapshot.confirmedCoins < 0) return Fail("negative balance");
                if (content.Pack.Rules.Expansion != null && !content.Pack.Rules.Expansion.ValidSize(snapshot.width, snapshot.height))
                    return Fail($"room size {snapshot.width}x{snapshot.height} is not a land step");
                var editor = new LayoutEditor(layout, content.Pack);
                // Older save formats that no longer fit are migrated. A room that merely walls something in is kept
                // as saved (as in the reference game it just serves less), so the player's layout is never rearranged.
                if (!snapshot.TryRestore(editor.Draft, content.Pack))
                {
                    if (LayoutMigration.TryStoreIndoorStreetDecor(snapshot, layout, content.Pack, out var migrated) && migrated.TryRestore(editor.Draft, content.Pack))
                        snapshot = migrated;
                    else if (LayoutMigration.TryMoveEntrance(snapshot, layout, content.Pack, out migrated) && migrated.TryRestore(editor.Draft, content.Pack))
                        snapshot = migrated;
                    else if (LayoutMigration.TryUpdateShelfFootprints(snapshot, layout, content.Pack, out migrated) && migrated.TryRestore(editor.Draft, content.Pack))
                        snapshot = migrated;
                    else return Fail("furniture doesn't fit");
                    Debug.Log("Migrated restaurant furniture to keep the doorway, queue and shelf bases clear.");
                }
                var issue = editor.ValidateForService();
                if (issue != ServiceLayoutIssue.None && issue != ServiceLayoutIssue.BlockedAccess)
                    return Fail("the room can't open: " + issue);
                if (!snapshot.TryRestore(layout, content.Pack)) return Fail("restore failed");
                if (snapshot.hasBalance) balance = snapshot.confirmedCoins;
                owned = snapshot.OwnedCounts();
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { Debug.LogWarning("Could not load restaurant layout: " + e.Message); return false; }
        }

        public static bool TrySave(LayoutSnapshot snapshot, LoadedContentPack content)
        {
            string path = PathFor(content), temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                File.WriteAllText(temp, JsonUtility.ToJson(snapshot, true));
                if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
                else File.Move(temp, path);
                return true;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is NotSupportedException)
            { Debug.LogWarning("Could not save restaurant layout: " + e.Message); return false; }
        }

        public static bool TrySave(LayoutSnapshot snapshot, LoadedContentPack content, long balance)
        {
            snapshot.hasBalance = true; snapshot.confirmedCoins = balance;
            return TrySave(snapshot, content);
        }

        /// <summary>Logs why the saved room was not used (the starter room is kept), then returns false.</summary>
        private static bool Fail(string reason)
        {
            Debug.LogWarning("Restaurant layout save not loaded: " + reason + ". Keeping the starter room; the save file is untouched.");
            return false;
        }

        private static bool ValidFloors(LayoutSnapshot snapshot, LoadedContentPack content)
        {
            if (snapshot.floors == null) return false;
            foreach (var id in snapshot.floors)
            {
                if (id == null || id.Length == 0) continue; // JsonUtility represents null array strings as empty strings.
                bool found = false;
                foreach (var f in content.Theme.floors) if (f.id == id) found = true;
                if (!found) return false;
            }
            for (int i = 0; i < snapshot.floors.Length; i++)
                if (snapshot.floors[i] == "") snapshot.floors[i] = null;
            return true;
        }
    }
}
