using System.Linq;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>Erfolge und Titel (Kapitel 38) sowie das Tutorial-Fenster der KI-Erklärerin (Kapitel 39).</summary>
    public sealed partial class GameUI
    {
        private bool _showAchievements;
        private string _achCategory = "";
        private Vector2 _scrollAch;
        private readonly System.Collections.Generic.List<(S_AchievementEarned a, float at)> _achToasts = new System.Collections.Generic.List<(S_AchievementEarned, float)>();

        private void OnProgressMessage(Message m)
        {
            if (m is S_AchievementEarned ae)
            {
                _achToasts.Add((ae, Time.time));
                AudioManager.Instance?.PlayUi("quest_done");
            }
        }

        /// <summary>Kurze Einblendung oben in der Mitte bei einem neuen Erfolg.</summary>
        private void DrawAchievementToasts()
        {
            _achToasts.RemoveAll(x => Time.time - x.at > 6);
            float y = 120;
            foreach (var (a, _) in _achToasts)
            {
                var r = new Rect(ScreenW / 2 - 200, y, 400, 54);
                GUI.Box(r, "");
                GUI.Label(new Rect(r.x + 10, r.y + 4, 380, 48), $"<color=#ffd24a><b>Erfolg errungen: {a.Name}</b></color>  +{a.Points}\n{(a.Title != "" ? $"Neuer Titel: <b>{a.Title}</b>" : "")}", _centerWrap);
                y += 58;
            }
        }

        private void DrawAchievements()
        {
            var st = Client.Achievements;
            var area = Window($"Erfolge – {st.Points} Punkte", 680, 520);
            GUILayout.BeginArea(area);
            var cats = Content.Achievements.Values.Select(a => a.Category).Distinct().ToList();
            if (!cats.Contains(_achCategory)) _achCategory = cats.FirstOrDefault() ?? "";
            int sel = GUILayout.Toolbar(cats.IndexOf(_achCategory), cats.ToArray());
            if (sel >= 0) _achCategory = cats[sel];
            GUILayout.BeginHorizontal();
            GUILayout.Label("Titel:", GUILayout.Width(40));
            if (GUILayout.Toggle(st.ActiveTitle == "", "keiner", GUI.skin.button, GUILayout.ExpandWidth(false)) && st.ActiveTitle != "") Client.Send(new C_SetTitle { Title = "" });
            foreach (var t in st.Titles)
                if (GUILayout.Toggle(st.ActiveTitle == t, t, GUI.skin.button, GUILayout.ExpandWidth(false)) && st.ActiveTitle != t) Client.Send(new C_SetTitle { Title = t });
            GUILayout.EndHorizontal();
            _scrollAch = GUILayout.BeginScrollView(_scrollAch);
            foreach (var a in Content.Achievements.Values.Where(a => a.Category == _achCategory))
            {
                var pr = st.Progress.FirstOrDefault(x => x.Id == a.Id);
                bool done = pr != null && pr.CompletedAt > 0;
                if (a.Hidden && !done) { GUILayout.Label("<color=#777>??? – verborgener Erfolg</color>", _wrap); continue; }
                if (a.Faction != null && Client.Me != null && a.Faction != Client.Me.Faction) continue;
                GUILayout.BeginVertical(GUI.skin.box);
                GUILayout.Label($"{(done ? "<color=#ffd24a>★</color>" : "☆")} <b>{a.Name}</b>  <size=11>{a.Points} Punkte{(a.Title != null ? $" · Titel „{a.Title}“" : "")}</size>", _wrap);
                GUILayout.Label($"<size=11>{a.Description}{(done ? $" – errungen am {System.DateTimeOffset.FromUnixTimeSeconds(pr!.CompletedAt).LocalDateTime:dd.MM.yyyy}" : "")}</size>", _wrap);
                if (!done)
                    for (int i = 0; i < a.Criteria.Count; i++)
                    {
                        var c = a.Criteria[i];
                        int have = pr != null && i < pr.Counts.Count ? pr.Counts[i] : 0;
                        var r = GUILayoutUtility.GetRect(200, 14, GUILayout.ExpandWidth(true));
                        Bar(r, have / (float)c.Count, Palette.Friendly, $"{(c.Description != "" ? c.Description + ": " : "")}{have}/{c.Count}");
                    }
                GUILayout.EndVertical();
            }
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        /// <summary>Tutorial: Schritt, Text mit der eigenen Tastenbelegung (vom Server eingesetzt), Fortschritt, Überspringen.</summary>
        private void DrawTutorial()
        {
            var tu = Client.Tutorial;
            if (tu == null || tu.Done || _hideUi) return;
            // Zwischen Zielrahmen (links) und Minikarte (rechts)
            float w = Mathf.Clamp(ScreenW - 730, 300, 520);
            string body = $"<b>{tu.Title}</b>  <size=11>Schritt {tu.Step + 1}/{tu.Steps}{(tu.Goal > 1 ? $" · {tu.Progress}/{tu.Goal}" : "")}</size>\n{tu.Text}{(tu.Hint != "" ? $"\n<color=#9fd3ff>Tipp: {tu.Hint}</color>" : "")}";
            float h = _wrap!.CalcHeight(new GUIContent(body), w - 110) + 14;
            var r = Box(new Rect(Mathf.Max(510, ScreenW / 2 - w / 2), 8, w, Mathf.Max(56, h)));
            GUI.Box(r, "");
            GUI.Label(new Rect(r.x + 10, r.y + 6, w - 110, h), $"<color=#7fe0ff>Lysa (KI-Erklärerin):</color> {body}", _wrap);
            if (GUI.Button(new Rect(r.xMax - 92, r.y + 8, 84, 22), "Überspringen")) Client.Send(new C_TutorialSkip());
        }
    }
}
