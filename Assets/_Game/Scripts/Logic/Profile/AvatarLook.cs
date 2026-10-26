using System;
using System.Collections.Generic;
using System.Text;

namespace DemoRestaurant.Logic.Profile
{
    /// <summary>
    /// The player's chosen avatar: one part id per slot. Always kept valid against the catalog: unknown ids
    /// (e.g. from an older save) fall back to the slot's default.
    /// </summary>
    public sealed class AvatarLook
    {
        private readonly Dictionary<AvatarSlot, string> _parts = new Dictionary<AvatarSlot, string>();

        public string this[AvatarSlot slot] => _parts.TryGetValue(slot, out var id) ? id : null;

        public static AvatarLook Default(AvatarCatalog catalog)
        {
            var look = new AvatarLook();
            foreach (var slot in AvatarCatalog.AllSlots)
            {
                var d = catalog.Default(slot);
                if (d != null) look._parts[slot] = d.Id;
            }
            return look;
        }

        public AvatarLook Clone()
        {
            var copy = new AvatarLook();
            foreach (var pair in _parts) copy._parts[pair.Key] = pair.Value;
            return copy;
        }

        /// <summary>Picks a part if it exists and is unlocked at <paramref name="playerLevel"/>.</summary>
        public bool TrySet(AvatarCatalog catalog, AvatarSlot slot, string id, int playerLevel)
        {
            var part = catalog.Find(slot, id);
            if (part == null || part.UnlockLevel > playerLevel)
                return false;
            _parts[slot] = id;
            return true;
        }

        /// <summary>Replaces unknown or missing parts with defaults (loading an old save, content changed).</summary>
        public void Sanitize(AvatarCatalog catalog)
        {
            foreach (var slot in AvatarCatalog.AllSlots)
            {
                if (catalog.Find(slot, this[slot]) != null) continue;
                var d = catalog.Default(slot);
                if (d != null) _parts[slot] = d.Id;
                else _parts.Remove(slot);
            }
        }

        /// <summary>Random look using only parts unlocked at <paramref name="playerLevel"/>.</summary>
        public static AvatarLook Random(AvatarCatalog catalog, Random random, int playerLevel)
        {
            var look = Default(catalog);
            var choices = new List<AvatarPartDef>();
            foreach (var slot in AvatarCatalog.AllSlots)
            {
                choices.Clear();
                foreach (var p in catalog.PartsFor(slot))
                    if (p.UnlockLevel <= playerLevel) choices.Add(p);
                if (choices.Count > 0)
                    look._parts[slot] = choices[random.Next(choices.Count)].Id;
            }
            return look;
        }

        /// <summary>Stable text key (cache key for rendered avatars, change detection).</summary>
        public string Key
        {
            get
            {
                var sb = new StringBuilder();
                foreach (var slot in AvatarCatalog.AllSlots)
                    sb.Append(this[slot]).Append('|');
                return sb.ToString();
            }
        }

        public IEnumerable<KeyValuePair<AvatarSlot, string>> Parts => _parts;

        public void Restore(IEnumerable<KeyValuePair<AvatarSlot, string>> parts, AvatarCatalog catalog)
        {
            _parts.Clear();
            foreach (var pair in parts) _parts[pair.Key] = pair.Value;
            Sanitize(catalog);
        }
    }
}
