using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Freundesliste, Ignorieren und Melden (Kapitel 32). Freunde werden auch offline über die Datenbank gefunden.
    /// Die KI-Freundesliste zeigt gespeicherte Lieblingsbegleiter mit Rolle, Ausrüstung, Aktivität, Verfügbarkeit
    /// und gemeinsamen Abenteuern.
    /// </summary>
    public sealed class SocialSystem
    {
        public const int MaxFriends = 50, MaxIgnored = 50;
        private readonly GameWorld _w;
        /// <summary>Letzte Chatzeilen je Spieler als Beleg für Meldungen.</summary>
        private readonly Dictionary<long, Queue<string>> _recentChat = new();

        public SocialSystem(GameWorld world) { _w = world; }

        public bool Ignores(Player listener, Player speaker) => listener.Data.Ignored.Any(i => i.CharacterId == speaker.Data.Id && speaker.Data.Id > 0);

        public void RememberChat(Player from, string text)
        {
            if (from.Data.Id <= 0) return;
            if (!_recentChat.TryGetValue(from.Data.Id, out var q)) _recentChat[from.Data.Id] = q = new Queue<string>();
            q.Enqueue($"{DateTime.UtcNow:HH:mm:ss} {text}");
            while (q.Count > 10) q.Dequeue();
        }

        /// <summary>Name auflösen – online sofort, sonst über die Datenbank; Ergebnis kommt im Spielthread an.</summary>
        private void Resolve(Player asker, string name, Action<CharacterRef?> then)
        {
            var online = _w.FindPlayer(name);
            if (online != null) { then(new CharacterRef { Id = online.Data.Id, AccountId = online.Data.AccountId, Name = online.Name, Race = online.Race }); return; }
            if (_w.EconomyStore == null || _w.Persistence == null) { then(null); return; }
            var store = _w.EconomyStore;
            _w.Persistence.EnqueueAsync(() => store.FindCharacterAsync(name)).ContinueWith(t =>
            {
                var r = t.IsCompletedSuccessfully ? t.Result : null;
                _w.Post(() => { if (!asker.Removed) then(r); });
            });
        }

        public void AddFriend(Player p, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            Resolve(p, name, r =>
            {
                string? err = r == null ? $"Niemand namens '{name}' gefunden." : r.Id == p.Data.Id ? "Ihr könnt Euch nicht selbst hinzufügen."
                    : p.Data.Friends.Any(f => f.CharacterId == r.Id) ? $"{r.Name} ist bereits Euer Freund."
                    : p.Data.Friends.Count >= MaxFriends ? "Eure Freundesliste ist voll."
                    : _w.Content.Races.TryGetValue(r.Race, out var race) && race.Faction != p.Faction ? "Nur Charaktere der eigenen Fraktion." : null;
                if (err != null) { p.Notice(err, true); return; }
                p.Data.Friends.Add(new SocialEntry { CharacterId = r!.Id, Name = r.Name });
                p.Notice($"{r.Name} wurde Eurer Freundesliste hinzugefügt.");
                _w.Save(p);
                p.Send(BuildList(p));
            });
        }

        public void RemoveFriend(Player p, long characterId)
        {
            if (p.Data.Friends.RemoveAll(f => f.CharacterId == characterId) > 0) { _w.Save(p); p.Send(BuildList(p)); }
        }

        public void Ignore(Player p, string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0) return;
            Resolve(p, name, r =>
            {
                if (r == null || r.Id == p.Data.Id) { p.Notice($"Niemand namens '{name}' gefunden.", true); return; }
                if (p.Data.Ignored.Any(i => i.CharacterId == r.Id)) return;
                if (p.Data.Ignored.Count >= MaxIgnored) { p.Notice("Eure Ignorierliste ist voll.", true); return; }
                p.Data.Ignored.Add(new SocialEntry { CharacterId = r.Id, Name = r.Name });
                p.Data.Friends.RemoveAll(f => f.CharacterId == r.Id);
                p.Notice($"Ihr ignoriert jetzt {r.Name}.");
                _w.Save(p);
                p.Send(BuildList(p));
            });
        }

        public void Unignore(Player p, long characterId)
        {
            if (p.Data.Ignored.RemoveAll(i => i.CharacterId == characterId) > 0) { _w.Save(p); p.Send(BuildList(p)); }
        }

        public void SetShareLocation(Player p, bool share)
        {
            p.Data.ShareLocation = share;
            p.Notice(share ? "Freunde sehen jetzt Euren Aufenthaltsort." : "Euer Aufenthaltsort ist jetzt verborgen.");
            p.Send(BuildList(p));
        }

        public void Report(Player p, string name, string reason, string text)
        {
            name = (name ?? "").Trim();
            Resolve(p, name, r =>
            {
                if (r == null) { p.Notice($"Niemand namens '{name}' gefunden.", true); return; }
                var rep = new PlayerReport
                {
                    ReporterId = p.Data.Id, ReporterName = p.Name, TargetId = r.Id, TargetName = r.Name, Reason = Cut(reason, 40), Text = Cut(text, 500),
                    Evidence = _recentChat.TryGetValue(r.Id, out var q) ? q.ToList() : new List<string>(),
                };
                _w.Log($"Meldung: {p.Name} meldet {r.Name} ({rep.Reason})");
                if (_w.Characters is ISocialRepository repo && _w.Persistence != null) _w.Persistence.Enqueue(() => repo.SaveReportAsync(rep));
                p.Notice("Danke. Eure Meldung wurde an die Spielleitung weitergegeben.");
            });
        }

        private static string Cut(string? s, int n) => s == null ? "" : s.Length > n ? s.Substring(0, n) : s;

        /// <summary>Beim Ein- und Ausloggen: Freunde, die einen auf der Liste haben, werden benachrichtigt.</summary>
        public void NotifyFriends(Player p, bool online)
        {
            foreach (var other in _w.AllPlayers.Where(o => !o.IsBot && o != p && o.Data.Friends.Any(f => f.CharacterId == p.Data.Id)))
                other.Notice(online ? $"{p.Name} ist jetzt online." : $"{p.Name} ist offline.");
        }

        public S_FriendList BuildList(Player p)
        {
            var list = new S_FriendList { ShareLocation = p.Data.ShareLocation, Ignored = p.Data.Ignored.Select(i => i.Name).ToList(), IgnoredIds = p.Data.Ignored.Select(i => i.CharacterId).ToList() };
            foreach (var f in p.Data.Friends)
            {
                var on = _w.AllPlayers.FirstOrDefault(x => x.Data.Id == f.CharacterId && !x.IsBot);
                if (on != null) f.Name = on.Name;
                list.Friends.Add(new FriendInfo
                {
                    CharacterId = f.CharacterId, Name = f.Name, Note = f.Note, Online = on != null, Level = on?.Level ?? 0, Class = on?.Class ?? CharClass.None,
                    Zone = on != null && on.Data.ShareLocation ? on.Zone?.Def.Name ?? "" : "",
                });
            }
            foreach (var (data, online) in _w.Bots.Companions(p))
            {
                var bd = data.Bot!;
                string availability = online == null ? "verfügbar (ruht)" : online.Group == p.Group && p.Group != null ? "in Eurer Gruppe"
                    : online.Group != null ? $"unterwegs mit {online.Group.Leader.Name}" : "verfügbar";
                string activity = online == null ? "ruht" : online.Group != null ? (online.InCombat ? "kämpft" : "begleitet eine Gruppe") : online.Bot!.AutoActivity;
                list.Companions.Add(new FriendInfo
                {
                    CharacterId = data.Id, Name = data.Name, Level = online?.Level ?? data.Level, Class = data.Class, Online = online != null, IsBot = true,
                    Role = bd.Role, ItemLevel = ItemLevel(data), Activity = activity, Availability = availability,
                    Zone = online?.Zone?.Def.Name ?? "", SharedAdventures = bd.History.Where(h => h.Contains(p.Name)).Reverse().Take(5).Select(h => h.Split(" (mit")[0]).ToList(),
                });
            }
            return list;
        }

        /// <summary>Durchschnittliche Gegenstandsstufe der angelegten Ausrüstung (KI ohne Ausrüstung: ihre Stufe – virtuelle Grundausrüstung).</summary>
        private int ItemLevel(CharacterData d)
        {
            var levels = d.Equipment.Values.Select(e => _w.Content.Items.TryGetValue(e.ItemId, out var it) ? it.ItemLevel : 0).Where(l => l > 0).ToList();
            return levels.Count == 0 ? d.Level : (int)Math.Round(levels.Average());
        }
    }
}
