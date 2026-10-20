using System.Linq;
using EternalRealms.Shared;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;
using UnityEngine;

namespace EternalRealms.Client
{
    /// <summary>Charakterfenster (Kapitel 28): Ausrüstung mit Haltbarkeit, Werte, Widerstände, Fraktion und Ruf; Vergleichs-Tooltips.</summary>
    public sealed partial class GameUI
    {
        private bool _showCharacter;
        private Vector2 _scrollCharacter;
        private int _characterTab;

        private static readonly EquipSlot[] PaperDoll =
        {
            EquipSlot.Head, EquipSlot.Neck, EquipSlot.Shoulders, EquipSlot.Back, EquipSlot.Chest, EquipSlot.Wrist, EquipSlot.Hands, EquipSlot.Waist,
            EquipSlot.Legs, EquipSlot.Feet, EquipSlot.Finger1, EquipSlot.Finger2, EquipSlot.Trinket1, EquipSlot.Trinket2, EquipSlot.MainHand, EquipSlot.OffHand, EquipSlot.Ranged,
        };

        private static string SlotName(EquipSlot s) => s switch
        {
            EquipSlot.Head => "Kopf", EquipSlot.Neck => "Hals", EquipSlot.Shoulders => "Schultern", EquipSlot.Back => "Rücken", EquipSlot.Chest => "Brust",
            EquipSlot.Wrist => "Handgelenke", EquipSlot.Hands => "Hände", EquipSlot.Waist => "Taille", EquipSlot.Legs => "Beine", EquipSlot.Feet => "Füße",
            EquipSlot.Finger1 => "Finger 1", EquipSlot.Finger2 => "Finger 2", EquipSlot.Trinket1 => "Schmuck 1", EquipSlot.Trinket2 => "Schmuck 2",
            EquipSlot.MainHand => "Waffenhand", EquipSlot.OffHand => "Schildhand", EquipSlot.Ranged => "Distanz", _ => s.ToString(),
        };

