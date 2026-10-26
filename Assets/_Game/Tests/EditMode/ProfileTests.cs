using System;
using System.Linq;
using DemoRestaurant.Logic.Content;
using DemoRestaurant.Logic.Grid;
using DemoRestaurant.Logic.Layout;
using DemoRestaurant.Logic.Profile;
using DemoRestaurant.Logic.Session;
using NUnit.Framework;

namespace DemoRestaurant.Logic.Tests
{
    public class NameRulesTests
    {
        [Test]
        public void ValidNames_InAnyScript_Pass()
        {
            Assert.AreEqual(NameCheck.Ok, NameRules.Validate("Kedai Aminah"));
            Assert.AreEqual(NameCheck.Ok, NameRules.Validate("小岛餐厅"));
            Assert.AreEqual(NameCheck.Ok, NameRules.Validate("Chef_Ali-88"));
            Assert.AreEqual(NameCheck.Ok, NameRules.Validate("Mak's Kitchen"));
        }

        [Test]
        public void Length_IsCountedInVisibleCharacters_AfterTrimming()
        {
            Assert.AreEqual(NameCheck.TooShort, NameRules.Validate("  ab  "));
            Assert.AreEqual(NameCheck.Ok, NameRules.Validate("小岛餐"), "3 Chinese characters is long enough");
            Assert.AreEqual(NameCheck.TooLong, NameRules.Validate(new string('a', 17)));
            Assert.AreEqual("Nasi Lemak Bar", NameRules.Normalize("  Nasi   Lemak  Bar "));
        }

        [Test]
        public void Symbols_AndBlockedWords_AreRejected()
        {
            Assert.AreEqual(NameCheck.InvalidCharacters, NameRules.Validate("Cool<script>"));
            Assert.AreEqual(NameCheck.InvalidCharacters, NameRules.Validate("Hi@there"));
            Assert.AreEqual(NameCheck.NotAllowed, NameRules.Validate("The Admin"));
            Assert.AreEqual(NameCheck.NotAllowed, NameRules.Validate("b a b i boy"), "spacing doesn't dodge the filter");
        }
    }

