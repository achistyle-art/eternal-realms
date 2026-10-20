using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>Bereitschaftscheck, Abstimmung zum Entfernen, Rollen und Schlachtzug-Untergruppen (Kapitel 9.5).</summary>
    public sealed partial class GroupSystem
    {
        public const float ReadySeconds = 30, VoteSeconds = 60;

        private sealed class ReadyCheck
        {
            public Group Group = null!;
            public double Until;
            public readonly Dictionary<Player, bool?> Answers = new();
        }

        private sealed class KickVote
        {
            public int Id;
            public Group Group = null!;
            public Player Target = null!;
            public double Until;
            public readonly Dictionary<Player, bool?> Votes = new();
        }

        private readonly List<ReadyCheck> _ready = new();
        private readonly Dictionary<int, KickVote> _votes = new();
        private int _nextVote = 1;

        public string? StartReadyCheck(Player p)
        {
            var g = p.Group;
            if (g == null) return "Ihr seid in keiner Gruppe.";
            if (g.Leader != p) return "Nur der Gruppenanführer kann einen Bereitschaftscheck starten.";
            if (_ready.Any(r => r.Group == g)) return "Es läuft bereits ein Bereitschaftscheck.";
            var rc = new ReadyCheck { Group = g, Until = _w.Time + ReadySeconds };
            foreach (var m in g.Members) rc.Answers[m] = m == p ? true : null;
            _ready.Add(rc);
            foreach (var m in g.Humans.Where(m => m != p)) m.Send(new S_ReadyCheck { From = p.Name, Seconds = ReadySeconds });
            // KI-Mitspieler antworten nach kurzer Bedenkzeit ehrlich nach ihrem Zustand.
            foreach (var b in g.Bots)
            {
                bool ok = b.IsAlive && b.HealthPct > 0.7f && (b.PowerType != PowerType.Mana || b.PowerPct > 0.6f);
                var bot = b;
                double at = _w.Time + 1 + _w.Random.NextDouble() * 2;
                _pendingBotAnswers.Add((at, rc, bot, ok));
            }
            return null;
        }

        private readonly List<(double at, ReadyCheck rc, Player bot, bool ok)> _pendingBotAnswers = new();

        public void ReadyResponse(Player p, bool ready)
        {
            var rc = _ready.FirstOrDefault(r => r.Group == p.Group);
            if (rc != null && rc.Answers.ContainsKey(p)) rc.Answers[p] = ready;
        }

        public string? StartKickVote(Player p, int targetId, string reason)
        {
            var g = p.Group;
            if (g == null) return "Ihr seid in keiner Gruppe.";
            var target = g.Members.FirstOrDefault(m => m.Id == targetId);
            if (target == null || target == p) return "Ungültiges Ziel.";
            if (target.IsBot)
            {
                if (g.Leader != p) return "Nur der Gruppenanführer kann KI-Mitspieler entlassen.";
                return _w.Bots.Dismiss(p, target.Id);
            }
            var voters = g.Humans.Where(h => h != target).ToList();
            if (voters.Count <= 1)
            {
                if (g.Leader != p) return "Nur der Gruppenanführer kann entfernen.";
                Kick(g, target);
                return null;
            }
            if (_votes.Values.Any(v => v.Group == g)) return "Es läuft bereits eine Abstimmung.";
            var vote = new KickVote { Id = _nextVote++, Group = g, Target = target, Until = _w.Time + VoteSeconds };
            foreach (var v in voters) vote.Votes[v] = v == p ? true : null;
            _votes[vote.Id] = vote;
            foreach (var v in voters.Where(v => v != p))
                v.Send(new S_VoteKick { VoteId = vote.Id, TargetName = target.Name, Reason = (reason ?? "").Length > 100 ? reason!.Substring(0, 100) : reason ?? "", Seconds = VoteSeconds });
            g.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"Abstimmung: {target.Name} aus der Gruppe entfernen?" });
            CheckVote(vote);
            return null;
        }

        public void VoteResponse(Player p, int voteId, bool yes)
        {
            if (!_votes.TryGetValue(voteId, out var v) || !v.Votes.ContainsKey(p) || v.Votes[p] != null) return;
            v.Votes[p] = yes;
            CheckVote(v);
        }

        private void CheckVote(KickVote v)
        {
            int total = v.Votes.Count, yes = v.Votes.Values.Count(x => x == true), no = v.Votes.Values.Count(x => x == false);
            bool passed = yes * 2 > total, failed = no * 2 >= total || (_w.Time >= v.Until && !passed);
            if (!passed && !failed) return;
            _votes.Remove(v.Id);
            if (passed && v.Target.Group == v.Group) Kick(v.Group, v.Target);
            else v.Group.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"Die Abstimmung über {v.Target.Name} ist gescheitert." });
        }

        private void Kick(Group g, Player target)
        {
            target.Notice("Ihr wurdet aus der Gruppe entfernt.");
            g.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"{target.Name} wurde aus der Gruppe entfernt." });
            Leave(target);
            // Wer aus einer Instanz geworfen wird, landet am Ausgang.
            if (target.Zone?.IsInstance == true && target.Zone.Def.ExitZone != null) _w.Transfer(target, target.Zone.Def.ExitZone, target.Zone.Def.ExitPosition);
        }

        public string? SetRole(Player p, Role role)
        {
            if (!Enum.IsDefined(typeof(Role), role)) return "Unbekannte Rolle.";
            p.SelectedRole = role;
            p.Group?.Broadcast(p.Group.BuildUpdate());
            return null;
        }

        public string? SetBotRole(Player p, int botId, Role role)
        {
            var g = p.Group;
            var bot = g?.Bots.FirstOrDefault(b => b.Id == botId);
            if (bot == null) return "Dieser KI-Mitspieler ist nicht in Eurer Gruppe.";
            if (g!.Leader != p) return "Nur der Gruppenanführer kann Rollen zuweisen.";
            if (bot.InCombat) return "Nicht während des Kampfes.";
            var err = bot.Bot!.ChangeRole(role);
            if (err == null) { g.Broadcast(g.BuildUpdate()); bot.Bot.Say("role_change", force: true); }
            return err;
        }

        public string? ConvertToRaid(Player p)
        {
            var g = p.Group;
            if (g == null) return "Ihr seid in keiner Gruppe.";
            if (g.Leader != p) return "Nur der Gruppenanführer kann einen Schlachtzug bilden.";
            if (g.IsRaid) return "Die Gruppe ist bereits ein Schlachtzug.";
            g.IsRaid = true;
            g.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = "Die Gruppe ist jetzt ein Schlachtzug (bis 40 Mitglieder, 8 Untergruppen)." });
            g.Broadcast(g.BuildUpdate());
            return null;
        }

        public string? SetSubgroup(Player p, int entityId, byte subgroup)
        {
            var g = p.Group;
            if (g == null || !g.IsRaid) return "Nur im Schlachtzug.";
            if (g.Leader != p) return "Nur der Schlachtzugsleiter verteilt Untergruppen.";
            if (subgroup < 1 || subgroup > 8) return "Untergruppe 1–8.";
            if (!g.Members.Any(m => m.Id == entityId)) return "Ungültiges Mitglied.";
            if (g.Subgroups.Count(kv => kv.Value == subgroup && kv.Key != entityId) >= 5) return $"Untergruppe {subgroup} ist voll.";
            g.Subgroups[entityId] = subgroup;
            g.Broadcast(g.BuildUpdate());
            return null;
        }

        private void UpdateTools()
        {
            foreach (var (at, rc, bot, ok) in _pendingBotAnswers.ToList())
            {
                if (at > _w.Time) continue;
                _pendingBotAnswers.Remove((at, rc, bot, ok));
                if (!rc.Answers.ContainsKey(bot)) continue;
                rc.Answers[bot] = ok;
                bot.Bot?.Say(ok ? "ready_yes" : "ready_no", force: true);
            }
            foreach (var rc in _ready.ToList())
            {
                foreach (var m in rc.Answers.Keys.ToList()) if (m.Group != rc.Group) rc.Answers.Remove(m);
                if (rc.Answers.Values.Any(v => v == null) && _w.Time < rc.Until) continue;
                _ready.Remove(rc);
                var result = new S_ReadyCheckResult
                {
                    Ready = rc.Answers.Where(kv => kv.Value == true).Select(kv => kv.Key.Name).ToList(),
                    NotReady = rc.Answers.Where(kv => kv.Value != true).Select(kv => kv.Key.Name).ToList(),
                };
                rc.Group.Broadcast(result);
                rc.Group.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = result.NotReady.Count == 0 ? "Alle sind bereit." : "Nicht bereit: " + string.Join(", ", result.NotReady) });
            }
            foreach (var v in _votes.Values.ToList()) if (_w.Time >= v.Until || v.Target.Group != v.Group) CheckVote(v);
        }
    }
}
