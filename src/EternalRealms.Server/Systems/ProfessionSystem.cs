using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Berufe (Kapitel 17): höchstens zwei Hauptberufe, dazu Angeln, Kochen und Erste Hilfe. Ränge Lehrling (75),
    /// Geselle (150), Experte (225), Meister (300). Fortschritt nach Schwierigkeit: orange immer, gelb meist, grün
    /// selten, grau nie. Alle Prüfungen (Fertigkeit, Werkzeug, Werkbank, Zutaten, Platz) erfolgen auf dem Server.
    /// </summary>
    public sealed class ProfessionSystem
    {
        public const int MaxPrimary = 2;
        public const float StationRange = 8f, SkinRange = 6f;
        public static readonly HashSet<string> SkinnableFamilies = new() { "Wolf", "Hyena", "Boar", "Lizard", "Wyrm", "Bear", "Cat", "Raptor" };
        public static readonly HashSet<string> MeatFamilies = new() { "Wolf", "Hyena", "Boar", "Lizard", "Wyrm", "Bear", "Cat", "Raptor", "Spider" };
        public static readonly HashSet<string> ClothFamilies = new() { "Humanoid", "Grubenwicht", "Undead" };

        private readonly GameWorld _w;
        public ProfessionSystem(GameWorld world) { _w = world; }

        private ProfessionProgress? Prog(Player p, string prof) => p.Data.Professions.GetValueOrDefault(prof);

        public S_Professions Build(Player p) => new()
        {
            Professions = p.Data.Professions.Select(kv => new ProfessionData { Id = kv.Key, Skill = kv.Value.Skill, MaxSkill = kv.Value.MaxSkill, Recipes = kv.Value.Recipes.OrderBy(r => r).ToList() }).ToList(),
        };

        private void Refresh(Player p) => p.Send(Build(p));

        /// <summary>Fortschrittschance nach Schwierigkeit (Farben wie im klassischen Vorbild).</summary>
        public static double SkillUpChance(int skill, int required, int yellow, int green, int gray) =>
            skill < required ? 0 : skill < yellow ? 1.0 : skill < green ? 0.75 : skill < gray ? 0.25 : 0;

        private void SkillUp(Player p, string prof, int required, int yellow, int green, int gray)
        {
            var pr = Prog(p, prof);
            if (pr == null || pr.Skill >= pr.MaxSkill) return;
            if (!_w.Random.Chance(SkillUpChance(pr.Skill, required, yellow, green, gray))) return;
            pr.Skill++;
            p.Notice($"{_w.Content.Professions[prof].Name}: Fertigkeit {pr.Skill}/{pr.MaxSkill}");
            Refresh(p);
        }

        // ------------------------------------------------------------ Lehrer

        private Creature? Trainer(Player p, int npcId) =>
            _w.Interactions.NpcWithRole(p, npcId, NpcRole.ProfessionTrainer) is Creature c && c.Template.TrainerProfession != null ? c : null;

        public S_ProfessionTrainer? BuildTrainer(Player p, Creature npc)
        {
            var profId = npc.Template.TrainerProfession;
            if (profId == null || !_w.Content.Professions.TryGetValue(profId, out var def)) return null;
            var pr = Prog(p, profId);
            var msg = new S_ProfessionTrainer { NpcId = npc.Id, Profession = profId, Known = pr != null, LearnCost = def.Ranks[0].Cost };
            if (pr != null)
            {
                var next = def.Ranks.FirstOrDefault(r => r.MaxSkill > pr.MaxSkill);
                if (next != null) { msg.NextRank = next.Name; msg.NextRankCost = next.Cost; }
                foreach (var r in _w.Content.Recipes.Values.Where(r => r.Profession == profId && !pr.Recipes.Contains(r.Id)).OrderBy(r => r.SkillRequired))
                {
                    msg.Recipes.Add(r.Id);
                    msg.RecipeCosts.Add(r.TrainerCost);
                }
            }
            return msg;
        }

        public string? Learn(Player p, int npcId)
        {
            var npc = Trainer(p, npcId);
            if (npc == null) return "Kein Berufslehrer in der Nähe.";
            var def = _w.Content.Professions[npc.Template.TrainerProfession!];
            if (p.Data.Professions.ContainsKey(def.Id)) return $"Ihr beherrscht {def.Name} bereits.";
            if (def.IsPrimary && p.Data.Professions.Keys.Count(k => _w.Content.Professions[k].IsPrimary) >= MaxPrimary)
                return "Ihr könnt höchstens zwei Hauptberufe erlernen. Verlernt zuerst einen anderen.";
            var rank = def.Ranks[0];
            if (p.Level < rank.RequiredLevel) return $"{def.Name} erfordert Stufe {rank.RequiredLevel}.";
            if (!_w.Economy.Debit(p, rank.Cost, "profession", def.Id)) return $"Das kostet {LootSystem.Money(rank.Cost)}.";
            var pr = new ProfessionProgress { Skill = 1, MaxSkill = rank.MaxSkill };
            foreach (var r in _w.Content.Recipes.Values.Where(r => r.Profession == def.Id && r.SkillRequired <= 1)) pr.Recipes.Add(r.Id);
            p.Data.Professions[def.Id] = pr;
            p.Notice($"Ihr habt {def.Name} ({rank.Name}) erlernt.");
            Refresh(p);
            p.Send(BuildTrainer(p, npc)!);
            return null;
        }

        public string? TrainRank(Player p, int npcId)
        {
            var npc = Trainer(p, npcId);
            if (npc == null) return "Kein Berufslehrer in der Nähe.";
            var def = _w.Content.Professions[npc.Template.TrainerProfession!];
            var pr = Prog(p, def.Id);
            if (pr == null) return $"Lernt zuerst {def.Name}.";
            var next = def.Ranks.FirstOrDefault(r => r.MaxSkill > pr.MaxSkill);
            if (next == null) return "Ihr seid bereits Meister.";
            if (pr.Skill < pr.MaxSkill - 25) return $"Erreicht zuerst Fertigkeit {pr.MaxSkill - 25}.";
            if (p.Level < next.RequiredLevel) return $"{next.Name} erfordert Stufe {next.RequiredLevel}.";
            if (!_w.Economy.Debit(p, next.Cost, "profession", def.Id + ":" + next.Name)) return $"Das kostet {LootSystem.Money(next.Cost)}.";
            pr.MaxSkill = next.MaxSkill;
            p.Notice($"{def.Name}: Ihr seid jetzt {next.Name} (bis {next.MaxSkill}).");
            Refresh(p);
            p.Send(BuildTrainer(p, npc)!);
            return null;
        }

        public string? LearnRecipe(Player p, int npcId, string recipeId)
        {
            var npc = Trainer(p, npcId);
            if (npc == null) return "Kein Berufslehrer in der Nähe.";
            if (!_w.Content.Recipes.TryGetValue(recipeId, out var r) || r.Profession != npc.Template.TrainerProfession) return "Dieses Rezept lehrt dieser Lehrer nicht.";
            var pr = Prog(p, r.Profession);
            if (pr == null) return "Lernt zuerst den Beruf.";
            if (pr.Recipes.Contains(r.Id)) return "Das Rezept kennt Ihr bereits.";
            if (pr.Skill < r.SkillRequired) return $"Erfordert Fertigkeit {r.SkillRequired}.";
            if (!_w.Economy.Debit(p, r.TrainerCost, "recipe", r.Id)) return $"Das Rezept kostet {LootSystem.Money(r.TrainerCost)}.";
            pr.Recipes.Add(r.Id);
            Refresh(p);
            p.Send(BuildTrainer(p, npc)!);
            return null;
        }

        public string? Unlearn(Player p, string prof)
        {
            if (!p.Data.Professions.Remove(prof)) return "Diesen Beruf beherrscht Ihr nicht.";
            p.Notice($"Ihr habt {_w.Content.Professions[prof].Name} verlernt.");
            Refresh(p);
            return null;
        }

        // ------------------------------------------------------------ Sammeln

        /// <summary>Darf der Spieler dieses Vorkommen abbauen? (vor Beginn der Interaktion)</summary>
        public string? CanGather(Player p, GameObjectDef node)
        {
            var prof = node.Profession!;
            var def = _w.Content.Professions[prof];
            var pr = Prog(p, prof);
            if (pr == null) return $"Dafür braucht Ihr {def.Name}.";
            if (pr.Skill < node.SkillRequired) return $"Erfordert {def.Name} {node.SkillRequired}.";
            if (def.Tool != null && p.CountItem(def.Tool) == 0) return $"Ihr braucht: {_w.Content.Items[def.Tool].Name}.";
            return null;
        }

        public void OnGathered(Player p, GameObjectDef node)
        {
            int req = Math.Max(1, node.SkillRequired);
            SkillUp(p, node.Profession!, req, req + 25, req + 50, req + 100);
        }

        public static int SkinningRequired(int level) => level <= 10 ? 1 : (level - 10) * 10;

        public string? Skin(Player p, int corpseId)
        {
            if (p.Zone?.Get(corpseId) is not Creature c || !c.IsDead) return "Hier gibt es nichts zu häuten.";
            if (!SkinnableFamilies.Contains(c.Template.Family) || c.Skinned) return "Das kann man nicht häuten.";
            if (c.Lootable) return "Plündert zuerst die Beute.";
            if (c.TappedBy != null && c.TappedBy != p && (p.Group == null || c.TappedBy.Group != p.Group)) return "Diese Beute gehört jemand anderem.";
            if (p.DistanceTo(c) > SkinRange) return "Zu weit entfernt.";
            var pr = Prog(p, "skinning");
            if (pr == null) return "Dafür braucht Ihr Kürschnerei.";
            int req = SkinningRequired(c.Level);
            if (pr.Skill < req) return $"Erfordert Kürschnerei {req}.";
            var tool = _w.Content.Professions["skinning"].Tool;
            if (tool != null && p.CountItem(tool) == 0) return $"Ihr braucht: {_w.Content.Items[tool].Name}.";
            return Begin(p, 2f, "skin", () =>
            {
                if (c.Skinned || c.Removed) return;
                c.Skinned = true;
                int tier = c.Level <= 10 ? 0 : c.Level <= 20 ? 1 : c.Level <= 30 ? 2 : 3;
                int n = p.AddItem($"item_leather{tier}", _w.Random.Range(1, 3));
                if (n > 0) p.Send(new S_LootResult { CorpseId = c.Id, Items = { new ItemStackData { ItemId = $"item_leather{tier}", Count = n } } });
                p.Send(p.BuildInventory());
                SkillUp(p, "skinning", req, req + 25, req + 50, req + 100);
                c.CorpseTimer = Math.Min(c.CorpseTimer, 5);
            });
        }

        // ------------------------------------------------------------ Herstellen

        public bool NearStation(Player p, string station) =>
            p.Zone != null && p.Zone.Objects.Any(o => o.Def.Station == station && p.DistanceTo(o) <= StationRange);

        public string? CanCraft(Player p, RecipeDef r)
        {
            var pr = Prog(p, r.Profession);
            if (pr == null || !pr.Recipes.Contains(r.Id)) return "Dieses Rezept kennt Ihr nicht.";
            if (pr.Skill < r.SkillRequired) return $"Erfordert Fertigkeit {r.SkillRequired}.";
            if (r.Tool != null && p.CountItem(r.Tool) == 0) return $"Ihr braucht: {_w.Content.Items[r.Tool].Name}.";
            if (r.Station != null && !NearStation(p, r.Station))
                return r.Station switch { "anvil" => "Ihr müsst an einem Amboss stehen.", "forge" => "Ihr müsst an einem Schmelzofen stehen.", _ => "Ihr müsst an einem Kochfeuer stehen." };
            foreach (var g in r.Reagents) if (p.CountItem(g.Item) < g.Count) return $"Es fehlt: {_w.Content.Items[g.Item].Name}.";
            if (r.Enchant != null)
            {
                if (!p.Data.Equipment.TryGetValue(r.Enchant.Slot, out var target)) return "Dort ist kein Gegenstand angelegt.";
                if (target.Enchant == r.Id) return "Diese Verzauberung trägt der Gegenstand bereits.";
            }
            else if (p.SpaceFor(r.Creates!.Item) < r.Creates.Count) return "Eure Taschen sind voll.";
            return null;
        }

        public string? Craft(Player p, string recipeId, int count)
        {
            if (!_w.Content.Recipes.TryGetValue(recipeId, out var r)) return "Unbekanntes Rezept.";
            if (p.InCombat) return "Nicht im Kampf.";
            if (p.Cast != null) return "Ihr seid beschäftigt.";
            var err = CanCraft(p, r);
            if (err != null) return err;
            count = Math.Clamp(count, 1, r.Enchant != null ? 1 : 100);
            return Begin(p, r.CastTime, "craft:" + r.Id, () => FinishCraft(p, r, count - 1));
        }

        private void FinishCraft(Player p, RecipeDef r, int remaining)
        {
            var err = CanCraft(p, r);
            if (err != null) { p.Notice(err, true); return; }
            foreach (var g in r.Reagents) p.RemoveItem(g.Item, g.Count);
            if (r.Enchant != null)
            {
                var target = p.Data.Equipment[r.Enchant.Slot];
                target.Enchant = r.Id;
                p.MarkStatsDirty();
                p.Notice($"{_w.Content.Items[target.ItemId].Name}: {r.Enchant.Name}");
                p.Send(p.BuildPlayerState());
            }
            else
            {
                p.AddItem(r.Creates!.Item, r.Creates.Count);
                p.Send(new S_LootResult { Items = { new ItemStackData { ItemId = r.Creates.Item, Count = r.Creates.Count } } });
                _w.Quests.OnItemsChanged(p);
            }
            p.Send(p.BuildInventory());
            SkillUp(p, r.Profession, r.SkillRequired, r.Yellow, r.Green, r.Gray);
            if (remaining > 0 && CanCraft(p, r) == null && !p.InCombat)
                Begin(p, r.CastTime, "craft:" + r.Id, () => FinishCraft(p, r, remaining - 1));
        }

        // ------------------------------------------------------------ Entzaubern

        public static int DisenchantRequired(int itemLevel) => itemLevel <= 15 ? 1 : itemLevel <= 25 ? 75 : itemLevel <= 35 ? 150 : 225;

        public string? Disenchant(Player p, int bagIndex)
        {
            if (bagIndex < 0 || bagIndex >= p.Data.Bag.Count || p.Data.Bag[bagIndex] is not { } stack) return "Kein Gegenstand.";
            var def = _w.Content.Items[stack.ItemId];
            if (!def.IsEquippable || def.Quality < ItemQuality.Uncommon || def.Quality > ItemQuality.Epic) return "Nur ungewöhnliche bis epische Ausrüstung lässt sich entzaubern.";
            var pr = Prog(p, "enchanting");
            if (pr == null) return "Dafür braucht Ihr Verzauberkunst.";
            int req = DisenchantRequired(def.ItemLevel);
            if (pr.Skill < req) return $"Erfordert Verzauberkunst {req}.";
            if (p.InCombat) return "Nicht im Kampf.";
            return Begin(p, 3f, "disenchant", () =>
            {
                if (bagIndex >= p.Data.Bag.Count || p.Data.Bag[bagIndex] != stack) return; // Gegenstand inzwischen bewegt
                p.Data.Bag[bagIndex] = null;
                int tier = def.ItemLevel <= 15 ? 0 : def.ItemLevel <= 25 ? 1 : def.ItemLevel <= 35 ? 2 : 3;
                var got = new S_LootResult();
                int dust = p.AddItem($"item_dust{tier}", _w.Random.Range(1, 2 + (int)def.Quality - 2));
                if (dust > 0) got.Items.Add(new ItemStackData { ItemId = $"item_dust{tier}", Count = dust });
                if (def.Quality >= ItemQuality.Rare || _w.Random.Chance(0.25))
                {
                    int e = p.AddItem($"item_essence{tier}", 1);
                    if (e > 0) got.Items.Add(new ItemStackData { ItemId = $"item_essence{tier}", Count = e });
                }
                p.Send(got);
                p.Send(p.BuildInventory());
                SkillUp(p, "enchanting", req, req + 25, req + 50, req + 100);
            });
        }

        // ------------------------------------------------------------ Beute

        /// <summary>Stoff von Humanoiden, Fleisch von Tieren – nach Stufe des Gegners.</summary>
        public void AddGenericLoot(Creature c)
        {
            int tier = c.Level <= 10 ? 0 : c.Level <= 20 ? 1 : c.Level <= 30 ? 2 : 3;
            if (ClothFamilies.Contains(c.Template.Family) && _w.Random.Chance(0.3)) c.Loot.Add(new LootItem { ItemId = $"item_cloth{tier}", Count = _w.Random.Range(1, 2) });
            if (MeatFamilies.Contains(c.Template.Family) && _w.Random.Chance(0.35)) c.Loot.Add(new LootItem { ItemId = $"item_meat{tier}", Count = 1 });
        }

        // ------------------------------------------------------------ Hilfen

        private string? Begin(Player p, float seconds, string label, Action done)
        {
            if (p.Cast != null) return "Ihr seid beschäftigt.";
            if (p.IsMounted) _w.Mounts.Dismount(p);
            p.Cast = new CastState { Ability = Work, Remaining = seconds, Total = seconds, StartPosition = p.Position, OnComplete = done, Label = label };
            p.Zone?.BroadcastFrom(p, new S_CastStart { CasterId = p.Id, TargetId = p.Id, AbilityId = label, CastTime = seconds });
            return null;
        }

        private static readonly AbilityDef Work = new() { Id = "work", Name = "Handwerk", Target = TargetType.Self, TriggersGcd = false };

        // ------------------------------------------------------------ KI-Bewohner

        private static readonly (string a, string b)[] BotPairs =
        {
            ("mining", "blacksmithing"), ("herbalism", "alchemy"), ("skinning", "leatherworking"), ("mining", "engineering"),
            ("herbalism", "skinning"), ("tailoring", "enchanting"), ("mining", "herbalism"),
        };

        /// <summary>Weltbewohner bekommen zwei passende Berufe mit Fertigkeit nach ihrer Stufe.</summary>
        public void AssignBotProfessions(CharacterData bot)
        {
            if (bot.Professions.Count > 0) return;
            var (a, b) = BotPairs[(int)((uint)StableHash.Of(bot.Name) % BotPairs.Length)];
            foreach (var id in new[] { a, b, "first_aid", "cooking" })
            {
                if (!_w.Content.Professions.TryGetValue(id, out var def)) continue;
                int skill = Math.Clamp(bot.Level * 5, 1, 300);
                var rank = def.Ranks.FirstOrDefault(r => r.MaxSkill >= skill && r.RequiredLevel <= Math.Max(5, bot.Level)) ?? def.Ranks[0];
                var pr = new ProfessionProgress { Skill = Math.Min(skill, rank.MaxSkill), MaxSkill = rank.MaxSkill };
                foreach (var r in _w.Content.Recipes.Values.Where(r => r.Profession == id && r.SkillRequired <= pr.Skill)) pr.Recipes.Add(r.Id);
                bot.Professions[id] = pr;
            }
        }

        /// <summary>Weltbewohner stellen her, was ohne Werkbank geht und wofür Zutaten da sind.</summary>
        public void BotCraft(Player bot)
        {
            foreach (var r in _w.Content.Recipes.Values.Where(r => r.Station == null && r.Enchant == null && bot.Data.Professions.TryGetValue(r.Profession, out var pr) && pr.Recipes.Contains(r.Id)))
                for (int i = 0; i < 5 && CanCraft(bot, r) == null; i++)
                {
                    foreach (var g in r.Reagents) bot.RemoveItem(g.Item, g.Count);
                    bot.AddItem(r.Creates!.Item, r.Creates.Count);
                    SkillUp(bot, r.Profession, r.SkillRequired, r.Yellow, r.Green, r.Gray);
                }
        }
    }
}
