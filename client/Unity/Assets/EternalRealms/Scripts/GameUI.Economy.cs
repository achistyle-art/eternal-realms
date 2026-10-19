using System.Collections.Generic;
using System.Linq;
using EternalRealms.Shared;
using EternalRealms.Shared.Client;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>Fenster für Bank, Post, Auktionshaus, Bedarf/Gier-Würfe, Beuteregel und Talent-Reset.</summary>
    public sealed partial class GameUI
    {
        private bool _bankOpen, _mailOpen, _ahOpen;
        private int _ahNpc, _ahPage, _ahTab, _ahSellIndex = -1, _ahDuration = 1;
        private string _ahQuery = "", _ahStart = "1", _ahBuyout = "", _mailTo = "", _mailSubject = "", _mailBody = "", _mailGold = "0";
        private readonly List<int> _attachments = new List<int>();
        private Vector2 _scrollMail, _scrollAh, _scrollBank;

        private void OnEconomyMessage(Message m)
        {
            switch (m)
            {
                case S_Bank: _bankOpen = true; _showBags = true; break;
                case S_MailList: _mailOpen = true; _showBags = true; break;
                case S_NewMail nm when nm.Unread > 0: Toast("Ihr habt neue Post."); break;
                case S_LootRollResult rr: Toast($"{rr.WinnerName} gewinnt {ItemName(rr.ItemId)}."); break;
            }
        }

        private string ItemName(string id) => Content.Items.TryGetValue(id, out var d) ? d.Name : id;

        private void DrawEconomyWindows()
        {
            if (_bankOpen) DrawBank();
            if (_mailOpen) DrawMail();
            if (_ahOpen) DrawAuctionHouse();
            DrawRolls();
            if (Client.TalentResetOffer != null) DrawTalentReset();
            if (Client.UnreadMail > 0) GUI.Label(Box(new Rect(UnityEngine.Screen.width - 150, 200, 140, 22)), "<color=yellow>✉ Neue Post</color>", _wrap);
        }

        // ------------------------------------------------------------ Bank

        private void DrawBank()
        {
            var bank = Client.Bank;
            if (bank == null) { _bankOpen = false; return; }
            var area = Window("Bank", 420, 420);
            GUILayout.BeginArea(area);
            GUILayout.Label("Klick auf ein Fach nimmt heraus · Rechtsklick in den Taschen lagert ein.", _wrap);
            _scrollBank = GUILayout.BeginScrollView(_scrollBank);
            for (int i = 0; i < bank.Slots.Count; i++)
            {
                var s = bank.Slots[i];
                if (s.ItemId == "") continue;
                if (GUILayout.Button($"{s.Count}× {ItemLabel(s.ItemId)}")) Client.Send(new C_BankWithdraw { NpcId = bank.NpcId, BankIndex = i });
            }
            GUILayout.EndScrollView();
            GUILayout.Label($"{bank.Slots.Count(s => s.ItemId != "")}/{bank.Slots.Count} Fächer belegt");
            if (GUILayout.Button("Schließen")) _bankOpen = false;
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------ Post

        private int NearestMailbox()
        {
            var me = Client.Me;
            if (me == null) return 0;
            return Client.State.Entities.Values.Where(e => e.TemplateId == "obj_mailbox").OrderBy(e => Vec3.DistanceXZ(e.Position, me.Position)).FirstOrDefault()?.Id ?? 0;
        }

        private void ToggleAttachment(int bagIndex)
        {
            if (_attachments.Contains(bagIndex)) _attachments.Remove(bagIndex);
            else if (_attachments.Count < 12) _attachments.Add(bagIndex);
        }

        private void DrawMail()
        {
            var area = Window("Briefkasten", 560, 520);
            GUILayout.BeginArea(area);
            int box = NearestMailbox();
            _scrollMail = GUILayout.BeginScrollView(_scrollMail, GUILayout.Height(220));
            if (Client.Mail.Count == 0) GUILayout.Label("<i>Keine Post.</i>", _wrap);
            foreach (var m in Client.Mail.ToList())
            {
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"<b>{m.Subject}</b> – von {m.Sender} ({m.DaysLeft:0} Tage)\n{m.Body}", _wrap);
                string att = string.Join(", ", m.Items.Select(i => $"{i.Count}× {ItemLabel(i.ItemId)}"));
                if (m.Money > 0 || att != "") GUILayout.Label($"Anhang: {(m.Money > 0 ? Money(m.Money) + " " : "")}{att}{(m.Cod > 0 ? $" – Nachnahme {Money(m.Cod)}" : "")}", _wrap);
                GUILayout.BeginHorizontal();
                if ((m.Money > 0 || m.Items.Count > 0) && GUILayout.Button(m.Cod > 0 ? "Bezahlen und nehmen" : "Alles nehmen")) Client.Send(new C_MailTake { ObjectId = box, MailId = m.Id, Index = 255 });
                if (m.Money == 0 && m.Items.Count == 0 && GUILayout.Button("Löschen")) Client.Send(new C_MailDelete { ObjectId = box, MailId = m.Id });
                if (!m.Returned && m.Sender != "Auktionshaus" && GUILayout.Button("Zurücksenden")) Client.Send(new C_MailReturn { ObjectId = box, MailId = m.Id });
                GUILayout.EndHorizontal();
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.Label("<b>Brief schreiben</b> (Rechtsklick auf Taschenplätze hängt an)", _wrap);
            GUILayout.BeginHorizontal();
            GUILayout.Label("An", GUILayout.Width(60)); _mailTo = GUILayout.TextField(_mailTo, 24);
            GUILayout.Label("Gold", GUILayout.Width(40)); _mailGold = GUILayout.TextField(_mailGold, 8, GUILayout.Width(60));
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            GUILayout.Label("Betreff", GUILayout.Width(60)); _mailSubject = GUILayout.TextField(_mailSubject, 64);
            GUILayout.EndHorizontal();
            _mailBody = GUILayout.TextArea(_mailBody, 500, GUILayout.Height(50));
            var inv = Client.State.Inventory;
            if (inv != null && _attachments.Count > 0)
                GUILayout.Label("Anhänge: " + string.Join(", ", _attachments.Where(i => i < inv.Bag.Count && inv.Bag[i].ItemId != "").Select(i => ItemLabel(inv.Bag[i].ItemId))), _wrap);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button($"Senden (Porto {Money(30L * System.Math.Max(1, _attachments.Count))})"))
            {
                long money = (long)(float.TryParse(_mailGold.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var g) ? g * 10000 : 0);
                Client.Send(new C_MailSend { ObjectId = box, To = _mailTo, Subject = _mailSubject, Body = _mailBody, Money = money, BagIndices = _attachments.ToList() });
                _attachments.Clear(); _mailSubject = ""; _mailBody = ""; _mailGold = "0";
            }
            if (GUILayout.Button("Schließen")) { _mailOpen = false; _attachments.Clear(); }
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------ Auktionshaus

        /// <summary>Wird aus dem NPC-Fenster eines Auktionators geöffnet.</summary>
        public void OpenAuctionHouse(int npcId)
        {
            _ahNpc = npcId;
            _ahOpen = true;
            _showBags = true;
            Client.Send(new C_AuctionSearch { NpcId = npcId });
        }

        private void DrawAuctionHouse()
        {
            var area = Window("Auktionshaus", 640, 540);
            GUILayout.BeginArea(area);
            _ahTab = GUILayout.Toolbar(_ahTab, new[] { "Kaufen", "Verkaufen", "Meine Angebote", "Meine Gebote" });
            if (_ahTab == 0)
            {
                GUILayout.BeginHorizontal();
                _ahQuery = GUILayout.TextField(_ahQuery, 40);
                if (GUILayout.Button("Suchen", GUILayout.Width(90))) { _ahPage = 0; Client.Send(new C_AuctionSearch { NpcId = _ahNpc, Query = _ahQuery }); }
                GUILayout.EndHorizontal();
            }
            else if (_ahTab == 1) DrawAuctionSell();
            var list = Client.Auctions;
            if (_ahTab != 1 && list != null)
            {
                if ((_ahTab == 2 && list.Kind != 1) || (_ahTab == 3 && list.Kind != 2)) Client.Send(new C_AuctionMine { NpcId = _ahNpc, Bids = _ahTab == 3 });
                GUILayout.Label($"{list.Total} Angebote", _wrap);
                _scrollAh = GUILayout.BeginScrollView(_scrollAh, GUILayout.Height(330));
                foreach (var a in list.Auctions)
                {
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    string time = a.TimeLeft switch { 0 => "kurz", 1 => "mittel", 2 => "lang", _ => "sehr lang" };
                    GUILayout.Label($"{a.Count}× {ItemLabel(a.ItemId)}\n<size=10>{a.Seller} · {time}{(a.IsMyBid ? " · <color=green>Höchstgebot</color>" : "")}</size>", _wrap, GUILayout.Width(250));
                    GUILayout.Label($"Gebot {Money(a.MinBid)}" + (a.Buyout > 0 ? $"\nSofort {Money(a.Buyout)}" : ""), _wrap, GUILayout.Width(150));
                    if (!a.IsMine && !a.IsMyBid && GUILayout.Button("Bieten")) Client.Send(new C_AuctionBid { NpcId = _ahNpc, AuctionId = a.Id, Amount = a.MinBid });
                    if (!a.IsMine && a.Buyout > 0 && GUILayout.Button("Kaufen")) Client.Send(new C_AuctionBid { NpcId = _ahNpc, AuctionId = a.Id, Amount = a.Buyout });
                    if (a.IsMine && GUILayout.Button("Abbrechen")) Client.Send(new C_AuctionCancel { NpcId = _ahNpc, AuctionId = a.Id });
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
                if (_ahTab == 0)
                {
                    GUILayout.BeginHorizontal();
                    if (_ahPage > 0 && GUILayout.Button("◀")) Client.Send(new C_AuctionSearch { NpcId = _ahNpc, Query = _ahQuery, Page = --_ahPage });
                    if ((_ahPage + 1) * 50 < list.Total && GUILayout.Button("▶")) Client.Send(new C_AuctionSearch { NpcId = _ahNpc, Query = _ahQuery, Page = ++_ahPage });
                    GUILayout.EndHorizontal();
                }
            }
            if (GUILayout.Button("Schließen")) _ahOpen = false;
            GUILayout.EndArea();
        }

        private void DrawAuctionSell()
        {
            var inv = Client.State.Inventory;
            if (inv == null || _ahSellIndex < 0 || _ahSellIndex >= inv.Bag.Count || inv.Bag[_ahSellIndex].ItemId == "")
            {
                GUILayout.Label("Rechtsklick auf einen Gegenstand in den Taschen wählt ihn zum Verkauf aus.", _wrap);
                return;
            }
            var st = inv.Bag[_ahSellIndex];
            GUILayout.Label($"Verkaufen: {st.Count}× {ItemLabel(st.ItemId)}", _wrap);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Mindestgebot (Gold)", GUILayout.Width(140)); _ahStart = GUILayout.TextField(_ahStart, 10, GUILayout.Width(80));
            GUILayout.Label("Sofortkauf (Gold)", GUILayout.Width(120)); _ahBuyout = GUILayout.TextField(_ahBuyout, 10, GUILayout.Width(80));
            GUILayout.EndHorizontal();
            _ahDuration = GUILayout.Toolbar(_ahDuration, new[] { "12 Stunden", "24 Stunden", "48 Stunden" });
            if (GUILayout.Button("Einstellen"))
            {
                long G(string t) => (long)(float.TryParse(t.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v * 10000 : 0);
                Client.Send(new C_AuctionPost { NpcId = _ahNpc, BagIndex = _ahSellIndex, Count = st.Count, StartBid = G(_ahStart), Buyout = G(_ahBuyout), DurationHours = new[] { 12, 24, 48 }[_ahDuration] });
                _ahSellIndex = -1;
                _ahTab = 2;
            }
        }

        // ------------------------------------------------------------ Würfe und Talente

        private void DrawRolls()
        {
            int i = 0;
            foreach (var r in Client.OpenRolls.Values.ToList())
            {
                var rect = Box(new Rect(UnityEngine.Screen.width / 2 - 170, 120 + i++ * 64, 340, 58));
                GUI.Box(rect, "");
                GUI.Label(new Rect(rect.x + 8, rect.y + 4, 324, 22), $"{r.Count}× {ItemLabel(r.ItemId)}", _wrap);
                GUI.enabled = r.CanNeed;
                if (GUI.Button(new Rect(rect.x + 8, rect.y + 28, 100, 24), "Bedarf")) Client.Roll(r.RollId, RollChoice.Need);
                GUI.enabled = true;
                if (GUI.Button(new Rect(rect.x + 118, rect.y + 28, 100, 24), "Gier")) Client.Roll(r.RollId, RollChoice.Greed);
                if (GUI.Button(new Rect(rect.x + 228, rect.y + 28, 100, 24), "Passen")) Client.Roll(r.RollId, RollChoice.Pass);
            }
        }

        private void DrawTalentReset()
        {
            var offer = Client.TalentResetOffer!;
            var r = Box(new Rect(UnityEngine.Screen.width / 2 - 180, UnityEngine.Screen.height / 2 - 50, 360, 100));
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x + 10, r.y + 8, 340, 40), $"Alle Talente zurücksetzen für {Money(offer.Cost)}?", _wrap);
            if (GUI.Button(new Rect(r.x + 10, r.y + 60, 160, 28), "Zurücksetzen")) { Client.Send(new C_ResetTalents { NpcId = offer.NpcId, Confirm = true }); Client.TalentResetOffer = null; }
            if (GUI.Button(new Rect(r.x + 190, r.y + 60, 160, 28), "Abbrechen")) Client.TalentResetOffer = null;
        }

        /// <summary>Beuteregel im Gruppenfenster (nur Anführer).</summary>
        private void DrawLootRule()
        {
            var lm = Client.LootMethod;
            GUILayout.Label($"Beuteregel: <b>{LootName(lm.Method)}</b> ab {lm.Threshold}", _wrap);
            if (Client.GroupLeaderId != Client.State.MyId) return;
            GUILayout.BeginHorizontal();
            foreach (LootMethod m in System.Enum.GetValues(typeof(LootMethod)))
                if (GUILayout.Button(LootName(m))) Client.Send(new C_SetLootMethod { Method = m, Threshold = lm.Threshold, MasterLooterId = Client.State.MyId });
            GUILayout.EndHorizontal();
        }

        private static string LootName(LootMethod m) => m switch
        {
            LootMethod.FreeForAll => "Jeder für sich", LootMethod.RoundRobin => "Reihum", LootMethod.GroupLoot => "Gruppe",
            LootMethod.NeedBeforeGreed => "Bedarf vor Gier", _ => "Plündermeister",
        };

        // ------------------------------------------------------------ Reittiere

        private bool _showMounts;

        private void DrawMountJournal()
        {
            var j = Client.MountJournal;
            var area = Window("Reittiere (J) – Aufsitzen mit Y", 420, 420);
            GUILayout.BeginArea(area);
            string skill = j.RidingSkill >= 150 ? "Experte (+100 %)" : j.RidingSkill >= 75 ? "Lehrling (+60 %)" : "keine – beim Stallmeister ab Stufe 40";
            GUILayout.Label($"Reitfertigkeit: {skill}", _wrap);
            if (j.Mounts.Count == 0) GUILayout.Label("<i>Noch keine Reittiere.</i>", _wrap);
            foreach (var id in j.Mounts)
            {
                if (!Content.Mounts.TryGetValue(id, out var m)) continue;
                string col = m.Quality switch { ItemQuality.Rare => "#0070dd", ItemQuality.Epic => "#a335ee", ItemQuality.Legendary => "#ff8000", _ => "#1eff00" };
                if (GUILayout.Button($"<color={col}>{m.Name}</color> (+{m.Speed} %){(j.Active == id ? " – aktiv" : "")}", new GUIStyle(GUI.skin.button) { richText = true }))
                    Client.Send(new C_Mount { MountId = id });
            }
            if (GUILayout.Button("Schließen")) _showMounts = false;
            GUILayout.EndArea();
        }
    }
}