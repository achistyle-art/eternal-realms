using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.AI
{
    /// <summary>
    /// Kommunikation und Persönlichkeit: Warnungen, taktische Hinweise, Pausenempfehlungen, Erinnerungen,
    /// Routenvorschläge, Reaktionen auf Siege – sowie Vertrautheit und Erfolgshistorie (langfristige Entwicklung).
    /// Alles lokal und regelbasiert: Kampfentscheidungen hängen nie von einem Sprachmodell ab.
    /// </summary>
    public sealed partial class BotBrain
    {
        private readonly HashSet<Creature> _warnedAbout = new();
        private readonly Dictionary<Creature, int> _announcedPhase = new();
        private int _killsThisFight;
        private bool _wasFighting;
        private double _lastReminder, _lastRoute;

        /// <summary>Gegner unter einem Kontrolleffekt (oder als Kontrollziel markiert) werden nicht angegriffen.</summary>
        private bool IsControlled(Creature c)
        {
            if (c.Auras.Any(a => a.Def.BreaksOnDamage && a.Def.IsDebuff)) return true;
            var g = Me.Group;
            return g != null && g.Markers.TryGetValue(Group.MarkMoon, out var id) && id == c.Id;
        }

        private Creature? MarkedTarget(List<Creature> usable)
        {
            var g = Me.Group;
            if (g == null) return null;
            foreach (var mark in new[] { Group.MarkSkull, Group.MarkCross })
                if (g.Markers.TryGetValue(mark, out var id) && usable.FirstOrDefault(c => c.Id == id) is Creature c) return c;
            return null;
        }

        private bool TryRequestedHeal()
        {
            var t = _healRequest;
            if (t == null) return false;
            if (t.IsDead || t.Removed || t.HealthPct > 0.98f) { _healRequest = null; return false; }
            var spell = Profile?.Heals.Where(h => Me.KnownRank(h) > 0).Select(Ranked).FirstOrDefault(a => a != null && !_w.Combat.BlockedByAura(a, t) && a.Effects.Any(e => e.Type == EffectType.Heal));
            if (spell == null) { _healRequest = null; return false; }
            if (Me.DistanceTo(t) > spell.MaxRange - 2) { MoveTo(t.Position, spell.MaxRange - 6); return true; }
            if (_w.Combat.TryCast(Me, spell, t) == null) { _healRequest = null; Decide($"Heile auf Bitte {t.Name}"); return true; }
            return false;
        }

        /// <summary>Beobachtet den laufenden Kampf: Warnungen vor starken Gegnern, Bosstipps, Phasenwarnungen.</summary>
        private void ObserveFight(List<Creature> enemies)
        {
            _wasFighting = true;
            if (!InGroup) return;
            var leaderLevel = Leader?.Level ?? Me.Level;
            foreach (var c in enemies)
            {
                if (_warnedAbout.Contains(c)) continue;
                bool strong = c.IsBoss || c.Template.Elite || c.Level >= leaderLevel + 3;
                if (!strong) continue;
                _warnedAbout.Add(c);
                bool speaker = Data.Archetype is BotPersonality.Vorsichtig or BotPersonality.Veteran || (Role == Role.Tank && !Me.Group!.Bots.Any(b => b.Bot!.Data.Archetype is BotPersonality.Vorsichtig or BotPersonality.Veteran));
                if (!speaker || Me.Group!.Bots.Any(b => b != Me && b.Bot!._warnedAbout.Contains(c) && b.Bot._spokeWarning == c)) continue;
                _spokeWarning = c;
                if (c.IsBoss && Data.Archetype == BotPersonality.Veteran && _w.Bots.BossTip(c.Template.Id) is string tip) _w.Bots.ScheduleChat(Me, Shared.ChatChannel.Party, tip, 0.5);
                else Say("warn_strong", c, force: true);
            }
            if (enemies.Count >= 3 && Data.Archetype == BotPersonality.Vorsichtig && !_warnedAbout.Contains(enemies[0])) { Say("warn_many"); _warnedAbout.Add(enemies[0]); }
            // Veteranen kündigen den nächsten Bosswechsel an, kurz bevor er eintritt.
            if (Data.Archetype == BotPersonality.Veteran)
                foreach (var boss in enemies.Where(e => e.IsBoss && e.Template.Phases.Count > 1))
                {
                    int next = boss.AI.PhaseIndex + 1;
                    if (next <= 0 || next >= boss.Template.Phases.Count) continue;
                    float threshold = boss.Template.Phases[next].HealthBelow;
                    if (boss.HealthPct < threshold + 0.06f && boss.HealthPct >= threshold && _announcedPhase.GetValueOrDefault(boss, -1) < next)
                    {
                        _announcedPhase[boss] = next;
                        Say("phase_warning", boss, force: true);
                    }
                }
            int groupLow = Me.Group!.Members.Count(m => m.IsAlive && m.HealthPct < 0.35f);
            if (groupLow >= 2 && Role == Role.Healer) Say("warn_danger");
        }

        private Creature? _spokeWarning;

        /// <summary>Nach dem Kampf: Siege feiern, Pausen empfehlen, an Quests erinnern, Routen vorschlagen.</summary>
        private void AfterFight()
        {
            if (_wasFighting)
            {
                _wasFighting = false;
                if (InGroup && _killsThisFight >= 3 && _w.Random.Chance(Data.Chattiness)) Say("victory");
                _killsThisFight = 0;
                _warnedAbout.Clear();
                if (InGroup && Data.Archetype == BotPersonality.Vorsichtig)
                {
                    bool needsRest = Me.Group!.Members.Any(m => m.IsAlive && (m.HealthPct < 0.6f || (m.PowerType == PowerType.Mana && m.PowerPct < 0.4f)));
                    if (needsRest) Say("recommend_rest");
                }
            }
            if (!InGroup || Leader == null) return;
            // Erinnerung an abgeschlossene Quests des Anführers
            if (Now - _lastReminder > 300 && _w.Random.Chance(Data.Chattiness * 0.02))
            {
                var done = Leader.Data.Quests.Values.FirstOrDefault(q => q.State == QuestState.Complete);
                if (done != null && Me.Group!.Bots.All(b => Now - b.Bot!._lastReminder > 120))
                {
                    _lastReminder = Now;
                    var q = _w.Content.Quests[done.QuestId];
                    var line = _w.Bots.PickLine("quest_reminder", Role, Data.Archetype);
                    if (line != null) _w.Bots.ScheduleChat(Me, Shared.ChatChannel.Party, Fill(line, null).Replace("{quest}", q.Title).Replace("{npc}", _w.Content.Creatures[q.TurnIn].Name), 0);
                }
            }
            // Routenvorschlag: unentdecktes Gebiet mit Gegnern passender Stufe
            if (Now - _lastRoute > 420 && _w.Random.Chance(Data.Chattiness * 0.01) && Data.Archetype is BotPersonality.Abenteuerlustig or BotPersonality.Veteran or BotPersonality.Mutig)
            {
                _lastRoute = Now;
                var area = SuggestArea();
                var line = _w.Bots.PickLine("route", Role, Data.Archetype);
                if (area != null && line != null) _w.Bots.ScheduleChat(Me, Shared.ChatChannel.Party, Fill(line, null).Replace("{area}", area.Name), 0);
            }
        }

        private AreaDef? SuggestArea()
        {
            var zone = Me.Zone!;
            var leader = Leader!;
            int gray = Experience.GrayLevel(leader.Level);
            return zone.Def.Areas.Where(a => !leader.Data.DiscoveredAreas.Contains(a.Id) && !a.IsSecret)
                .Where(a => zone.SpawnPoints.Any(s => Vec3.DistanceXZ(s.Position, a.Center) < a.Radius + 20 &&
                                                      _w.Content.Creatures[s.Def.Creature].MaxLevel > gray && _w.Content.Creatures[s.Def.Creature].MinLevel <= leader.Level + 2))
                .OrderBy(a => Vec3.DistanceXZ(a.Center, leader.Position)).FirstOrDefault();
        }

        public void OnAreaDiscovered(AreaDef area)
        {
            if (Data.Archetype == BotPersonality.Abenteuerlustig && InGroup && (area.IsViewpoint || area.IsSecret || area.DiscoveryXp >= 60))
                Say("new_area", null, delay: 1.5f);
        }

        public void OnRareLoot(ItemDef item)
        {
            if (Data.Archetype == BotPersonality.Abenteuerlustig && InGroup)
            {
                var line = _w.Bots.PickLine("rare_loot", Role, Data.Archetype);
                if (line != null) _w.Bots.ScheduleChat(Me, Shared.ChatChannel.Party, Fill(line, null).Replace("{item}", item.Name), 1.2);
            }
        }

        /// <summary>Gemeinsame Erlebnisse mit menschlichen Gruppenmitgliedern merken (Vertrautheit und Erinnerungen).</summary>
        public void RecordShared(string memory, int weight)
        {
            if (Me.Group == null) return;
            foreach (var h in Me.Group.Humans)
            {
                long acc = h.Data.AccountId ?? -1;
                Data.Familiarity[acc] = Data.Familiarity.GetValueOrDefault(acc) + weight;
            }
            if (memory.Length > 0)
            {
                var with = string.Join(", ", Me.Group.Humans.Select(h => h.Name));
                Data.History.Add(with.Length > 0 ? $"{memory} (mit {with})" : memory);
                if (Data.History.Count > 50) Data.History.RemoveAt(0);
            }
        }

        /// <summary>Begrüßung je nach Vertrautheit: Langjährige Begleiter erinnern sich an gemeinsame Erlebnisse.</summary>
        public void Greet(Player leader)
        {
            int fam = Data.Familiarity.GetValueOrDefault(leader.Data.AccountId ?? -1);
            var memory = Data.History.LastOrDefault(h => h.Contains(leader.Name));
            if (fam >= 5 && memory != null)
            {
                var line = _w.Bots.PickLine("join_familiar", Role, Data.Archetype);
                if (line != null)
                {
                    _w.Bots.ScheduleChat(Me, Shared.ChatChannel.Party, Fill(line, null).Replace("{memory}", memory.Split(" (mit")[0]), 0.8);
                    return;
                }
            }
            Say("join", force: true, delay: 0.8f);
        }
    }
}
