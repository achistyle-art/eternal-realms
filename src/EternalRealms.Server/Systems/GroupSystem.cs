using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    public sealed partial class GroupSystem
    {
        private readonly GameWorld _w;
        private float _updateTimer;
        public GroupSystem(GameWorld world) { _w = world; }

        public List<Group> All { get; } = new();

        public Group Create(Player leader)
        {
            var g = new Group(leader);
            All.Add(g);
            leader.Send(g.BuildUpdate());
            return g;
        }

        public bool Join(Group g, Player p)
        {
            if (p.Group != null) Leave(p);
            if (g.IsFull && !g.IsRaid && g.Members.Count < 40) g.IsRaid = true; // automatisch zum Schlachtzug erweitern
            if (!g.Add(p)) return false;
            g.Broadcast(g.BuildUpdate());
            g.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"{p.Name} ist der Gruppe beigetreten." });
            return true;
        }

        public void Leave(Player p)
        {
            var g = p.Group;
            if (g == null) return;
            g.Remove(p);
            p.Send(new S_GroupUpdate());
            g.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"{p.Name} hat die Gruppe verlassen." });
            // Eine Gruppe nur noch aus Bots löst sich auf: Bots ohne Spieler kehren in die Welt zurück.
            if (!g.Humans.Any())
            {
                foreach (var b in g.Bots.ToList())
                {
                    g.Remove(b);
                    if (b.Data.Bot?.Ambient == true) continue;
                    _w.Bots.DespawnBot(b);
                }
            }
            if (g.Members.Count <= 1)
            {
                foreach (var m in g.Members.ToList()) { g.Remove(m); m.Send(new S_GroupUpdate()); }
                All.Remove(g);
                return;
            }
            g.Broadcast(g.BuildUpdate());
        }

        public string? Invite(Player from, string name)
        {
            var target = _w.FindPlayer(name);
            if (target == null) return $"Niemand namens '{name}' ist online.";
            if (target == from) return "Ihr könnt Euch nicht selbst einladen.";
            if (target.Faction != from.Faction) return "Ihr könnt nur Mitglieder Eurer Fraktion einladen.";
            if (target.Group != null) return $"{target.Name} ist bereits in einer Gruppe.";
            if (from.Group != null && from.Group.Leader != from) return "Nur der Gruppenanführer kann einladen.";
            if (from.Group != null && from.Group.Members.Count >= 40) return "Der Schlachtzug ist voll.";
            if (target.IsBot)
            {
                // Eigenständige Bots entscheiden nach Persönlichkeit und Stufenabstand.
                var bot = target.Bot!;
                bool accept = Math.Abs(target.Level - from.Level) <= 6 && (bot.Data.Archetype != "wortkarg" || _w.Random.Chance(0.6));
                if (!accept) { _w.Bots.Whisper(target, from, "Danke, aber ich spiele gerade lieber allein."); return null; }
                if (from.Group == null) Create(from);
                Join(from.Group!, target);
                bot.Data.KnownAccounts.Add(from.Data.AccountId ?? -1);
                bot.Say("join", force: true, delay: 0.6f);
                return null;
            }
            target.PendingInviteFrom = from;
            target.Send(new S_GroupInviteRequest { FromName = from.Name });
            return null;
        }

        public void Respond(Player p, bool accept)
        {
            var from = p.PendingInviteFrom;
            p.PendingInviteFrom = null;
            if (from == null || from.Removed) return;
            if (!accept) { from.Notice($"{p.Name} lehnt die Einladung ab."); return; }
            if (from.Group == null) Create(from);
            Join(from.Group!, p);
        }

        public void Update(float dt)
        {
            UpdateTools();
            _updateTimer -= dt;
            if (_updateTimer > 0) return;
            _updateTimer = 1f;
            foreach (var g in All) if (g.Humans.Any()) g.Broadcast(g.BuildUpdate());
        }

        public string? SetLootMethod(Player p, LootMethod method, ItemQuality threshold, int masterLooterId)
        {
            var g = p.Group;
            if (g == null) return "Ihr seid in keiner Gruppe.";
            if (g.Leader != p) return "Nur der Gruppenanführer legt die Beuteregel fest.";
            if (!Enum.IsDefined(typeof(LootMethod), method) || threshold < ItemQuality.Uncommon || threshold > ItemQuality.Epic) return "Ungültige Beuteregel.";
            if (method == LootMethod.MasterLooter && g.Members.All(m => m.Id != masterLooterId)) masterLooterId = p.Id;
            g.LootMethod = method;
            g.LootThreshold = threshold;
            g.MasterLooterId = method == LootMethod.MasterLooter ? masterLooterId : 0;
            g.Broadcast(g.BuildLootMethod());
            string name = method switch
            {
                LootMethod.FreeForAll => "Jeder für sich", LootMethod.RoundRobin => "Reihum", LootMethod.GroupLoot => "Gruppenbeute",
                LootMethod.NeedBeforeGreed => "Bedarf vor Gier", _ => "Plündermeister",
            };
            g.Broadcast(new S_Chat { Channel = ChatChannel.System, Text = $"Beuteregel: {name} (ab {threshold})." });
            return null;
        }
    }
}