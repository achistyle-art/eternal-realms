using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Post (Kapitel 21). Briefe liegen im Speicher und werden zusammen mit den beteiligten Charakteren in einer
    /// Transaktion geschrieben: Ein Gegenstand ist immer genau an einem Ort – in der Tasche oder im Brief.
    /// </summary>
    public sealed class MailSystem
    {
        public const long PostagePerItem = 30;
        public const int MaxItems = 12;
        public const float MailboxRange = 6f;
        public static readonly TimeSpan ItemDelay = TimeSpan.FromHours(1), Lifetime = TimeSpan.FromDays(30), CodLifetime = TimeSpan.FromDays(3);

        private readonly GameWorld _w;
        private readonly Dictionary<long, List<MailData>> _byRecipient = new();
        private long _nextId = 1;
        private float _timer;
        private readonly HashSet<long> _announced = new();

        public MailSystem(GameWorld world) { _w = world; }

        public void Load(IEnumerable<MailData> mails)
        {
            foreach (var m in mails) Insert(m);
        }

        private void Insert(MailData m)
        {
            if (!_byRecipient.TryGetValue(m.RecipientId, out var list)) _byRecipient[m.RecipientId] = list = new List<MailData>();
            list.Add(m);
            _nextId = Math.Max(_nextId, m.Id + 1);
        }

        private void Remove(MailData m)
        {
            if (_byRecipient.TryGetValue(m.RecipientId, out var list)) list.Remove(m);
        }

        public IReadOnlyList<MailData> Inbox(long characterId) =>
            _byRecipient.TryGetValue(characterId, out var l) ? l.Where(m => m.DeliverAt <= DateTime.UtcNow).OrderByDescending(m => m.DeliverAt).ToList() : new List<MailData>();

        public int Unread(long characterId) => Inbox(characterId).Count(m => !m.Read);

        /// <summary>Neuer Brief im Speicher; der Aufrufer schreibt ihn mit seinem EconomyCommit.</summary>
        public MailData Create(long recipientId, long? senderId, string senderName, string subject, string body, long money, IEnumerable<ItemStack> items,
                               TimeSpan? delay = null, long cod = 0)
        {
            var m = new MailData
            {
                Id = _nextId++, RecipientId = recipientId, SenderId = senderId, SenderName = senderName, Subject = Trim(subject, 64), Body = Trim(body, 500),
                Money = money, Cod = cod, Items = items.ToList(), DeliverAt = DateTime.UtcNow + (delay ?? TimeSpan.Zero),
                ExpiresAt = DateTime.UtcNow + (delay ?? TimeSpan.Zero) + (cod > 0 ? CodLifetime : Lifetime),
            };
            Insert(m);
            return m;
        }

        /// <summary>Systembrief (Auktionshaus, Beute bei vollen Taschen) mit sofortigem Schreiben.</summary>
        public void SendSystem(long recipientId, string senderName, string subject, string body, long money, IEnumerable<ItemStack> items, params Player[] involved)
        {
            var m = Create(recipientId, null, senderName, subject, body, money, items);
            var c = new EconomyCommit();
            c.UpsertMail.Add(m);
            _w.Economy.Commit(c, involved);
            Announce(recipientId);
        }

        private static string Trim(string s, int max) => s.Length > max ? s.Substring(0, max) : s;

        private void Announce(long recipientId)
        {
            var p = _w.AllPlayers.FirstOrDefault(x => x.Data.Id == recipientId && !x.IsBot);
            if (p != null) p.Send(new S_NewMail { Unread = Unread(recipientId) });
        }

        // ------------------------------------------------------------ Briefkasten

        public bool NearMailbox(Player p, int objectId) =>
            p.Zone?.Get(objectId) is GameObjectEntity go && go.Def.IsMailbox && p.DistanceTo(go) <= MailboxRange && !p.IsDead;

        public S_MailList BuildList(Player p) => new()
        {
            Mails = Inbox(p.Data.Id).Select(m => new MailSummaryData
            {
                Id = m.Id, Sender = m.SenderName, Subject = m.Subject, Body = m.Body, Money = m.Money, Cod = m.Cod, Items = m.Items.Select(i => i.ToNet()).ToList(),
                DaysLeft = (float)(m.ExpiresAt - DateTime.UtcNow).TotalDays, IsRead = m.Read, Returned = m.Returned,
            }).ToList(),
        };

        public void Open(Player p)
        {
            foreach (var m in Inbox(p.Data.Id)) m.Read = true;
            p.Send(BuildList(p));
        }

        /// <summary>
        /// Brief verschicken. Der Empfänger kann offline sein; er wird über die Datenbank gesucht (Persistenz-Warteschlange)
        /// und die eigentliche Ausführung läuft danach wieder im Spielthread.
        /// </summary>
        public void Send(Player from, C_MailSend m)
        {
            string to = (m.To ?? "").Trim();
            if (to.Length == 0) { from.Notice("Empfänger fehlt.", true); return; }
            var online = _w.FindPlayer(to);
            if (online != null) { Execute(from, new CharacterRef { Id = online.Data.Id, AccountId = online.Data.AccountId, Name = online.Name, Race = online.Race }, m); return; }
            if (_w.EconomyStore == null || _w.Persistence == null) { from.Notice("Empfänger nicht gefunden.", true); return; }
            var store = _w.EconomyStore;
            _w.Persistence.EnqueueAsync(() => store.FindCharacterAsync(to)).ContinueWith(t =>
            {
                var r = t.IsCompletedSuccessfully ? t.Result : null;
                _w.Post(() =>
                {
                    if (from.Removed) return;
                    if (r == null) { from.Notice("Empfänger nicht gefunden.", true); return; }
                    Execute(from, r, m);
                });
            });
        }

        private void Execute(Player from, CharacterRef to, C_MailSend m)
        {
            var err = Validate(from, to, m, out long cost);
            if (err != null) { from.Notice(err, true); return; }
            var items = new List<ItemStack>();
            foreach (var i in m.BagIndices.Distinct())
            {
                items.Add(from.Data.Bag[i]!);
                from.Data.Bag[i] = null;
            }
            _w.Economy.Debit(from, cost, "mail_send", to.Name);
            bool sameAccount = to.AccountId != null && to.AccountId == from.Data.AccountId;
            var mail = Create(to.Id, from.Data.Id, from.Name, m.Subject, m.Body, m.Money, items, items.Count > 0 && !sameAccount ? ItemDelay : TimeSpan.Zero, m.Cod);
            var c = new EconomyCommit();
            c.UpsertMail.Add(mail);
            _w.Economy.Commit(c, from);
            from.Send(from.BuildInventory());
            from.Send(from.BuildPlayerState());
            from.Notice($"Brief an {to.Name} verschickt.");
            if (mail.DeliverAt <= DateTime.UtcNow) Announce(to.Id);
        }

        private string? Validate(Player from, CharacterRef to, C_MailSend m, out long cost)
        {
            cost = 0;
            if (to.Id == from.Data.Id) return "Ihr könnt Euch nicht selbst schreiben.";
            if (_w.Content.Races.TryGetValue(to.Race, out var race) && race.Faction != from.Faction) return "Briefe an die andere Fraktion werden nicht zugestellt.";
            if (m.Money < 0 || m.Cod < 0) return "Ungültiger Betrag.";
            if (m.Cod > 0 && m.BagIndices.Count == 0) return "Nachnahme nur mit Anhang.";
            if (m.Cod > 0 && m.Money > 0) return "Nachnahme und Geld zugleich sind nicht möglich.";
            var idx = m.BagIndices.Distinct().ToList();
            if (idx.Count != m.BagIndices.Count || idx.Count > MaxItems) return $"Höchstens {MaxItems} verschiedene Anhänge.";
            foreach (var i in idx)
            {
                if (i < 0 || i >= from.Data.Bag.Count || from.Data.Bag[i] is not { } s) return "Ungültiger Anhang.";
                var def = _w.Content.Items[s.ItemId];
                if (def.Category == ItemCategory.Quest || def.BindOnPickup) return $"{def.Name} ist gebunden und kann nicht verschickt werden.";
            }
            cost = m.Money + PostagePerItem * Math.Max(1, idx.Count);
            if (from.Money < cost) return "Ihr habt nicht genug Geld (inkl. Porto).";
            return null;
        }

        /// <summary>Anhang entnehmen: −1 Geld, 0..11 Gegenstand, 255 alles. Bei Nachnahme wird zuerst bezahlt.</summary>
        public string? Take(Player p, long mailId, int index)
        {
            var mail = Inbox(p.Data.Id).FirstOrDefault(x => x.Id == mailId);
            if (mail == null) return "Brief nicht gefunden.";
            var c = new EconomyCommit();
            var involved = new List<Player> { p };
            if (mail.Cod > 0 && index != -1 && mail.Items.Count > 0)
            {
                if (!_w.Economy.Debit(p, mail.Cod, "mail_cod", mail.SenderName)) return "Ihr könnt die Nachnahme nicht bezahlen.";
                if (mail.SenderId is long sid)
                    c.UpsertMail.Add(Create(sid, null, "Post", $"Nachnahme bezahlt: {mail.Subject}", $"{p.Name} hat die Nachnahme bezahlt.", mail.Cod, Array.Empty<ItemStack>()));
                mail.Cod = 0;
            }
            string? err = null;
            if ((index == -1 || index == 255) && mail.Money > 0)
            {
                _w.Economy.Credit(p, mail.Money, "mail_take", mail.SenderName);
                mail.Money = 0;
            }
            if (index >= 0)
            {
                var targets = index == 255 ? mail.Items.ToList() : index < mail.Items.Count ? new List<ItemStack> { mail.Items[index] } : new List<ItemStack>();
                if (index != 255 && targets.Count == 0) err = "Kein Anhang an dieser Stelle.";
                foreach (var it in targets)
                {
                    int free = p.Data.Bag.IndexOf(null);
                    if (free < 0) { err = "Eure Taschen sind voll."; break; }
                    p.Data.Bag[free] = it;
                    mail.Items.Remove(it);
                }
            }
            c.UpsertMail.Add(mail);
            _w.Economy.Commit(c, involved.ToArray());
            p.Send(p.BuildInventory());
            p.Send(p.BuildPlayerState());
            p.Send(BuildList(p));
            _w.Quests.OnItemsChanged(p);
            return err;
        }

        public string? Delete(Player p, long mailId)
        {
            var mail = Inbox(p.Data.Id).FirstOrDefault(x => x.Id == mailId);
            if (mail == null) return "Brief nicht gefunden.";
            if (mail.Items.Count > 0 || mail.Money > 0) return "Entnehmt zuerst die Anhänge.";
            Remove(mail);
            var c = new EconomyCommit();
            c.DeleteMail.Add(mail.Id);
            _w.Economy.Commit(c);
            p.Send(BuildList(p));
            return null;
        }

        public string? Return(Player p, long mailId)
        {
            var mail = Inbox(p.Data.Id).FirstOrDefault(x => x.Id == mailId);
            if (mail == null) return "Brief nicht gefunden.";
            if (mail.SenderId == null || mail.Returned) return "Dieser Brief kann nicht zurückgeschickt werden.";
            ReturnToSender(mail);
            p.Send(BuildList(p));
            return null;
        }

        private void ReturnToSender(MailData mail)
        {
            Remove(mail);
            mail.RecipientId = mail.SenderId!.Value;
            mail.SenderId = null;
            mail.SenderName = "Post (zurück)";
            mail.Subject = "Zurück: " + mail.Subject;
            mail.Returned = true;
            mail.Cod = 0;
            mail.Read = false;
            mail.DeliverAt = DateTime.UtcNow;
            mail.ExpiresAt = DateTime.UtcNow + Lifetime;
            Insert(mail);
            var c = new EconomyCommit();
            c.UpsertMail.Add(mail);
            _w.Economy.Commit(c);
            Announce(mail.RecipientId);
        }

        /// <summary>Abgelaufene Briefe zurückschicken oder löschen, neu zugestellte ankündigen.</summary>
        public void Update(float dt)
        {
            _timer -= dt;
            if (_timer > 0) return;
            _timer = 10;
            var now = DateTime.UtcNow;
            foreach (var mail in _byRecipient.Values.SelectMany(l => l).ToList())
            {
                if (mail.ExpiresAt <= now)
                {
                    bool hasValue = mail.Items.Count > 0 || mail.Money > 0;
                    if (hasValue && mail.SenderId != null && !mail.Returned) ReturnToSender(mail);
                    else
                    {
                        Remove(mail);
                        var c = new EconomyCommit();
                        c.DeleteMail.Add(mail.Id);
                        _w.Economy.Commit(c);
                    }
                    continue;
                }
                if (mail.DeliverAt <= now && !mail.Read && _announced.Add(mail.Id)) Announce(mail.RecipientId);
            }
        }
    }
}
