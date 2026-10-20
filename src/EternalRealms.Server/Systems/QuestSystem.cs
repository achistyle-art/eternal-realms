using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.Systems
{
    /// <summary>Annahme, Fortschritt, Abgabe und Belohnungen aller Quests – inklusive Eskorten und Entscheidungsquests.</summary>
    public sealed class QuestSystem
    {
        private readonly GameWorld _w;
        public QuestSystem(GameWorld world) { _w = world; }

        public const float GroupCreditRange = 100f;
        private ContentDatabase C => _w.Content;

        // ------------------------------------------------------------ Verfügbarkeit

        public string? CanAccept(Player p, QuestDef q)
        {
            if (p.Data.CompletedQuests.Contains(q.Id)) return "Bereits abgeschlossen.";
            if (p.Data.Quests.ContainsKey(q.Id)) return "Bereits angenommen.";
            if (q.Faction != Faction.None && q.Faction != p.Faction) return "Diese Quest ist nicht für Eure Fraktion.";
            if (q.Class != CharClass.None && q.Class != p.Class) return "Diese Quest ist nicht für Eure Klasse.";
            if (q.Race.HasValue && q.Race != p.Race) return "Diese Quest ist nicht für Euer Volk.";
            if (p.Level < q.MinLevel) return $"Benötigt Stufe {q.MinLevel}.";
            foreach (var r in q.Requires) if (!p.Data.CompletedQuests.Contains(r)) return "Ihr müsst zuerst eine andere Aufgabe erledigen.";
            foreach (var x in q.ExclusiveWith)
                if (p.Data.CompletedQuests.Contains(x) || p.Data.Quests.ContainsKey(x)) return "Ihr habt Euch bereits anders entschieden.";
            if (p.Data.Quests.Count >= Player.MaxQuests) return "Euer Questlog ist voll.";
            return null;
        }

        public IEnumerable<QuestDef> AvailableFrom(Player p, Creature npc) => C.QuestsGivenBy(npc.Template.Id).Where(q => CanAccept(p, q) == null);

        public QuestMarker MarkerFor(Player p, Creature npc)
        {
            var id = npc.Template.Id;
            foreach (var qp in p.Data.Quests.Values)
                if (qp.State == QuestState.Complete && C.Quests[qp.QuestId].TurnIn == id) return QuestMarker.ReadyToTurnIn;
            var available = AvailableFrom(p, npc).ToList();
            if (available.Count > 0)
                return available.Any(q => q.Level > Experience.GrayLevel(p.Level)) ? QuestMarker.Available : QuestMarker.AvailableLowLevel;
            foreach (var qp in p.Data.Quests.Values)
                if (C.Quests[qp.QuestId].TurnIn == id) return QuestMarker.Incomplete;
            return QuestMarker.None;
        }

        public void RefreshMarkers(Player p)
        {
            if (p.Connection == null || p.Zone == null) return;
            foreach (var id in p.VisibleEntities)
                if (p.Zone.Get(id) is Creature c && c.Template.Roles.HasFlag(NpcRole.QuestGiver))
                    p.Send(new S_QuestMarker { NpcId = c.Id, Marker = MarkerFor(p, c) });
        }

        // ------------------------------------------------------------ Annahme & Abgabe

        public string? Accept(Player p, Creature npc, string questId)
        {
            if (!C.Quests.TryGetValue(questId, out var q)) return "Unbekannte Quest.";
            if (q.Giver != npc.Template.Id) return "Diese Person bietet diese Quest nicht an.";
            if (p.DistanceTo(npc) > 10) return "Zu weit entfernt.";
            var err = CanAccept(p, q);
            if (err != null) return err;
            foreach (var it in q.ProvidedItems)
                if (p.SpaceFor(it.Item) < it.Count) return "Eure Taschen sind voll.";
            var progress = new QuestProgress { QuestId = q.Id, State = QuestState.Active, Counts = new int[q.Objectives.Count], TimeLeft = q.TimeLimitSeconds };
            p.Data.Quests[q.Id] = progress;
            foreach (var it in q.ProvidedItems) p.AddItem(it.Item, it.Count);
            if (q.ProvidedItems.Count > 0) p.Send(p.BuildInventory());
            // Bereits erfüllte Ziele (Gegenstände schon im Inventar, Gebiet schon betreten) sofort zählen.
            for (int i = 0; i < q.Objectives.Count; i++)
            {
                var o = q.Objectives[i];
                if (o.Type == ObjectiveType.Explore && p.CurrentArea?.Id == o.Target) progress.Counts[i] = 1;
            }
            Recompute(p, q, progress);
            if (q.Escort != null) StartEscort(p, npc, q);
            p.Send(p.BuildQuestLog());
            RefreshMarkers(p);
            p.Bot?.OnQuestAccepted(q);
            if (!p.IsBot) _w.Bots.ShareQuest(p, npc, q);
            return null;
        }

        public string? TurnIn(Player p, Creature npc, string questId, int choice, bool ignoreRange = false)
        {
            if (!C.Quests.TryGetValue(questId, out var q)) return "Unbekannte Quest.";
            if (q.TurnIn != npc.Template.Id) return "Diese Person erwartet diese Quest nicht.";
            if (!ignoreRange && p.DistanceTo(npc) > 10) return "Zu weit entfernt.";
            if (!p.Data.Quests.TryGetValue(questId, out var progress)) return "Ihr habt diese Quest nicht.";
            Recompute(p, q, progress);
            if (progress.State != QuestState.Complete) return "Die Quest ist noch nicht erfüllt.";
            if (q.RewardChoices.Count > 0 && (choice < 0 || choice >= q.RewardChoices.Count)) return "Bitte wählt eine Belohnung.";
            int needed = q.RewardItems.Count + (q.RewardChoices.Count > 0 ? 1 : 0);
            int freeAfter = p.FreeBagSlots + q.Objectives.Count(o => o.Type == ObjectiveType.Collect);
            if (freeAfter < needed) return "Eure Taschen sind voll.";

            for (int i = 0; i < q.Objectives.Count; i++)
                if (q.Objectives[i].Type == ObjectiveType.Collect) p.RemoveItem(q.Objectives[i].Target, q.Objectives[i].Count);
            foreach (var it in q.ProvidedItems) p.RemoveItem(it.Item, p.CountItem(it.Item));
            p.Data.Quests.Remove(q.Id);
            p.Data.CompletedQuests.Add(q.Id);
            _w.ReportProgress(p, ProgressEvent.CompleteQuest, q.Id);

            foreach (var it in q.RewardItems) p.AddItem(it.Item, it.Count);
            if (q.RewardChoices.Count > 0) p.AddItem(q.RewardChoices[choice].Item, q.RewardChoices[choice].Count);
            int money = p.Level >= Experience.MaxLevel ? q.RewardMoney + q.RewardXp / 2 : q.RewardMoney;
            _w.Economy.Credit(p, money, "quest", q.Id);
            foreach (var r in q.RewardReputation) _w.Progression.AddReputation(p, r.Faction, r.Amount);
            if (q.RewardAbility != null) { p.Learn(q.RewardAbility, Math.Max(1, C.HighestRankForLevel(q.RewardAbility, p.Level))); p.Send(p.BuildSpellBook()); }
            p.Notice($"Quest abgeschlossen: {q.Title}");
            int xp = Experience.QuestXp(p.Level, q.Level, q.RewardXp);
            _w.Progression.GiveXp(p, xp, "quest");
            p.Send(p.BuildInventory());
            p.Send(p.BuildQuestLog());
            p.Send(p.BuildPlayerState());
            RefreshMarkers(p);
            if (p.IsBot) { BotGearEquip(p); p.Bot?.RecordShared(q.Category == QuestCategory.Dungeon ? $"Quest „{q.Title}“ abgeschlossen" : "", 1); }
            else _w.Bots.ShareTurnIn(p, npc, q);
            return null;
        }

        private void BotGearEquip(Player p)
        {
            for (int i = 0; i < p.Data.Bag.Count; i++)
                if (p.Data.Bag[i] != null) _w.Bots.ConsiderEquip(p, i);
        }

        public void Abandon(Player p, string questId)
        {
            if (!p.Data.Quests.Remove(questId)) return;
            if (C.Quests.TryGetValue(questId, out var q))
            {
                foreach (var it in q.ProvidedItems) p.RemoveItem(it.Item, p.CountItem(it.Item));
                if (p.EscortNpc?.Escort?.Quest == q) { p.EscortNpc.Zone?.Remove(p.EscortNpc); p.EscortNpc = null; }
            }
            p.Send(p.BuildInventory());
            p.Send(p.BuildQuestLog());
            RefreshMarkers(p);
        }

        // ------------------------------------------------------------ Fortschritt

        private IEnumerable<(QuestDef q, QuestProgress prog)> Active(Player p)
        {
            foreach (var prog in p.Data.Quests.Values.ToList())
                if (prog.State == QuestState.Active || prog.State == QuestState.Complete)
                    yield return (C.Quests[prog.QuestId], prog);
        }

        /// <summary>Prüft Sammelziele gegen das Inventar und setzt den Status.</summary>
        public bool Recompute(Player p, QuestDef q, QuestProgress prog)
        {
            if (prog.State == QuestState.Failed) return false;
            var before = prog.State;
            bool all = true;
            for (int i = 0; i < q.Objectives.Count; i++)
            {
                var o = q.Objectives[i];
                if (o.Type == ObjectiveType.Collect) prog.Counts[i] = Math.Min(o.Count, p.CountItem(o.Target));
                if (prog.Counts[i] < o.Count) all = false;
            }
            prog.State = all ? QuestState.Complete : QuestState.Active;
            if (prog.State == QuestState.Complete && before != QuestState.Complete)
            {
                p.Notice($"{q.Title} – abgeschlossen. Kehrt zurück zu {C.Creatures[q.TurnIn].Name}.");
                p.Bot?.OnQuestReady(q);
            }
            return before != prog.State;
        }

        private void Increment(Player p, Func<ObjectiveDef, bool> match, int amount = 1)
        {
            bool changed = false;
            foreach (var (q, prog) in Active(p))
            {
                for (int i = 0; i < q.Objectives.Count; i++)
                {
                    var o = q.Objectives[i];
                    if (o.Type == ObjectiveType.Collect || !match(o) || prog.Counts[i] >= o.Count) continue;
                    prog.Counts[i] = Math.Min(o.Count, prog.Counts[i] + amount);
                    changed = true;
                    p.Notice($"{o.Description}: {prog.Counts[i]}/{o.Count}");
                }
                Recompute(p, q, prog);
            }
            if (changed) { p.Send(p.BuildQuestLog()); RefreshMarkers(p); }
        }

        public void OnCreatureKilled(Player tapper, Creature c)
        {
            foreach (var m in CreditMembers(tapper, c.Position))
                Increment(m, o => o.Type == ObjectiveType.Kill && o.Target == c.Template.Id);
        }

        public List<Player> CreditMembers(Player tapper, Vec3 at)
        {
            if (tapper.Group == null) return new List<Player> { tapper };
            return tapper.Group.Members.Where(m => m.Zone == tapper.Zone && Vec3.DistanceXZ(m.Position, at) <= GroupCreditRange).ToList();
        }

        public void OnItemsChanged(Player p)
        {
            bool changed = false;
            foreach (var (q, prog) in Active(p))
            {
                var before = prog.Counts.ToArray();
                changed |= Recompute(p, q, prog) || !before.SequenceEqual(prog.Counts);
            }
            if (changed) { p.Send(p.BuildQuestLog()); RefreshMarkers(p); }
        }

        public void OnAreaEntered(Player p, AreaDef area) => Increment(p, o => o.Type == ObjectiveType.Explore && o.Target == area.Id);

        public void OnObjectUsed(Player p, GameObjectDef obj) => Increment(p, o => o.Type == ObjectiveType.Interact && o.Target == obj.Id);

        /// <summary>Gespräch mit einem NPC: erfüllt Sprechen- und Lieferziele (Lieferung verbraucht den Gegenstand).</summary>
        public void OnTalkedTo(Player p, Creature npc)
        {
            var id = npc.Template.Id;
            bool changed = false;
            foreach (var (q, prog) in Active(p))
            {
                for (int i = 0; i < q.Objectives.Count; i++)
                {
                    var o = q.Objectives[i];
                    if (o.Target != id || prog.Counts[i] >= o.Count) continue;
                    if (o.Type == ObjectiveType.Talk) { prog.Counts[i] = o.Count; changed = true; }
                    else if (o.Type == ObjectiveType.Deliver)
                    {
                        if (q.ProvidedItems.Count > 0 && q.ProvidedItems.Any(it => p.CountItem(it.Item) < it.Count)) { p.Notice("Euch fehlt der Gegenstand, den Ihr überbringen sollt.", true); continue; }
                        foreach (var it in q.ProvidedItems) p.RemoveItem(it.Item, it.Count);
                        prog.Counts[i] = o.Count;
                        changed = true;
                        p.Send(p.BuildInventory());
                    }
                }
                Recompute(p, q, prog);
            }
            if (changed) { p.Send(p.BuildQuestLog()); RefreshMarkers(p); }
        }

        /// <summary>Wird diese Quest-Beute für dieses Mitglied noch gebraucht?</summary>
        public bool NeedsQuestItem(Player p, string questId, string itemId)
        {
            if (!p.Data.Quests.TryGetValue(questId, out var prog) || prog.State == QuestState.Failed) return false;
            var q = C.Quests[questId];
            for (int i = 0; i < q.Objectives.Count; i++)
                if (q.Objectives[i].Type == ObjectiveType.Collect && q.Objectives[i].Target == itemId && p.CountItem(itemId) < q.Objectives[i].Count) return true;
            return false;
        }

        // ------------------------------------------------------------ Eskorte

        private void StartEscort(Player p, Creature giver, QuestDef q)
        {
            var e = q.Escort!;
            if (p.EscortNpc != null && !p.EscortNpc.Removed) p.EscortNpc.Zone?.Remove(p.EscortNpc);
            var zone = p.Zone!;
            var npc = zone.SpawnCreature(e.Npc, e.Waypoints[0], Math.Max(1, p.Level));
            npc.IsSummoned = false;
            npc.Owner = p;
            npc.Escort = new EscortRun { Quest = q, Owner = p };
            npc.BaseMoveSpeed = 3.5f;
            npc.MarkStatsDirty();
            p.EscortNpc = npc;
            if (e.StartText != null) _w.Chat.NpcSay(npc, e.StartText);
        }

        /// <summary>Eskorte-KI: läuft die Wegpunkte ab, wartet auf den Spieler und löst Hinterhalte aus.</summary>
        public void UpdateEscort(Creature npc, float dt)
        {
            var run = npc.Escort!;
            var owner = run.Owner;
            if (run.Finished) return;
            if (owner.Removed || owner.Zone != npc.Zone || !owner.Data.Quests.ContainsKey(run.Quest.Id))
            {
                npc.Zone?.Remove(npc);
                return;
            }
            var e = run.Quest.Escort!;
            if (npc.InCombat) return; // kämpft (CreatureAI greift Angreifer an)
            if (npc.DistanceTo(owner) > 30) return; // wartet auf den Spieler
            if (run.Waypoint >= e.Waypoints.Count)
            {
                run.DefendTimer += dt;
                if (run.DefendTimer < e.DefendSeconds) return;
                run.Finished = true;
                if (e.EndText != null) _w.Chat.NpcSay(npc, e.EndText);
                foreach (var m in CreditMembers(owner, npc.Position))
                    Increment(m, o => o.Type == ObjectiveType.Escort && o.Target == npc.Template.Id);
                npc.CorpseTimer = 8;
                npc.AI.DespawnAfter(8);
                return;
            }
            var wp = e.Waypoints[run.Waypoint];
            if (npc.DistanceTo(wp) < 1.5f)
            {
                foreach (var amb in e.Ambushes.Where(a => a.AtWaypoint == run.Waypoint))
                {
                    if (!run.TriggeredAmbushes.Add(e.Ambushes.IndexOf(amb))) continue;
                    if (amb.Text != null) _w.Chat.NpcSay(npc, amb.Text);
                    for (int i = 0; i < amb.Count; i++)
                    {
                        double ang = _w.Random.NextDouble() * Math.PI * 2;
                        var pos = npc.Position + new Vec3((float)Math.Cos(ang) * 14, 0, (float)Math.Sin(ang) * 14);
                        var add = npc.Zone!.SpawnCreature(amb.Creature, pos);
                        add.AI.Engage(npc);
                    }
                }
                run.Waypoint++;
                return;
            }
            npc.AI.MoveTowards(wp, dt);
        }

        public void EscortFailed(Creature npc)
        {
            var run = npc.Escort;
            if (run == null || run.Finished) return;
            run.Finished = true;
            if (run.Owner.Data.Quests.TryGetValue(run.Quest.Id, out var prog))
            {
                prog.State = QuestState.Failed;
                run.Owner.Notice($"{run.Quest.Title} – fehlgeschlagen. Gebt die Quest auf und versucht es erneut.", true);
                run.Owner.Send(run.Owner.BuildQuestLog());
            }
            run.Owner.EscortNpc = null;
        }
    }
}