    public class PlayerProfileTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 10, 8, 9, 0, 0, DateTimeKind.Utc);

        private static AvatarCatalog Catalog() => new AvatarCatalog(new[]
        {
            new AvatarPartDef("female", AvatarSlot.Gender, "a.female"),
            new AvatarPartDef("male", AvatarSlot.Gender, "a.male"),
            new AvatarPartDef("bob", AvatarSlot.HairStyle, "a.bob"),
            new AvatarPartDef("bun", AvatarSlot.HairStyle, "a.bun"),
            new AvatarPartDef("none", AvatarSlot.Hat, "a.none"),
            new AvatarPartDef("songkok", AvatarSlot.Hat, "a.songkok", unlockLevel: 5),
            new AvatarPartDef("brown", AvatarSlot.HairColor, "a.brown", "#6B4226"),
        });

        [Test]
        public void FirstRenameIsFree_ThenCooldownApplies()
        {
            var p = new PlayerProfile(null, "Player", AvatarLook.Default(Catalog()));
            var cooldown = TimeSpan.FromHours(24);

            Assert.AreEqual(NameCheck.Ok, p.TryRename("Kedai Siti", T0, cooldown));
            Assert.AreEqual(NameCheck.Cooldown, p.TryRename("Kedai Lagi", T0.AddHours(23), cooldown));
            Assert.AreEqual(NameCheck.Unchanged, p.TryRename("  Kedai Siti ", T0.AddHours(1), cooldown));
            Assert.AreEqual(NameCheck.Ok, p.TryRename("Kedai Lagi", T0.AddHours(24), cooldown));
            Assert.AreEqual("Kedai Lagi", p.DisplayName);
        }

        [Test]
        public void PlayerId_IsGenerated_AndStable()
        {
            var p = new PlayerProfile(null, "Player", AvatarLook.Default(Catalog()));
            Assert.AreEqual(32, p.PlayerId.Length);
            Assert.IsTrue(p.IsGuest);
            p.MarkLinked(AccountProvider.GooglePlayGames);
            Assert.IsFalse(p.IsGuest);
        }

        [Test]
        public void Avatar_DefaultsToFirstPart_AndRespectsUnlockLevels()
        {
            var catalog = Catalog();
            var look = AvatarLook.Default(catalog);
            Assert.AreEqual("female", look[AvatarSlot.Gender]);
            Assert.AreEqual("none", look[AvatarSlot.Hat]);

            Assert.IsFalse(look.TrySet(catalog, AvatarSlot.Hat, "songkok", playerLevel: 4), "locked until level 5");
            Assert.IsTrue(look.TrySet(catalog, AvatarSlot.Hat, "songkok", playerLevel: 5));
            Assert.IsFalse(look.TrySet(catalog, AvatarSlot.Hat, "crown", playerLevel: 99), "unknown part");
        }

        [Test]
        public void Random_OnlyUsesUnlockedParts()
        {
            var catalog = Catalog();
            var rng = new Random(1);
            for (int i = 0; i < 50; i++)
                Assert.AreEqual("none", AvatarLook.Random(catalog, rng, playerLevel: 1)[AvatarSlot.Hat]);
        }

        [Test]
        public void Sanitize_ReplacesUnknownPartsWithDefaults()
        {
            var catalog = Catalog();
            var look = AvatarLook.Default(catalog);
            look.Restore(new[] { new System.Collections.Generic.KeyValuePair<AvatarSlot, string>(AvatarSlot.HairStyle, "mohawk") }, catalog);
            Assert.AreEqual("bob", look[AvatarSlot.HairStyle]);
            Assert.AreEqual("female", look[AvatarSlot.Gender]);
        }
    }

    public class ProfileSessionTests
    {
        private static ContentPack Pack()
        {
            var pack = new ContentPack("test", TestContent.Dishes, new FurnitureDef[0], new CustomerTypeDef[0],
                null, TestContent.AllIngredients, TestContent.Rules()).WithEconomy();
            pack.Avatar = new AvatarCatalog(new[]
            {
                new AvatarPartDef("female", AvatarSlot.Gender, "a.female"),
                new AvatarPartDef("male", AvatarSlot.Gender, "a.male"),
                new AvatarPartDef("none", AvatarSlot.Hat, "a.none"),
                new AvatarPartDef("songkok", AvatarSlot.Hat, "a.songkok", unlockLevel: 5),
            });
            return pack;
        }

        private static RestaurantSession Session(ContentPack pack) =>
            new RestaurantSession(pack, new RestaurantLayout(6, 6, new GridPos(5, 5)), 1, playerName: "NOMU WORLD");

        [Test]
        public void Rename_AndAvatar_AreLedgered_AndSurviveSaveLoad()
        {
            var pack = Pack();
            var s = Session(pack);
            Assert.AreEqual(NameCheck.Ok, s.Rename("Kedai Aminah"));
            var look = s.Profile.Avatar.Clone();
            look.TrySet(pack.Avatar, AvatarSlot.Gender, "male", 1);
            s.SaveAvatar(look);

            Assert.IsTrue(s.Ledger.Entries.Any(e => e.kind == "Renamed"));
            Assert.IsTrue(s.Ledger.Entries.Any(e => e.kind == "AvatarChanged"));

            var save = s.CaptureSave();
            var loaded = Session(pack);
            loaded.ApplySave(save);

            Assert.AreEqual("Kedai Aminah", loaded.Profile.DisplayName);
            Assert.AreEqual(s.Profile.PlayerId, loaded.Profile.PlayerId);
            Assert.AreEqual("male", loaded.Profile.Avatar[AvatarSlot.Gender]);
            Assert.AreEqual(NameCheck.Cooldown, loaded.Rename("Another Name"), "cooldown survives a reload");
        }

        [Test]
        public void SaveAvatar_RefusesPartsAboveThePlayersLevel()
        {
            var pack = Pack();
            var s = Session(pack);
            var look = s.Profile.Avatar.Clone();
            look.TrySet(pack.Avatar, AvatarSlot.Hat, "songkok", playerLevel: 99); // pretend a tampered UI picked it

            s.SaveAvatar(look);

            Assert.AreEqual("none", s.Profile.Avatar[AvatarSlot.Hat]);
        }
    }
}
