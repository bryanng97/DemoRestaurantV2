using System;
using System.Collections.Generic;

namespace DemoRestaurant.Logic.Profile
{
    /// <summary>The parts of the player's avatar. Each slot holds exactly one part id.</summary>
    public enum AvatarSlot
    {
        Gender,
        SkinTone,
        Eyes,
        Eyebrows,
        HairStyle,
        HairColor,
        Hat,
        Shirt,
        ShirtColor,
        Pants,
        PantsColor,
    }

    /// <summary>One choice for a slot, e.g. hair style "bob" or shirt colour "teal".</summary>
    public sealed class AvatarPartDef
    {
        public string Id { get; }
        public AvatarSlot Slot { get; }
        public string NameKey { get; }
        /// <summary>Hex colour for colour slots (skin, hair, shirt, pants), otherwise empty.</summary>
        public string Color { get; }
        /// <summary>Player level needed to pick it (1 = always available).</summary>
        public int UnlockLevel { get; }
        /// <summary>Art key of a designer-made layer, if any; the procedural painter draws it otherwise.</summary>
        public string Sprite { get; }
        public int Price { get; }
        public string OwnershipKey => Slot + ":" + Id;

        public AvatarPartDef(string id, AvatarSlot slot, string nameKey, string color = "", int unlockLevel = 1, string sprite = "", int price = 0)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Avatar part id must not be empty.", nameof(id));
            Id = id;
            Slot = slot;
            NameKey = nameKey ?? throw new ArgumentNullException(nameof(nameKey));
            Color = color ?? "";
            UnlockLevel = Math.Max(1, unlockLevel);
            Sprite = sprite ?? "";
            Price = Math.Max(0, price);
        }
    }

    /// <summary>All avatar choices for a content pack. The first part listed for a slot is its default.</summary>
    public sealed class AvatarCatalog
    {
        private readonly Dictionary<AvatarSlot, List<AvatarPartDef>> _bySlot = new Dictionary<AvatarSlot, List<AvatarPartDef>>();
        private readonly Dictionary<(AvatarSlot, string), AvatarPartDef> _byId = new Dictionary<(AvatarSlot, string), AvatarPartDef>();

        public static readonly AvatarSlot[] AllSlots = (AvatarSlot[])Enum.GetValues(typeof(AvatarSlot));

        public AvatarCatalog(IEnumerable<AvatarPartDef> parts)
        {
            foreach (var slot in AllSlots)
                _bySlot[slot] = new List<AvatarPartDef>();
            foreach (var p in parts ?? Array.Empty<AvatarPartDef>())
            {
                if (_byId.ContainsKey((p.Slot, p.Id)))
                    throw new Content.ContentException($"Duplicate avatar part '{p.Id}' in slot {p.Slot}.");
                _byId[(p.Slot, p.Id)] = p;
                _bySlot[p.Slot].Add(p);
            }
        }

        public IReadOnlyList<AvatarPartDef> PartsFor(AvatarSlot slot) => _bySlot[slot];

        public AvatarPartDef Find(AvatarSlot slot, string id) =>
            id != null && _byId.TryGetValue((slot, id), out var p) ? p : null;

        public AvatarPartDef Default(AvatarSlot slot) => _bySlot[slot].Count > 0 ? _bySlot[slot][0] : null;

        public bool IsEmpty
        {
            get
            {
                foreach (var list in _bySlot.Values)
                    if (list.Count > 0) return false;
                return true;
            }
        }
    }
}
