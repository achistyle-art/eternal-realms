using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>NPCs ansprechen, Objekte benutzen, Händler, Lehrer, Gasthaus, Flugmeister, Gegenstände.</summary>
    public sealed class InteractionSystem
    {
        private readonly GameWorld _w;
        public InteractionSystem(GameWorld world) { _w = world; }

        public const float NpcRange = 8f;
        public const float ObjectRange = 5f;

        public string? Interact(Player p, int entityId)
        {
            if (p.Zone == null) return "Nicht in der Welt.";
            var e = p.Zone.Get(entityId);
            switch (e)
            {
                case Creature c when c.IsDead:
                    if (p.IsDead) return "Ihr seid tot.";
                    return _w.Loot.TryLoot(p, c);
                case Creature npc when npc.Template.IsNpc && !npc.IsHostileTo(p):
                    return TalkToNpc(p, npc);
                case GameObjectEntity go:
                    return UseObject(p, go);
                default:
                    return "Damit könnt Ihr nicht interagieren.";
            }
        }

        private string? TalkToNpc(Player p, Creature npc)
        {
            if (p.DistanceTo(npc) > NpcRange) return "Zu weit entfernt.";
            var roles = npc.Template.Roles;
            if (p.DeathState == DeathState.Ghost && !roles.HasFlag(NpcRole.SpiritHealer)) return "Geister können nicht sprechen.";
            if (p.DeathState == DeathState.Dead) return "Ihr seid tot.";
            npc.Facing = Vec3.FacingTowards(npc.Position, p.Position);
            npc.AI.PauseFor(20);
            _w.Quests.OnTalkedTo(p, npc);
            var msg = new S_NpcInteraction { NpcId = npc.Id, TemplateId = npc.Template.Id };
            foreach (var q in _w.Content.QuestsGivenBy(npc.Template.Id))
                if (_w.Quests.CanAccept(p, q) == null) msg.QuestsAvailable.Add(q.Id);
            foreach (var qp in p.Data.Quests.Values)
            {
                var q = _w.Content.Quests[qp.QuestId];
                if (q.TurnIn != npc.Template.Id) continue;
                if (qp.State == QuestState.Complete) msg.QuestsToTurnIn.Add(q.Id); else msg.QuestsInProgress.Add(q.Id);
            }
            if (roles.HasFlag(NpcRole.Vendor)) msg.VendorItems.AddRange(npc.Template.VendorItems);
            if (roles.HasFlag(NpcRole.ClassTrainer)) msg.Trainable.AddRange(_w.Progression.Trainable(p, npc.Template.TrainerClass).Select(a => a.RankedId));
            if (roles.HasFlag(NpcRole.FlightMaster))
            {
                var node = NodeAt(npc);
                if (node != null)
                {
                    if (p.Data.KnownTravelNodes.Add(node.Id)) p.Notice($"Neuer Flugpunkt entdeckt: {node.Name}");
                    msg.FlightDestinations.AddRange(_w.Content.FlightPaths.Where(f => f.From == node.Id && p.Data.KnownTravelNodes.Contains(f.To)).Select(f => f.To));
                }
            }
            if (roles.HasFlag(NpcRole.SpiritHealer)) msg.CanResurrect = p.DeathState == DeathState.Ghost;
            if (roles.HasFlag(NpcRole.Innkeeper))
            {
                msg.IsInnkeeper = true;
                if (p.Data.BindZone != p.Zone!.Def.Id || Vec3.DistanceXZ(p.Data.BindPosition, npc.Position) > 30)
                {
                    p.Data.BindZone = p.Zone.Def.Id;
                    p.Data.BindPosition = npc.Position;
                    p.Notice($"{npc.Template.Name}: Euer Ruhestein ist jetzt an dieses Gasthaus gebunden.");
                }
            }
            if (roles.HasFlag(NpcRole.Repair))
            {
                int cost = p.RepairCost();
                if (cost > 0 && p.Money >= cost) { p.Money -= cost; p.RepairAll(); p.Notice($"Ausrüstung repariert für {LootSystem.Money(cost)}."); p.Send(p.BuildInventory()); }
            }
            p.Send(msg);
            return null;
        }

        private TravelNodeDef? NodeAt(Creature npc) =>
            _w.Content.TravelNodes.Values.FirstOrDefault(n => n.Zone == npc.Zone?.Def.Id && Vec3.DistanceXZ(n.Position, npc.Position) < 20);

        private string? UseObject(Player p, GameObjectEntity go)
        {
            if (p.IsDead) return "Ihr seid tot.";
            if (!go.Available) return "Das wurde bereits benutzt.";
            if (p.DistanceTo(go) > ObjectRange) return "Zu weit entfernt.";
            if (p.InCombat && go.Def.TeleportZone == null) return "Nicht im Kampf.";
            if (go.Def.Quest != null && !NeedsObject(p, go.Def)) return "Ihr habt dafür keine Verwendung.";
            if (go.Def.TeleportZone != null)
            {
                _w.TeleportSpecial(p, go.Def.TeleportZone, go.Def.TeleportPosition);
                return null;
            }
            if (p.Cast != null) return "Ihr seid beschäftigt.";
            p.Cast = new CastState { Object = go, Remaining = go.Def.InteractTime, Total = go.Def.InteractTime, StartPosition = p.Position, Ability = InteractAbility };
            p.Zone!.BroadcastFrom(p, new S_CastStart { CasterId = p.Id, TargetId = go.Id, AbilityId = "interact", CastTime = go.Def.InteractTime });
            return null;
        }

        private static readonly AbilityDef InteractAbility = new() { Id = "interact", Name = "Benutzen", Target = TargetType.Self, TriggersGcd = false };

        private bool NeedsObject(Player p, GameObjectDef obj)
        {
            if (obj.Quest == null) return true;
            if (!p.Data.Quests.TryGetValue(obj.Quest, out var prog) || prog.State != QuestState.Active) return false;
            var q = _w.Content.Quests[obj.Quest];
            for (int i = 0; i < q.Objectives.Count; i++)
            {
                var o = q.Objectives[i];
                if (o.Type == ObjectiveType.Interact && o.Target == obj.Id && prog.Counts[i] < o.Count) return true;
                if (o.Type == ObjectiveType.Collect && obj.Loot.Any(l => l.Item == o.Target) && p.CountItem(o.Target) < o.Count) return true;
            }
            return false;
        }

        public void CompleteObjectInteraction(Player p, GameObjectEntity go)
        {
            if (!go.Available || go.Removed) return;
            go.Available = false;
            go.RespawnTimer = go.RespawnSeconds;
            var result = new S_LootResult { CorpseId = go.Id };
            foreach (var l in go.Def.Loot)
            {
                if (l.Quest != null && !_w.Quests.NeedsQuestItem(p, l.Quest, l.Item)) continue;
                if (!_w.Random.Chance(l.Chance)) continue;
                int n = _w.Random.Range(l.Min, l.Max);
                int added = p.AddItem(l.Item, n);
                if (added > 0) result.Items.Add(new ItemStackData { ItemId = l.Item, Count = added });
            }
            _w.Quests.OnObjectUsed(p, go.Def);
            if (result.Items.Count > 0)
            {
                p.Send(result);
                p.Send(p.BuildInventory());
                _w.Quests.OnItemsChanged(p);
            }
        }

        // ------------------------------------------------------------ Gegenstände

        public string? UseItem(Player p, int bagIndex, int targetId)
        {
            p.EnsureBagSize();
            if (bagIndex < 0 || bagIndex >= p.Data.Bag.Count || p.Data.Bag[bagIndex] == null) return "Kein Gegenstand.";
            var stack = p.Data.Bag[bagIndex]!;
            var def = _w.Content.Items[stack.ItemId];
            if (def.IsEquippable) { var err = p.Equip(bagIndex); if (err == null) p.Send(p.BuildInventory()); return err; }
            if (def.UseAbility == null) return "Dieser Gegenstand kann nicht benutzt werden.";
            if (p.Level < def.RequiredLevel) return $"Benötigt Stufe {def.RequiredLevel}.";
            var ab = _w.Content.GetAbilityRank(def.UseAbility, 1)!;
            var target = targetId != 0 ? p.Zone?.GetUnit(targetId) : null;
            bool consumable = def.Category == ItemCategory.Consumable;
            return _w.Combat.TryCast(p, ab, target, consumable ? def.Id : null);
        }

        public string? Buy(Player p, int npcId, string itemId, int count)
        {
            if (p.Zone?.Get(npcId) is not Creature npc || !npc.Template.Roles.HasFlag(NpcRole.Vendor) || p.DistanceTo(npc) > NpcRange) return "Kein Händler in der Nähe.";
            if (!npc.Template.VendorItems.Contains(itemId)) return "Das verkauft dieser Händler nicht.";
            count = Math.Clamp(count, 1, 200);
            var def = _w.Content.Items[itemId];
            long cost = (long)def.BuyPrice * count;
            if (p.Money < cost) return "Ihr habt nicht genug Geld.";
            if (p.SpaceFor(itemId) < count) return "Eure Taschen sind voll.";
            p.Money -= cost;
            p.AddItem(itemId, count);
            p.Send(p.BuildInventory());
            return null;
        }

        public string? Sell(Player p, int npcId, int bagIndex)
        {
            if (p.Zone?.Get(npcId) is not Creature npc || !npc.Template.Roles.HasFlag(NpcRole.Vendor) || p.DistanceTo(npc) > NpcRange) return "Kein Händler in der Nähe.";
            p.EnsureBagSize();
            if (bagIndex < 0 || bagIndex >= p.Data.Bag.Count || p.Data.Bag[bagIndex] == null) return "Kein Gegenstand.";
            var stack = p.Data.Bag[bagIndex]!;
            var def = _w.Content.Items[stack.ItemId];
            if (def.Category == ItemCategory.Quest || def.SellPrice <= 0) return "Das will der Händler nicht.";
            p.Money += (long)def.SellPrice * stack.Count;
            p.Data.Bag[bagIndex] = null;
            p.Send(p.BuildInventory());
            _w.Quests.OnItemsChanged(p);
            return null;
        }

        public string? Train(Player p, int npcId, string abilityId)
        {
            if (p.Zone?.Get(npcId) is not Creature npc || p.DistanceTo(npc) > NpcRange) return "Kein Lehrer in der Nähe.";
            return _w.Progression.Train(p, npc, abilityId);
        }

        public string? TakeFlight(Player p, int npcId, string destination)
        {
            if (p.Zone?.Get(npcId) is not Creature npc || !npc.Template.Roles.HasFlag(NpcRole.FlightMaster) || p.DistanceTo(npc) > NpcRange) return "Kein Flugmeister in der Nähe.";
            if (p.InCombat) return "Nicht im Kampf.";
            var from = NodeAt(npc);
            var path = from == null ? null : _w.Content.FlightPaths.FirstOrDefault(f => f.From == from.Id && f.To == destination);
            if (path == null || !p.Data.KnownTravelNodes.Contains(destination)) return "Diese Route kennt Ihr nicht.";
            if (p.Money < path.Cost) return "Ihr habt nicht genug Geld.";
            var to = _w.Content.TravelNodes[destination];
            if (to.Zone != p.Zone!.Def.Id) return "Flüge in andere Regionen folgen mit den Schiffs- und Flugrouten der nächsten Meilensteine.";
            p.Money -= path.Cost;
            p.FlightFrom = p.Position;
            p.FlightTo = to.Position;
            p.FlightTime = 0;
            p.FlightDuration = path.DurationSeconds;
            p.Notice($"Flug nach {to.Name}.");
            return null;
        }
    }
}
