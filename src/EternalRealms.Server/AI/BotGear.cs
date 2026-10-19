using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;

namespace EternalRealms.Server.AI
{
    /// <summary>
    /// Ausrüstung der KI-Mitspieler. Bots tragen eine "virtuelle Grundausrüstung", deren Werte etwa grüner
    /// Questausrüstung ihrer Stufe entsprechen. Jeder echte Gegenstand, den ein Bot anlegt, ersetzt den
    /// virtuellen Anteil seines Platzes – so profitieren Bots sichtbar von Beute und Questbelohnungen.
    /// </summary>
    public static class BotGear
    {
        private const int Slots = 14;

        private static readonly EquipSlot[] ArmorSlots =
        {
            EquipSlot.Head, EquipSlot.Neck, EquipSlot.Shoulders, EquipSlot.Back, EquipSlot.Chest, EquipSlot.Wrist, EquipSlot.Hands,
            EquipSlot.Waist, EquipSlot.Legs, EquipSlot.Feet, EquipSlot.Finger1, EquipSlot.Finger2, EquipSlot.Trinket1, EquipSlot.MainHand,
        };

        public static void AddVirtualGear(Player p, List<ModifierDef> into)
        {
            var role = p.Data.Bot?.Role ?? Role.MeleeDps;
            int filled = ArmorSlots.Count(s => p.Data.Equipment.ContainsKey(s));
            float share = (Slots - filled) / (float)Slots;
            if (share <= 0) return;
            float budget = p.Level * 1.6f * share;
            foreach (var (stat, w) in Weights(p.Class, role))
                into.Add(new ModifierDef { Type = ModType.StatFlat, Stat = stat, Value = (float)Math.Round(budget * w) });
            int armorFactor = p.ClassDef.MaxArmor switch { ArmorType.Plate => 7, ArmorType.Mail => 4, ArmorType.Leather => 2, _ => 1 };
            into.Add(new ModifierDef { Type = ModType.ArmorFlat, Value = (p.Level * 2 + 10) * armorFactor * 4 * share });
        }

        public static IEnumerable<(Stat, float)> Weights(CharClass cls, Role role)
        {
            switch (role)
            {
                case Role.Tank: return new[] { (Stat.Stamina, 0.45f), (Stat.Strength, 0.35f), (Stat.Agility, 0.2f) };
                case Role.Healer: return new[] { (Stat.Intellect, 0.4f), (Stat.Spirit, 0.3f), (Stat.Stamina, 0.3f) };
                case Role.RangedDps when cls == CharClass.Hunter: return new[] { (Stat.Agility, 0.6f), (Stat.Stamina, 0.3f), (Stat.Intellect, 0.1f) };
                case Role.RangedDps: return new[] { (Stat.Intellect, 0.5f), (Stat.Stamina, 0.3f), (Stat.Spirit, 0.2f) };
                default:
                    return cls is CharClass.Rogue or CharClass.Druid
                        ? new[] { (Stat.Agility, 0.6f), (Stat.Stamina, 0.4f) }
                        : new[] { (Stat.Strength, 0.6f), (Stat.Stamina, 0.4f) };
            }
        }

        public static WeaponInfo VirtualWeapon(Player p)
        {
            bool twoHand = p.Data.Bot?.Role is Role.MeleeDps && p.Class is CharClass.Warrior or CharClass.Paladin or CharClass.Shaman;
            float speed = twoHand ? 3.3f : p.Class == CharClass.Rogue ? 1.8f : 2.5f;
            float dps = (1.2f + p.Level * 0.55f) * 1.15f * (twoHand ? 1.3f : 1f);
            float avg = dps * speed;
            return new WeaponInfo { Min = Math.Max(1, (int)(avg * 0.75f)), Max = Math.Max(2, (int)(avg * 1.25f)), Speed = speed, Present = true };
        }

        /// <summary>Bewertet einen Gegenstand für Klasse und Rolle eines Bots (höher = besser).</summary>
        public static float Score(Player p, ItemDef item)
        {
            var role = p.Data.Bot?.Role ?? Role.MeleeDps;
            float s = item.Armor * (role == Role.Tank ? 0.15f : 0.03f);
            var weights = Weights(p.Class, role).ToDictionary(x => x.Item1, x => x.Item2);
            foreach (var m in item.Modifiers)
            {
                if (m.Type == ModType.StatFlat && m.Stat.HasValue) s += m.Value * (weights.TryGetValue(m.Stat.Value, out var w) ? w * 2 : 0.2f);
                else if (m.Type == ModType.SpellPowerFlat) s += m.Value * (role is Role.Healer or Role.RangedDps && p.Class != CharClass.Hunter ? 0.8f : 0);
                else if (m.Type == ModType.AttackPowerFlat) s += m.Value * (role is Role.MeleeDps or Role.Tank || p.Class == CharClass.Hunter ? 0.4f : 0);
                else if (m.Type is ModType.CritChance or ModType.HitChance) s += m.Value * 8;
                else if (m.Type == ModType.BlockChance) s += m.Value * (role == Role.Tank ? 4 : 0);
                else if (m.Type == ModType.MaxHealthPercent) s += m.Value * 5;
            }
            if (item.Category == ItemCategory.Weapon)
            {
                bool casterWeapon = role is Role.Healer || (role is Role.RangedDps && p.Class != CharClass.Hunter);
                s += item.Dps * (casterWeapon ? 0.5f : 3f);
                if (role == Role.Tank && item.InventoryType == InventoryType.TwoHand) s *= 0.6f;
            }
            if (item.WeaponType == WeaponType.Shield && role != Role.Tank && !(p.Class is CharClass.Paladin or CharClass.Shaman && role == Role.Healer)) s *= 0.3f;
            return s;
        }

        /// <summary>Virtueller Wert eines leeren Platzes (Grundausrüstung) – ein echter Gegenstand muss besser sein.</summary>
        public static float VirtualSlotScore(Player p)
        {
            var role = p.Data.Bot?.Role ?? Role.MeleeDps;
            float statsPerSlot = p.Level * 1.6f / Slots;
            return Weights(p.Class, role).Sum(x => statsPerSlot * x.Item2 * 2);
        }
    }
}
