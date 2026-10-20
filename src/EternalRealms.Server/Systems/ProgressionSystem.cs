using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.Systems
{
    /// <summary>Erfahrung, Stufenaufstieg, Ruhebonus, Lehrer, Talente und Ruf.</summary>
    public sealed class ProgressionSystem
    {
        private readonly GameWorld _w;
        public ProgressionSystem(GameWorld world) { _w = world; }

        public const float GroupXpRange = 100f;

        /// <summary>Verteilt Erfahrung für einen besiegten Gegner an den Tapper bzw. dessen Gruppe in Reichweite.</summary>
        public void AwardKillXp(Player tapper, Creature c)
        {
            var members = EligibleMembers(tapper, c.Position);
            if (members.Count == 0) return;
            int maxLevel = members.Max(m => m.Level);
            int sumLevels = members.Sum(m => m.Level);
            int baseXp = Experience.KillXp(maxLevel, c.Level, c.Template.Elite);
            if (baseXp <= 0) return;
            double total = members.Count == 1 ? baseXp : baseXp * Experience.GroupBonus(members.Count);
            foreach (var m in members)
            {
                // Jedes Mitglied erhält höchstens, was es allein für diesen Gegner bekommen hätte.
                int own = Experience.KillXp(m.Level, c.Level, c.Template.Elite);
                int share = members.Count == 1 ? own : (int)Math.Min(own, total * m.Level / sumLevels);
                if (share > 0) GiveXp(m, share, "kill", c.Id, useRested: true);
            }
        }

        public List<Player> EligibleMembers(Player tapper, Vec3 at)
        {
            if (tapper.Group == null) return tapper.IsAlive ? new List<Player> { tapper } : new List<Player>();
            return tapper.Group.Members.Where(m => m.Zone == tapper.Zone && m.IsAlive && Vec3.DistanceXZ(m.Position, at) <= GroupXpRange).ToList();
        }

        public void GiveXp(Player p, int amount, string reason, int sourceId = 0, bool useRested = false)
        {
            if (p.Level >= Experience.MaxLevel || amount <= 0) return;
            int rested = 0;
            if (useRested && p.Data.RestedXp > 0)
            {
                rested = Math.Min(amount, p.Data.RestedXp);
                p.Data.RestedXp -= rested;
            }
            p.Data.Xp += amount + rested;
            p.Send(new S_XpGain { Amount = amount + rested, RestedBonus = rested, SourceId = sourceId, Reason = reason });
            while (p.Level < Experience.MaxLevel && p.Data.Xp >= Experience.XpToNextLevel(p.Level))
            {
                p.Data.Xp -= Experience.XpToNextLevel(p.Level);
                LevelUp(p);
            }
            if (p.Level >= Experience.MaxLevel) p.Data.Xp = 0;
            p.Send(p.BuildPlayerState());
        }

        public void LevelUp(Player p)
        {
            int oldHp = p.MaxHealth, oldPower = p.MaxPower;
            p.Level++;
            p.Data.Level = p.Level;
            p.MarkStatsDirty();
            p.EnsureStats();
            if (p.IsAlive)
            {
                p.Health = p.MaxHealth;
                if (p.PowerType == PowerType.Mana) p.Power = p.MaxPower;
            }
            var trainable = _w.Content.ClassAbilityBases(p.Class)
                .Where(a => !a.NotTrainable && _w.Content.HighestRankForLevel(a.Id, p.Level) > p.KnownRank(a.Id))
                .Select(a => a.Id).ToList();
            var msg = new S_LevelUp
            {
                EntityId = p.Id, NewLevel = p.Level, HealthGained = p.MaxHealth - oldHp, PowerGained = p.MaxPower - oldPower,
                TalentPoints = p.TalentPointsFree, NewTrainableAbilities = trainable,
            };
            p.Zone?.BroadcastFrom(p, msg);
            if (p.Group != null) p.Group.Broadcast(p.Group.BuildUpdate());
            p.Bot?.OnLevelUp();
            _w.Guilds.OnLevelUp(p);
            if (p.Pet != null) { p.Pet.Level = p.Level; p.Pet.MarkStatsDirty(); }
            p.Send(p.BuildTalents());
            _w.Quests.RefreshMarkers(p);
        }

        /// <summary>Stufenaufstieg ohne Nachrichten (Hintergrundentwicklung offline befindlicher Bots).</summary>
        public void LevelUpSilently(Player p)
        {
            p.Level++;
            p.Data.Level = p.Level;
            p.Data.Xp = 0;
            p.MarkStatsDirty();
        }

        public void DiscoverArea(Player p, AreaDef area)
        {
            if (!p.Data.DiscoveredAreas.Add(area.Id)) return;
            p.Bot?.OnAreaDiscovered(area);
            if (!p.IsBot) p.Send(new S_Exploration { Areas = new List<string> { area.Id } });
            p.Notice($"Entdeckt: {area.Name}");
            int xp = Experience.DiscoveryXp(p.Level, area.DiscoveryXp);
            if (xp > 0) GiveXp(p, xp, "explore");
        }

        public void AccrueRested(Player p, double seconds)
        {
            if (p.Level >= Experience.MaxLevel) return;
            bool inInn = p.CurrentArea != null && (p.CurrentArea.IsInn || p.CurrentArea.IsCity);
            int gain = Experience.RestedGain(p.Level, seconds, inInn);
            if (gain <= 0) return;
            p.Data.RestedXp = Math.Min(Experience.MaxRestedXp(p.Level), p.Data.RestedXp + gain);
        }

        // ------------------------------------------------------------ Lehrer

        /// <summary>Was ein Klassenlehrer dieser Figur jetzt beibringen kann (Rang-IDs).</summary>
        public List<AbilityDef> Trainable(Player p, CharClass trainerClass)
        {
            if (trainerClass != p.Class) return new List<AbilityDef>();
            var list = new List<AbilityDef>();
            foreach (var a in _w.Content.ClassAbilityBases(p.Class))
            {
                if (a.NotTrainable) continue;
                int best = _w.Content.HighestRankForLevel(a.Id, p.Level);
                int known = p.KnownRank(a.Id);
                if (best > known) list.Add(_w.Content.GetAbilityRank(a.Id, known + 1)!);
            }
            return list;
        }

        public string? Train(Player p, Creature trainer, string abilityBaseId, bool free = false)
        {
            if (!trainer.Template.Roles.HasFlag(NpcRole.ClassTrainer) || trainer.Template.TrainerClass != p.Class) return "Dieser Lehrer kann Euch nichts beibringen.";
            var next = Trainable(p, p.Class).FirstOrDefault(a => a.Id == abilityBaseId);
            if (next == null) return "Ihr könnt diese Fähigkeit noch nicht lernen.";
            if (!free && p.Money < next.TrainingCost) return "Ihr habt nicht genug Geld.";
            if (!free) _w.Economy.Debit(p, next.TrainingCost, "training", next.Id);
            p.Learn(next.Id, next.Rank);
            p.Send(p.BuildSpellBook());
            p.Send(p.BuildPlayerState());
            p.Notice($"Gelernt: {next.Name} (Rang {next.Rank})");
            return null;
        }

        /// <summary>Lernt alle verfügbaren Ränge ohne Lehrer (KI-Mitspieler beim Stufenaufstieg).</summary>
        public void AutoTrain(Player p)
        {
            foreach (var a in _w.Content.ClassAbilityBases(p.Class))
            {
                if (a.NotTrainable) continue;
                int best = _w.Content.HighestRankForLevel(a.Id, p.Level);
                if (best > p.KnownRank(a.Id)) p.Learn(a.Id, best);
            }
            // Talentfähigkeiten mitskalieren
            foreach (var kv in p.Data.Abilities.ToList())
            {
                int best = _w.Content.HighestRankForLevel(kv.Key, p.Level);
                if (best > kv.Value && p.Data.Talents.Keys.Any(t => _w.Content.Talents[t].GrantsAbility == kv.Key)) p.Data.Abilities[kv.Key] = best;
            }
        }

        // ------------------------------------------------------------ Talente

        public string? LearnTalent(Player p, string talentId)
        {
            if (!_w.Content.Talents.TryGetValue(talentId, out var tal)) return "Unbekanntes Talent.";
            var tree = _w.Content.TalentTrees[_w.Content.TalentToTree[talentId]];
            if (tree.Class != p.Class) return "Dieses Talent gehört nicht zu Eurer Klasse.";
            if (p.TalentPointsFree <= 0) return "Keine Talentpunkte verfügbar.";
            int rank = p.Data.Talents.GetValueOrDefault(talentId);
            if (rank >= tal.MaxRank) return "Bereits voll ausgebaut.";
            if (p.PointsInTree(tree.Id) < tal.Tier * 5) return $"Benötigt {tal.Tier * 5} Punkte in {tree.Name}.";
            if (tal.Requires != null)
            {
                var req = _w.Content.Talents[tal.Requires];
                if (p.Data.Talents.GetValueOrDefault(tal.Requires) < req.MaxRank) return $"Benötigt {req.MaxRank} Punkte in {req.Name}.";
            }
            p.Data.Talents[talentId] = rank + 1;
            if (tal.GrantsAbility != null)
            {
                int abRank = Math.Max(1, _w.Content.HighestRankForLevel(tal.GrantsAbility, p.Level));
                p.Learn(tal.GrantsAbility, abRank);
                p.Send(p.BuildSpellBook());
            }
            p.MarkStatsDirty();
            p.Send(p.BuildTalents());
            p.Send(p.BuildPlayerState());
            return null;
        }

        /// <summary>Verteilt freie Punkte entlang eines Baums (KI-Mitspieler): Reihe für Reihe, Talente in Listenreihenfolge.</summary>
        public void AutoSpendTalents(Player p, string treeId)
        {
            if (!_w.Content.TalentTrees.TryGetValue(treeId, out var tree)) return;
            int guard = 100;
            while (p.TalentPointsFree > 0 && guard-- > 0)
            {
                bool spent = false;
                foreach (var t in tree.Talents.OrderBy(t => t.Tier).ThenBy(t => t.Column))
                    if (LearnTalent(p, t.Id) == null) { spent = true; break; }
                if (!spent) break;
            }
        }

        // ------------------------------------------------------------ Talente zurücksetzen

        /// <summary>
        /// Preis: 1 Gold beim ersten Mal, dann 5, 10, 15 … bis höchstens 50 Gold. Alle 30 Tage ohne Zurücksetzen sinkt
        /// der Preis wieder um eine Stufe – die Kosten bremsen ständiges Umskillen, ohne es zu verbieten.
        /// </summary>
        public long TalentResetCost(Player p)
        {
            int steps = p.Data.TalentResets;
            if (p.Data.LastTalentReset != default)
                steps -= (int)((DateTime.UtcNow - p.Data.LastTalentReset).TotalDays / 30);
            steps = Math.Max(0, steps);
            return steps == 0 ? 10000 : Math.Min(50, steps * 5) * 10000L;
        }

        public string? ResetTalents(Player p, Creature trainer, bool confirm)
        {
            if (!trainer.Template.Roles.HasFlag(NpcRole.ClassTrainer) || trainer.Template.TrainerClass != p.Class) return "Nur Euer Klassenlehrer kann Eure Talente zurücksetzen.";
            if (p.DistanceTo(trainer) > InteractionSystem.NpcRange) return "Zu weit entfernt.";
            if (p.TalentPointsSpent == 0) return "Ihr habt keine Talentpunkte verteilt.";
            long cost = TalentResetCost(p);
            if (!confirm) { p.Send(new S_TalentResetCost { NpcId = trainer.Id, Cost = cost }); return null; }
            if (!_w.Economy.Debit(p, cost, "talent_reset")) return $"Das Zurücksetzen kostet {LootSystem.Money(cost)}.";
            var granted = p.Data.Talents.Keys.Select(t => _w.Content.Talents[t].GrantsAbility).Where(a => a != null).ToList();
            foreach (var a in granted) p.Data.Abilities.Remove(a!);
            p.Data.Talents.Clear();
            int steps = Math.Max(0, p.Data.TalentResets - (p.Data.LastTalentReset == default ? 0 : (int)((DateTime.UtcNow - p.Data.LastTalentReset).TotalDays / 30)));
            p.Data.TalentResets = steps + 1;
            p.Data.LastTalentReset = DateTime.UtcNow;
            p.MarkStatsDirty();
            p.Send(p.BuildTalents());
            p.Send(p.BuildSpellBook());
            p.Send(p.BuildPlayerState());
            p.Notice($"Eure Talente wurden für {LootSystem.Money(cost)} zurückgesetzt.");
            _w.Save(p);
            return null;
        }

        // ------------------------------------------------------------ Ruf

        public void AddReputation(Player p, string factionId, int amount)
        {
            if (p.Race == Race.Aldorer && amount > 0) amount = (int)(amount * 1.1f); // Volksfähigkeit "Diplomatie"
            int old = p.Data.Reputation.GetValueOrDefault(factionId);
            int now = Math.Clamp(old + amount, ReputationMin, ReputationMax);
            p.Data.Reputation[factionId] = now;
            if (!_w.Content.Reputations.TryGetValue(factionId, out var rep) || now == old) return;
            p.Notice($"Ruf bei {rep.Name} um {Math.Abs(now - old)} {(now > old ? "gestiegen" : "gesunken")}.");
            if (Standing(now) != Standing(old)) p.Notice($"Ihr seid jetzt „{Standing(now)}“ bei {rep.Name}.");
        }

        public const int ReputationMin = -42000, ReputationMax = 42999;

        /// <summary>Rufstufen (Kapitel 24): Hasserfüllt bis Ehrfürchtig.</summary>
        public static string Standing(int value) => value switch
        {
            < -6000 => "Hasserfüllt",
            < -3000 => "Feindselig",
            < 0 => "Unfreundlich",
            < 3000 => "Neutral",
            < 9000 => "Freundlich",
            < 21000 => "Wohlwollend",
            < 42000 => "Respektvoll",
            _ => "Ehrfürchtig",
        };
    }
}
