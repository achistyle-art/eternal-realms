using System.Linq;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>Sozialmenü (Taste O): Freunde, KI-Begleiter, Gilde, Ignorierliste (Kapitel 22, 32).</summary>
    public sealed partial class GameUI
    {
        private bool _showSocial;
        private int _socialTab;
        private string _socialName = "", _guildMotd = "";
        private Vector2 _scrollSocial;

        private void DrawSocial()
        {
            var area = Window("Sozial (O)", 620, 520);
            GUILayout.BeginArea(area);
            _socialTab = GUILayout.Toolbar(_socialTab, new[] { "Freunde", "KI-Begleiter", "Gilde", "Ignoriert" });
            var fl = Client.Friends;
            _scrollSocial = GUILayout.BeginScrollView(_scrollSocial, GUILayout.Height(360));
            switch (_socialTab)
            {
                case 0:
                    foreach (var f in fl.Friends)
                    {
                        GUILayout.BeginHorizontal(GUI.skin.box);
                        GUILayout.Label(f.Online ? $"<b>{f.Name}</b> – St. {f.Level} {ClassName(f.Class)} – {(f.Zone != "" ? f.Zone : "Standort verborgen")}" : $"<color=grey>{f.Name} – offline</color>", _wrap, GUILayout.Width(380));
                        if (f.Online && GUILayout.Button("Einladen")) Client.Send(new C_GroupInvite { Name = f.Name });
                        if (f.Online && GUILayout.Button("Flüstern")) { _chatInput = $"/w {f.Name} "; }
                        if (GUILayout.Button("✕", GUILayout.Width(28))) Client.Send(new C_FriendRemove { CharacterId = f.CharacterId });
                        GUILayout.EndHorizontal();
                    }
                    break;
                case 1:
                    GUILayout.Label("Gespeicherte Lieblingsbegleiter (★ im Gruppenfenster). Sie entwickeln sich weiter und erinnern sich an Euch.", _wrap);
                    foreach (var f in fl.Companions)
                    {
                        GUILayout.BeginVertical(GUI.skin.box);
                        GUILayout.Label($"<b>{f.Name}</b> <color=cyan>(KI)</color> – St. {f.Level} {ClassName(f.Class)}, {RoleName(f.Role)}, Gegenstandsstufe {f.ItemLevel}\n" +
                                        $"Aktivität: {f.Activity} · {f.Availability}" + (f.SharedAdventures.Count > 0 ? "\nGemeinsam erlebt: " + string.Join(" · ", f.SharedAdventures) : ""), _wrap);
                        if (!f.Availability.StartsWith("in Eurer") && GUILayout.Button("In die Gruppe rufen")) Client.Send(new C_InviteFavorite { CharacterId = f.CharacterId });
                        GUILayout.EndVertical();
                    }
                    break;
                case 2:
                    var g = Client.Guild;
                    if (g.Name == "")
                    {
                        GUILayout.Label("Ihr seid in keiner Gilde. Gründung: 1 Gold.", _wrap);
                        break;
                    }
                    GUILayout.Label($"<b>&lt;{g.Name}&gt;</b> – {g.Members.Count} Mitglieder\n<i>{g.Motd}</i>", _wrap);
                    foreach (var m in g.Members)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label($"{g.RankNames[m.Rank],-14} {m.Name}{(m.IsBot ? " <color=cyan>(KI)</color>" : "")} – St. {m.Level} {(m.Online ? "<color=green>online</color> " + m.Zone : "<color=grey>offline</color>")}", _wrap, GUILayout.Width(400));
                        if (GUILayout.Button("▲", GUILayout.Width(28)) && m.Rank > 1) Client.Send(new C_GuildSetRank { CharacterId = m.CharacterId, Rank = m.Rank - 1 });
                        if (GUILayout.Button("▼", GUILayout.Width(28)) && m.Rank < 4) Client.Send(new C_GuildSetRank { CharacterId = m.CharacterId, Rank = m.Rank + 1 });
                        if (GUILayout.Button("✕", GUILayout.Width(28))) Client.Send(new C_GuildKick { CharacterId = m.CharacterId });
                        GUILayout.EndHorizontal();
                    }
                    break;
                case 3:
                    for (int i = 0; i < fl.Ignored.Count; i++)
                    {
                        GUILayout.BeginHorizontal();
                        GUILayout.Label(fl.Ignored[i], GUILayout.Width(400));
                        if (GUILayout.Button("Nicht mehr ignorieren")) Client.Send(new C_IgnoreRemove { CharacterId = fl.IgnoredIds[i] });
                        GUILayout.EndHorizontal();
                    }
                    break;
            }
            GUILayout.EndScrollView();
            GUILayout.BeginHorizontal();
            _socialName = GUILayout.TextField(_socialName, 40, GUILayout.Width(200));
            if (_socialTab == 0 && GUILayout.Button("Freund hinzufügen")) Client.Send(new C_FriendAdd { Text = _socialName });
            if (_socialTab == 2 && Client.Guild.Name == "" && GUILayout.Button("Gilde gründen")) Client.Send(new C_GuildCreate { Text = _socialName });
            if (_socialTab == 2 && Client.Guild.Name != "" && GUILayout.Button("Einladen")) Client.Send(new C_GuildInvite { Text = _socialName });
            if (_socialTab == 3 && GUILayout.Button("Ignorieren")) Client.Send(new C_IgnoreAdd { Text = _socialName });
            if (GUILayout.Button("Melden")) Client.Send(new C_ReportPlayer { Name = _socialName, Reason = "Spielermeldung" });
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            if (_socialTab == 2 && Client.Guild.Name != "")
            {
                _guildMotd = GUILayout.TextField(_guildMotd, 200, GUILayout.Width(300));
                if (GUILayout.Button("Nachricht des Tages")) Client.Send(new C_GuildMotd { Text = _guildMotd });
                if (GUILayout.Button("Verlassen")) Client.Send(new C_GuildLeave());
            }
            if (_socialTab == 0)
            {
                bool share = GUILayout.Toggle(fl.ShareLocation, "Freunde sehen meinen Standort");
                if (share != fl.ShareLocation) Client.Send(new C_ShareLocation { Share = share });
            }
            if (GUILayout.Button("Schließen")) _showSocial = false;
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private string ClassName(CharClass c) => Content.Classes.TryGetValue(c, out var d) ? d.Name : "";

        private void DrawGuildInvite()
        {
            var inv = Client.PendingGuildInvite!;
            var r = Box(new Rect(ScreenW / 2 - 170, 220, 340, 90));
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x + 10, r.y + 8, 320, 40), $"{inv.From} lädt Euch in die Gilde &lt;{inv.GuildName}&gt; ein.", _wrap);
            if (GUI.Button(new Rect(r.x + 10, r.y + 52, 150, 28), "Beitreten")) { Client.Send(new C_GuildRespond { Accept = true }); Client.PendingGuildInvite = null; }
            if (GUI.Button(new Rect(r.x + 180, r.y + 52, 150, 28), "Ablehnen")) { Client.Send(new C_GuildRespond { Accept = false }); Client.PendingGuildInvite = null; }
        }
    }
}
