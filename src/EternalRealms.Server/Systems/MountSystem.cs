using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Reiten (Kapitel 18): Reitfertigkeit beim Stallmeister (Stufen, Preise und Tempo aus <c>content/riding.json</c>), Reittier-Sammlung,
    /// Aufsitzen über eine Fähigkeit mit 1,5 s Zauberzeit (Aura „Beritten“). Schaden, eigene Angriffe oder Zauber,
    /// Instanzen und der Tod werfen ab. Fliegende Reittiere gibt es nicht.
    /// </summary>
    public sealed class MountSystem
    {
        private readonly GameWorld _w;
        public MountSystem(GameWorld world) { _w = world; }

        /// <summary>Nötige Reitfertigkeit für ein Tempo (int.MaxValue: keine Reitstufe erlaubt es).</summary>
        public int SkillFor(int speed) => _w.Content.RidingRankFor(speed)?.Skill ?? int.MaxValue;

        public S_MountJournal BuildJournal(Player p) => new()
        {
            RidingSkill = p.Data.RidingSkill, Mounts = p.Data.Mounts.OrderBy(m => m, StringComparer.Ordinal).ToList(), Active = p.ActiveMount,
        };

        public string? LearnRiding(Player p, int npcId)
        {
            if (_w.Interactions.NpcWithRole(p, npcId, NpcRole.RidingTrainer) == null) return "Kein Stallmeister in der Nähe.";
            var next = _w.Content.RidingRanks.FirstOrDefault(r => r.Skill > p.Data.RidingSkill);
            if (next == null) return "Ihr beherrscht das Reiten bereits meisterhaft.";
            if (p.Level < next.Level) return $"{next.Name} erfordert Stufe {next.Level}.";
            if (!_w.Economy.Debit(p, next.Cost, "riding", next.Name)) return $"{next.Name} kostet {LootSystem.Money(next.Cost)}.";
            p.Data.RidingSkill = next.Skill;
            _w.ReportProgress(p, ProgressEvent.LearnRiding);
            p.Notice($"Ihr habt {next.Name} erlernt.");
            p.Send(p.BuildPlayerState());
            p.Send(BuildJournal(p));
            return null;
        }

        /// <summary>Zügel benutzen: Reittier wird Teil der Sammlung, der Gegenstand verbraucht.</summary>
        public string? LearnMount(Player p, int bagIndex)
        {
            if (bagIndex < 0 || bagIndex >= p.Data.Bag.Count || p.Data.Bag[bagIndex] is not { } stack) return "Kein Gegenstand.";
            var item = _w.Content.Items[stack.ItemId];
            if (item.TeachesMount == null || !_w.Content.Mounts.TryGetValue(item.TeachesMount, out var m)) return "Das ist kein Reittier.";
            if (m.Faction != Faction.None && m.Faction != p.Faction) return "Dieses Reittier gehört zur anderen Fraktion.";
            if (p.Data.Mounts.Contains(m.Id)) return "Dieses Reittier besitzt Ihr bereits.";
            if (_w.Interactions.ReputationMissing(p, item) is string repErr) return repErr;
            if (p.Level < m.RequiredLevel) return $"Benötigt Stufe {m.RequiredLevel}.";
            if (p.Data.RidingSkill < SkillFor(m.Speed))
                return p.Data.RidingSkill == 0 ? "Lernt zuerst beim Stallmeister das Reiten." : $"Dafür braucht Ihr „{_w.Content.RidingRankFor(m.Speed)?.Name}“.";
            if (--stack.Count <= 0) p.Data.Bag[bagIndex] = null;
            p.Data.Mounts.Add(m.Id);
            _w.ReportProgress(p, ProgressEvent.MountsOwned, "", p.Data.Mounts.Count);
            p.Notice($"Neues Reittier: {m.Name}");
            p.Send(p.BuildInventory());
            p.Send(BuildJournal(p));
            _w.Save(p);
            return null;
        }

        /// <summary>Schnellstes nutzbares Reittier der Sammlung (für „Aufsitzen“ ohne Auswahl und für Bots).</summary>
        public MountDef? BestMount(Player p) => p.Data.Mounts.Select(id => _w.Content.Mounts.GetValueOrDefault(id)).Where(m => m != null && CanUse(p, m) == null)
            .OrderByDescending(m => m!.Speed).ThenByDescending(m => m!.Quality).FirstOrDefault();

        private string? CanUse(Player p, MountDef m)
        {
            if (p.Level < m.RequiredLevel) return $"Benötigt Stufe {m.RequiredLevel}.";
            if (p.Data.RidingSkill < SkillFor(m.Speed)) return "Eure Reitfertigkeit reicht nicht aus.";
            return null;
        }

        public string? Mount(Player p, string mountId)
        {
            if (p.IsMounted) { Dismount(p); return null; }
            if (p.IsDead) return "Ihr seid tot.";
            if (p.Zone == null || p.Zone.IsInstance) return "Hier könnt Ihr nicht reiten.";
            if (p.InCombat) return "Nicht im Kampf.";
            if (p.IsFlying) return "Nicht während des Fluges.";
            var m = string.IsNullOrEmpty(mountId) ? BestMount(p) : p.Data.Mounts.Contains(mountId) ? _w.Content.Mounts.GetValueOrDefault(mountId) : null;
            if (m == null) return string.IsNullOrEmpty(mountId) ? "Ihr besitzt kein nutzbares Reittier." : "Dieses Reittier besitzt Ihr nicht.";
            var err = CanUse(p, m);
            if (err != null) return err;
            var ab = _w.Content.GetAbilityRank("mount_ride_" + m.Speed, 1)!;
            p.PendingMount = m.Id;
            err = _w.Combat.TryCast(p, ab, p);
            if (err != null) p.PendingMount = "";
            return err;
        }

        public void Dismount(Player p) => p.RemoveAuras(a => a.Def.Mounted);

        /// <summary>Aufgerufen, wenn die Aura „Beritten“ angelegt wurde.</summary>
        public void OnMounted(Player p)
        {
            p.ActiveMount = p.PendingMount != "" ? p.PendingMount : BestMount(p)?.Id ?? "";
            p.PendingMount = "";
            p.Zone?.BroadcastFrom(p, p.BuildSpawn(null));
            p.Send(BuildJournal(p));
        }

        public void OnDismounted(Player p)
        {
            if (p.ActiveMount == "") return;
            p.ActiveMount = "";
            p.Zone?.BroadcastFrom(p, p.BuildSpawn(null));
            p.Send(BuildJournal(p));
        }

        /// <summary>
        /// KI-Mitspieler lernen das Reiten ab der Stufe aus <c>riding.json</c>, sobald sie es sich leisten können, und kaufen das
        /// Fraktionsreittier ihres Volkes – ohne Abkürzung über virtuelle Gegenstände.
        /// </summary>
        public void BotProgress(Player bot)
        {
            if (!bot.IsBot) return;
            foreach (var r in _w.Content.RidingRanks)
            {
                if (bot.Data.RidingSkill >= r.Skill || bot.Level < r.Level) continue;
                if (!_w.Economy.Debit(bot, r.Cost, "riding", r.Name)) return;
                bot.Data.RidingSkill = r.Skill;
            }
            var wanted = _w.Content.Mounts.Values.Where(m => m.Source == "vendor" && (m.Faction == bot.Faction || m.Faction == Faction.None) && CanUse(bot, m) == null
                                                             && !bot.Data.Mounts.Contains(m.Id))
                .OrderByDescending(m => m.Speed).ThenBy(m => StableHash.Of(bot.Name + m.Id)).FirstOrDefault();
            if (wanted == null || (BestMount(bot)?.Speed ?? 0) >= wanted.Speed) return;
            if (_w.Economy.Debit(bot, wanted.Price, "vendor_buy", wanted.Item)) bot.Data.Mounts.Add(wanted.Id);
        }
    }
}
