using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.World
{
    /// <summary>Gruppe (bis 5) oder Schlachtzug (bis 40) aus Spielern und KI-Mitspielern.</summary>
    public sealed class Group
    {
        private static int _nextId = 1;
        public int Id { get; } = _nextId++;
        public Player Leader { get; set; }
        public List<Player> Members { get; } = new();
        public bool IsRaid { get; set; }
        public int MaxSize => IsRaid ? 40 : 5;
        public int LootRobin { get; set; }
        /// <summary>Zielmarkierungen: Symbol (1–8) → Einheit. 8 = Totenkopf (zuerst töten), 7 = Kreuz (danach), 5 = Mond (kontrollieren).</summary>
        public Dictionary<int, int> Markers { get; } = new();
        public const int MarkSkull = 8, MarkCross = 7, MarkMoon = 5;

        /// <summary>Setzt (oder löscht mit targetId 0) eine Markierung und informiert alle Mitglieder.</summary>
        public void SetMarker(int marker, int targetId)
        {
            if (marker < 1 || marker > 8) return;
            foreach (var k in Markers.Where(kv => kv.Value == targetId && targetId != 0).Select(kv => kv.Key).ToList()) Markers.Remove(k);
            if (targetId == 0) Markers.Remove(marker); else Markers[marker] = targetId;
            Broadcast(BuildMarkers());
        }

        public S_GroupMarkers BuildMarkers() => new()
        {
            Markers = Markers.Keys.ToList(),
            Targets = Markers.Values.ToList(),
        };

        public Group(Player leader)
        {
            Leader = leader;
            Members.Add(leader);
            leader.Group = this;
        }

        public bool IsFull => Members.Count >= MaxSize;
        public IEnumerable<Player> Bots => Members.Where(m => m.IsBot);
        public IEnumerable<Player> Humans => Members.Where(m => !m.IsBot);

        public bool Add(Player p)
        {
            if (IsFull || Members.Contains(p)) return false;
            Members.Add(p);
            p.Group = this;
            return true;
        }

        public void Remove(Player p)
        {
            Members.Remove(p);
            if (p.Group == this) p.Group = null;
            if (Leader == p && Members.Count > 0)
                Leader = Members.FirstOrDefault(m => !m.IsBot) ?? Members[0];
        }

        public Player? MemberWithRole(Role role) => Members.FirstOrDefault(m => m.Bot != null && m.Bot.Role == role && m.IsAlive) ??
                                                    (role == Role.Tank ? Members.FirstOrDefault(m => !m.IsBot && m.IsAlive && (m.Form == Form.DefensiveStance || m.Form == Form.Bear)) : null);

        public S_GroupUpdate BuildUpdate() => new()
        {
            LeaderId = Leader.Id,
            IsRaid = IsRaid,
            Members = Members.Select(m => new GroupMemberData
            {
                EntityId = m.Id, Name = m.Name, Class = m.Class, Level = m.Level, Health = m.Health, MaxHealth = m.MaxHealth, Power = m.Power,
                MaxPower = m.MaxPower, PowerType = m.PowerType, IsBot = m.IsBot, Role = m.Bot?.Role ?? Role.MeleeDps, Online = !m.Removed,
            }).ToList(),
        };

        public void Broadcast(Message m)
        {
            foreach (var p in Members) p.Send(m);
        }
    }
}
