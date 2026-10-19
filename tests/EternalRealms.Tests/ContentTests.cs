using System.Linq;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;

namespace EternalRealms.Tests
{
    public class ContentTests
    {
        private readonly ContentDatabase _db = TestContent.Db;

        [Fact]
        public void AllCrossReferencesAreValid()
        {
            var errors = _db.Validate();
            Assert.True(errors.Count == 0, "Inhaltsfehler:\n" + string.Join("\n", errors));
        }

        [Fact]
        public void GameHasRequiredScope()
        {
            Assert.Equal(2, _db.Factions.Count);
            Assert.True(_db.Races.Count >= 8);
            Assert.Equal(9, _db.Classes.Count);
            Assert.Equal(27, _db.TalentTrees.Count);
            Assert.True(_db.Abilities.Count >= 300, $"nur {_db.Abilities.Count} Fähigkeitsränge");
            Assert.True(_db.Zones.Values.Count(z => !z.IsInstance) >= 20, "Weltkarte braucht mindestens 20 Regionen");
            Assert.True(_db.Zones.Values.Select(z => z.Continent).Distinct().Count(c => c != "Unterwelt") >= 2);
            Assert.Contains(_db.Zones.Values, z => z.IsInstance && z.MaxPlayers == 5 && z.Playable);
            Assert.Contains(_db.Zones.Values, z => z.IsInstance && z.MaxPlayers == 40);
            Assert.Contains(_db.Creatures.Values, c => c.WorldBoss);
        }

        [Fact]
        public void EveryClassHasThreeTreesAndOwnFactionStartZone()
        {
            foreach (var cls in _db.Classes.Values)
            {
                Assert.Equal(3, cls.TalentTrees.Count);
                Assert.Equal(3, cls.TalentTrees.Distinct().Count());
                Assert.NotEmpty(cls.StartingAbilities);
            }
            foreach (var race in _db.Races.Values)
            {
                var zone = _db.Zones[race.StartZone];
                Assert.True(zone.Playable, $"Startzone von {race.Id} ist nicht spielbar");
                Assert.Equal(race.Faction, zone.Faction);
            }
        }

        [Fact]
        public void EveryClassIsPlayableInEveryFactionItAppearsIn()
        {
            foreach (var cls in _db.Classes.Keys)
            {
                var factions = _db.Races.Values.Where(r => r.Classes.Contains(cls)).Select(r => r.Faction).Distinct().ToList();
                Assert.NotEmpty(factions);
                foreach (var f in factions)
                {
                    var startZones = _db.Races.Values.Where(r => r.Faction == f).Select(r => r.StartZone).Distinct();
                    // Für jede Klasse muss es in jeder Startzone der Fraktion einen Lehrer geben.
                    foreach (var zoneId in startZones)
                    {
                        var zone = _db.Zones[zoneId];
                        Assert.Contains(zone.Npcs, n => _db.Creatures[n.Npc].TrainerClass == cls);
                    }
                }
            }
        }

        [Fact]
        public void BotProfilesCoverEveryClassRole()
        {
            foreach (var cls in _db.Classes.Values)
                foreach (var role in cls.Roles)
                    Assert.NotNull(_db.GetBotProfile(cls.Id, role));
        }

        [Fact]
        public void AbilityRanksAreGeneratedInOrder()
        {
            var ranks = _db.AbilityRanks["mage_fireball"];
            Assert.True(ranks.Count >= 10);
            for (int i = 1; i < ranks.Count; i++)
            {
                Assert.True(ranks[i].Level > ranks[i - 1].Level);
                Assert.True(ranks[i].Effects[0].Max > ranks[i - 1].Effects[0].Max, "höhere Ränge müssen stärker sein");
            }
            Assert.Equal("mage_fireball#3", ranks[2].RankedId);
            Assert.Equal(0, _db.HighestRankForLevel("mage_frostbolt", 3));
            Assert.Equal(1, _db.HighestRankForLevel("mage_frostbolt", 4));
        }

        [Fact]
        public void StartZonesHaveFullQuestFlowsWithoutPlaceholders()
        {
            foreach (var zoneId in new[] { "zone_morgentauwald", "zone_aschensteppe" })
            {
                var quests = _db.Quests.Values.Where(q => q.Zone == zoneId).ToList();
                Assert.True(quests.Count >= 20, $"{zoneId}: nur {quests.Count} Quests");
                var types = quests.SelectMany(q => q.Objectives).Select(o => o.Type).Distinct().ToList();
                foreach (var t in new[] { ObjectiveType.Kill, ObjectiveType.Collect, ObjectiveType.Explore, ObjectiveType.Interact, ObjectiveType.Escort, ObjectiveType.Deliver, ObjectiveType.Talk })
                    Assert.Contains(t, types);
                Assert.Contains(quests, q => q.ExclusiveWith.Count > 0);
                Assert.Contains(quests, q => q.Category == QuestCategory.Class);
                Assert.Contains(quests, q => q.Category == QuestCategory.Dungeon);
                Assert.Contains(quests, q => q.RewardChoices.Count > 1);
            }
        }
    }
}
