using System.Collections.Generic;
using System.Linq;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>Gruppenfinder „Abenteurer suchen“, Bereitschaftscheck, Abstimmung, Rollen und Untergruppen (Kapitel 9.5).</summary>
    public sealed partial class GameUI
    {
        private readonly HashSet<string> _finderPick = new HashSet<string>();
        private readonly bool[] _finderRoles = new bool[4];
        private int _finderMode = 1;
        private bool _finderRequested;

        private void DrawFinder()
        {
            GUILayout.Space(6);
            GUILayout.Label("<b>Abenteurer suchen</b>", _wrap);
            if (!_finderRequested) { Client.Send(new C_FinderActivities()); _finderRequested = true; }
            var st = Client.FinderStatus;
            if (st.Queued)
            {
                GUILayout.Label($"{st.Text} – Wartezeit {st.WaitSeconds:0} s", _wrap);
                if (GUILayout.Button("Suche beenden")) Client.Send(new C_FinderLeave());
                return;
            }
            GUILayout.BeginHorizontal();
            foreach (var a in Client.FinderActivities)
            {
                bool on = _finderPick.Contains(a.Id);
                bool now = GUILayout.Toggle(on, $"{a.Name} ({a.MinLevel}–{a.MaxLevel})");
                if (now && !on) _finderPick.Add(a.Id); else if (!now && on) _finderPick.Remove(a.Id);
            }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            var roleNames = new[] { "Tank", "Heiler", "Nahkampf", "Fernkampf" };
            for (int i = 0; i < 4; i++) _finderRoles[i] = GUILayout.Toggle(_finderRoles[i], roleNames[i]);
            GUILayout.EndHorizontal();
            _finderMode = GUILayout.Toolbar(_finderMode, new[] { "Nur Menschen", "Menschen bevorzugt", "Sofort mit KI" });
            if (GUILayout.Button("Gruppe suchen") && _finderPick.Count > 0)
            {
                var join = new C_FinderJoin { Activities = _finderPick.ToList(), Mode = (FinderAiMode)_finderMode };
                for (int i = 0; i < 4; i++) if (_finderRoles[i]) join.Roles.Add((byte)i);
                Client.Send(join);
            }
        }

        private void DrawGroupTools()
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Bereitschaftscheck")) Client.Send(new C_ReadyCheck());
            if (GUILayout.Button("Zum Schlachtzug")) Client.Send(new C_ConvertToRaid());
            GUILayout.Label("Meine Rolle:", GUILayout.Width(80));
            if (GUILayout.Button("T")) Client.Send(new C_SetRole { Role = Role.Tank });
            if (GUILayout.Button("H")) Client.Send(new C_SetRole { Role = Role.Healer });
            if (GUILayout.Button("N")) Client.Send(new C_SetRole { Role = Role.MeleeDps });
            if (GUILayout.Button("F")) Client.Send(new C_SetRole { Role = Role.RangedDps });
            GUILayout.EndHorizontal();
        }

        private void DrawFinderPopups()
        {
            var y = 300f;
            if (Client.FinderProposal is S_FinderProposal fp)
            {
                var r = Box(new Rect(ScreenW / 2 - 200, y, 400, 120));
                GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 10, r.y + 6, 380, 70), $"<b>{(fp.IsReplacement ? "Platz frei" : "Gruppe gefunden")}: {fp.Activity}</b>\nEure Rolle: {RoleName(fp.Role)}\n{string.Join(", ", fp.Members)}", _wrap);
                if (GUI.Button(new Rect(r.x + 10, r.y + 84, 180, 28), "Bereit")) { Client.Send(new C_FinderAccept { ProposalId = fp.ProposalId, Accept = true }); Client.FinderProposal = null; }
                if (GUI.Button(new Rect(r.x + 210, r.y + 84, 180, 28), "Ablehnen")) { Client.Send(new C_FinderAccept { ProposalId = fp.ProposalId, Accept = false }); Client.FinderProposal = null; }
                y += 130;
            }
            if (Client.ReadyCheck is S_ReadyCheck rc)
            {
                var r = Box(new Rect(ScreenW / 2 - 160, y, 320, 80));
                GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 10, r.y + 6, 300, 30), $"{rc.From}: Seid Ihr bereit?", _wrap);
                if (GUI.Button(new Rect(r.x + 10, r.y + 44, 140, 26), "Bereit")) { Client.Send(new C_ReadyResponse { Ready = true }); Client.ReadyCheck = null; }
                if (GUI.Button(new Rect(r.x + 170, r.y + 44, 140, 26), "Nicht bereit")) { Client.Send(new C_ReadyResponse { Ready = false }); Client.ReadyCheck = null; }
                y += 90;
            }
            if (Client.VoteKick is S_VoteKick vk)
            {
                var r = Box(new Rect(ScreenW / 2 - 180, y, 360, 90));
                GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 10, r.y + 6, 340, 44), $"{vk.TargetName} aus der Gruppe entfernen?\nGrund: {vk.Reason}", _wrap);
                if (GUI.Button(new Rect(r.x + 10, r.y + 56, 160, 26), "Ja")) { Client.Send(new C_VoteKickResponse { VoteId = vk.VoteId, Yes = true }); Client.VoteKick = null; }
                if (GUI.Button(new Rect(r.x + 190, r.y + 56, 160, 26), "Nein")) { Client.Send(new C_VoteKickResponse { VoteId = vk.VoteId, Yes = false }); Client.VoteKick = null; }
            }
        }
    }
}
