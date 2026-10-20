using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.AI
{
    /// <summary>
    /// Eigenständiges Leben der Weltbewohner (Kapitel 9.4): jagen, Quests annehmen und abgeben, in der Stadt Händler,
    /// Lehrer, Gasthaus und Auktionshaus besuchen, sich unterhalten und weiterziehen, wenn die Zone zu leicht wird.
    /// </summary>
    public sealed partial class BotBrain
    {
        private enum AutoState { Hunt, ToTown, Town, Errand, TurnIn }
        private AutoState _auto = AutoState.Hunt;
        private Creature? _prey;
        private int _killsSinceTown;
        private float _townTimer, _preySearchTimer, _errandTimer;
        private readonly Queue<(string templateId, string kind)> _errands = new();
        private Creature? _errandNpc;
        private string _errandKind = "";
        private const int MaxAmbientQuests = 4;

        /// <summary>Aktueller Lebensabschnitt (für Tests, Debug und das Weltdashboard).</summary>
        public string AutoActivity => _auto switch
        {
            AutoState.Hunt => _prey != null ? "jagt " + _prey.Name : "sucht Beute",
            AutoState.ToTown => "auf dem Weg in die Stadt",
            AutoState.Town => "in der Stadt",
            AutoState.Errand => "besucht " + (_errandNpc?.Name ?? "jemanden"),
            AutoState.TurnIn => "gibt eine Quest ab",
            _ => "",
        };

        /// <summary>Schickt den Bewohner sofort in die Stadt (Tests, Ereignisse). stayInTown: lange verweilen.</summary>
        public void ForceTownVisit(bool stayInTown = false)
        {
            _auto = AutoState.ToTown;
            _prey = null;
            _stayLong = stayInTown;
        }

        private bool _stayLong;

        private void ThinkAutonomous()
        {
            var zone = Me.Zone!;
            // Weite Wege reitend zurücklegen, am Ziel absitzen.
            if (_moveTarget is Vec3 dest && !Me.InCombat && Me.Cast == null)
            {
                float dist = Vec3.DistanceXZ(Me.Position, dest);
                if (dist > 80 && !Me.IsMounted && _w.Mounts.BestMount(Me) != null) { _w.Mounts.Mount(Me, ""); return; }
                if (dist < 25 && Me.IsMounted) _w.Mounts.Dismount(Me);
            }
            switch (_auto)
            {
                case AutoState.Hunt: Hunt(zone); return;
                case AutoState.ToTown:
                {
                    var town = Town(zone);
                    if (town == null) { _auto = AutoState.Hunt; return; }
                    if (Me.DistanceTo(town.Center) > 15) { MoveTo(town.Center, 10); return; }
                    PlanErrands(zone);
                    _auto = AutoState.Town;
                    _townTimer = _stayLong ? 3600 : 20 + (float)_w.Random.NextDouble() * 40;
                    if (_w.Random.Chance(Data.Chattiness * 0.6)) Say("general", channel: ChatChannel.General);
                    return;
                }
                case AutoState.Town:
                    if (_errands.Count > 0) { StartNextErrand(zone); return; }
                    _townTimer -= ReactionTime;
                    if (_w.Random.Chance(Data.Chattiness * 0.01)) TownChat(zone);
                    if (_townTimer <= 0) LeaveTown(zone);
                    return;
                case AutoState.Errand: DoErrand(); return;
                case AutoState.TurnIn: DoErrand(); return;
            }
        }

        // ------------------------------------------------------------------ Jagd und Quests

        private void Hunt(ZoneInstance zone)
        {
            if (_killsSinceTown >= 12 || Me.FreeBagSlots < 2 || HasCompletedQuest()) { _auto = AutoState.ToTown; _prey = null; return; }
            if (_prey != null && (_prey.Removed || _prey.IsDead))
            {
                if (_prey.Lootable && _prey.TappedBy == Me && Me.DistanceTo(_prey) < 6) { _w.Loot.TryLoot(Me, _prey); _prey = null; return; }
                if (_prey.Lootable && _prey.TappedBy == Me) { MoveTo(_prey.Position, 3); return; }
                _prey = null;
            }
            if (_prey == null && TryGatherNearby(zone)) return;
            if (_prey == null || _prey.TappedBy != null && _prey.TappedBy != Me)
            {
                _preySearchTimer -= ReactionTime;
                if (_preySearchTimer > 0 && _moveTarget != null) return;
                _preySearchTimer = 2;
                _prey = FindPrey(zone);
                if (_prey == null) { MoveTo(HuntingGround(zone), 5); return; }
            }
            Me.Target = _prey;
            var ab = OpeningAbility();
            bool rangedHunter = Profile?.Ranged == true || (Role == Role.Healer && Me.Class != CharClass.Paladin);
            float range = rangedHunter ? Math.Min(28, ab?.MaxRange ?? 25) : Me.MeleeReachTo(_prey) - 0.6f;
            if (Me.DistanceTo(_prey) > range) { MoveTo(_prey.Position, range); return; }
            _moveTarget = null;
            Me.AutoAttacking = !(Profile?.Ranged == true) || Me.Class == CharClass.Hunter;
            if (ab != null) _w.Combat.TryCast(Me, ab, _prey);
        }

        /// <summary>Weltbewohner mit Sammelberufen nehmen Vorkommen am Weg mit (bis 25 m, außerhalb des Kampfes).</summary>
        private bool TryGatherNearby(ZoneInstance zone)
        {
            if (Me.Data.Professions.Count == 0 || Me.Cast != null || Me.InCombat || Me.FreeBagSlots < 2) return false;
            var node = zone.Objects.Where(o => o.Available && o.Def.Profession != null && Me.Data.Professions.ContainsKey(o.Def.Profession) && Vec3.DistanceXZ(o.Position, Me.Position) < 25
                                               && _w.Professions.CanGather(Me, o.Def) == null)
                .OrderBy(o => Vec3.DistanceXZ(o.Position, Me.Position)).FirstOrDefault();
            if (node == null) return false;
            if (Me.DistanceTo(node) > 3) { MoveTo(node.Position, 2); return true; }
            _moveTarget = null;
            _w.Interactions.Interact(Me, node.Id);
            return true;
        }

        private bool HasCompletedQuest() => Me.Data.Quests.Values.Any(q => q.State == QuestState.Complete);

        /// <summary>Kreaturen, die für aktive Quests gebraucht werden (zu tötende Gegner und Träger von Questgegenständen).</summary>
        private HashSet<string> QuestTargets()
        {
            var set = new HashSet<string>();
            foreach (var prog in Me.Data.Quests.Values)
            {
                if (prog.State != QuestState.Active || !_w.Content.Quests.TryGetValue(prog.QuestId, out var q)) continue;
                for (int i = 0; i < q.Objectives.Count; i++)
                {
                    var o = q.Objectives[i];
                    if (i < prog.Counts.Length && prog.Counts[i] >= o.Count) continue;
                    if (o.Type == ObjectiveType.Kill) set.Add(o.Target);
                    else if (o.Type == ObjectiveType.Collect) foreach (var c in _w.Bots.DroppersOf(o.Target)) set.Add(c);
                }
            }
            return set;
        }

        private Creature? FindPrey(ZoneInstance zone)
        {
            int gray = Experience.GrayLevel(Me.Level);
            var wanted = QuestTargets();
            return zone.UnitsInRange(Me.Position, 60).OfType<Creature>()
                .Where(c => c.IsAlive && !c.InCombat && c.TappedBy == null && !c.Template.Elite && !c.Template.Rare && !c.IsSummoned && c.Owner == null && !c.IsPet && !c.Template.IsNpc
                            && (c.Faction == Faction.Hostile || c.Faction == Faction.Neutral) && c.Level <= Me.Level + 1
                            && (c.Level > gray || wanted.Contains(c.Template.Id)))
                .OrderBy(c => c.DistanceTo(Me) - (wanted.Contains(c.Template.Id) ? 45 : 0)).FirstOrDefault();
        }

        private Vec3 HuntingGround(ZoneInstance zone)
        {
            int gray = Experience.GrayLevel(Me.Level);
            var wanted = QuestTargets();
            var spots = zone.SpawnPoints.Where(s =>
            {
                var t = _w.Content.Creatures[s.Def.Creature];
                return !t.Elite && !t.Rare && t.MinLevel <= Me.Level + 1 && (t.Faction == Faction.Hostile || t.Faction == Faction.Neutral)
                       && (t.MaxLevel > gray || wanted.Contains(t.Id));
            }).ToList();
            if (spots.Count == 0) return Town(zone)?.Center ?? Me.Position;
            // Questziele zuerst, dann Gegner der eigenen Stufe, dann die Nähe.
            var best = spots.OrderBy(s =>
            {
                var t = _w.Content.Creatures[s.Def.Creature];
                return (wanted.Contains(t.Id) ? -10000 : 0) + Math.Abs(t.MaxLevel - Me.Level) * 100 + Vec3.DistanceXZ(s.Position, Me.Position);
            }).First();
            return best.Position;
        }

        /// <summary>Quests, die ein Weltbewohner sinnvoll allein erledigen kann (Gegner töten, Gegenstände sammeln).</summary>
        private bool SuitsAmbient(QuestDef q) =>
            (q.Category is QuestCategory.Main or QuestCategory.Side or QuestCategory.Class) && q.Objectives.Count > 0 &&
            q.Objectives.All(o => o.Type is ObjectiveType.Kill or ObjectiveType.Collect) && q.Level <= Me.Level + 1 && q.Level >= Me.Level - 4 &&
            q.TimeLimitSeconds <= 0 && q.Objectives.All(o => o.Type != ObjectiveType.Kill || !_w.Content.Creatures.TryGetValue(o.Target, out var c) || !c.Elite) &&
            _w.Quests.CanAccept(Me, q) == null;

        private static AreaDef? Town(ZoneInstance zone) => zone.Def.Areas.FirstOrDefault(a => a.IsCity) ?? zone.Def.Areas.FirstOrDefault();

        // ------------------------------------------------------------------ Stadtleben

        /// <summary>Besorgungen in der Stadt: Quests abgeben, verkaufen, beim Lehrer lernen, Quests annehmen, ins Gasthaus oder Auktionshaus.</summary>
        private void PlanErrands(ZoneInstance zone)
        {
            _errands.Clear();
            foreach (var prog in Me.Data.Quests.Values.Where(q => q.State == QuestState.Complete).ToList())
                if (_w.Content.Quests.TryGetValue(prog.QuestId, out var q)) _errands.Enqueue((q.TurnIn, "turnin:" + q.Id));
            var npcs = TownNpcs(zone);
            var vendor = npcs.FirstOrDefault(n => n.Template.Roles.HasFlag(NpcRole.Vendor));
            if (vendor != null) _errands.Enqueue((vendor.Template.Id, "vendor"));
            var trainer = npcs.FirstOrDefault(n => n.Template.Roles.HasFlag(NpcRole.ClassTrainer) && (n.Template.TrainerClass == CharClass.None || n.Template.TrainerClass == Me.Class));
            if (trainer != null && _w.Random.Chance(0.6)) _errands.Enqueue((trainer.Template.Id, "trainer"));
            int room = MaxAmbientQuests - Me.Data.Quests.Count;
            foreach (var giver in npcs.Where(n => n.Template.Roles.HasFlag(NpcRole.QuestGiver)))
            {
                if (room <= 0) break;
                var offers = _w.Content.Quests.Values.Where(q => q.Giver == giver.Template.Id && SuitsAmbient(q)).Take(room).ToList();
                foreach (var q in offers) { _errands.Enqueue((giver.Template.Id, "accept:" + q.Id)); room--; }
            }
            var extra = new List<(string, string)>();
            var inn = npcs.FirstOrDefault(n => n.Template.Roles.HasFlag(NpcRole.Innkeeper));
            if (inn != null && _w.Random.Chance(0.5)) extra.Add((inn.Template.Id, "inn"));
            var auctioneer = npcs.FirstOrDefault(n => n.Template.Roles.HasFlag(NpcRole.Auctioneer));
            if (auctioneer != null && _w.Random.Chance(0.4)) extra.Add((auctioneer.Template.Id, "auction"));
            var banker = npcs.FirstOrDefault(n => n.Template.Roles.HasFlag(NpcRole.Banker));
            if (banker != null && _w.Random.Chance(0.2)) extra.Add((banker.Template.Id, "bank"));
            foreach (var e in extra.OrderBy(_ => _w.Random.NextDouble())) _errands.Enqueue(e);
        }

        private List<Creature> TownNpcs(ZoneInstance zone)
        {
            var town = Town(zone);
            return zone.Entities.Values.OfType<Creature>()
                .Where(c => c.IsAlive && c.Template.IsNpc && c.Template.Roles != NpcRole.None && !Me.IsHostileTo(c) &&
                            (town == null || Vec3.DistanceXZ(c.Position, town.Center) < town.Radius + 40))
                .OrderBy(c => c.DistanceTo(Me)).ToList();
        }

        private void StartNextErrand(ZoneInstance zone)
        {
            var (templateId, kind) = _errands.Dequeue();
            // Abgabe-NPCs können auch außerhalb der Stadt stehen.
            _errandNpc = zone.Entities.Values.OfType<Creature>().Where(c => c.Template.Id == templateId && c.IsAlive).OrderBy(c => c.DistanceTo(Me)).FirstOrDefault();
            if (_errandNpc == null) return;
            _errandKind = kind;
            _errandTimer = 0;
            _auto = kind.StartsWith("turnin:") ? AutoState.TurnIn : AutoState.Errand;
        }

        private void DoErrand()
        {
            var npc = _errandNpc;
            if (npc == null || npc.Removed || npc.IsDead) { EndErrand(); return; }
            if (_errandTimer <= 0)
            {
                if (Me.DistanceTo(npc) > 4) { MoveTo(npc.Position, 3); return; }
                _moveTarget = null;
                Me.Facing = Vec3.FacingTowards(Me.Position, npc.Position);
                _errandTimer = PerformErrand(npc, _errandKind);
                if (_errandTimer <= 0) { EndErrand(); return; }
                return;
            }
            _errandTimer -= ReactionTime;
            if (_errandTimer <= 0) EndErrand();
        }

        /// <summary>Führt eine Besorgung aus. Rückgabe: wie lange der Bot dort verweilt (Sekunden).</summary>
        private float PerformErrand(Creature npc, string kind)
        {
            if (kind.StartsWith("turnin:"))
            {
                var q = _w.Content.Quests[kind.Substring(7)];
                int choice = q.RewardChoices.Count > 0 ? BestRewardChoice(q) : 0;
                if (_w.Quests.TurnIn(Me, npc, q.Id, choice) == null) Decide($"Quest abgegeben: {q.Title}");
                return 2 + (float)_w.Random.NextDouble() * 2;
            }
            if (kind.StartsWith("accept:"))
            {
                var id = kind.Substring(7);
                if (_w.Quests.Accept(Me, npc, id) == null) Decide($"Quest angenommen: {_w.Content.Quests[id].Title}");
                return 1.5f + (float)_w.Random.NextDouble() * 2;
            }
            switch (kind)
            {
                case "vendor": _w.Professions.BotCraft(Me); SellJunk(); return 3 + (float)_w.Random.NextDouble() * 4;
                case "trainer": _w.Progression.AutoTrain(Me); return 4 + (float)_w.Random.NextDouble() * 4;
                case "inn":
                    Resting = true;
                    StartRestAbilities(1);
                    if (_w.Random.Chance(Data.Chattiness * 0.5)) Say("town_inn");
                    return 15 + (float)_w.Random.NextDouble() * 30;
                case "auction":
                    _w.Bots.OnBotVisitsAuctioneer(Me);
                    if (_w.Random.Chance(Data.Chattiness * 0.3)) Say("town_auction");
                    return 6 + (float)_w.Random.NextDouble() * 8;
                case "bank": return 3 + (float)_w.Random.NextDouble() * 3;
            }
            return 0;
        }

        private int BestRewardChoice(QuestDef q)
        {
            int best = 0;
            float bestScore = float.MinValue;
            for (int i = 0; i < q.RewardChoices.Count; i++)
            {
                if (!_w.Content.Items.TryGetValue(q.RewardChoices[i].Item, out var it)) continue;
                float score = it.IsEquippable && Me.CanEquip(it) == null ? BotGear.Score(Me, it) : it.SellPrice * 0.001f;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        private void EndErrand()
        {
            if (Resting) StopResting();
            _errandNpc = null;
            _errandKind = "";
            _auto = AutoState.Town;
        }

        private void TownChat(ZoneInstance zone)
        {
            // Kurzes Gespräch mit einem anderen Bewohner in der Nähe – der antwortet manchmal.
            var other = zone.PlayersInRange(Me.Position, 20).FirstOrDefault(p => p != Me && p.IsBot && p.Group == null && p.Faction == Me.Faction);
            if (other == null) return;
            Say("town_chat", other);
            if (_w.Random.Chance(other.Bot!.Data.Chattiness)) other.Bot.Say("town_chat_reply", Me, delay: 2.5f + (float)_w.Random.NextDouble() * 2);
        }

        private void LeaveTown(ZoneInstance zone)
        {
            _killsSinceTown = 0;
            // Zu leicht geworden? Dann zieht der Bewohner weiter (Platz für neue Abenteurer der passenden Stufe).
            if (Data.Ambient && Me.Level > zone.Def.MaxLevel + 2) { _w.Bots.MoveOn(Me); return; }
            _auto = AutoState.Hunt;
        }

        private void SellJunk()
        {
            for (int i = 0; i < Me.Data.Bag.Count; i++)
            {
                var s = Me.Data.Bag[i];
                if (s == null) continue;
                var def = _w.Content.Items[s.ItemId];
                if (def.Category == ItemCategory.Quest || def.Category == ItemCategory.Consumable || def.Id == "item_hearthstone") continue;
                if (def.IsEquippable && _w.Bots.ConsiderEquip(Me, i)) continue;
                // Gute Stücke bringen auf dem Auktionshaus mehr – sie bleiben für den nächsten Besuch dort in der Tasche.
                if (def.Quality >= ItemQuality.Uncommon && def.Category != ItemCategory.TradeGoods && _w.Random.Chance(0.5)) continue;
                _w.Economy.Credit(Me, (long)def.SellPrice * s.Count, "vendor_sell", $"{s.ItemId}x{s.Count}");
                Me.Data.Bag[i] = null;
            }
        }
    }
}
