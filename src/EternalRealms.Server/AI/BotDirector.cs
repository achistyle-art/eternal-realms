using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.AI
{
    /// <summary>
    /// Verwaltet alle KI-Mitspieler: die Suche "KI-Mitspieler suchen", Einladen/Entlassen, die
    /// eigenständige Bevölkerung jeder Zone, Ausrüstungsentscheidungen und den zeitversetzten Chat.
    /// </summary>
    public sealed class BotDirector
    {
        private readonly GameWorld _w;
        private readonly Dictionary<long, Candidate> _candidates = new();
        /// <summary>Gespeicherte, gerade nicht aktive Bots (für "Bekannte" und die Zonenbevölkerung).</summary>
        private readonly Dictionary<long, CharacterData> _offline = new();
        private readonly List<(double at, Player bot, ChatChannel ch, string text)> _chatQueue = new();
        private readonly List<(double at, Player bot, string emote, int target)> _emoteQueue = new();
        private long _nextCandidateId = 1;
        private float _populationTimer = 2;
        private bool _loaded;


        private sealed class Candidate
        {
            public CharacterData Data = null!;
            public double Expires;
            public long OwnerAccount;
        }

        public BotDirector(GameWorld world) { _w = world; }

        public void LoadOffline(IEnumerable<CharacterData> bots)
        {
            foreach (var b in bots)
            {
                var r = ContentMigrator.Apply(b, _w.Content);
                b.Money += r.Refund; // Bots haben kein Geldbuch-Konto für Erstattungen; der Stand wird beim nächsten Speichern geschrieben
                _offline[b.Id] = b;
            }
            _loaded = true;
        }

        public IEnumerable<Player> ActiveBots => _w.AllPlayers.Where(p => p.IsBot);

        // ================================================================== Suche & Einladung

        /// <summary>
        /// "KI-Mitspieler suchen": schlägt Bots vor, die der Gruppe fehlende Rollen füllen –
        /// zuerst bekannte Bots, mit denen das Konto schon gespielt hat, dann neue.
        /// </summary>
        public List<BotCandidateData> FindCandidates(Player p, Role? roleFilter, BotDifficulty? difficulty = null)
        {
            foreach (var k in _candidates.Where(kv => kv.Value.Expires < _w.Time || kv.Value.OwnerAccount == (p.Data.AccountId ?? -1)).Select(kv => kv.Key).ToList())
                _candidates.Remove(k);
            var groupRoles = p.Group?.Members.Where(m => m != p).Select(m => m.Bot?.Role ?? Role.MeleeDps).ToList() ?? new List<Role>();
            var needed = new List<Role>();
            if (roleFilter.HasValue) needed.AddRange(Enumerable.Repeat(roleFilter.Value, 3));
            else
            {
                if (!groupRoles.Contains(Role.Tank)) needed.Add(Role.Tank);
                if (!groupRoles.Contains(Role.Healer)) needed.Add(Role.Healer);
                needed.Add(Role.MeleeDps);
                needed.Add(Role.RangedDps);
                needed.Add(Role.RangedDps);
            }
            var result = new List<BotCandidateData>();
            long account = p.Data.AccountId ?? -1;
            bool Fits(CharacterData b) => b.Bot != null && _w.Content.Races[b.Race].Faction == p.Faction && (!roleFilter.HasValue || b.Bot.Role == roleFilter)
                                          && (!difficulty.HasValue || b.Bot.Difficulty == difficulty);
            // 1. Bevorzugte Begleiter – unabhängig von der Stufe; sie haben inzwischen eigene Abenteuer erlebt.
            foreach (var fav in _offline.Values.Where(b => Fits(b) && b.Bot!.FavoriteOf.Contains(account)).Take(4).ToList())
            {
                CatchUp(fav, p.Level);
                result.Add(Register(fav, account, known: true));
            }
            // 2. Bekannte Bots, mit denen das Konto schon unterwegs war
            foreach (var known in _offline.Values.Where(b => Fits(b) && !b.Bot!.Ambient && b.Bot.KnownAccounts.Contains(account) && !b.Bot.FavoriteOf.Contains(account)
                                                            && Math.Abs(b.Level - p.Level) <= 3).Take(3).ToList())
                result.Add(Register(known, account, known: true));
            // 3. Neue Abenteurer für fehlende Rollen
            foreach (var role in needed)
            {
                if (result.Count >= 8) break;
                var data = CreateBotData(p.Faction, role, Math.Clamp(p.Level - (_w.Random.Chance(0.3) ? 1 : 0), 1, _w.Content.LevelCap), null, p.Zone!.Def.Id, difficulty: difficulty);
                if (data != null) result.Add(Register(data, account, known: false));
            }
            return result;
        }

        private BotCandidateData Register(CharacterData data, long account, bool known)
        {
            long id = _nextCandidateId++;
            _candidates[id] = new Candidate { Data = data, Expires = _w.Time + 300, OwnerAccount = account };
            var bot = data.Bot!;
            var line = PickLine("join", bot.Role, bot.Archetype) ?? "Hallo!";
            bool favorite = bot.FavoriteOf.Contains(account);
            return new BotCandidateData
            {
                CandidateId = id, Name = data.Name, Race = data.Race, Class = data.Class, Level = data.Level, Role = bot.Role,
                Personality = (favorite ? "Favorit, " : known ? "bekannt, " : "") + BotPersonality.Label(bot.Archetype) + ", " + BotPersonality.DifficultyLabel(bot.Difficulty),
                Greeting = line.Replace("{leader}", "").Replace("{me}", data.Name).Replace(" !", "!"),
                Difficulty = (byte)bot.Difficulty, IsFavorite = favorite,
            };
        }

        /// <summary>Klassischer Realm: keine KI-Mitspieler (Kapitel 60).</summary>
        public const string NoCompanions = "Auf diesem Realm (klassisch) gibt es keine KI-Mitspieler.";

        public string? Invite(Player p, long candidateId)
        {
            if (!_w.Config.BotCompanions) return NoCompanions;
            if (!_candidates.TryGetValue(candidateId, out var cand)) return "Dieser KI-Mitspieler ist nicht mehr verfügbar. Sucht erneut.";
            if (p.Group != null && p.Group.IsFull) return "Eure Gruppe ist voll.";
            if (p.Zone == null) return "Nicht in der Welt.";
            _candidates.Remove(candidateId);
            Summon(p, cand.Data);
            return null;
        }

        /// <summary>Gespeicherten Lieblingsbegleiter direkt in die Gruppe rufen (KI-Freundesliste, Kapitel 32).</summary>
        public string? InviteFavorite(Player p, long characterId)
        {
            if (!_w.Config.BotCompanions) return NoCompanions;
            long account = p.Data.AccountId ?? -1;
            if (p.Group != null && p.Group.IsFull) return "Eure Gruppe ist voll.";
            if (p.Group != null && p.Group.Leader != p) return "Nur der Gruppenanführer kann einladen.";
            if (p.Zone == null) return "Nicht in der Welt.";
            var online = _w.AllPlayers.FirstOrDefault(b => b.IsBot && b.Data.Id == characterId);
            if (online != null)
            {
                if (!online.Data.Bot!.FavoriteOf.Contains(account)) return "Dieser KI-Charakter ist kein gespeicherter Begleiter.";
                if (online.Group == p.Group && p.Group != null) return $"{online.Name} ist bereits in Eurer Gruppe.";
                if (online.Group != null) return $"{online.Name} ist gerade mit {online.Group.Leader.Name} unterwegs.";
                DespawnBot(online);
            }
            if (!_offline.TryGetValue(characterId, out var data) || !data.Bot!.FavoriteOf.Contains(account)) return "Dieser KI-Charakter ist kein gespeicherter Begleiter.";
            CatchUp(data, p.Level);
            Summon(p, data);
            return null;
        }

        /// <summary>Alle gespeicherten Begleiter eines Kontos mit ihrem aktuellen Aufenthalt (null = ruht).</summary>
        public List<(CharacterData data, Player? online)> Companions(Player p)
        {
            long account = p.Data.AccountId ?? -1;
            var list = new List<(CharacterData, Player?)>();
            foreach (var b in _w.AllPlayers.Where(b => b.IsBot && b.Data.Bot!.FavoriteOf.Contains(account))) list.Add((b.Data, b));
            foreach (var d in _offline.Values.Where(d => d.Bot != null && d.Bot.FavoriteOf.Contains(account))) list.Add((d, null));
            return list;
        }

        /// <summary>
        /// KI-Mitspieler für eine Rolle in die Gruppe holen (Gruppenfinder): erst Lieblingsbegleiter, dann Bekannte,
        /// dann ein neuer Charakter passender Stufe.
        /// </summary>
        public Player? SummonForRole(Player leader, Role role, int level, int minLevel, int maxLevel)
        {
            long account = leader.Data.AccountId ?? -1;
            int lo = Math.Max(minLevel, level - 3), hi = Math.Min(Math.Max(maxLevel, minLevel), level + 3);
            CharacterData? data = _offline.Values.Where(b => b.Bot != null && b.Bot.Role == role && !b.Bot.Ambient && _w.Content.Races[b.Race].Faction == leader.Faction)
                .OrderByDescending(b => b.Bot!.FavoriteOf.Contains(account)).ThenByDescending(b => b.Bot!.KnownAccounts.Contains(account))
                .FirstOrDefault(b => (b.Bot!.FavoriteOf.Contains(account) || b.Bot.KnownAccounts.Contains(account)) && b.Level >= lo - 2 && b.Level <= hi + 2);
            if (data != null) CatchUp(data, level);
            data ??= CreateBotData(leader.Faction, role, Math.Clamp(level, lo, Math.Max(lo, hi)), null, leader.Zone!.Def.Id);
            if (data == null) return null;
            Summon(leader, data);
            return leader.Group?.Members.LastOrDefault(m => m.Data == data);
        }

        private void Summon(Player p, CharacterData data)
        {
            data.Bot!.KnownAccounts.Add(p.Data.AccountId ?? -1);
            _offline.Remove(data.Id);
            // Bots kommen angelaufen, statt neben dem Spieler aufzutauchen.
            double ang = _w.Random.NextDouble() * Math.PI * 2;
            data.Position = p.Position + new Vec3((float)Math.Cos(ang) * 18, 0, (float)Math.Sin(ang) * 18);
            data.ZoneId = p.Zone.Def.Id;
            var bot = Spawn(data, p.Zone);
            if (p.Group == null) _w.Groups.Create(p);
            _w.Groups.Join(p.Group!, bot);
            bot.Bot!.Greet(p);
            if (data.Id == 0) PersistNew(bot);
        }

        /// <summary>
        /// Hintergrundentwicklung eines bevorzugten Begleiters: Pro Stunde ohne den Spieler steigt er eine Stufe,
        /// höchstens bis zur Stufe des Spielers. Die Entwicklung wird gespeichert und in seiner Chronik vermerkt.
        /// </summary>
        public void CatchUp(CharacterData bot, int playerLevel)
        {
            if (bot.Bot == null || bot.Level >= playerLevel) return;
            double hours = (DateTime.UtcNow - bot.Bot.LastActive).TotalHours;
            int target = Math.Min(playerLevel, bot.Level + (int)Math.Floor(hours));
            if (target <= bot.Level) return;
            var tmp = new Player(bot, _w.Content);
            while (tmp.Level < target) _w.Progression.LevelUpSilently(tmp);
            _w.Progression.AutoTrain(tmp);
            var profile = _w.Content.GetBotProfile(bot.Class, bot.Bot.Role);
            if (profile != null) _w.Progression.AutoSpendTalents(tmp, profile.TalentTree);
            bot.Level = tmp.Level;
            bot.Bot.History.Add($"Auf eigenen Abenteuern Stufe {bot.Level} erreicht");
            bot.Bot.LastActive = DateTime.UtcNow;
            _w.SaveData(bot);
        }

        public string? SetFavorite(Player owner, int botEntityId, bool favorite)
        {
            var bot = owner.Group?.Members.FirstOrDefault(m => m.Id == botEntityId && m.IsBot);
            if (bot == null) return "Dieser KI-Mitspieler ist nicht in Eurer Gruppe.";
            long acc = owner.Data.AccountId ?? -1;
            if (favorite) bot.Data.Bot!.FavoriteOf.Add(acc); else bot.Data.Bot!.FavoriteOf.Remove(acc);
            owner.Notice(favorite ? $"{bot.Name} ist jetzt ein bevorzugter Begleiter." : $"{bot.Name} ist kein bevorzugter Begleiter mehr.");
            if (favorite) bot.Bot!.Say("favorite", owner, force: true);
            _w.Save(bot);
            return null;
        }

        /// <summary>Taktischer Hinweis eines Veteranen zu einem Boss (content/bots/dialog.json, Ereignis "boss_tip:&lt;id&gt;").</summary>
        public string? BossTip(string creatureId)
        {
            var d = _w.Content.BotDialog.FirstOrDefault(x => x.Event == "boss_tip:" + creatureId);
            return d == null || d.Lines.Count == 0 ? null : d.Lines[_w.Random.Range(0, d.Lines.Count - 1)];
        }

        public string? Dismiss(Player leader, int botEntityId)
        {
            var bot = leader.Group?.Members.FirstOrDefault(m => m.Id == botEntityId && m.IsBot);
            if (bot == null) return "Dieser KI-Mitspieler ist nicht in Eurer Gruppe.";
            bot.Bot!.Say("dismiss", force: true);
            DespawnBot(bot);
            return null;
        }

        /// <summary>Entfernt einen Bot aus der Welt (speichert ihn und merkt ihn sich als "bekannt").</summary>
        public void DespawnBot(Player bot)
        {
            if (bot.Data.Bot != null) bot.Data.Bot.LastActive = DateTime.UtcNow;
            if (bot.Group != null) _w.Groups.Leave(bot);
            _w.Save(bot);
            if (bot.Data.Id > 0) _offline[bot.Data.Id] = bot.Data;
            _w.RemoveFromWorld(bot);
        }

        public Player Spawn(CharacterData data, ZoneInstance zone)
        {
            var bot = new Player(data, _w.Content);
            bot.Bot = new BotBrain(bot, _w);
            bot.EnsureBagSize();
            _w.AddToZone(bot, zone, data.Position);
            return bot;
        }

        private void PersistNew(Player bot)
        {
            var repo = _w.Characters;
            if (repo == null) return;
            var snapshot = bot.Data.Clone();
            _w.Persistence!.Enqueue(async () =>
            {
                try
                {
                    long id = await repo.CreateAsync(snapshot);
                    _w.Post(() => bot.Data.Id = id);
                }
                catch (Exception ex) { _w.Log("Bot konnte nicht gespeichert werden: " + ex.Message); }
            });
        }

        // ================================================================== Erzeugen

        /// <summary>Erzeugt einen vollständigen Bot-Charakter: Volk/Klasse passend zur Rolle, Fähigkeiten und Talente seiner Stufe.</summary>
        public CharacterData? CreateBotData(Faction faction, Role role, int level, CharClass? cls, string zoneId, bool ambient = false, BotDifficulty? difficulty = null)
        {
            // Nur Klassen, die diese Rolle auf dieser Stufe schon ausfüllen können (z. B. Bärengestalt erst ab Stufe 10).
            var classes = _w.Content.BotProfiles.Where(pr => pr.Role == role && RoleAvailableAt(pr, level)).Select(pr => pr.Class).Distinct()
                .Where(c => _w.Content.Races.Values.Any(r => r.Faction == faction && r.Classes.Contains(c))).ToList();
            if (cls.HasValue) classes = classes.Where(c => c == cls).ToList();
            if (classes.Count == 0) return null;
            var chosen = classes[_w.Random.Range(0, classes.Count - 1)];
            var races = _w.Content.Races.Values.Where(r => r.Faction == faction && r.Classes.Contains(chosen)).ToList();
            var race = races[_w.Random.Range(0, races.Count - 1)];
            var name = NameGenerator.Generate(race, _w.Random, n => _w.NameTaken(n) || _offline.Values.Any(o => o.Name == n) || _candidates.Values.Any(c => c.Data.Name == n));
            var data = _w.CreateCharacterTemplate(name, race.Id, chosen, _w.Random.Chance(0.5) ? Gender.Male : Gender.Female, RandomAppearance(race));
            data.IsBot = true;
            data.AccountId = null;
            data.ZoneId = zoneId;
            string archetype = BotPersonality.All[_w.Random.Range(0, BotPersonality.All.Length - 1)];
            data.Bot = new BotData { Role = role, Ambient = ambient, HomeZone = zoneId };
            var diff = difficulty ?? (BotDifficulty)(_w.Random.Chance(0.15) ? 0 : _w.Random.Chance(0.6) ? 1 : _w.Random.Chance(0.7) ? 2 : 3);
            BotPersonality.Apply(data.Bot, archetype, diff, _w.Random);
            data.Level = level;
            // Ab Stufe 6 ersetzt die stufengerechte Grundausrüstung die Startausrüstung.
            if (level >= 6) data.Equipment.Clear();
            // Fähigkeiten und Talente der Stufe
            var tmp = new Player(data, _w.Content);
            _w.Progression.AutoTrain(tmp);
            var profile = _w.Content.GetBotProfile(chosen, role);
            if (profile != null) _w.Progression.AutoSpendTalents(tmp, profile.TalentTree);
            data.Money = level * level * 15;
            data.Health = 0;
            data.Mana = 0;
            return data;
        }

        private bool RoleAvailableAt(BotProfileDef profile, int level)
        {
            if (profile.Form == Form.None) return true;
            var formAbility = _w.Content.ClassAbilityBases(profile.Class).FirstOrDefault(a =>
                a.Effects.Any(e => e.Type == EffectType.ApplyAura && e.Aura != null && _w.Content.Auras[e.Aura].Form == profile.Form));
            return formAbility == null || formAbility.Level <= level;
        }

        private Persistence.AppearanceDto RandomAppearance(RaceDef r)
        {
            var o = r.Appearance;
            byte Pick(int n) => (byte)(n <= 0 ? 0 : _w.Random.Range(0, n - 1));
            return new Persistence.AppearanceDto
            {
                SkinColor = Pick(o.SkinColors), Face = Pick(o.Faces), HairStyle = Pick(o.HairStyles), HairColor = Pick(o.HairColors), Beard = Pick(o.Beards),
                EyeColor = Pick(o.EyeColors), Scar = Pick(o.Scars), Tattoo = Pick(o.Tattoos),
                Height = r.MinHeight + (float)_w.Random.NextDouble() * (r.MaxHeight - r.MinHeight),
            };
        }

        // ================================================================== Bevölkerung

        public void Update(float dt)
        {
            for (int i = _chatQueue.Count - 1; i >= 0; i--)
            {
                var (at, bot, ch, text) = _chatQueue[i];
                if (at > _w.Time) continue;
                _chatQueue.RemoveAt(i);
                if (!bot.Removed && bot.Zone != null) _w.Chat.BotSay(bot, ch, text);
            }
            for (int i = _emoteQueue.Count - 1; i >= 0; i--)
            {
                var (at, bot, emote, target) = _emoteQueue[i];
                if (at > _w.Time) continue;
                _emoteQueue.RemoveAt(i);
                if (!bot.Removed && bot.Zone != null && !bot.InCombat) _w.Emotes.Perform(bot, emote, target);
            }
            if (!_w.Config.AmbientBots || !_loaded) return;
            _populationTimer -= dt;
            if (_populationTimer > 0) return;
            _populationTimer = 10;
            foreach (var zone in _w.OpenZones)
            {
                int target = zone.Def.AmbientBots;
                int current = zone.Players.Count(p => p.IsBot && p.Data.Bot!.Ambient && p.Group == null);
                if (current >= target) continue;
                // Zuerst gespeicherte Bewohner dieser Zone zurückholen, dann neue erschaffen.
                var stored = _offline.Values.FirstOrDefault(b => b.Bot?.Ambient == true && b.Bot.HomeZone == zone.Def.Id);
                CharacterData? data = stored;
                if (data != null) _offline.Remove(data.Id);
                else
                {
                    var roles = new[] { Role.Tank, Role.Healer, Role.MeleeDps, Role.RangedDps };
                    int lvl = _w.Random.Range(zone.Def.MinLevel, Math.Max(zone.Def.MinLevel, zone.Def.MaxLevel - 4));
                    data = CreateBotData(zone.Def.Faction, roles[_w.Random.Range(0, 3)], lvl, null, zone.Def.Id, ambient: true);
                    if (data == null) continue;
                    _w.Professions.AssignBotProfessions(data);
                    var town = zone.Def.Areas.FirstOrDefault(a => a.IsCity) ?? zone.Def.Areas.First();
                    data.Position = town.Center + new Vec3(_w.Random.Range(-10, 10), 0, _w.Random.Range(-10, 10));
                }
                var bot = Spawn(data, zone);
                if (data.Id == 0) PersistNew(bot);
            }
        }

        // ================================================================== Ausrüstung

        /// <summary>Legt einen Gegenstand aus der Tasche an, wenn er für den Bot besser ist. Rückgabe: angelegt.</summary>
        public bool ConsiderEquip(Player bot, int bagIndex)
        {
            var stack = bot.Data.Bag[bagIndex];
            if (stack == null || !_w.Content.Items.TryGetValue(stack.ItemId, out var item)) return false;
            return IsUpgrade(bot, item) && bot.Equip(bagIndex) == null;
        }

        /// <summary>Wäre der Gegenstand für diesen Bot eine echte Verbesserung (mindestens 5 %)?</summary>
        public bool IsUpgrade(Player bot, ItemDef item)
        {
            if (!item.IsEquippable || bot.CanEquip(item) != null) return false;
            var slot = bot.SlotFor(item);
            float current = bot.Data.Equipment.TryGetValue(slot, out var cur) ? BotGear.Score(bot, _w.Content.Items[cur.ItemId]) : BotGear.VirtualSlotScore(bot);
            return BotGear.Score(bot, item) > current * 1.05f;
        }

        // ================================================================== Weltbewohner (Kapitel 9.4)

        private Dictionary<string, List<string>>? _droppers;

        /// <summary>Welche Kreaturen einen Gegenstand fallen lassen (für Sammelquests der Weltbewohner).</summary>
        public IReadOnlyList<string> DroppersOf(string itemId)
        {
            _droppers ??= _w.Content.Creatures.Values.SelectMany(c => c.Loot.Select(l => (l.Item, c.Id)))
                .GroupBy(x => x.Item).ToDictionary(g => g.Key, g => g.Select(x => x.Id).Distinct().ToList());
            return _droppers.TryGetValue(itemId, out var list) ? list : (IReadOnlyList<string>)Array.Empty<string>();
        }

        /// <summary>
        /// Besuch beim Auktionator: Post abholen (Erlöse, Rücksendungen) und gute Funde in begrenzter Zahl einstellen.
        /// Was nicht eingestellt werden kann, verkauft der Bot zum Händlerpreis.
        /// </summary>
        public void OnBotVisitsAuctioneer(Player bot)
        {
            foreach (var m in _w.Mail.Inbox(bot.Data.Id).ToList()) _w.Mail.Take(bot, m.Id, 255);
            for (int i = 0; i < bot.Data.Bag.Count; i++)
            {
                var st = bot.Data.Bag[i];
                if (st == null || !_w.Content.Items.TryGetValue(st.ItemId, out var d)) continue;
                if (d.Quality < ItemQuality.Uncommon || d.Category is ItemCategory.Quest or ItemCategory.Consumable or ItemCategory.Container) continue;
                if (d.IsEquippable && ConsiderEquip(bot, i)) continue;
                _w.Auctions.TryBotPost(bot, i);
            }
            for (int i = 0; i < bot.Data.Bag.Count; i++)
            {
                var s = bot.Data.Bag[i];
                if (s == null || !_w.Content.Items.TryGetValue(s.ItemId, out var def)) continue;
                if (def.Quality < ItemQuality.Uncommon || def.Category is ItemCategory.Quest or ItemCategory.Consumable) continue;
                if (def.IsEquippable && ConsiderEquip(bot, i)) continue;
                _w.Economy.Credit(bot, (long)def.SellPrice * s.Count, "vendor_sell", $"{s.ItemId}x{s.Count}");
                bot.Data.Bag[i] = null;
            }
        }

        /// <summary>Ein Weltbewohner ist der Zone entwachsen: Er zieht in eine passende Zone oder wird zum Begleiter für Spieler.</summary>
        public void MoveOn(Player bot)
        {
            var data = bot.Data;
            var next = _w.OpenZones.Where(z => z != bot.Zone && z.Def.Playable && z.Def.AmbientBots > 0 && (z.Def.Faction == bot.Faction || z.Def.Faction == Faction.None)
                                               && z.Def.MinLevel <= bot.Level && z.Def.MaxLevel + 2 >= bot.Level).FirstOrDefault();
            data.Bot!.History.Add(next != null ? $"Weitergezogen nach {next.Def.Name}" : $"Hat {bot.Zone?.Def.Name} hinter sich gelassen");
            if (next != null)
            {
                data.Bot.HomeZone = next.Def.Id;
                var town = next.Def.Areas.FirstOrDefault(a => a.IsCity) ?? next.Def.Areas.First();
                _w.Transfer(bot, next.Def.Id, town.Center + new Vec3(_w.Random.Range(-8, 8), 0, _w.Random.Range(-8, 8)));
                return;
            }
            data.Bot.Ambient = false;
            data.Bot.HomeZone = "";
            DespawnBot(bot);
        }

        /// <summary>
        /// Hintergrundfortschritt in ruhenden Zonen: statt jeden Schlag zu simulieren, sammeln Bewohner pauschal Erfahrung
        /// und Beutegeld (etwa eine Beute je 45 Sekunden, abhängig vom Geschick) und erholen sich.
        /// </summary>
        public void BackgroundProgress(Player bot, float seconds)
        {
            var bd = bot.Data.Bot;
            if (bd == null || !bd.Ambient || bot.Group != null || bot.Zone == null) return;
            if (!bot.IsAlive) _w.Death.ReviveQuietly(bot);
            bot.AutoAttacking = false;
            bot.Health = bot.MaxHealth;
            if (bot.PowerType == PowerType.Mana) bot.Power = bot.MaxPower;
            double expected = seconds / 45.0 * (0.6 + bd.Skill * 0.6);
            int kills = (int)expected + (_w.Random.Chance(expected - Math.Floor(expected)) ? 1 : 0);
            if (kills <= 0) return;
            if (bot.Level < _w.Content.LevelCap) _w.Progression.GiveXp(bot, kills * Experience.KillXp(bot.Level, bot.Level, false), "background");
            _w.Economy.Credit(bot, kills * (long)_w.Random.Range(bot.Level * 2, bot.Level * 5), "loot_background");
            if (bot.Level > bot.Zone.Def.MaxLevel + 2) MoveOn(bot);
        }

        public void OnBotReceivedItem(Player bot, string itemId)
        {
            int idx = bot.Data.Bag.FindIndex(s => s?.ItemId == itemId);
            if (idx < 0) return;
            if (ConsiderEquip(bot, idx))
            {
                var def = _w.Content.Items[itemId];
                var line = PickLine("loot_upgrade", bot.Bot!.Role, bot.Bot.Data.Archetype);
                if (line != null) ScheduleChat(bot, ChatChannel.Party, line.Replace("{item}", def.Name), 1.0);
            }
        }

        /// <summary>Wenn der Anführer eine Quest annimmt, übernehmen Bots in der Nähe sie mit (wie Mitspieler, die "teilen" annehmen).</summary>
        public void ShareQuest(Player leader, Creature npc, QuestDef q)
        {
            if (leader.Group == null) return;
            foreach (var b in leader.Group.Bots.Where(b => b.DistanceTo(leader) < 40 && b.Zone == leader.Zone))
            {
                if (_w.Quests.CanAccept(b, q) != null) continue;
                var prog = new QuestProgress { QuestId = q.Id, State = QuestState.Active, Counts = new int[q.Objectives.Count] };
                b.Data.Quests[q.Id] = prog;
                foreach (var it in q.ProvidedItems) b.AddItem(it.Item, it.Count);
            }
        }

        /// <summary>Gibt der Anführer eine Quest ab, geben Bots in der Nähe ihre erfüllte Kopie ebenfalls ab.</summary>
        public void ShareTurnIn(Player leader, Creature npc, QuestDef q)
        {
            if (leader.Group == null) return;
            foreach (var b in leader.Group.Bots.Where(b => b.Zone == leader.Zone && b.DistanceTo(npc) < 40).ToList())
            {
                if (!b.Data.Quests.TryGetValue(q.Id, out var prog)) continue;
                _w.Quests.Recompute(b, q, prog);
                if (prog.State != QuestState.Complete)
                {
                    // Gemeinsame Ziele (Sprechen, Liefern, Eskorte) hat der Anführer für die Gruppe erledigt.
                    for (int i = 0; i < q.Objectives.Count; i++)
                        if (q.Objectives[i].Type is ObjectiveType.Talk or ObjectiveType.Deliver or ObjectiveType.Escort or ObjectiveType.Explore)
                            prog.Counts[i] = q.Objectives[i].Count;
                    _w.Quests.Recompute(b, q, prog);
                }
                if (prog.State != QuestState.Complete) continue;
                int choice = q.RewardChoices.Count == 0 ? -1 : BestChoice(b, q);
                var err = _w.Quests.TurnIn(b, npc, q.Id, choice, ignoreRange: true);
                if (err != null) _w.Log($"Bot {b.Name} konnte {q.Id} nicht abgeben: {err}");
            }
        }

        private int BestChoice(Player bot, QuestDef q)
        {
            int best = 0;
            float bestScore = float.MinValue;
            for (int i = 0; i < q.RewardChoices.Count; i++)
            {
                var item = _w.Content.Items[q.RewardChoices[i].Item];
                float s = bot.CanEquip(item) == null ? BotGear.Score(bot, item) : -1;
                if (s > bestScore) { bestScore = s; best = i; }
            }
            return best;
        }

        // ================================================================== Chat

        public string? PickLine(string ev, Role role, string archetype)
        {
            var all = _w.Content.BotDialog.Where(d => d.Event == ev).ToList();
            if (all.Count == 0) return null;
            var specific = all.Where(d => (d.Role == null || d.Role == role) && (d.Personality == null || d.Personality == archetype)).ToList();
            var best = specific.Where(d => d.Personality == archetype).ToList();
            if (best.Count == 0) best = specific.Where(d => d.Role == role).ToList();
            if (best.Count == 0 || _w.Random.Chance(0.3)) best = specific.Where(d => d.Role == null && d.Personality == null).ToList();
            if (best.Count == 0) best = specific;
            if (best.Count == 0) return null;
            var lines = best[_w.Random.Range(0, best.Count - 1)].Lines;
            return lines.Count == 0 ? null : lines[_w.Random.Range(0, lines.Count - 1)];
        }

        public void ScheduleChat(Player bot, ChatChannel ch, string text, double delay)
        {
            if (delay <= 0) { _w.Chat.BotSay(bot, ch, text); return; }
            _chatQueue.Add((_w.Time + delay, bot, ch, text));
        }

        /// <summary>Geste eines Bots mit Verzögerung (wirkt menschlicher als eine sofortige Reaktion).</summary>
        public void ScheduleEmote(Player bot, string emoteId, Unit? target, double delay) => _emoteQueue.Add((_w.Time + delay, bot, emoteId, target?.Id ?? 0));

        public void Whisper(Player bot, Player to, string text) =>
            to.Send(new S_Chat { Channel = ChatChannel.Whisper, SenderId = bot.Id, SenderName = bot.Name, Text = text, SenderIsBot = true });
    }
}
