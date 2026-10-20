using System.Linq;
using EternalRealms.Shared;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>Direkter Handel (Kapitel 46) und Gegenstandslinks im Chat (Kapitel 31).</summary>
    public sealed partial class GameUI
    {
        private string _tradeGold = "0", _tradeSilver = "0", _tradeCopper = "0";
        private string _pinnedLink = "";

        private void OfferToTrade(int bagIndex)
        {
            var t = Client.Trade;
            if (t == null) return;
            int free = t.MyItems.FindIndex(x => x.ItemId == "");
            if (free < 0) { Toast("Alle sechs Handelsplätze sind belegt."); return; }
            Client.Send(new C_TradeSetItem { Slot = free, BagIndex = bagIndex });
        }

        private void DrawTradeWindows()
        {
            var req = Client.TradeRequest;
            if (req != null)
            {
                var r = Box(new Rect(ScreenW / 2 - 170, 300, 340, 84));
                GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 10, r.y + 8, 320, 30), $"{req.FromName} möchte mit Euch handeln.", _wrap);
                if (GUI.Button(new Rect(r.x + 10, r.y + 48, 150, 26), "Handeln")) { Client.Send(new C_TradeRespond { Accept = true }); Client.TradeRequest = null; }
                if (GUI.Button(new Rect(r.x + 180, r.y + 48, 150, 26), "Ablehnen")) { Client.Send(new C_TradeRespond { Accept = false }); Client.TradeRequest = null; }
            }
            if (Client.Trade != null) DrawTrade(Client.Trade);
        }

        private void DrawTrade(S_TradeState t)
        {
            if (!_showBags) _showBags = true; // Taschen offen halten: Rechtsklick legt Gegenstände hinein
            var area = Window($"Handel mit {t.PartnerName}{(t.PartnerIsBot ? " (KI)" : "")}", 560, 420);
            GUILayout.BeginArea(area);
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(area.width / 2 - 6));
            GUILayout.Label($"<b>Ihr gebt</b>{(t.MyAccepted ? " <color=lime>✔ bestätigt</color>" : "")}", _wrap);
            for (int i = 0; i < t.MyItems.Count; i++)
            {
                var it = t.MyItems[i];
                var label = it.ItemId == "" ? "<color=#777>– leer (Rechtsklick in den Taschen) –</color>" : $"{it.Count}× {ItemLabel(it.ItemId)}";
                var rr = GUILayoutUtility.GetRect(new GUIContent(label), GUI.skin.button);
                if (it.ItemId != "" && rr.Contains(Event.current.mousePosition) && Content.Items.TryGetValue(it.ItemId, out var d)) _tooltip = ItemTooltipText(d, it);
                if (GUI.Button(rr, label) && it.ItemId != "") Client.Send(new C_TradeSetItem { Slot = i, BagIndex = -1 });
            }
            GUILayout.Label($"Geld: {Money(t.MyMoney)}", _wrap);
            GUILayout.BeginHorizontal();
            _tradeGold = GUILayout.TextField(_tradeGold, 6, GUILayout.Width(50)); GUILayout.Label("g", GUILayout.Width(12));
            _tradeSilver = GUILayout.TextField(_tradeSilver, 2, GUILayout.Width(32)); GUILayout.Label("s", GUILayout.Width(12));
            _tradeCopper = GUILayout.TextField(_tradeCopper, 2, GUILayout.Width(32)); GUILayout.Label("k", GUILayout.Width(12));
            if (GUILayout.Button("Setzen"))
            {
                long.TryParse(_tradeGold, out var g); long.TryParse(_tradeSilver, out var s); long.TryParse(_tradeCopper, out var k);
                Client.Send(new C_TradeSetMoney { Money = System.Math.Max(0, g * 10000 + s * 100 + k) });
            }
            GUILayout.EndHorizontal();
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            GUILayout.Label($"<b>{t.PartnerName} gibt</b>{(t.TheirAccepted ? " <color=lime>✔ bestätigt</color>" : "")}", _wrap);
            foreach (var it in t.TheirItems)
            {
                var label = it.ItemId == "" ? "<color=#777>–</color>" : $"{it.Count}× {ItemLabel(it.ItemId)}";
                var rr = GUILayoutUtility.GetRect(new GUIContent(label), _wrap);
                GUI.Label(rr, label, _wrap);
                if (it.ItemId != "" && rr.Contains(Event.current.mousePosition) && Content.Items.TryGetValue(it.ItemId, out var d)) _tooltip = ItemTooltipText(d, it);
            }
            GUILayout.Label($"Geld: {Money(t.TheirMoney)}", _wrap);
            if (t.PartnerIsBot) GUILayout.Label("<size=11>KI-Mitspieler nehmen Geschenke an und legen Verbesserungen sofort an; sie geben selbst nichts her.</size>", _wrap);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            GUILayout.Label("<size=11>Jede Änderung hebt beide Bestätigungen auf. Getauscht wird erst, wenn beide denselben Stand bestätigt haben.</size>", _wrap);
            GUILayout.BeginHorizontal();
            if (!t.MyAccepted) { if (GUILayout.Button("Handel bestätigen", GUILayout.Height(28))) Client.TradeAccept(true); }
            else if (GUILayout.Button("Bestätigung zurücknehmen", GUILayout.Height(28))) Client.TradeAccept(false);
            if (GUILayout.Button("Abbrechen", GUILayout.Height(28))) Client.Send(new C_TradeCancel());
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ------------------------------------------------------------ Links im Chat

        private void InsertItemLink(string itemId)
        {
            _chatInput = (_chatInput.Length > 0 && !_chatInput.EndsWith(" ") ? _chatInput + " " : _chatInput) + ChatLinks.Item(itemId) + " ";
            _focusChat = true;
        }

        private string LinkColor(ChatLink l) => l.Kind switch
        {
            ChatLinkKind.Item => Content.Items.TryGetValue(l.Id, out var it) ? Palette.Quality(it.Quality) : "#ffffff",
            ChatLinkKind.Quest => "#ffd200",
            _ => "#71d5ff",
        };

        /// <summary>Links als farbige [Namen] im Chattext.</summary>
        private string RenderLinks(string text, string baseColor)
        {
            if (text.IndexOf('|') < 0) return text;
            var sb = new System.Text.StringBuilder();
            foreach (var (part, link) in ChatLinks.Split(text))
                sb.Append(link == null ? part : $"</color><color={LinkColor(link.Value)}>[{ChatLinks.Name(link.Value, Content)}]</color><color={baseColor}>");
            return sb.ToString();
        }

        /// <summary>Unter einer Zeile mit Links: kleine Knöpfe, die beim Überfahren den Tooltip zeigen und beim Klick festhalten.</summary>
        private void DrawLinkButtons(string text)
        {
            if (text.IndexOf('|') < 0) return;
            var links = ChatLinks.Split(text).Where(x => x.link != null).Select(x => x.link!.Value).ToList();
            if (links.Count == 0) return;
            GUILayout.BeginHorizontal();
            foreach (var l in links)
            {
                var label = $"<color={LinkColor(l)}>[{ChatLinks.Name(l, Content)}]</color>";
                var rr = GUILayoutUtility.GetRect(new GUIContent(label), _small, GUILayout.ExpandWidth(false));
                if (GUI.Button(rr, label, _small)) _pinnedLink = _pinnedLink == LinkTooltip(l) ? "" : LinkTooltip(l);
                if (rr.Contains(Event.current.mousePosition)) _tooltip = LinkTooltip(l);
            }
            GUILayout.EndHorizontal();
        }

        private string LinkTooltip(ChatLink l)
        {
            switch (l.Kind)
            {
                case ChatLinkKind.Item: return Content.Items.TryGetValue(l.Id, out var it) ? ItemTooltipText(it, null) : l.Id;
                case ChatLinkKind.Quest:
                    if (!Content.Quests.TryGetValue(l.Id, out var q)) return l.Id;
                    return $"<b><color=#ffd200>{q.Title}</color></b>\nStufe {q.Level}\n{q.ObjectivesText}";
                default:
                    var ab = Content.GetAbilityRank(l.Id, 1);
                    return ab == null ? l.Id : $"<b>{ab.Name}</b>\n{ab.Description}";
            }
        }

        private void DrawPinnedLink()
        {
            if (_pinnedLink == "") return;
            float w = 320, h = _wrap!.CalcHeight(new GUIContent(_pinnedLink), w - 12) + 34;
            var r = Box(new Rect(480, ScreenH - 270 - h - 4, w, h));
            GUI.Box(r, "");
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x + 6, r.y + 4, w - 12, h - 30), _pinnedLink, _wrap);
            if (GUI.Button(new Rect(r.xMax - 70, r.yMax - 24, 64, 20), "Schließen")) _pinnedLink = "";
        }
    }
}
