using System;
using System.Collections.Generic;
using System.Text;
using DemoRestaurant.Logic.Profile;

namespace DemoRestaurant.Logic.Online
{
    /// <summary>Avatar slot names on the server (<c>catalog.wardrobe</c>): the client's slot names in snake_case.</summary>
    public static class WardrobeIds
    {
        private static readonly Dictionary<string, AvatarSlot> BySlotId = new Dictionary<string, AvatarSlot>();

        static WardrobeIds()
        {
            foreach (var slot in AvatarCatalog.AllSlots) BySlotId[SlotId(slot)] = slot;
        }

        /// <summary><c>HairStyle</c> → <c>hair_style</c>.</summary>
        public static string SlotId(AvatarSlot slot)
        {
            string name = slot.ToString();
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        public static bool TryParseSlot(string slotId, out AvatarSlot slot) =>
            BySlotId.TryGetValue(slotId ?? "", out slot);

        /// <summary>A look as the server stores it: <c>{ slot_id: partId }</c>.</summary>
        public static JsonValue Look(AvatarLook look)
        {
            var body = JsonValue.Object();
            if (look == null) return body;
            foreach (var slot in AvatarCatalog.AllSlots)
                if (look[slot] != null) body.Set(SlotId(slot), look[slot]);
            return body;
        }
    }
}
