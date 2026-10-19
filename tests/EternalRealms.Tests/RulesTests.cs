using EternalRealms.Shared;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Tests
{
    public class RulesTests
    {
        [Fact]
        public void XpCurveRisesAndTotalIsAClassicLevelingLength()
        {
            for (int l = 1; l < 59; l++) Assert.True(Experience.XpToNextLevel(l + 1) > Experience.XpToNextLevel(l), $"Stufe {l + 1} muss teurer sein als {l}");
            Assert.Equal(0, Experience.XpToNextLevel(60));
            long total = Experience.TotalXpToReach(60);
            Assert.InRange(total, 3_500_000, 7_000_000);
            // Nur durch Monster wären es auf Stufe 10 rund 80 Gegner – Quests liefern den Großteil, wie im klassischen Vorbild.
            int kills = Experience.XpToNextLevel(10) / Experience.KillXp(10, 10, false);
            Assert.InRange(kills, 40, 120);
        }

        [Fact]
        public void KillXpDependsOnLevelDifference()
        {
            Assert.True(Experience.KillXp(10, 12, false) > Experience.KillXp(10, 10, false));
            Assert.True(Experience.KillXp(10, 8, false) < Experience.KillXp(10, 10, false));
            Assert.Equal(0, Experience.KillXp(20, 10, false)); // graue Gegner
            Assert.Equal(2 * Experience.KillXp(10, 10, false), Experience.KillXp(10, 10, true));
            Assert.Equal(0, Experience.KillXp(60, 60, false));
        }

        [Fact]
        public void TalentPointsStartAtLevelTen()
        {
            Assert.Equal(0, Experience.TalentPointsForLevel(9));
            Assert.Equal(1, Experience.TalentPointsForLevel(10));
            Assert.Equal(51, Experience.TalentPointsForLevel(60));
        }

        [Fact]
        public void RestedXpAccumulatesFasterInInnsAndIsCapped()
        {
            int inn = Experience.RestedGain(20, 8 * 3600, true);
            int field = Experience.RestedGain(20, 8 * 3600, false);
            Assert.True(inn > field * 3);
            Assert.Equal((int)(Experience.XpToNextLevel(20) * 1.5), Experience.MaxRestedXp(20));
        }

        [Fact]
        public void ArmorAndMissChancesStayInSaneBounds()
        {
            Assert.Equal(0, CombatMath.ArmorReduction(0, 10));
            Assert.True(CombatMath.ArmorReduction(100000, 60) <= 0.75f);
            Assert.True(CombatMath.MeleeMissChance(60, 63, 0, false) > CombatMath.MeleeMissChance(60, 60, 0, false));
            Assert.True(CombatMath.SpellMissChance(60, 63, 0) > 0.15f);
            Assert.True(CombatMath.SpellMissChance(60, 60, 50) >= 0.01f);
        }

        [Fact]
        public void AttackTableIsOneRoll()
        {
            var rng = new GameRandom(7);
            var c = new AttackTable.Chances { Miss = 0.1f, Dodge = 0.1f, Parry = 0.1f, Glancing = 0.1f, Block = 0.1f, Crit = 0.1f };
            var counts = new Dictionary<CombatResult, int>();
            for (int i = 0; i < 20000; i++)
            {
                var r = AttackTable.Roll(c, rng, true);
                counts[r] = counts.GetValueOrDefault(r) + 1;
            }
            foreach (var r in new[] { CombatResult.Miss, CombatResult.Dodge, CombatResult.Parry, CombatResult.Glancing, CombatResult.Block, CombatResult.Crit })
                Assert.InRange(counts[r], 1700, 2300);
            Assert.InRange(counts[CombatResult.Hit], 7500, 8500);
        }
    }
}
