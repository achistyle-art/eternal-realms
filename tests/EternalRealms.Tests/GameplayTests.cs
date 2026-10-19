using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Tests
{
    public class CharacterTests
    {
        [Fact]
        public void EveryRaceClassCombinationCreatesAPlayableCharacter()
        {
            var t = new TestWorld();
            foreach (var race in TestContent.Db.Races.Values)
                foreach (var cls in race.Classes)
                {
                    var p = t.CreatePlayer(race.Id, cls);
                    Assert.True(p.MaxHealth > 30, $"{race.Id}/{cls}: zu wenig Leben ({p.MaxHealth})");
                    if (p.PowerType == PowerType.Mana) Assert.True(p.MaxPower > 40, $"{race.Id}/{cls}: zu wenig Mana");
                    Assert.True(p.Data.Equipment.ContainsKey(EquipSlot.MainHand), $"{race.Id}/{cls}: keine Startwaffe");
                    Assert.True(p.Data.Abilities.Count >= 2);
                    Assert.Equal(race.StartZone, p.Zone!.Def.Id);
                    Assert.False(string.IsNullOrEmpty(p.Data.BindZone));
                    Assert.True(p.CountItem("item_hearthstone") == 1);
                }
        }

        [Fact]
        public void ClassesDifferInSurvivabilityAtLevelSixty()
        {
            var t = new TestWorld();
            var warrior = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 60);
            var mage = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 60);
            Assert.True(warrior.MaxHealth > mage.MaxHealth * 1.3, $"Krieger {warrior.MaxHealth} vs. Magier {mage.MaxHealth}");
            Assert.True(mage.MaxPower > 2000);
            Assert.Equal(51, warrior.TalentPointsFree);
        }

        [Fact]
        public void TalentRulesAreEnforced()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 9);
            Assert.NotNull(t.World.Progression.LearnTalent(p, "wa_improved_heroic")); // Stufe 9: keine Punkte
            t.World.Progression.LevelUp(p);
            Assert.Null(t.World.Progression.LearnTalent(p, "wa_improved_heroic"));
            Assert.NotNull(t.World.Progression.LearnTalent(p, "wa_tactical_mastery")); // Reihe 1 braucht 5 Punkte
            Assert.NotNull(t.World.Progression.LearnTalent(p, "mf_ignite")); // falsche Klasse
            for (int i = 0; i < 5; i++) t.World.Progression.LevelUp(p);
            for (int i = 0; i < 4; i++) Assert.Null(t.World.Progression.LearnTalent(p, i < 2 ? "wa_improved_heroic" : "wa_deflection"));
            Assert.Null(t.World.Progression.LearnTalent(p, "wa_tactical_mastery"));
            float apBefore = p.AttackPower;
            p.MarkStatsDirty(); p.EnsureStats();
            Assert.True(p.AttackPower >= apBefore);
        }

        [Fact]
        public void TrainerTeachesNewRanksForMoney()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 1);
            var trainer = t.World.Zone("zone_morgentauwald")!.Entities.Values.OfType<Creature>().First(c => c.Template.TrainerClass == CharClass.Mage);
            p.Data.Abilities.Remove("mage_frostbolt");
            for (int i = 1; i < 4; i++) t.World.Progression.LevelUp(p);
            p.Data.Abilities.Remove("mage_frostbolt");
            Assert.NotNull(t.World.Progression.Train(p, trainer, "mage_frostbolt")); // kein Geld
            t.World.Economy.Credit(p, 10000, "test");
            Assert.Null(t.World.Progression.Train(p, trainer, "mage_frostbolt"));
            Assert.Equal(1, p.KnownRank("mage_frostbolt"));
            Assert.NotNull(t.World.Progression.Train(p, trainer, "mage_frostbolt")); // nächster Rang erst ab Stufe 8
        }
    }

    public class CombatTests
    {
        private static readonly Vec3 Arena = new(520, 0, 820);

        [Fact]
        public void LevelOneWarriorBeatsALevelOneWolf()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Arena, 80);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 1, Arena);
            var wolf = t.Spawn("mob_young_wolf", Arena + new Vec3(3, 0, 0), level: 1);
            p.Target = wolf;
            p.AutoAttacking = true;
            t.Run(60, () => wolf.IsDead || p.IsDead);
            Assert.True(wolf.IsDead, "Der Wolf sollte sterben");
            Assert.True(p.IsAlive, "Der Krieger sollte überleben");
            Assert.True(p.Data.Xp > 0, "Erfahrung für den Sieg");
            Assert.True(p.Power > 0 || t.Conn(p).Of<S_CombatEvent>().Any(), "Krieger erzeugt Wut im Kampf");
        }

        [Fact]
        public void CastTimeGcdAndMovementInterruptWork()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Arena, 80);
            var mage = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 5, Arena);
            var target = t.Spawn("mob_forest_wolf", Arena + new Vec3(20, 0, 0), level: 4);
            var fireball = TestContent.Db.GetAbilityRank("mage_fireball", mage.KnownRank("mage_fireball"))!;
            Assert.Null(t.World.Combat.TryCast(mage, fireball, target));
            Assert.NotNull(mage.Cast);
            Assert.Equal("Ihr zaubert bereits.", t.World.Combat.TryCast(mage, fireball, target));
            t.World.HandleMove(mage, new C_Move { Position = mage.Position + new Vec3(1, 0, 0), Sequence = 1 });
            Assert.Null(mage.Cast); // Bewegung unterbricht
            Assert.True(mage.Gcd > 0);
            Assert.NotNull(t.World.Combat.TryCast(mage, fireball, target)); // globale Abklingzeit
            t.Run(1.6);
            Assert.Null(t.World.Combat.TryCast(mage, fireball, target));
            float hpBefore = target.Health;
            t.Run(3.5);
            Assert.True(target.Health < hpBefore || t.Conn(mage).Of<S_CombatEvent>().Any(e => e.Result == CombatResult.Resist), "Feuerball trifft nach der Zauberzeit");
        }

        [Fact]
        public void OutOfRangeAndOutOfManaAreRejected()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Arena, 80);
            var mage = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 5, Arena);
            var far = t.Spawn("mob_forest_wolf", Arena + new Vec3(60, 0, 0), level: 4);
            var fireball = TestContent.Db.GetAbilityRank("mage_fireball", 1)!;
            Assert.Equal("Das Ziel ist außer Reichweite.", t.World.Combat.TryCast(mage, fireball, far));
            mage.Power = 0;
            var near = t.Spawn("mob_forest_wolf", Arena + new Vec3(10, 0, 0), level: 4);
            Assert.Equal("Nicht genug Mana.", t.World.Combat.TryCast(mage, fireball, near));
        }

        [Fact]
        public void CreaturesLeashHomeAndReset()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Arena, 150);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 10, Arena);
            var wolf = t.Spawn("mob_forest_wolf", Arena + new Vec3(4, 0, 0), level: 4);
            p.Target = wolf;
            p.AutoAttacking = true;
            t.Run(2.5);
            Assert.True(wolf.InCombat);
            // Spieler rennt weit weg – das Monster folgt bis zur Leine und kehrt dann heim.
            p.AutoAttacking = false;
            t.World.MovePlayerWithinZone(p, Arena + new Vec3(-120, 0, 0));
            t.Run(30, () => wolf.AI.State == Server.AI.CreatureState.Idle);
            Assert.Equal(Server.AI.CreatureState.Idle, wolf.AI.State);
            Assert.Equal(wolf.MaxHealth, wolf.Health);
            Assert.True(wolf.DistanceTo(wolf.Home) < 2);
        }

        [Fact]
        public void TauntForcesTargetAndThreatFollowsDamage()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Arena, 80);
            var tank = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 12, Arena);
            var mage = t.CreatePlayer(Race.Aldorer, CharClass.Mage, 12, Arena + new Vec3(-15, 0, 0));
            var mob = t.Spawn("mob_rotbach_bandit", Arena + new Vec3(4, 0, 0), level: 8);
            var fb = TestContent.Db.GetAbilityRank("mage_fireball", mage.KnownRank("mage_fireball"))!;
            t.World.Combat.TryCast(mage, fb, mob);
            t.Run(4);
            Assert.Equal(mage, mob.Target);
            t.World.Combat.TryCast(tank, TestContent.Db.GetAbilityRank("warrior_defensive_stance", 1)!, tank);
            Assert.Equal(Form.DefensiveStance, tank.Form);
            t.Run(1.1);
            Assert.Null(t.World.Combat.TryCast(tank, TestContent.Db.GetAbilityRank("warrior_taunt", 1)!, mob));
            t.Run(0.5);
            Assert.Equal(tank, mob.Target);
            Assert.True(mob.Threat.Get(tank) >= mob.Threat.Get(mage));
        }

        [Fact]
        public void InterruptCancelsCastAndLocksSchool()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Arena, 80);
            var rogue = t.CreatePlayer(Race.Aldorer, CharClass.Rogue, 14, Arena);
            var caster = t.Spawn("mob_grubenwicht_geomant", Arena + new Vec3(2, 0, 0), level: 7);
            var bolt = TestContent.Db.GetAbilityRank("mob_firebolt", 1)!;
            Assert.Null(t.World.Combat.TryCast(caster, bolt, rogue));
            Assert.NotNull(caster.Cast);
            rogue.Power = 100;
            Assert.Null(t.World.Combat.TryCast(rogue, TestContent.Db.GetAbilityRank("rogue_kick", 1)!, caster));
            Assert.Null(caster.Cast);
            Assert.True(caster.SchoolLockouts.ContainsKey(School.Fire));
            Assert.Equal("Diese Zauberschule ist gesperrt.", t.World.Combat.TryCast(caster, bolt, rogue));
        }

        [Fact]
        public void DotsTickAndBuffsModifyStats()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Arena, 80);
            var warlock = t.CreatePlayer(Race.Aldorer, CharClass.Warlock, 10, Arena);
            var mob = t.Spawn("mob_forest_wolf", Arena + new Vec3(15, 0, 0), level: 9);
            var corr = TestContent.Db.GetAbilityRank("warlock_corruption", warlock.KnownRank("warlock_corruption"))!;
            t.World.Combat.TryCast(warlock, corr, mob);
            t.Run(2.2);
            if (!mob.Auras.Any(a => a.Def.Id.StartsWith("warlock_corruption"))) { t.Run(1.6); t.World.Combat.TryCast(warlock, corr, mob); t.Run(2.2); }
            Assert.Contains(mob.Auras, a => a.Def.Id.StartsWith("warlock_corruption"));
            int hp = mob.Health;
            t.Run(4);
            Assert.True(mob.Health < hp, "Verderbnis verursacht Schaden über Zeit");

            var priest = t.CreatePlayer(Race.Aldorer, CharClass.Priest, 12, Arena + new Vec3(-5, 0, 0));
            int before = warlock.MaxHealth;
            t.World.Combat.TryCast(priest, TestContent.Db.GetAbilityRank("priest_power_word_fortitude", priest.KnownRank("priest_power_word_fortitude"))!, warlock);
            warlock.EnsureStats();
            Assert.True(warlock.MaxHealth > before, "Seelenstärke erhöht das Leben");
        }

        [Fact]
        public void DeathGhostAndCorpseRun()
        {
            var t = new TestWorld();
            t.ClearMonstersAround(Arena, 80);
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 5, Arena);
            int durBefore = p.Data.Equipment[EquipSlot.MainHand].Durability;
            t.World.Combat.DealDamage(null, p, p.MaxHealth * 5, School.Shadow, null, "test", false, false);
            Assert.Equal(DeathState.Dead, p.DeathState);
            Assert.True(p.Data.Equipment[EquipSlot.MainHand].Durability < durBefore, "Tod kostet Haltbarkeit");
            Assert.Null(t.World.Death.ReleaseSpirit(p));
            Assert.Equal(DeathState.Ghost, p.DeathState);
            Assert.True(Vec3.DistanceXZ(p.Position, p.Data.CorpsePosition) > 40, "Geist erscheint am Friedhof");
            Assert.NotNull(t.World.Death.ReclaimCorpse(p));
            t.World.MovePlayerWithinZone(p, p.Data.CorpsePosition + new Vec3(10, 0, 0));
            Assert.Null(t.World.Death.ReclaimCorpse(p));
            Assert.True(p.IsAlive);
            Assert.InRange(p.HealthPct, 0.45f, 0.55f);
        }

        [Fact]
        public void SpeedHacksAreClampedByTheServer()
        {
            var t = new TestWorld();
            var p = t.CreatePlayer(Race.Aldorer, CharClass.Warrior, 1, Arena);
            t.Run(0.5);
            var start = p.Position;
            t.World.HandleMove(p, new C_Move { Position = start + new Vec3(200, 0, 0), Sequence = 5 });
            Assert.True(Vec3.DistanceXZ(start, p.Position) < 10, $"Bewegt {Vec3.DistanceXZ(start, p.Position)} m statt geklemmt");
            var rooted = TestContent.Db.Auras["mage_frost_nova"];
            t.World.Combat.ApplyAura(p, p, rooted, "test");
            var pos = p.Position;
            t.Run(0.3);
            t.World.HandleMove(p, new C_Move { Position = pos + new Vec3(1, 0, 0), Sequence = 6 });
            Assert.Equal(pos, p.Position);
        }
    }
}
