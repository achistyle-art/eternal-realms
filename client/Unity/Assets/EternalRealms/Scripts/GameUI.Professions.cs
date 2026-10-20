using System.Linq;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>Berufe (Taste U) und Angebot der Berufslehrer (Kapitel 17).</summary>
    public sealed partial class GameUI
    {
        private bool _showProfessions;
        private int _professionTab;
        private Vector2 _scrollProf;

        private void DrawProfessions()
        {
            var area = Window("Berufe (U)", 600, 520);
            GUILayout.BeginArea(area);
            var list = Client.Professions;
            if (list.Count == 0) GUILayout.Label("Ihr beherrscht noch keinen Beruf. Berufslehrer findet Ihr im Handwerksviertel der Städte.", _wrap);
            else
            {
                _professionTab = Mathf.Clamp(_professionTab, 0, list.Count - 1);
                _professionTab = GUILayout.Toolbar(_professionTab, list.Select(p => Content.Professions.TryGetValue(p.Id, out var d) ? d.Name : p.Id).ToArray());
                var pr = list[_professionTab];
                GUILayout.Label($"Fertigkeit {pr.Skill} / {pr.MaxSkill}", _wrap);
                _scrollProf = GUILayout.BeginScrollView(_scrollProf, GUILayout.Height(380));
                foreach (var id in pr.Recipes)
                {
                    if (!Content.Recipes.TryGetValue(id, out var r)) continue;
                    string col = pr.Skill < r.Yellow ? "#ff8040" : pr.Skill < r.Green ? "#ffff00" : pr.Skill < r.Gray ? "#40ff40" : "#9d9d9d";
                    GUILayout.BeginHorizontal(GUI.skin.box);
                    string reag = string.Join(", ", r.Reagents.Select(g => $"{g.Count}× {ItemName(g.Item)}"));
                    GUILayout.Label($"<color={col}>{r.Name}</color>\n<size=10>{reag}{(r.Station != null ? " · Werkbank: " + r.Station : "")}</size>", _wrap, GUILayout.Width(400));
                    if (GUILayout.Button("1×")) Client.Send(new C_Craft { RecipeId = r.Id, Count = 1 });
                    if (r.Enchant == null && GUILayout.Button("5×")) Client.Send(new C_Craft { RecipeId = r.Id, Count = 5 });
                    GUILayout.EndHorizontal();
                }
                GUILayout.EndScrollView();
                if (GUILayout.Button("Beruf verlernen")) Client.Send(new C_UnlearnProfession { Profession = pr.Id });
            }
            var target = Client.Me != null ? Client.State.Get(Client.Me.TargetId) : null;
            if (target != null && target.IsDead && GUILayout.Button("Ziel häuten")) Client.Send(new C_Skin { CorpseId = target.Id });
            if (GUILayout.Button("Schließen")) _showProfessions = false;
            GUILayout.EndArea();
        }

        private void DrawProfessionTrainer(S_ProfessionTrainer pt)
        {
            if (!Content.Professions.TryGetValue(pt.Profession, out var def)) return;
            GUILayout.Label($"<b>{def.Name}</b> – {def.Description}", _wrap);
            if (!pt.Known)
            {
                if (GUILayout.Button($"{def.Name} erlernen ({Money(pt.LearnCost)})")) Client.Send(new C_LearnProfession { NpcId = pt.NpcId });
                return;
            }
            if (pt.NextRank != "" && GUILayout.Button($"Rang {pt.NextRank} lernen ({Money(pt.NextRankCost)})")) Client.Send(new C_TrainProfessionRank { NpcId = pt.NpcId });
            for (int i = 0; i < pt.Recipes.Count; i++)
                if (Content.Recipes.TryGetValue(pt.Recipes[i], out var r) && GUILayout.Button($"{r.Name} (ab {r.SkillRequired}) – {Money(pt.RecipeCosts[i])}"))
                    Client.Send(new C_LearnRecipe { NpcId = pt.NpcId, RecipeId = r.Id });
        }
    }
}
