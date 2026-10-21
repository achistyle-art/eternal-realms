using EternalRealms.Server.AI;
using EternalRealms.Server.Net;
using EternalRealms.Server.Persistence;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.World
{
    /// <summary>
    /// Eine Spielfigur – gesteuert von einem Menschen (Connection) oder einem KI-Mitspieler (Bot).
    /// Beide nutzen exakt dieselben Regeln, Fähigkeiten, Quests und Erfahrungswege.
    /// </summary>
    public sealed partial class Player : Unit
    {
        public const int BagSize = 20;
        public const int MaxQuests = 20;

        public CharacterData Data { get; }
        public ContentDatabase Content { get; }
        public ClassDef ClassDef { get; }
        public RaceDef RaceDef { get; }
        public IPlayerConnection? Connection { get; set; }
        public BotBrain? Bot { get; set; }
        public Group? Group { get; set; }
        public Creature? Pet { get; set; }
        public Creature? EscortNpc { get; set; }
        public HashSet<int> VisibleEntities { get; } = new();
        public AreaDef? CurrentArea { get; set; }
        public uint LastMoveSequence { get; set; }
        public double LastMoveTime { get; set; }
        public bool PvP { get; set; }
        public double SaveTimer { get; set; }
        public Player? PendingInviteFrom { get; set; }
        public Vec3 FlightFrom, FlightTo;
        public float FlightTime, FlightDuration;
        public bool IsFlying => FlightDuration > 0;
        public float RestTickTimer { get; set; }
        /// <summary>Letzter Befehl/Aktion eines Spielers, für die Inaktivitätserkennung.</summary>
        public double LastActivity { get; set; }

        // Konto (Kapitel 40/50): Spielleiter-Stufe und Stummschaltung kommen beim Weltbetritt vom Konto.
        public int GmLevel { get; set; }
        public DateTime? MutedUntil { get; set; }
        public bool IsGm => GmLevel >= Persistence.GmLevels.GameMaster;
        /// <summary>Verbindung abgerissen: bleibt bis zu diesem Zeitpunkt in der Welt und kann übernommen werden (0 = verbunden).</summary>
        public double LinkdeadUntil { get; set; }
        public bool IsLinkdead => LinkdeadUntil > 0;

        private readonly Dictionary<PowerType, int> _pools = new();

        public Player(CharacterData data, ContentDatabase content)
        {
            Data = data;
            Content = content;
            ClassDef = content.Classes[data.Class];
            RaceDef = content.Races[data.Race];
            Name = data.Name;
            Level = data.Level;
            Faction = RaceDef.Faction;
            Position = data.Position;
            Facing = data.Facing;
            DeathState = data.DeathState;
            _pools[PowerType.Mana] = data.Mana;
            _pools[PowerType.Rage] = 0;
            _pools[PowerType.Energy] = 100;
            MarkStatsDirty();
            EnsureStats();
            Health = data.Health > 0 ? Math.Min(data.Health, MaxHealth) : (DeathState == DeathState.Alive ? MaxHealth : 1);
            Power = PowerType == PowerType.Mana ? (data.Mana > 0 ? Math.Min(data.Mana, MaxPower) : MaxPower) : _pools[PowerType];
            if (data.Health <= 0 && DeathState == DeathState.Alive) Power = MaxPower;
        }

        public override EntityKind Kind => EntityKind.Player;
        public override bool IsPlayerControlled => true;
        public override bool IsPvPFlagged => PvP;
        public bool IsBot => Data.IsBot;
        public CharClass Class => Data.Class;
        public Race Race => Data.Race;
        public long Money => Data.Money;

        /// <summary>Nur über EconomySystem aufrufen – dort wird jede Geldbewegung protokolliert.</summary>
        internal void ApplyMoney(long delta) => Data.Money = Math.Max(0, Data.Money + delta);

        public override PowerType PowerType
        {
            get
            {
                if (Data.Class == CharClass.Druid)
                    return Form == Form.Bear ? PowerType.Rage : Form == Form.Cat ? PowerType.Energy : PowerType.Mana;
                return ClassDef.PowerType;
            }
            protected set { }
        }

        // ---------------------------------------------------------------- Reiten

        /// <summary>Aktives Reittier (leer = zu Fuß). Wird nicht gespeichert: Nach dem Einloggen ist man abgesessen.</summary>
        public string ActiveMount { get; set; } = "";
        /// <summary>Rolle, die ein menschlicher Spieler in der Gruppe übernimmt (Gruppenfinder, Anzeige).</summary>
        public Role SelectedRole { get; set; } = Role.MeleeDps;
        /// <summary>Reittier, das gerade gerufen wird (während der Zauberzeit).</summary>
        public string PendingMount { get; set; } = "";
        public bool IsMounted => Auras.Any(a => a.Def.Mounted);

        protected override void OnAuraRemoved(Aura aura)
        {
            base.OnAuraRemoved(aura);
            if (aura.Def.Mounted && Zone != null) World.Mounts.OnDismounted(this);
        }

        public override void SetForm(Form form)
        {
            if (form == Form) return;
            var oldType = PowerType;
            _pools[oldType] = Power;
            base.SetForm(form);
            var newType = PowerType;
            if (newType != oldType)
            {
                MarkStatsDirty();
                EnsureStats();
                Power = newType == PowerType.Rage ? 0 : newType == PowerType.Energy ? 0 : Math.Min(_pools.GetValueOrDefault(PowerType.Mana), MaxPower);
            }
        }

        public int StoredMana => PowerType == PowerType.Mana ? Power : _pools.GetValueOrDefault(PowerType.Mana);

        public void Send(Message m) => Connection?.Send(m);

        public void Notice(string text, bool error = false) => Connection?.Send(new S_Notice { IsError = error, Text = text });

        // ================================================================ Werte

        protected override void CollectModifiers(List<ModifierDef> into)
        {
            base.CollectModifiers(into);
            foreach (var t in RaceDef.Traits) into.AddRange(t.Modifiers);
            foreach (var kv in Data.Talents)
                if (Content.Talents.TryGetValue(kv.Key, out var tal))
                    foreach (var m in tal.Modifiers) into.Add(kv.Value > 1 ? m.Scaled(kv.Value) : m);
            foreach (var kv in Data.Equipment)
            {
                if (!Content.Items.TryGetValue(kv.Value.ItemId, out var item)) continue;
                if (item.MaxDurability > 0 && kv.Value.Durability <= 0) continue; // zerbrochen
                into.AddRange(item.Modifiers);
                if (kv.Value.Enchant != "" && Content.Recipes.TryGetValue(kv.Value.Enchant, out var ench) && ench.Enchant != null) into.AddRange(ench.Enchant.Modifiers);
            }
            if (Data.IsBot) BotGear.AddVirtualGear(this, into);
        }

        protected override void RecalculateStats()
        {
            foreach (Stat s in Enum.GetValues(typeof(Stat)))
            {
                float v = StatFormulas.BaseStat(ClassDef, RaceDef, s, Level) + ModifierQuery.Sum(Modifiers, ModType.StatFlat, stat: s);
                v *= 1 + ModifierQuery.Sum(Modifiers, ModType.StatPercent, stat: s) / 100f;
                Stats.Set(s, Math.Max(0, v));
            }
            float hp = StatFormulas.BaseHealth(ClassDef, Level) + StatFormulas.HealthFromStamina(Stats.Stamina) + Mod(ModType.MaxHealthFlat);
            MaxHealth = Math.Max(1, (int)(hp * (1 + Mod(ModType.MaxHealthPercent) / 100f)));
            switch (PowerType)
            {
                case PowerType.Mana:
                    float mana = StatFormulas.BaseMana(ClassDef, Level) + StatFormulas.ManaFromIntellect(Stats.Intellect);
                    MaxPower = (int)(mana * (1 + Mod(ModType.MaxPowerPercent) / 100f));
                    break;
                case PowerType.Rage:
                case PowerType.Energy:
                    MaxPower = 100;
                    break;
                default:
                    MaxPower = 0;
                    break;
            }

            int itemArmor = 0;
            MainHand = new WeaponInfo { Min = 1 + Level / 2, Max = 2 + Level, Speed = 2.0f, School = School.Physical, Present = false };
            RangedWeapon = default;
            HasShield = false;
            foreach (var kv in Data.Equipment)
            {
                if (!Content.Items.TryGetValue(kv.Value.ItemId, out var item)) continue;
                bool broken = item.MaxDurability > 0 && kv.Value.Durability <= 0;
                if (broken) continue;
                itemArmor += item.Armor;
                if (item.WeaponType == WeaponType.Shield) HasShield = true;
                if (item.Category == ItemCategory.Weapon)
                {
                    var w = new WeaponInfo { Min = item.MinDamage, Max = item.MaxDamage, Speed = item.Speed, School = item.DamageSchool, Present = true };
                    if (kv.Key == EquipSlot.MainHand) MainHand = w;
                    else if (kv.Key == EquipSlot.Ranged) RangedWeapon = w;
                }
            }
            if (Data.IsBot)
            {
                // Bots nutzen ihre echte Waffe nur, wenn sie besser ist als die Grundausrüstung ihrer Stufe.
                var virtualWeapon = BotGear.VirtualWeapon(this);
                if (!MainHand.Present || MainHand.Average / MainHand.Speed < virtualWeapon.Average / virtualWeapon.Speed * 0.9f) MainHand = virtualWeapon;
            }
            Armor = Math.Max(0, (int)((itemArmor + Stats.Agility * 2 + Mod(ModType.ArmorFlat)) * (1 + Mod(ModType.ArmorPercent) / 100f)));
            for (int i = 0; i < Resist.Length; i++) Resist[i] = Mod(ModType.ResistFlat, (School)i);
            AttackPower = Math.Max(0, (int)(Stats.Strength * ClassDef.ApPerStrength + Stats.Agility * ClassDef.ApPerAgility - 20 + Level * 2 + Mod(ModType.AttackPowerFlat)));
            RangedAttackPower = Math.Max(0, (int)(Stats.Agility * ClassDef.RangedApPerAgility + Level * 2 - 10 + Mod(ModType.AttackPowerFlat)));
            SpellPower = Math.Max(0, (int)Mod(ModType.SpellPowerFlat));
            HealingPower = SpellPower + (int)Mod(ModType.HealingPowerFlat);
            MeleeCrit = StatFormulas.MeleeCrit(Stats.Agility, Level) + Mod(ModType.CritChance, crit: CritCategory.Melee);
            SpellCrit = StatFormulas.SpellCrit(Stats.Intellect, Level) + Mod(ModType.CritChance, crit: CritCategory.Spell);
            Dodge = StatFormulas.Dodge(Stats.Agility, Level) + Mod(ModType.DodgeChance);
            bool canParry = Class is CharClass.Warrior or CharClass.Paladin or CharClass.Rogue or CharClass.Hunter or CharClass.Shaman;
            Parry = canParry ? 5 + Mod(ModType.ParryChance) : 0;
            Block = HasShield ? 5 + Mod(ModType.BlockChance) : 0;
            HitBonus = Mod(ModType.HitChance);
            float speed = DeathState == DeathState.Ghost ? 1.25f : 1f;
            MoveSpeed = Math.Max(1f, BaseMoveSpeed * speed * (1 + Mod(ModType.MoveSpeedPercent) / 100f));
            AttackSpeedMultiplier = Math.Max(0.2f, 1 + Mod(ModType.AttackSpeedPercent) / 100f);
            ClampPools();
        }

        public void SyncToData()
        {
            Data.Level = Level;
            Data.Position = Position;
            Data.Facing = Facing;
            Data.Health = DeathState == DeathState.Alive ? Health : 0;
            Data.Mana = StoredMana;
            Data.DeathState = DeathState;
            if (Zone != null) Data.ZoneId = Zone.Def.Id;
        }

        // ================================================================ Inventar

        public void EnsureBagSize()
        {
            int cap = BagCapacity;
            while (Data.Bag.Count < cap) Data.Bag.Add(null);
        }

        public int CountItem(string itemId)
        {
            int n = 0;
            foreach (var s in Data.Bag) if (s != null && s.ItemId == itemId) n += s.Count;
            return n;
        }

        public int FreeBagSlots => Data.Bag.Count(s => s == null);

        /// <summary>Wie viele Exemplare passen noch in die Taschen?</summary>
        public int SpaceFor(string itemId)
        {
            if (!Content.Items.TryGetValue(itemId, out var def)) return 0;
            int space = 0;
            foreach (var s in Data.Bag)
            {
                if (s == null) space += def.MaxStack;
                else if (s.ItemId == itemId) space += Math.Max(0, def.MaxStack - s.Count);
            }
            if (def.Unique && (CountItem(itemId) > 0 || Data.Equipment.Values.Any(e => e.ItemId == itemId))) return 0;
            return space;
        }

        /// <summary>Legt Gegenstände in die Taschen. Rückgabe: tatsächlich hinzugefügt.</summary>
        public int AddItem(string itemId, int count)
        {
            if (!Content.Items.TryGetValue(itemId, out var def) || count <= 0) return 0;
            EnsureBagSize();
            int toAdd = Math.Min(count, SpaceFor(itemId));
            int left = toAdd;
            for (int i = 0; i < Data.Bag.Count && left > 0; i++)
            {
                var s = Data.Bag[i];
                if (s != null && s.ItemId == itemId && s.Count < def.MaxStack)
                {
                    int put = Math.Min(left, def.MaxStack - s.Count);
                    s.Count += put;
                    left -= put;
                }
            }
            for (int i = 0; i < Data.Bag.Count && left > 0; i++)
            {
                if (Data.Bag[i] != null) continue;
                int put = Math.Min(left, def.MaxStack);
                Data.Bag[i] = new ItemStack { ItemId = itemId, Count = put, Durability = def.MaxDurability };
                left -= put;
            }
            return toAdd - left;
        }

        public int RemoveItem(string itemId, int count)
        {
            int left = count;
            for (int i = Data.Bag.Count - 1; i >= 0 && left > 0; i--)
            {
                var s = Data.Bag[i];
                if (s == null || s.ItemId != itemId) continue;
                int take = Math.Min(left, s.Count);
                s.Count -= take;
                left -= take;
                if (s.Count <= 0) Data.Bag[i] = null;
            }
            return count - left;
        }

        public string? CanEquip(ItemDef item)
        {
            if (!item.IsEquippable) return "Dieser Gegenstand kann nicht angelegt werden.";
            if (Level < item.RequiredLevel) return $"Benötigt Stufe {item.RequiredLevel}.";
            if (item.Classes.Count > 0 && !item.Classes.Contains(Class)) return "Eure Klasse kann das nicht tragen.";
            if (item.Category == ItemCategory.Armor && item.ArmorType != ArmorType.None && item.InventoryType != InventoryType.Back && item.ArmorType > ClassDef.MaxArmor)
                return "Ihr könnt diese Rüstungsart nicht tragen.";
            if ((item.Category == ItemCategory.Weapon || item.WeaponType == WeaponType.Shield) && item.WeaponType != WeaponType.OffhandFrill &&
                item.WeaponType != WeaponType.None && !ClassDef.Weapons.Contains(item.WeaponType))
                return "Ihr beherrscht diese Waffenart nicht.";
            return null;
        }

        public EquipSlot SlotFor(ItemDef item) => item.InventoryType switch
        {
            InventoryType.Head => EquipSlot.Head,
            InventoryType.Neck => EquipSlot.Neck,
            InventoryType.Shoulders => EquipSlot.Shoulders,
            InventoryType.Back => EquipSlot.Back,
            InventoryType.Chest => EquipSlot.Chest,
            InventoryType.Wrist => EquipSlot.Wrist,
            InventoryType.Hands => EquipSlot.Hands,
            InventoryType.Waist => EquipSlot.Waist,
            InventoryType.Legs => EquipSlot.Legs,
            InventoryType.Feet => EquipSlot.Feet,
            InventoryType.Finger => Data.Equipment.ContainsKey(EquipSlot.Finger1) && !Data.Equipment.ContainsKey(EquipSlot.Finger2) ? EquipSlot.Finger2 : EquipSlot.Finger1,
            InventoryType.Trinket => Data.Equipment.ContainsKey(EquipSlot.Trinket1) && !Data.Equipment.ContainsKey(EquipSlot.Trinket2) ? EquipSlot.Trinket2 : EquipSlot.Trinket1,
            InventoryType.OneHand or InventoryType.MainHandOnly or InventoryType.TwoHand => EquipSlot.MainHand,
            InventoryType.OffHand or InventoryType.Shield => EquipSlot.OffHand,
            InventoryType.Ranged => EquipSlot.Ranged,
            _ => EquipSlot.None,
        };

        /// <summary>Legt einen Gegenstand aus der Tasche an; der bisherige wandert in die Tasche.</summary>
        public string? Equip(int bagIndex)
        {
            EnsureBagSize();
            if (bagIndex < 0 || bagIndex >= Data.Bag.Count || Data.Bag[bagIndex] == null) return "Kein Gegenstand.";
            if (InCombat && Cast != null) return "Nicht während des Zauberns.";
            var stack = Data.Bag[bagIndex]!;
            var item = Content.Items[stack.ItemId];
            var err = CanEquip(item);
            if (err != null) return err;
            var slot = SlotFor(item);
            Data.Bag[bagIndex] = null;
            var displaced = new List<ItemStack>();
            if (Data.Equipment.TryGetValue(slot, out var old)) displaced.Add(old);
            ItemStack? removedOff = null, removedMain = null;
            if (item.InventoryType == InventoryType.TwoHand && Data.Equipment.TryGetValue(EquipSlot.OffHand, out var off))
            {
                removedOff = off;
                displaced.Add(off);
                Data.Equipment.Remove(EquipSlot.OffHand);
            }
            if (slot == EquipSlot.OffHand && Data.Equipment.TryGetValue(EquipSlot.MainHand, out var main) &&
                Content.Items[main.ItemId].InventoryType == InventoryType.TwoHand)
            {
                removedMain = main;
                displaced.Add(main);
                Data.Equipment.Remove(EquipSlot.MainHand);
            }
            if (displaced.Count > 1 && FreeBagSlots < displaced.Count - 1)
            {
                Data.Bag[bagIndex] = stack;
                if (old != null) Data.Equipment[slot] = old;
                if (removedOff != null) Data.Equipment[EquipSlot.OffHand] = removedOff;
                if (removedMain != null) Data.Equipment[EquipSlot.MainHand] = removedMain;
                return "Nicht genug Platz in den Taschen.";
            }
            Data.Equipment[slot] = stack;
            if (displaced.Count > 0) Data.Bag[bagIndex] = displaced[0];
            for (int i = 1; i < displaced.Count; i++)
            {
                int free = Data.Bag.IndexOf(null);
                Data.Bag[free] = displaced[i];
            }
            MarkStatsDirty();
            return null;
        }

        public string? Unequip(EquipSlot slot)
        {
            EnsureBagSize();
            if (!Data.Equipment.TryGetValue(slot, out var stack)) return "Nichts angelegt.";
            int free = Data.Bag.IndexOf(null);
            if (free < 0) return "Eure Taschen sind voll.";
            Data.Bag[free] = stack;
            Data.Equipment.Remove(slot);
            MarkStatsDirty();
            return null;
        }

        /// <summary>Haltbarkeitsverlust aller angelegten Gegenstände (Tod: 10 %, Geisterheiler: 25 %).</summary>
        public void LoseDurability(float fraction)
        {
            foreach (var kv in Data.Equipment)
            {
                if (!Content.Items.TryGetValue(kv.Value.ItemId, out var item) || item.MaxDurability <= 0) continue;
                kv.Value.Durability = Math.Max(0, kv.Value.Durability - (int)Math.Ceiling(item.MaxDurability * fraction));
            }
            MarkStatsDirty();
        }

        public int RepairCost()
        {
            int cost = 0;
            foreach (var kv in Data.Equipment)
                if (Content.Items.TryGetValue(kv.Value.ItemId, out var item) && item.MaxDurability > 0)
                    cost += (item.MaxDurability - kv.Value.Durability) * Math.Max(1, item.ItemLevel) / 4;
            return cost;
        }

        public void RepairAll()
        {
            foreach (var kv in Data.Equipment)
                if (Content.Items.TryGetValue(kv.Value.ItemId, out var item)) kv.Value.Durability = item.MaxDurability;
            MarkStatsDirty();
        }

        // ================================================================ Fähigkeiten

        public int KnownRank(string abilityBaseId) => Data.Abilities.TryGetValue(abilityBaseId, out var r) ? r : 0;

        public void Learn(string abilityBaseId, int rank)
        {
            if (KnownRank(abilityBaseId) < rank) Data.Abilities[abilityBaseId] = rank;
        }

        public int TalentPointsSpent => Data.Talents.Values.Sum();
        public int TalentPointsFree => Math.Max(0, Experience.TalentPointsForLevel(Level) - TalentPointsSpent);

        public int PointsInTree(string treeId) => Data.Talents.Where(kv => Content.TalentToTree.TryGetValue(kv.Key, out var t) && t == treeId).Sum(kv => kv.Value);

        // ================================================================ Netz

        public override S_EntitySpawn BuildSpawn(Player? viewer) => new()
        {
            Id = Id, Kind = EntityKind.Player, TemplateId = "", Name = Name, Title = IsBot ? (Data.ActiveTitle.Length > 0 ? "KI-Mitspieler · " + Data.ActiveTitle : "KI-Mitspieler") : Data.ActiveTitle, Race = Race, Class = Class, Gender = Data.Gender,
            Level = Level, Faction = Faction, PowerType = PowerType, Appearance = Data.Appearance.ToNet(), State = BuildSnapshot(), MountId = ActiveMount,
        };

        public override uint Flags
        {
            get
            {
                var f = (UnitFlags)base.Flags;
                if (IsBot) f |= UnitFlags.Bot;
                if (CurrentArea != null && (CurrentArea.IsInn || CurrentArea.IsCity)) f |= UnitFlags.Resting;
                return (uint)f;
            }
        }

        public S_PlayerState BuildPlayerState()
        {
            EnsureStats();
            return new S_PlayerState
            {
                Level = Level, Xp = Data.Xp, XpToNext = Experience.XpToNextLevel(Level), RestedXp = Data.RestedXp, Money = Money,
                TalentPointsFree = TalentPointsFree, Strength = Stats.Strength, Agility = Stats.Agility, Stamina = Stats.Stamina, Intellect = Stats.Intellect,
                Spirit = Stats.Spirit, Armor = Armor, AttackPower = AttackPower, SpellPower = SpellPower, MeleeCrit = MeleeCrit, SpellCrit = SpellCrit, Dodge = Dodge,
                RangedAttackPower = RangedAttackPower, HealingPower = HealingPower, Parry = Parry, Block = Block, Hit = HitBonus, MoveSpeed = MoveSpeed,
                Resist = Resist.Select(v => (int)v).ToList(),
                ReputationIds = Data.Reputation.Keys.OrderBy(k => k).ToList(),
                ReputationValues = Data.Reputation.OrderBy(k => k.Key).Select(k => k.Value).ToList(),
            };
        }

        public S_Inventory BuildInventory()
        {
            EnsureBagSize();
            var inv = new S_Inventory { Money = Money, BagSize = Data.Bag.Count };
            foreach (var s in Data.Bag) inv.Bag.Add(s?.ToNet() ?? new ItemStackData { ItemId = "", Count = 0 });
            for (int i = 0; i < ContainerSlots; i++)
                inv.Containers.Add(Data.BagContainers.TryGetValue(i, out var c) ? c.ToNet() : new ItemStackData { ItemId = "", Count = 0 });
            int slots = Enum.GetValues(typeof(EquipSlot)).Length;
            for (int i = 0; i < slots; i++)
                inv.Equipment.Add(Data.Equipment.TryGetValue((EquipSlot)i, out var e) ? e.ToNet() : new ItemStackData { ItemId = "", Count = 0 });
            return inv;
        }

        public S_SpellBook BuildSpellBook()
        {
            var m = new S_SpellBook();
            foreach (var kv in Data.Abilities.OrderBy(k => k.Key)) { m.AbilityIds.Add(kv.Key); m.Ranks.Add(kv.Value); }
            return m;
        }

        public S_Talents BuildTalents()
        {
            var m = new S_Talents { FreePoints = TalentPointsFree };
            foreach (var kv in Data.Talents) { m.TalentIds.Add(kv.Key); m.Ranks.Add(kv.Value); }
            return m;
        }

        public S_QuestLog BuildQuestLog() => new()
        {
            Quests = Data.Quests.Values.Select(q => q.ToNet()).ToList(),
            Completed = Data.CompletedQuests.ToList(),
        };
    }
}
