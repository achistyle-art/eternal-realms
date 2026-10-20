using EternalRealms.Server.Systems;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    /// <summary>Kapitel 17: Berufe lernen, sammeln, herstellen, verzaubern, Fortschritt.</summary>
    public class ProfessionTests
    {
        private static Creature Npc(TestWorld t, string id) => t.World.Zone("zone_morgentauwald")!.Entities.Values.OfType<Creature>().First(c => c.Template.Id == id);

        private static Player Crafter(TestWorld t, string trainerId, int level = 10)
        {
            var trainer = Npc(t, trainerId);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, level, trainer.Position + new Vec3(2, 0, 0));
            t.World.Economy.Credit(p, 1_000_000, "test");
            return p;
        }

        [Fact]
        public void ContentHasAllProfessionsWithFourRanks()
        {
            var db = TestContent.Db;
            Assert.Equal(12, db.Professions.Count);
            Assert.Equal(9, db.Professions.Values.Count(p => p.IsPrimary));
            Assert.All(db.Professions.Values, p => Assert.Equal(new[] { "Lehrling", "Geselle", "Experte", "Meister" }, p.Ranks.Select(r => r.Name)));
            foreach (var prof in db.Professions.Values.Where(p => (p.Kind != ProfessionKind.Gathering && p.Id != "fishing") || p.Id == "mining"))
                Assert.Contains(db.Recipes.Values, r => r.Profession == prof.Id);
            Assert.Empty(db.Validate());
        }

        [Fact]
        public void AtMostTwoPrimaryProfessionsAndRanksNeedSkillAndLevel()
        {
            var t = new TestWorld();
            var p = Crafter(t, "npc_mt_prof_mining");
            Assert.Null(t.World.Professions.Learn(p, Npc(t, "npc_mt_prof_mining").Id));
            t.World.MovePlayerWithinZone(p, Npc(t, "npc_mt_prof_herbalism").Position + new Vec3(1, 0, 0));
            Assert.Null(t.World.Professions.Learn(p, Npc(t, "npc_mt_prof_herbalism").Id));
            t.World.MovePlayerWithinZone(p, Npc(t, "npc_mt_prof_alchemy").Position + new Vec3(1, 0, 0));
            Assert.NotNull(t.World.Professions.Learn(p, Npc(t, "npc_mt_prof_alchemy").Id)); // dritter Hauptberuf
            t.World.MovePlayerWithinZone(p, Npc(t, "npc_mt_prof_cooking").Position + new Vec3(1, 0, 0));
            Assert.Null(t.World.Professions.Learn(p, Npc(t, "npc_mt_prof_cooking").Id)); // Nebenberuf geht

            t.World.MovePlayerWithinZone(p, Npc(t, "npc_mt_prof_mining").Position + new Vec3(1, 0, 0));
            var mining = Npc(t, "npc_mt_prof_mining");
            Assert.NotNull(t.World.Professions.TrainRank(p, mining.Id)); // Fertigkeit zu niedrig
            p.Data.Professions["mining"].Skill = 50;
            Assert.Null(t.World.Professions.TrainRank(p, mining.Id));
            Assert.Equal(150, p.Data.Professions["mining"].MaxSkill);
            p.Data.Professions["mining"].Skill = 140;
            Assert.NotNull(t.World.Professions.TrainRank(p, mining.Id)); // Experte erst ab Stufe 20
        }

        [Fact]
        public void GatheringNeedsToolAndSkillAndRaisesSkill()
        {
            var t = new TestWorld(seed: 5);
            var p = Crafter(t, "npc_mt_prof_mining");
            t.World.Professions.Learn(p, Npc(t, "npc_mt_prof_mining").Id);
            var zone = p.Zone!;
            var vein = zone.Objects.First(o => o.Def.Id == "obj_prof_vein0" && o.Available);
            t.ClearMonstersAround(vein.Position, 80); // ein Biss würde das Abbauen unterbrechen
            t.World.MovePlayerWithinZone(p, vein.Position + new Vec3(1, 0, 0));
            Assert.Contains("Bergbauhacke", t.World.Interactions.Interact(p, vein.Id)); // ohne Werkzeug
            p.AddItem("item_tool_mining_pick", 1);
            var deeper = zone.Objects.First(o => o.Def.Id == "obj_prof_vein1");
            t.World.MovePlayerWithinZone(p, deeper.Position + new Vec3(1, 0, 0));
            Assert.Contains("75", t.World.Interactions.Interact(p, deeper.Id)); // Zinn erst ab 75
            t.World.MovePlayerWithinZone(p, vein.Position + new Vec3(1, 0, 0));
            Assert.Null(t.World.Interactions.Interact(p, vein.Id));
            t.Run(3);
            Assert.True(p.CountItem("item_ore0") >= 1);
            Assert.Equal(2, p.Data.Professions["mining"].Skill); // orange: immer +1
            Assert.False(vein.Available);
        }

        [Fact]
        public void SmeltingAndSmithingNeedStationsReagentsAndProduceItems()
        {
            var t = new TestWorld();
            var p = Crafter(t, "npc_mt_prof_blacksmithing");
            t.World.Professions.Learn(p, Npc(t, "npc_mt_prof_blacksmithing").Id);
            t.World.MovePlayerWithinZone(p, Npc(t, "npc_mt_prof_mining").Position + new Vec3(1, 0, 0));
            t.World.Professions.Learn(p, Npc(t, "npc_mt_prof_mining").Id);
            p.AddItem("item_ore0", 10);
            p.AddItem("item_stone0", 2);
            Assert.Contains("Schmelzofen", t.World.Professions.Craft(p, "smelt_copper", 1));
            var forge = p.Zone!.Objects.First(o => o.Def.Station == "forge");
            t.World.MovePlayerWithinZone(p, forge.Position + new Vec3(1, 0, 0));
            Assert.Null(t.World.Professions.Craft(p, "smelt_copper", 6));
            t.Run(14);
            Assert.Equal(6, p.CountItem("item_bar0"));
            Assert.Equal(4, p.CountItem("item_ore0"));
            var anvil = p.Zone.Objects.First(o => o.Def.Station == "anvil");
            t.World.MovePlayerWithinZone(p, anvil.Position + new Vec3(1, 0, 0));
            Assert.Contains("Schmiedehammer", t.World.Professions.Craft(p, "bs_0_0", 1));
            p.AddItem("item_tool_blacksmith_hammer", 1);
            Assert.Null(t.World.Professions.Craft(p, "bs_0_0", 1));
            t.World.MovePlayerWithinZone(p, p.Position + new Vec3(0.2f, 0, 0)); // kleine Bewegung bricht nicht ab? – Bewegung bricht ab
            t.Run(5);
            int made = p.CountItem("item_bs_0_0");
            if (made == 0) // Bewegung hat abgebrochen: erneut ohne Bewegung
            {
                Assert.Null(t.World.Professions.Craft(p, "bs_0_0", 1));
                t.Run(5);
            }
            Assert.Equal(1, p.CountItem("item_bs_0_0"));
            Assert.Equal(2, p.CountItem("item_bar0"));
        }

        [Fact]
        public void EnchantingImprovesEquippedGearAndDisenchantYieldsDust()
        {
            var t = new TestWorld(seed: 9);
            var p = Crafter(t, "npc_mt_prof_enchanting");
            t.World.Professions.Learn(p, Npc(t, "npc_mt_prof_enchanting").Id);
            var green = TestContent.Db.Items.Values.First(i => i.Quality == ItemQuality.Uncommon && i.IsEquippable && i.ItemLevel <= 15);
            p.AddItem(green.Id, 1);
            Assert.Null(t.World.Professions.Disenchant(p, p.Data.Bag.FindIndex(s => s?.ItemId == green.Id)));
            t.Run(4);
            Assert.Equal(0, p.CountItem(green.Id));
            Assert.True(p.CountItem("item_dust0") >= 1);

            p.AddItem("item_bar0", 1);
            p.AddItem("item_dust0", 5);
            Assert.Null(t.World.Professions.Craft(p, "ench_rod", 1));
            t.Run(4);
            Assert.Equal(1, p.CountItem("item_enchanting_rod"));
            // Armschienen anlegen und verzaubern
            var bracer = TestContent.Db.Items.Values.First(i => i.InventoryType == InventoryType.Wrist && i.RequiredLevel <= 10 && i.Classes.Count == 0);
            p.AddItem(bracer.Id, 1);
            Assert.Null(p.Equip(p.Data.Bag.FindIndex(s => s?.ItemId == bracer.Id)));
            p.EnsureStats();
            float sta = p.Stats.Stamina;
            Assert.Null(t.World.Professions.Craft(p, "ench_wrist_0", 1));
            t.Run(5);
            Assert.Equal("ench_wrist_0", p.Data.Equipment[EquipSlot.Wrist].Enchant);
            p.EnsureStats();
            Assert.Equal(sta + 1, p.Stats.Stamina, 1);
        }

        [Fact]
        public void SkinningAfterLootingAndClothFromHumanoids()
        {
            var t = new TestWorld(seed: 13);
            var field = new Vec3(520, 0, 820);
            t.ClearMonstersAround(field, 100);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10, field);
            p.Data.Professions["skinning"] = new() { Skill = 1, MaxSkill = 75 };
            p.AddItem("item_tool_skinning_knife", 1);
            var wolf = t.Spawn("mob_forest_wolf", field + new Vec3(2, 0, 0), level: 5);
            t.World.Combat.Kill(p, wolf);
            if (wolf.Lootable) Assert.NotNull(t.World.Professions.Skin(p, wolf.Id)); // erst plündern
            if (wolf.Lootable) t.World.Loot.TryLoot(p, wolf);
            Assert.Null(t.World.Professions.Skin(p, wolf.Id));
            t.Run(3);
            Assert.True(p.CountItem("item_leather0") >= 1);
            Assert.NotNull(t.World.Professions.Skin(p, wolf.Id)); // nur einmal
            Assert.Equal(1, ProfessionSystem.SkinningRequired(10));
            Assert.Equal(100, ProfessionSystem.SkinningRequired(20));

            int cloth = 0;
            for (int i = 0; i < 30; i++)
            {
                var bandit = t.Spawn("mob_rotbach_bandit", field + new Vec3(3, 0, i), level: 8);
                t.World.Combat.Kill(p, bandit);
                cloth += bandit.Loot.Where(l => l.ItemId == "item_cloth0").Sum(l => l.Count);
                bandit.Zone!.Remove(bandit);
            }
            Assert.True(cloth > 0, "Humanoide lassen Leinenstoff fallen");
        }

        [Fact]
        public void SkillUpChanceFollowsDifficultyColors()
        {
            Assert.Equal(1.0, ProfessionSystem.SkillUpChance(10, 10, 35, 50, 65));
            Assert.Equal(0.75, ProfessionSystem.SkillUpChance(40, 10, 35, 50, 65));
            Assert.Equal(0.25, ProfessionSystem.SkillUpChance(55, 10, 35, 50, 65));
            Assert.Equal(0.0, ProfessionSystem.SkillUpChance(70, 10, 35, 50, 65));
        }
    }
}
