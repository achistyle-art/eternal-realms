using System;
using System.Collections.Generic;
using System.Linq;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Shared.Client.Ui
{
    /// <summary>
    /// Tooltip-Text und Vergleich mit angelegter Ausrüstung (Kapitel 28). Reine Logik, damit Unity und Tests
    /// dieselben Zahlen sehen; die Darstellung (Farben) macht die Oberfläche.
    /// </summary>
    public static class ItemTooltip
    {
        public static string StatLabel(ModifierDef m)
        {
            switch (m.Type)
            {
                case ModType.StatFlat: return m.Stat switch { Stat.Strength => "Stärke", Stat.Agility => "Beweglichkeit", Stat.Stamina => "Ausdauer", Stat.Intellect => "Intelligenz", _ => "Willenskraft" };
                case ModType.ResistFlat: return SchoolName(m.School ?? School.Physical) + "widerstand";
                case ModType.AttackPowerFlat: return "Angriffskraft";
                case ModType.SpellPowerFlat: return "Zaubermacht";
                case ModType.HealingPowerFlat: return "Heilkraft";
                case ModType.ArmorFlat: return "Rüstung";
                case ModType.MaxHealthFlat: return "Gesundheit";
                case ModType.CritChance: return m.Crit == CritCategory.Spell ? "Zauberkrit (%)" : "Kritische Trefferchance (%)";
                case ModType.HitChance: return "Trefferchance (%)";
                case ModType.DodgeChance: return "Ausweichen (%)";
                case ModType.ParryChance: return "Parieren (%)";
                case ModType.BlockChance: return "Blocken (%)";
                case ModType.MoveSpeedPercent: return "Lauftempo (%)";
                case ModType.AttackSpeedPercent: return "Angriffstempo (%)";
                default: return m.Type.ToString();
            }
        }

        public static string SchoolName(School s) => s switch
        {
            School.Holy => "Heilig", School.Fire => "Feuer", School.Nature => "Natur", School.Frost => "Frost",
            School.Shadow => "Schatten", School.Arcane => "Arkan", _ => "Körperlich",
        };

        /// <summary>Alle vergleichbaren Werte eines Gegenstands (Rüstung, DPS, Modifikatoren, Verzauberung).</summary>
        public static Dictionary<string, float> Values(ItemDef item, ContentDatabase? content = null, string enchant = "")
        {
            var v = new Dictionary<string, float>();
            void Add(string k, float x) { if (x != 0) v[k] = (v.TryGetValue(k, out var o) ? o : 0) + x; }
            Add("Rüstung", item.Armor);
            if (item.IsWeapon) Add("Schaden pro Sekunde", (float)Math.Round(item.Dps, 1));
            foreach (var m in item.Modifiers) Add(StatLabel(m), m.Value);
            if (enchant != "" && content != null && content.Recipes.TryGetValue(enchant, out var r) && r.Enchant != null)
                foreach (var m in r.Enchant.Modifiers) Add(StatLabel(m), m.Value);
            return v;
        }

        /// <summary>Ausrüstungsplätze, auf die der Gegenstand passt (Ringe und Schmuck haben zwei).</summary>
        public static EquipSlot[] SlotsFor(InventoryType t) => t switch
        {
            InventoryType.Head => new[] { EquipSlot.Head }, InventoryType.Neck => new[] { EquipSlot.Neck },
            InventoryType.Shoulders => new[] { EquipSlot.Shoulders }, InventoryType.Back => new[] { EquipSlot.Back },
            InventoryType.Chest => new[] { EquipSlot.Chest }, InventoryType.Wrist => new[] { EquipSlot.Wrist },
            InventoryType.Hands => new[] { EquipSlot.Hands }, InventoryType.Waist => new[] { EquipSlot.Waist },
            InventoryType.Legs => new[] { EquipSlot.Legs }, InventoryType.Feet => new[] { EquipSlot.Feet },
            InventoryType.Finger => new[] { EquipSlot.Finger1, EquipSlot.Finger2 },
            InventoryType.Trinket => new[] { EquipSlot.Trinket1, EquipSlot.Trinket2 },
            InventoryType.OneHand => new[] { EquipSlot.MainHand, EquipSlot.OffHand },
            InventoryType.MainHandOnly or InventoryType.TwoHand => new[] { EquipSlot.MainHand },
            InventoryType.OffHand or InventoryType.Shield => new[] { EquipSlot.OffHand },
            InventoryType.Ranged => new[] { EquipSlot.Ranged },
            _ => Array.Empty<EquipSlot>(),
        };

        /// <summary>
        /// Unterschiede zum angelegten Gegenstand im passenden Platz (bei zwei Plätzen wird der schwächere verglichen).
        /// Positive Werte = Verbesserung. Leer, wenn nicht anlegbar.
        /// </summary>
        public static List<(string stat, float delta)> Compare(ItemDef candidate, S_Inventory? inv, ContentDatabase content)
        {
            var result = new List<(string, float)>();
            var slots = SlotsFor(candidate.InventoryType);
            if (slots.Length == 0) return result;
            var mine = Values(candidate, content);
            Dictionary<string, float>? weakest = null;
            float weakestScore = float.MaxValue;
            foreach (var slot in slots)
            {
                var eq = inv != null && (int)slot < inv.Equipment.Count ? inv.Equipment[(int)slot] : null;
                var values = eq != null && eq.ItemId != "" && content.Items.TryGetValue(eq.ItemId, out var def) ? Values(def, content, eq.Enchant) : new Dictionary<string, float>();
                float score = values.Values.Sum();
                if (score < weakestScore) { weakestScore = score; weakest = values; }
            }
            weakest ??= new Dictionary<string, float>();
            foreach (var key in mine.Keys.Union(weakest.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                float d = (mine.TryGetValue(key, out var a) ? a : 0) - (weakest.TryGetValue(key, out var b) ? b : 0);
                if (Math.Abs(d) > 0.001f) result.Add((key, (float)Math.Round(d, 1)));
            }
            return result;
        }

        /// <summary>Haltbarkeit in Prozent (100, wenn der Gegenstand keine hat).</summary>
        public static int DurabilityPercent(ItemDef item, int durability) =>
            item.MaxDurability <= 0 ? 100 : (int)Math.Round(100.0 * Math.Max(0, Math.Min(durability, item.MaxDurability)) / item.MaxDurability);

        /// <summary>Mehrzeiliger Tooltip ohne Farbcodes; "(!)" markiert nicht erfüllte Anforderungen.</summary>
        public static List<string> Lines(ItemDef item, ContentDatabase content, ItemStackData? stack = null, int playerLevel = 0, CharClass? playerClass = null)
        {
            var l = new List<string> { item.Name };
            if (item.BindOnPickup) l.Add("Wird beim Aufheben gebunden");
            if (item.Unique) l.Add("Einzigartig");
            if (item.IsEquippable) l.Add(item.IsWeapon ? $"{item.InventoryType} · {item.WeaponType}" : item.ArmorType != ArmorType.None ? $"{item.InventoryType} · {item.ArmorType}" : item.InventoryType.ToString());
            if (item.IsWeapon) l.Add($"{item.MinDamage}–{item.MaxDamage} Schaden · Tempo {item.Speed:0.0} · {item.Dps:0.0} DPS");
            if (item.Armor > 0) l.Add($"{item.Armor} Rüstung");
            foreach (var m in item.Modifiers) l.Add($"+{m.Value:0.#} {StatLabel(m)}");
            if (stack != null && stack.Enchant != "" && content.Recipes.TryGetValue(stack.Enchant, out var r) && r.Enchant != null) l.Add("Verzaubert: " + r.Enchant.Name);
            if (item.BagSlots > 0) l.Add($"{item.BagSlots} Plätze");
            if (item.MaxDurability > 0 && stack != null) l.Add($"Haltbarkeit {Math.Max(0, stack.Durability)} / {item.MaxDurability}");
            if (item.RequiredLevel > 1) l.Add((playerLevel > 0 && playerLevel < item.RequiredLevel ? "(!) " : "") + $"Benötigt Stufe {item.RequiredLevel}");
            if (item.Classes.Count > 0) l.Add((playerClass.HasValue && !item.Classes.Contains(playerClass.Value) ? "(!) " : "") + "Klassen: " + string.Join(", ", item.Classes));
            if (item.Description != "") l.Add(item.Description);
            if (item.SellPrice > 0) l.Add($"Verkaufspreis: {item.SellPrice / 10000}g {item.SellPrice / 100 % 100}s {item.SellPrice % 100}k");
            return l;
        }
    }
}