        private void DrawCharacter()
        {
            var me = Client.Me;
            if (me == null) return;
            var area = Window($"{me.Name} – Stufe {me.Level} {(Content.Races.TryGetValue(me.Race, out var rd) ? rd.Name : "")} {(Content.Classes.TryGetValue(me.Class, out var cd) ? cd.Name : "")}", 640, 520);
            GUILayout.BeginArea(area);
            _characterTab = GUILayout.Toolbar(_characterTab, new[] { "Ausrüstung & Werte", "Ruf" });
            _scrollCharacter = GUILayout.BeginScrollView(_scrollCharacter);
            if (_characterTab == 0) DrawEquipmentAndStats(); else DrawReputation();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawEquipmentAndStats()
        {
            var inv = Client.State.Inventory;
            var st = Client.State.Stats;
            GUILayout.BeginHorizontal();
            GUILayout.BeginVertical(GUILayout.Width(300));
            GUILayout.Label("<b>Ausrüstung</b> (Klick legt ab)", _wrap);
            foreach (var slot in PaperDoll)
            {
                var stack = inv != null && (int)slot < inv.Equipment.Count ? inv.Equipment[(int)slot] : null;
                bool has = stack != null && stack.ItemId != "" && Content.Items.ContainsKey(stack.ItemId);
                string dur = "";
                if (has)
                {
                    var def = Content.Items[stack!.ItemId];
                    int pct = ItemTooltip.DurabilityPercent(def, stack.Durability);
                    if (def.MaxDurability > 0) dur = pct <= 0 ? " <color=red>[kaputt]</color>" : pct < 25 ? $" <color=orange>[{pct}%]</color>" : $" <size=10>[{pct}%]</size>";
                }
                var label = $"{SlotName(slot)}: {(has ? ItemLabel(stack!.ItemId) : "<color=#777>–</color>")}{dur}";
                var r = GUILayoutUtility.GetRect(new GUIContent(label), _wrap);
                if (GUI.Button(r, label, _wrap) && has) Client.Send(new C_UnequipItem { Slot = slot });
                if (has && r.Contains(Event.current.mousePosition)) _tooltip = ItemTooltipText(Content.Items[stack!.ItemId], stack, compare: false);
            }
            GUILayout.EndVertical();
            GUILayout.BeginVertical();
            if (st != null)
            {
                GUILayout.Label("<b>Grundwerte</b>", _wrap);
                GUILayout.Label($"Stärke {st.Strength:0}\nBeweglichkeit {st.Agility:0}\nAusdauer {st.Stamina:0}\nIntelligenz {st.Intellect:0}\nWillenskraft {st.Spirit:0}", _wrap);
                GUILayout.Label("<b>Nahkampf</b>", _wrap);
                GUILayout.Label($"Angriffskraft {st.AttackPower}\nKritisch {st.MeleeCrit:0.00} %\nTrefferbonus {st.Hit:0.0} %", _wrap);
                GUILayout.Label("<b>Distanz</b>", _wrap);
                GUILayout.Label($"Angriffskraft {st.RangedAttackPower}", _wrap);
                GUILayout.Label("<b>Zauber</b>", _wrap);
                GUILayout.Label($"Zaubermacht {st.SpellPower}\nHeilkraft {st.HealingPower}\nKritisch {st.SpellCrit:0.00} %", _wrap);
                GUILayout.Label("<b>Verteidigung</b>", _wrap);
                GUILayout.Label($"Rüstung {st.Armor}\nAusweichen {st.Dodge:0.00} %\nParieren {st.Parry:0.00} %\nBlocken {st.Block:0.00} %\nLauftempo {st.MoveSpeed:0.0} m/s", _wrap);
                GUILayout.Label("<b>Widerstände</b>", _wrap);
                var lines = Enumerable.Range(1, 6).Select(i => $"{ItemTooltip.SchoolName((School)i)} {(i < st.Resist.Count ? st.Resist[i] : 0)}");
                GUILayout.Label(string.Join("\n", lines), _wrap);
            }
            else GUILayout.Label("Werte werden geladen …", _wrap);
            GUILayout.EndVertical();
            GUILayout.EndHorizontal();
        }

        private void DrawReputation()
        {
            var me = Client.Me!;
            var st = Client.State.Stats;
            GUILayout.Label($"Fraktion: <b>{(Content.Factions.TryGetValue(me.Faction, out var f) ? f.Name : me.Faction.ToString())}</b>", _wrap);
            if (st == null || st.ReputationIds.Count == 0) { GUILayout.Label("Noch kein Ruf bei einer Gruppierung.", _wrap); return; }
            for (int i = 0; i < st.ReputationIds.Count && i < st.ReputationValues.Count; i++)
            {
                var id = st.ReputationIds[i];
                int v = st.ReputationValues[i];
                var (cur, size) = Reputation.WithinStanding(v);
                GUILayout.Label($"<b>{(Content.Reputations.TryGetValue(id, out var rep) ? rep.Name : id)}</b> – {Reputation.Standing(v)}", _wrap);
                var r = GUILayoutUtility.GetRect(200, 16, GUILayout.ExpandWidth(true));
                Bar(r, cur / (float)size, v < 0 ? Palette.Hostile : v < 3000 ? Palette.Neutral : Palette.Friendly, $"{cur} / {size}");
            }
        }

        /// <summary>Tooltip mit Farben und – bei anlegbaren Gegenständen – dem Vergleich zur angelegten Ausrüstung.</summary>
        private string ItemTooltipText(ItemDef item, ItemStackData? stack, bool compare = true)
        {
            var me = Client.Me;
            var lines = ItemTooltip.Lines(item, Content, stack, me?.Level ?? 0, me?.Class);
            var text = $"<b><color={Palette.Quality(item.Quality)}>{lines[0]}</color></b>";
            foreach (var l in lines.Skip(1)) text += "\n" + (l.StartsWith("(!) ") ? $"<color={Palette.Hex(Palette.Hostile)}>{l.Substring(4)}</color>" : l);
            if (compare && item.IsEquippable)
            {
                var diff = ItemTooltip.Compare(item, Client.State.Inventory, Content);
                if (diff.Count > 0)
                {
                    text += "\n<b>Im Vergleich zur Ausrüstung:</b>";
                    foreach (var (stat, d) in diff)
                        text += $"\n<color={Palette.Hex(d > 0 ? Palette.Friendly : Palette.Hostile)}>{(d > 0 ? "+" : "")}{d:0.#} {stat}</color>";
                }
            }
            return text;
        }
    }
}
