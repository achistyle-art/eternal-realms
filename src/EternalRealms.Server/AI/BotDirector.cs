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
        private long _nextCandidateId = 1;
        private float _populationTimer = 2;
        private bool _loaded;

        private static readonly string[] Archetypes = { "freundlich", "ehrgeizig", "vorsichtig", "witzig", "wortkarg" };

        private sealed class Candidate
        {
            public CharacterData Data = null!;
            public double Expires;
            public long OwnerAccount;
        }

        public BotDirector(GameWorld world) { _w = world; }

        public void LoadOffline(IEnumerable<CharacterData> bots)
        {
            foreach (var b in bots) _offline[b.Id] = b;
            _loaded = true;
        }

        public IEnumerable<Player> ActiveBots => _w.AllPlayers.Where(p => p.IsBot);

        // ================================================================== Suche & Einladung

        /// <summary>
        /// "KI-Mitspieler suchen": schlägt Bots vor, die der Gruppe fehlende Rollen füllen –
        /// zuerst bekannte Bots, mit denen das Konto schon gespielt hat, dann neue.
        /// </summary>
        public List<BotCandidateData> FindCandidates(Player p, Role? roleFilter)
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
            // Bekannte Bots
            foreach (var known in _offline.Values.Where(b => b.Bot != null && b.Bot.KnownAccounts.Contains(account) && !b.Bot.Ambient
                                                            && Math.Abs(b.Level - p.Level) <= 3 && _w.Content.Races[b.Race].Faction == p.Faction
                                                            && (!roleFilter.HasValue || b.Bot.Role == roleFilter)).Take(3))
                result.Add(Register(known, account, known: true));
            foreach (var role in needed)
            {
                if (result.Count >= 8) break;
                var data = CreateBotData(p.Faction, role, Math.Clamp(p.Level - (_w.Random.Chance(0.3) ? 1 : 0), 1, Experience.MaxLevel), null, p.Zone!.Def.Id);
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
            return new BotCandidateData
            {
                CandidateId = id, Name = data.Name, Race = data.Race, Class = data.Class, Level = data.Level, Role = bot.Role,
                Personality = (known ? "bekannt, " : "") + bot.Archetype, Greeting = line.Replace("{leader}", "").Replace("{me}", data.Name).Replace(" !", "!"),
            };
        }

        public string? Invite(Player p, long candidateId)
        {
            if (!_candidates.TryGetValue(candidateId, out var cand)) return "Dieser KI-Mitspieler ist nicht mehr verfügbar. Sucht erneut.";
            if (p.Group != null && p.Group.IsFull) return "Eure Gruppe ist voll.";
            if (p.Zone == null) return "Nicht in der Welt.";
            _candidates.Remove(candidateId);
            var data = cand.Data;
            data.Bot!.KnownAccounts.Add(p.Data.AccountId ?? -1);
            _offline.Remove(data.Id);
            // Bots kommen angelaufen, statt neben dem Spieler aufzutauchen.
            double ang = _w.Random.NextDouble() * Math.PI * 2;
            data.Position = p.Position + new Vec3((float)Math.Cos(ang) * 18, 0, (float)Math.Sin(ang) * 18);
            data.ZoneId = p.Zone.Def.Id;
            var bot = Spawn(data, p.Zone);
            if (p.Group == null) _w.Groups.Create(p);
            _w.Groups.Join(p.Group!, bot);
            bot.Bot!.Say("join", force: true, delay: 0.8f);
            if (data.Id == 0) PersistNew(bot);
            return null;
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
            Task.Run(async () =>
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
        public CharacterData? CreateBotData(Faction faction, Role role, int level, CharClass? cls, string zoneId, bool ambient = false)
        {
            var classes = _w.Content.BotProfiles.Where(pr => pr.Role == role).Select(pr => pr.Class).Distinct()
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
            string archetype = Archetypes[_w.Random.Range(0, Archetypes.Length - 1)];
            data.Bot = new BotData
            {
                Role = role, Archetype = archetype, Ambient = ambient, HomeZone = zoneId,
                Aggression = (float)_w.Random.NextDouble(),
                Caution = archetype == "vorsichtig" ? 0.8f + (float)_w.Random.NextDouble() * 0.2f : (float)_w.Random.NextDouble() * 0.7f,
                Chattiness = archetype == "wortkarg" ? 0.1f : archetype == "witzig" ? 0.85f : 0.3f + (float)_w.Random.NextDouble() * 0.5f,
                Skill = 0.55f + (float)_w.Random.NextDouble() * 0.45f,
            };
            data.Level = level;
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
            if (stack == null || !_w.Content.Items.TryGetValue(stack.ItemId, out var item) || !item.IsEquippable) return false;
            if (bot.CanEquip(item) != null) return false;
            var slot = bot.SlotFor(item);
            float current = bot.Data.Equipment.TryGetValue(slot, out var cur) ? BotGear.Score(bot, _w.Content.Items[cur.ItemId]) : BotGear.VirtualSlotScore(bot);
            if (BotGear.Score(bot, item) <= current * 1.05f) return false;
            return bot.Equip(bagIndex) == null;
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

        public void Whisper(Player bot, Player to, string text) =>
            to.Send(new S_Chat { Channel = ChatChannel.Whisper, SenderId = bot.Id, SenderName = bot.Name, Text = text, SenderIsBot = true });
    }
}
