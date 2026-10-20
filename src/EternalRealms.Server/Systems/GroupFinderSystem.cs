using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// „Abenteurer suchen“ (Kapitel 9.5). Echte Spieler werden zuerst zusammengebracht. Je nach Modus füllen
    /// KI-Mitspieler fehlende Rollen sofort oder nach einer Wartezeit auf. Jeder Vorschlag muss von allen Menschen
    /// bestätigt werden. Später kann ein KI-Platz an einen Menschen übergehen – geordnet und nie mitten in einem
    /// Bosskampf.
    /// </summary>
    public sealed class GroupFinderSystem
    {
        public const float ProposalSeconds = 30, PreferHumansWait = 45;

        public sealed class Activity
        {
            public string Id = "", Name = "", Kind = "";
            public ZoneDef Zone = null!;
            public int MinLevel, MaxLevel, Size, Tanks, Healers, Dps;
        }

        private sealed class Entry
        {
            public Player Leader = null!;
            public readonly Dictionary<Player, HashSet<Role>> Roles = new();
            public List<string> Activities = new();
            public FinderAiMode Mode;
            public double JoinedAt;
            public bool InProposal;
        }

        private sealed class Proposal
        {
            public int Id;
            public Activity Activity = null!;
            public List<Entry> Entries = new();
            public Dictionary<Player, Role> Assigned = new();
            public Dictionary<Player, bool?> Answers = new();
            public List<Role> BotRoles = new();
            public double Until;
            /// <summary>Ersatzvorschlag: Mensch übernimmt den Platz dieses Bots in einer bestehenden Gruppe.</summary>
            public Player? ReplaceBot;
            public Group? TargetGroup;
        }

        private readonly GameWorld _w;
        private readonly List<Entry> _queue = new();
        private readonly Dictionary<int, Proposal> _proposals = new();
        private readonly List<(Proposal p, Player human)> _pendingReplacements = new();
        private List<Activity>? _activities;
        private int _nextProposal = 1;
        private float _timer, _statusTimer;

        public GroupFinderSystem(GameWorld world) { _w = world; }

        public IReadOnlyList<Activity> Activities => _activities ??= BuildActivities();

        private List<Activity> BuildActivities()
        {
            var list = new List<Activity>();
            foreach (var z in _w.Content.Zones.Values.Where(z => z.Playable).OrderBy(z => z.MinLevel))
            {
                if (z.IsInstance)
                {
                    int size = Math.Max(5, z.MaxPlayers);
                    var (t, h) = size switch { <= 5 => (1, 1), <= 10 => (2, 3), <= 20 => (3, 5), _ => (5, 10) };
                    list.Add(new Activity { Id = (z.IsRaid ? "raid:" : "dungeon:") + z.Id, Name = z.Name, Kind = z.IsRaid ? "Schlachtzug" : "Dungeon", Zone = z,
                                            MinLevel = z.MinLevel, MaxLevel = z.MaxLevel, Size = size, Tanks = t, Healers = h, Dps = size - t - h });
                }
                else if (z.MaxLevel > 1)
                    list.Add(new Activity { Id = "quest:" + z.Id, Name = "Quests in " + z.Name, Kind = "Quests", Zone = z, MinLevel = z.MinLevel, MaxLevel = z.MaxLevel,
                                            Size = 5, Tanks = 1, Healers = 1, Dps = 3 });
            }
            return list;
        }

        public S_FinderActivities BuildActivityList() => new()
        {
            Activities = Activities.Select(a => new FinderActivityData { Id = a.Id, Name = a.Name, Kind = a.Kind, MinLevel = a.MinLevel, MaxLevel = a.MaxLevel, Size = a.Size }).ToList(),
        };

        public bool IsQueued(Player p) => _queue.Any(e => e.Roles.ContainsKey(p));

        // ------------------------------------------------------------ Anmelden

        public string? Join(Player p, IEnumerable<string> activityIds, IEnumerable<Role> roles, FinderAiMode mode)
        {
            if (IsQueued(p)) return "Ihr sucht bereits eine Gruppe.";
            if (p.Group != null && p.Group.Leader != p) return "Nur der Gruppenanführer kann die Gruppe anmelden.";
            var acts = activityIds.Distinct().Select(id => Activities.FirstOrDefault(a => a.Id == id)).Where(a => a != null).Select(a => a!).ToList();
            if (acts.Count == 0) return "Wählt mindestens eine Aktivität.";
            var humans = p.Group?.Humans.ToList() ?? new List<Player> { p };
            foreach (var h in humans)
                if (IsQueued(h)) return $"{h.Name} sucht bereits eine Gruppe.";
            var tooLow = acts.FirstOrDefault(a => humans.Any(h => h.Level < a.MinLevel - 1));
            if (tooLow != null) return $"{tooLow.Name} erfordert Stufe {tooLow.MinLevel - 1}.";
            var e = new Entry { Leader = p, Activities = acts.Select(a => a.Id).ToList(), Mode = mode, JoinedAt = _w.Time };
            var own = roles.Distinct().ToHashSet();
            if (own.Count == 0) own.Add(p.SelectedRole);
            foreach (var h in humans) e.Roles[h] = h == p ? own : new HashSet<Role> { h.SelectedRole };
            if (p.Group != null && humans.Count + p.Group.Bots.Count() >= acts.Max(a => a.Size)) return "Eure Gruppe ist bereits vollständig.";
            _queue.Add(e);
            foreach (var h in humans) SendStatus(h, e, "Ihr sucht eine Gruppe.");
            return null;
        }

        public void Leave(Player p)
        {
            var e = _queue.FirstOrDefault(x => x.Roles.ContainsKey(p));
            if (e == null) return;
            if (e.InProposal) foreach (var pr in _proposals.Values.Where(x => x.Entries.Contains(e)).ToList()) Cancel(pr, p);
            _queue.Remove(e);
            foreach (var h in e.Roles.Keys) SendStatus(h, null, h == p ? "Suche beendet." : $"{p.Name} hat die Suche beendet.");
        }

        private void SendStatus(Player p, Entry? e, string text) => p.Send(new S_FinderStatus
        {
            Queued = e != null, Activities = e?.Activities.ToList() ?? new List<string>(), WaitSeconds = e == null ? 0 : (float)(_w.Time - e.JoinedAt), Text = text,
        });

        // ------------------------------------------------------------ Zuordnung

        private static Role Slot(Role r) => r == Role.RangedDps ? Role.MeleeDps : r;

        /// <summary>Freie Plätze je Rolle (Schaden zählt Nah- und Fernkampf gemeinsam).</summary>
        private static Dictionary<Role, int> Slots(Activity a) => new() { [Role.Tank] = a.Tanks, [Role.Healer] = a.Healers, [Role.MeleeDps] = a.Dps };

        /// <summary>Versucht, alle Menschen eines Eintrags auf freie Plätze zu verteilen (kleine Rückverfolgung).</summary>
        private static bool Fit(Entry e, Dictionary<Role, int> slots, Dictionary<Player, Role> assigned)
        {
            var humans = e.Roles.Keys.ToList();
            bool Rec(int i)
            {
                if (i == humans.Count) return true;
                foreach (var r in e.Roles[humans[i]].OrderBy(r => r switch { Role.Tank => 0, Role.Healer => 1, _ => 2 }))
                {
                    var s = Slot(r);
                    if (slots[s] <= 0) continue;
                    slots[s]--;
                    assigned[humans[i]] = r;
                    if (Rec(i + 1)) return true;
                    slots[s]++;
                    assigned.Remove(humans[i]);
                }
                return false;
            }
            return Rec(0);
        }

        public void Update(float dt)
        {
            foreach (var pr in _proposals.Values.ToList())
            {
                foreach (var h in pr.Answers.Keys.ToList()) if (h.Removed) pr.Answers[h] = false;
                if (pr.Answers.Values.Any(v => v == false)) { Cancel(pr, pr.Answers.First(kv => kv.Value == false).Key); continue; }
                if (pr.Answers.Values.All(v => v == true)) { Complete(pr); continue; }
                if (_w.Time >= pr.Until) Cancel(pr, pr.Answers.First(kv => kv.Value == null).Key);
            }
            foreach (var (pr, human) in _pendingReplacements.ToList()) TryReplace(pr, human);
            _queue.RemoveAll(e => e.Roles.Keys.Any(h => h.Removed));

            _timer -= dt;
            if (_timer <= 0)
            {
                _timer = 1;
                Match();
            }
            _statusTimer -= dt;
            if (_statusTimer <= 0)
            {
                _statusTimer = 10;
                foreach (var e in _queue.Where(e => !e.InProposal)) foreach (var h in e.Roles.Keys) SendStatus(h, e, "Suche läuft …");
            }
        }

        private void Match()
        {
            foreach (var act in Activities)
            {
                var waiting = _queue.Where(e => !e.InProposal && e.Activities.Contains(act.Id)).OrderBy(e => e.JoinedAt).ToList();
                if (waiting.Count == 0) continue;
                if (TryOfferReplacement(act, waiting)) continue;
                // 1. Nur Menschen: von alt nach neu auffüllen, bis alle Plätze belegt sind.
                foreach (var start in waiting)
                {
                    var (chosen, assigned, slots) = Collect(act, start, waiting, aiAllowedOnly: false);
                    if (chosen == null) continue;
                    if (slots.Values.Sum() == 0) { Propose(act, chosen, assigned, new List<Role>()); return; }
                }
                // 2. Mit KI auffüllen – nur Einträge, die KI erlauben; der Älteste muss lange genug gewartet haben.
                foreach (var start in waiting.Where(e => e.Mode != FinderAiMode.HumansOnly))
                {
                    var (chosen, assigned, slots) = Collect(act, start, waiting, aiAllowedOnly: true);
                    if (chosen == null) continue;
                    double wait = _w.Time - chosen.Min(e => e.JoinedAt);
                    if (!chosen.Any(e => e.Mode == FinderAiMode.FillWithAi) && wait < PreferHumansWait) continue;
                    var bots = new List<Role>();
                    bool melee = true;
                    foreach (var (role, n) in slots)
                        for (int i = 0; i < n; i++)
                        {
                            if (role == Role.MeleeDps) { bots.Add(melee ? Role.MeleeDps : Role.RangedDps); melee = !melee; }
                            else bots.Add(role);
                        }
                    Propose(act, chosen, assigned, bots);
                    return;
                }
            }
        }

        /// <summary>Ausgehend von einem Eintrag weitere Einzelspieler einsammeln, solange sie auf freie Plätze passen.</summary>
        private (List<Entry>? chosen, Dictionary<Player, Role> assigned, Dictionary<Role, int> slots) Collect(Activity act, Entry start, List<Entry> waiting, bool aiAllowedOnly)
        {
            var slots = Slots(act);
            var assigned = new Dictionary<Player, Role>();
            if (BaseBots(start, slots) < 0 || !Fit(start, slots, assigned)) return (null, assigned, slots);
            var chosen = new List<Entry> { start };
            foreach (var e in waiting.Where(e => e != start && e.Roles.Count == 1 && e.Leader.Group == null && (!aiAllowedOnly || e.Mode != FinderAiMode.HumansOnly)))
            {
                if (slots.Values.Sum() == 0) break;
                var trial = new Dictionary<Role, int>(slots);
                var trialAssigned = new Dictionary<Player, Role>(assigned);
                if (!Fit(e, trial, trialAssigned)) continue;
                slots = trial; assigned = trialAssigned; chosen.Add(e);
            }
            return (chosen, assigned, slots);
        }

        /// <summary>Bots einer angemeldeten Gruppe belegen ihre Plätze schon. −1 = passt nicht.</summary>
        private static int BaseBots(Entry e, Dictionary<Role, int> slots)
        {
            int n = 0;
            foreach (var b in e.Leader.Group?.Bots ?? Enumerable.Empty<Player>())
            {
                var s = Slot(b.Bot!.Role);
                if (slots[s] <= 0) return -1;
                slots[s]--;
                n++;
            }
            return n;
        }

        private void Propose(Activity act, List<Entry> entries, Dictionary<Player, Role> assigned, List<Role> botRoles, Player? replaceBot = null, Group? target = null)
        {
            var pr = new Proposal { Id = _nextProposal++, Activity = act, Entries = entries, Assigned = assigned, BotRoles = botRoles, Until = _w.Time + ProposalSeconds,
                                    ReplaceBot = replaceBot, TargetGroup = target };
            foreach (var e in entries) { e.InProposal = true; foreach (var h in e.Roles.Keys) pr.Answers[h] = null; }
            _proposals[pr.Id] = pr;
            var names = assigned.Select(kv => $"{kv.Key.Name} ({RoleName(kv.Value)})").Concat(botRoles.Select(r => $"KI-Mitspieler ({RoleName(r)})")).ToList();
            if (target != null) names = target.Members.Where(m => m != replaceBot).Select(m => m.Name + (m.IsBot ? " (KI)" : "")).ToList();
            foreach (var h in pr.Answers.Keys)
                h.Send(new S_FinderProposal { ProposalId = pr.Id, Activity = act.Name, Role = assigned[h], Seconds = ProposalSeconds, Members = names, IsReplacement = target != null });
        }

        private static string RoleName(Role r) => r switch { Role.Tank => "Tank", Role.Healer => "Heiler", Role.MeleeDps => "Nahkampf", _ => "Fernkampf" };

        public string? Answer(Player p, int proposalId, bool accept)
        {
            if (!_proposals.TryGetValue(proposalId, out var pr) || !pr.Answers.ContainsKey(p)) return "Dieser Vorschlag ist nicht mehr gültig.";
            pr.Answers[p] = accept;
            return null;
        }

        /// <summary>Abgelehnt oder abgelaufen: Wer abgelehnt hat, verlässt die Suche, die anderen bleiben mit ihrem Platz in der Reihe.</summary>
        private void Cancel(Proposal pr, Player culprit)
        {
            _proposals.Remove(pr.Id);
            foreach (var e in pr.Entries)
            {
                e.InProposal = false;
                if (e.Roles.ContainsKey(culprit)) { _queue.Remove(e); foreach (var h in e.Roles.Keys) SendStatus(h, null, "Der Vorschlag wurde abgelehnt – Suche beendet."); }
                else foreach (var h in e.Roles.Keys) SendStatus(h, e, "Ein Mitspieler hat abgelehnt. Die Suche geht weiter.");
            }
        }

        private void Complete(Proposal pr)
        {
            _proposals.Remove(pr.Id);
            foreach (var e in pr.Entries) _queue.Remove(e);
            if (pr.TargetGroup != null) { foreach (var h in pr.Answers.Keys) _pendingReplacements.Add((pr, h)); return; }
            var act = pr.Activity;
            var baseEntry = pr.Entries[0];
            var leader = baseEntry.Leader;
            var g = leader.Group ?? _w.Groups.Create(leader);
            foreach (var (h, role) in pr.Assigned) h.SelectedRole = role;
            foreach (var e in pr.Entries.Skip(1))
                foreach (var h in e.Roles.Keys) _w.Groups.Join(g, h);
            if (act.Size > 5) g.IsRaid = true;
            int level = (int)Math.Round(pr.Assigned.Keys.Average(h => h.Level));
            foreach (var role in pr.BotRoles) _w.Bots.SummonForRole(leader, role, level, act.MinLevel, act.MaxLevel);
            g.FinderActivity = act.Id;
            g.LootMethod = LootMethod.GroupLoot;
            g.Broadcast(g.BuildLootMethod());
            g.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"Gruppe für {act.Name} gefunden!" });
            g.Broadcast(g.BuildUpdate());
            if (act.Zone.IsInstance)
            {
                _w.Transfer(leader, act.Zone.Id, act.Zone.InstanceEntry);
                var key = "i" + leader.Zone!.InstanceId;
                foreach (var h in g.Humans.Where(h => h != leader)) _w.Transfer(h, act.Zone.Id, act.Zone.InstanceEntry + new Vec3(1, 0, 1), key);
            }
            else
                foreach (var h in g.Humans.Where(h => h != leader && h.Zone != leader.Zone)) _w.Transfer(h, leader.Zone!.Def.Id, leader.Position + new Vec3(2, 0, 2));
            foreach (var h in g.Humans) SendStatus(h, null, $"Gruppe für {act.Name} gebildet.");
        }

        // ------------------------------------------------------------ KI-Plätze für Menschen

        /// <summary>Wartet ein Mensch auf eine Aktivität, die eine bestehende Gruppe mit passendem Bot spielt, bekommt er dessen Platz angeboten.</summary>
        private bool TryOfferReplacement(Activity act, List<Entry> waiting)
        {
            foreach (var e in waiting.Where(e => e.Roles.Count == 1 && e.Leader.Group == null && e.Mode != FinderAiMode.HumansOnly))
            {
                var human = e.Leader;
                foreach (var g in _w.Groups.All.Where(g => g.FinderActivity == act.Id && g.Leader.Faction == human.Faction && g.Bots.Any()))
                {
                    var bot = g.Bots.FirstOrDefault(b => e.Roles[human].Any(r => Slot(r) == Slot(b.Bot!.Role)));
                    if (bot == null || _pendingReplacements.Any(x => x.p.ReplaceBot == bot)) continue;
                    var role = e.Roles[human].First(r => Slot(r) == Slot(bot.Bot!.Role));
                    Propose(act, new List<Entry> { e }, new Dictionary<Player, Role> { [human] = role }, new List<Role>(), bot, g);
                    return true;
                }
            }
            return false;
        }

        private static bool InBossFight(Group g) =>
            g.Members.Any(m => m.InCombat && m.Zone != null && m.Zone.Units.OfType<Creature>().Any(c => c.IsBoss && c.IsAlive && c.InCombat && c.CombatWith.Contains(m)));

        private void TryReplace(Proposal pr, Player human)
        {
            var g = pr.TargetGroup!;
            var bot = pr.ReplaceBot!;
            if (human.Removed || bot.Group != g || !g.Humans.Any()) { _pendingReplacements.Remove((pr, human)); return; }
            if (InBossFight(g))
            {
                if (!pr.Answers.ContainsKey(bot)) { pr.Answers[bot] = true; human.Notice("Die Gruppe kämpft gerade gegen einen Boss – Ihr kommt nach dem Kampf dazu."); }
                return;
            }
            _pendingReplacements.Remove((pr, human));
            bot.Bot?.Say("replaced", force: true);
            human.SelectedRole = pr.Assigned[human];
            _w.Bots.DespawnBot(bot);
            if (human.Group != null) _w.Groups.Leave(human);
            _w.Groups.Join(g, human);
            var leader = g.Leader;
            if (leader.Zone != null)
                _w.Transfer(human, leader.Zone.Def.Id, leader.Position + new Vec3(1.5f, 0, 1.5f), leader.Zone.IsInstance ? "i" + leader.Zone.InstanceId : null);
            g.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"{human.Name} übernimmt den Platz von {bot.Name}." });
        }
    }
}
