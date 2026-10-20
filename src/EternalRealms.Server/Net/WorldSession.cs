using EternalRealms.Server.AI;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Net
{
    public enum SessionState { Connected, Authenticated, InWorld, Closed }

    /// <summary>
    /// Eine Verbindung eines Spielers: Anmeldung → Charakterauswahl → Spielwelt. Prüft jede Nachricht
    /// (Zustand, Eingaben, Rate) bevor sie an die Spielsysteme geht. Läuft im Spielthread.
    /// </summary>
    public sealed class WorldSession
    {
        private readonly GameWorld _w;
        private readonly IAccountRepository _accounts;
        private readonly ICharacterRepository _chars;
        private readonly SessionRegistry _registry;
        private int _messagesThisSecond;
        private double _rateWindow;
        private bool _busy;

        public IPlayerConnection Connection { get; }
        public SessionState State { get; private set; } = SessionState.Connected;
        public Account? Account { get; private set; }
        public Player? Player { get; private set; }

        public WorldSession(GameWorld world, IAccountRepository accounts, ICharacterRepository chars, SessionRegistry registry, IPlayerConnection connection)
        {
            _w = world;
            _accounts = accounts;
            _chars = chars;
            _registry = registry;
            Connection = connection;
        }

        private void Error(string text) => Connection.Send(new S_Notice { IsError = true, Text = text });

        public void Handle(Message m)
        {
            if (State == SessionState.Closed) return;
            if (_w.Time - _rateWindow >= 1) { _rateWindow = _w.Time; _messagesThisSecond = 0; }
            if (++_messagesThisSecond > _w.Config.MaxMessagesPerSecond)
            {
                if (m is C_Move) return; // Bewegungen bei Überlast verwerfen statt trennen
                if (_messagesThisSecond > _w.Config.MaxMessagesPerSecond * 3) { Connection.Disconnect("Zu viele Nachrichten"); Close(); }
                return;
            }
            switch (State)
            {
                case SessionState.Connected:
                    if (m is C_Login login) HandleLogin(login);
                    else if (m is C_Ping ping) Pong(ping);
                    break;
                case SessionState.Authenticated:
                    HandleCharacterScreen(m);
                    break;
                case SessionState.InWorld:
                    if (Player != null) HandleInWorld(Player, m);
                    break;
            }
        }

        private void Pong(C_Ping p) => Connection.Send(new S_Pong { ClientTime = p.ClientTime, ServerTime = (long)(_w.Time * 1000) });

        // ------------------------------------------------------------ Anmeldung

        private void HandleLogin(C_Login m)
        {
            if (_busy) return;
            if (m.ProtocolVersion != ProtocolInfo.ProtocolVersion) { Connection.Send(new S_LoginResult { Message = "Veralteter Client. Bitte aktualisieren." }); return; }
            var user = (m.Account ?? "").Trim();
            if (user.Length < 3 || user.Length > 32 || !user.All(c => char.IsLetterOrDigit(c) || c == '_' || c == '.' || c == '-'))
            { Connection.Send(new S_LoginResult { Message = "Ungültiger Kontoname (3–32 Zeichen, Buchstaben, Ziffern, _ . -)." }); return; }
            var pass = m.Password ?? "";
            if (pass.Length < 6 || pass.Length > 128) { Connection.Send(new S_LoginResult { Message = "Das Passwort muss mindestens 6 Zeichen haben." }); return; }
            _busy = true;
            bool register = m.Register;
            Task.Run(async () =>
            {
                Account? acc = null;
                string error = "";
                try
                {
                    acc = await _accounts.FindByNameAsync(user);
                    if (register)
                    {
                        if (!_w.Config.AllowRegistration) error = "Neue Konten sind derzeit deaktiviert.";
                        else if (acc != null) { error = "Dieser Kontoname ist vergeben."; acc = null; }
                        else acc = await _accounts.CreateAsync(user, PasswordHasher.Hash(pass));
                    }
                    else if (acc == null || !PasswordHasher.Verify(pass, acc.PasswordHash)) { error = "Kontoname oder Passwort falsch."; acc = null; }
                    else if (acc.IsBanned) { error = "Dieses Konto ist gesperrt."; acc = null; }
                    if (acc != null) await _accounts.TouchLoginAsync(acc.Id);
                }
                catch (Exception ex) { error = "Anmeldung derzeit nicht möglich."; _w.Post(() => _w.Log("Login-Fehler: " + ex.Message)); }
                _w.Post(() =>
                {
                    _busy = false;
                    if (State == SessionState.Closed) return;
                    if (acc == null) { Connection.Send(new S_LoginResult { Success = false, Message = error }); return; }
                    _registry.KickOther(acc.Id, this);
                    Account = acc;
                    State = SessionState.Authenticated;
                    Connection.Send(new S_LoginResult { Success = true, Message = register ? "Konto erstellt. Willkommen in Eternal Realms!" : "Willkommen zurück!" });
                });
            });
        }

        // ------------------------------------------------------------ Charakterauswahl

        private void HandleCharacterScreen(Message m)
        {
            switch (m)
            {
                case C_CharacterList: SendCharacterList(); break;
                case C_CreateCharacter c: CreateCharacter(c); break;
                case C_DeleteCharacter d: DeleteCharacter(d.CharacterId); break;
                case C_EnterWorld e: EnterWorld(e.CharacterId); break;
                case C_Ping p: Pong(p); break;
            }
        }

        private void SendCharacterList()
        {
            var accountId = Account!.Id;
            Queue(async () =>
            {
                var list = await _chars.ListForAccountAsync(accountId);
                _w.Post(() => Connection.Send(new S_CharacterList { Characters = list.Select(c => c.ToSummary()).ToList() }));
            });
        }

        public string? ValidateCreation(C_CreateCharacter c)
        {
            var name = NameGenerator.Normalize((c.Name ?? "").Trim());
            if (!NameGenerator.IsValid(name)) return "Der Name muss 2–12 Buchstaben lang sein.";
            if (!_w.Content.Races.TryGetValue(c.Race, out var race)) return "Unbekanntes Volk.";
            if (!_w.Content.Classes.ContainsKey(c.Class)) return "Unbekannte Klasse.";
            if (!race.Classes.Contains(c.Class)) return $"{race.Name} können keine {_w.Content.Classes[c.Class].Name} werden.";
            var o = race.Appearance;
            var a = c.Appearance;
            if (a.SkinColor >= o.SkinColors || a.Face >= o.Faces || a.HairStyle >= o.HairStyles || a.HairColor >= o.HairColors ||
                (o.Beards > 0 ? a.Beard >= o.Beards : a.Beard > 0) || a.EyeColor >= o.EyeColors || (o.Scars > 0 ? a.Scar >= o.Scars : a.Scar > 0) ||
                (o.Tattoos > 0 ? a.Tattoo >= o.Tattoos : a.Tattoo > 0))
                return "Ungültige Aussehensauswahl.";
            if (float.IsNaN(a.Height) || a.Height < race.MinHeight - 0.001f || a.Height > race.MaxHeight + 0.001f) return "Ungültige Körpergröße.";
            return null;
        }

        private void CreateCharacter(C_CreateCharacter c)
        {
            if (_busy) return;
            var err = ValidateCreation(c);
            if (err != null) { Connection.Send(new S_CreateCharacterResult { Message = err }); return; }
            var name = NameGenerator.Normalize(c.Name.Trim());
            var data = _w.CreateCharacterTemplate(name, c.Race, c.Class, c.Gender, AppearanceDto.From(c.Appearance));
            data.AccountId = Account!.Id;
            _busy = true;
            int max = _w.Config.MaxCharactersPerAccount;
            Queue(async () =>
            {
                string message = "";
                long id = 0;
                try
                {
                    var existing = await _chars.ListForAccountAsync(data.AccountId!.Value);
                    if (existing.Count >= max) message = $"Höchstens {max} Charaktere pro Konto.";
                    else if (await _chars.NameExistsAsync(name)) message = "Dieser Name ist bereits vergeben.";
                    else id = await _chars.CreateAsync(data);
                }
                catch (Exception ex) { message = "Erstellen fehlgeschlagen."; _w.Post(() => _w.Log("Erstellen: " + ex.Message)); }
                _w.Post(() =>
                {
                    _busy = false;
                    if (_w.NameTaken(name) && id == 0 && message == "") message = "Dieser Name ist bereits vergeben.";
                    Connection.Send(new S_CreateCharacterResult { Success = id > 0, CharacterId = id, Message = id > 0 ? $"{name} wurde erschaffen." : message });
                    if (id > 0) SendCharacterList();
                });
            });
        }

        private void DeleteCharacter(long id)
        {
            var accountId = Account!.Id;
            Queue(async () =>
            {
                var c = await _chars.GetAsync(id);
                if (c != null && c.AccountId == accountId && !c.IsBot) await _chars.DeleteAsync(id);
                _w.Post(SendCharacterList);
            });
        }

        private void Queue(Func<Task> job)
        {
            if (_w.Persistence != null) _w.Persistence.Enqueue(job);
            else Task.Run(job);
        }

        private void EnterWorld(long id)
        {
            if (_busy) return;
            _busy = true;
            var accountId = Account!.Id;
            // Über die Persistenz-Warteschlange: geladen wird erst nach der letzten Speicherung dieses Charakters.
            Queue(async () =>
            {
                CharacterData? data = null;
                try { data = await _chars.GetAsync(id); }
                catch (Exception ex) { _w.Post(() => _w.Log("Laden: " + ex.Message)); }
                _w.Post(() =>
                {
                    _busy = false;
                    if (State != SessionState.Authenticated) return;
                    if (data == null || data.AccountId != accountId || data.IsBot) { Error("Charakter nicht gefunden."); return; }
                    if (_w.AllPlayers.Any(p => p.Data.Id == data.Id)) { Error("Dieser Charakter ist bereits in der Welt."); return; }
                    Player = _w.EnterWorld(data, Connection);
                    State = SessionState.InWorld;
                    _w.Log($"{Player.Name} betritt {Player.Zone!.Def.Name} ({Connection.RemoteAddress}).");
                });
            });
        }

        // ------------------------------------------------------------ Spielwelt

        private void HandleInWorld(Player p, Message m)
        {
            p.LastActivity = _w.Time;
            string? err = null;
            switch (m)
            {
                case C_Move mv: _w.HandleMove(p, mv); return;
                case C_Ping ping: Pong(ping); return;
                case C_SetTarget t:
                    p.Target = t.TargetId == 0 ? null : p.Zone?.GetUnit(t.TargetId);
                    if (p.Target != null && p.DistanceTo(p.Target) > ZoneInstance.ViewRange) p.Target = null;
                    return;
                case C_CastAbility c: err = CastAbility(p, c); break;
                case C_CancelCast: _w.Combat.CancelCast(p, "Abgebrochen"); return;
                case C_AttackStart a:
                {
                    var t = p.Zone?.GetUnit(a.TargetId);
                    if (t == null || !p.CanAttack(t)) { err = "Ungültiges Ziel."; break; }
                    if (p.IsDead) { err = "Ihr seid tot."; break; }
                    p.Target = t;
                    p.AutoAttacking = true;
                    if (p.IsMounted) _w.Mounts.Dismount(p);
                    return;
                }
                case C_AttackStop: p.AutoAttacking = false; return;
                case C_Chat chat:
                    if (chat.Text.Trim() == ".pvp") { p.PvP = !p.PvP; p.Notice(p.PvP ? "PvP aktiviert." : "PvP wird deaktiviert."); return; }
                    _w.Chat.Handle(p, chat.Channel, chat.Text, chat.WhisperTo ?? "");
                    return;
                case C_Interact i: err = _w.Interactions.Interact(p, i.EntityId); break;
                case C_QuestAccept qa: err = WithNpc(p, qa.NpcId, npc => _w.Quests.Accept(p, npc, qa.QuestId)); break;
                case C_QuestTurnIn qt: err = WithNpc(p, qt.NpcId, npc => _w.Quests.TurnIn(p, npc, qt.QuestId, qt.ChoiceIndex)); break;
                case C_QuestAbandon ab: _w.Quests.Abandon(p, ab.QuestId); return;
                case C_Loot l: err = p.Zone?.Get(l.CorpseId) is Creature corpse ? _w.Loot.TryLoot(p, corpse) : "Nichts zu plündern."; break;
                case C_UseItem u: err = _w.Interactions.UseItem(p, u.BagIndex, u.TargetId); break;
                case C_EquipItem e: err = p.Equip(e.BagIndex); if (err == null) { p.Send(p.BuildInventory()); p.Send(p.BuildPlayerState()); } break;
                case C_UnequipItem ue: err = p.Unequip(ue.Slot); if (err == null) { p.Send(p.BuildInventory()); p.Send(p.BuildPlayerState()); } break;
                case C_BuyItem b: err = _w.Interactions.Buy(p, b.NpcId, b.ItemId, b.Count); break;
                case C_SellItem s: err = _w.Interactions.Sell(p, s.NpcId, s.BagIndex); break;
                case C_TrainAbility tr: err = _w.Interactions.Train(p, tr.NpcId, tr.AbilityId); break;
                case C_LearnTalent lt: err = _w.Progression.LearnTalent(p, lt.TalentId); break;
                case C_ReleaseSpirit: err = _w.Death.ReleaseSpirit(p); break;
                case C_ReclaimCorpse: err = _w.Death.ReclaimCorpse(p); break;
                case C_SpiritHealerResurrect sh: err = p.Zone?.Get(sh.NpcId) is Creature healer ? _w.Death.SpiritHealer(p, healer) : "Kein Geisterheiler."; break;
                case C_GroupInvite gi: err = _w.Groups.Invite(p, gi.Name ?? ""); break;
                case C_GroupAccept ga: _w.Groups.Respond(p, ga.Accept); return;
                case C_GroupLeave: _w.Groups.Leave(p); return;
                case C_BotSearch bs:
                    p.Send(new S_BotCandidates { Candidates = _w.Bots.FindCandidates(p, bs.Role == 255 ? null : (Role)Math.Min((int)bs.Role, 3),
                        bs.Difficulty == 255 ? null : (BotDifficulty)Math.Min((int)bs.Difficulty, 3)) });
                    return;
                case C_BotInvite bi:
                    if (p.Group != null && p.Group.Leader != p) { err = "Nur der Gruppenanführer kann einladen."; break; }
                    err = _w.Bots.Invite(p, bi.CandidateId);
                    break;
                case C_BotDismiss bd: err = _w.Bots.Dismiss(p, bd.EntityId); break;
                case C_BotFavorite bf: err = _w.Bots.SetFavorite(p, bf.EntityId, bf.Favorite); break;
                case C_BotCommand bc: err = BotCommandFromUi(p, bc); break;
                case C_SetMarker sm:
                    if (p.Group == null) { err = "Ihr seid in keiner Gruppe."; break; }
                    if (sm.TargetId != 0 && p.Zone?.GetUnit(sm.TargetId) == null) { err = "Ungültiges Ziel."; break; }
                    p.Group.SetMarker(sm.Marker, sm.TargetId);
                    return;
                case C_CancelAura ca:
                {
                    var aura = p.FindAura(ca.AuraId);
                    if (aura != null && !aura.Def.IsDebuff) p.RemoveAura(aura);
                    return;
                }
                case C_TakeFlight tf: err = _w.Interactions.TakeFlight(p, tf.NpcId, tf.Destination ?? ""); break;
                case C_MoveItem mi: err = Inv(p, p.MoveItem(mi.From, mi.To)); break;
                case C_SplitStack ss: err = Inv(p, p.SplitStack(ss.From, ss.Count)); break;
                case C_SortBags: p.SortBags(); p.Send(p.BuildInventory()); return;
                case C_EquipBag eb: err = Inv(p, p.EquipBag(eb.BagIndex)); break;
                case C_UnequipBag ub: err = Inv(p, p.UnequipBag(ub.ContainerSlot)); break;
                case C_DestroyItem di: err = Inv(p, p.DestroyItem(di.BagIndex)); if (err == null) _w.Quests.OnItemsChanged(p); break;
                case C_BankDeposit bdp:
                    if (_w.Interactions.NpcWithRole(p, bdp.NpcId, NpcRole.Banker) == null) { err = "Kein Bankier in der Nähe."; break; }
                    err = Inv(p, p.BankDeposit(bdp.BagIndex));
                    if (err == null) { p.Send(p.BuildBank(bdp.NpcId)); _w.Quests.OnItemsChanged(p); }
                    break;
                case C_BankWithdraw bw:
                    if (_w.Interactions.NpcWithRole(p, bw.NpcId, NpcRole.Banker) == null) { err = "Kein Bankier in der Nähe."; break; }
                    err = Inv(p, p.BankWithdraw(bw.BankIndex));
                    if (err == null) { p.Send(p.BuildBank(bw.NpcId)); _w.Quests.OnItemsChanged(p); }
                    break;
                case C_MailList ml:
                    if (!_w.Mail.NearMailbox(p, ml.ObjectId)) { err = "Kein Briefkasten in der Nähe."; break; }
                    _w.Mail.Open(p);
                    return;
                case C_MailSend ms:
                    if (!_w.Mail.NearMailbox(p, ms.ObjectId)) { err = "Kein Briefkasten in der Nähe."; break; }
                    _w.Mail.Send(p, ms);
                    return;
                case C_MailTake mt: err = _w.Mail.NearMailbox(p, mt.ObjectId) ? _w.Mail.Take(p, mt.MailId, mt.Index) : "Kein Briefkasten in der Nähe."; break;
                case C_MailDelete md: err = _w.Mail.NearMailbox(p, md.ObjectId) ? _w.Mail.Delete(p, md.MailId) : "Kein Briefkasten in der Nähe."; break;
                case C_MailReturn mr: err = _w.Mail.NearMailbox(p, mr.ObjectId) ? _w.Mail.Return(p, mr.MailId) : "Kein Briefkasten in der Nähe."; break;
                case C_AuctionSearch asr:
                    if (Auctioneer(p, asr.NpcId) is string e1) { err = e1; break; }
                    p.Send(_w.Auctions.Search(p, asr));
                    return;
                case C_AuctionMine am:
                    if (Auctioneer(p, am.NpcId) is string e2) { err = e2; break; }
                    p.Send(_w.Auctions.Mine(p, am.Bids));
                    return;
                case C_AuctionPost ap:
                    err = Auctioneer(p, ap.NpcId) ?? _w.Auctions.Post(p, ap.BagIndex, ap.Count, ap.StartBid, ap.Buyout, ap.DurationHours);
                    if (err == null) p.Send(_w.Auctions.Mine(p, false));
                    break;
                case C_AuctionBid ab:
                    err = Auctioneer(p, ab.NpcId) ?? _w.Auctions.Bid(p, ab.AuctionId, ab.Amount);
                    break;
                case C_AuctionCancel ac:
                    err = Auctioneer(p, ac.NpcId) ?? _w.Auctions.Cancel(p, ac.AuctionId);
                    if (err == null) p.Send(_w.Auctions.Mine(p, false));
                    break;
                case C_LootRoll lr: err = _w.LootRolls.Choose(p, lr.RollId, lr.Choice); break;
                case C_MasterLoot mlt: err = _w.Loot.MasterLoot(p, mlt.CorpseId, mlt.ItemId ?? "", mlt.TargetId); break;
                case C_SetLootMethod slm: err = _w.Groups.SetLootMethod(p, slm.Method, slm.Threshold, slm.MasterLooterId); break;
                case C_ResetTalents rt: err = WithNpc(p, rt.NpcId, npc => _w.Progression.ResetTalents(p, npc, rt.Confirm)); break;
                case C_Mount mo: err = _w.Mounts.Mount(p, mo.MountId ?? ""); break;
                case C_Dismount: _w.Mounts.Dismount(p); return;
                case C_LearnRiding lrd: err = _w.Mounts.LearnRiding(p, lrd.NpcId); break;
                case C_GuildCreate gc: err = _w.Guilds.Create(p, gc.Text); break;
                case C_GuildInvite gin: err = _w.Guilds.Invite(p, gin.Text); break;
                case C_GuildRespond gr: err = _w.Guilds.Respond(p, gr.Accept); break;
                case C_GuildLeave: err = _w.Guilds.Leave(p); break;
                case C_GuildKick gk: err = _w.Guilds.Kick(p, gk.CharacterId); break;
                case C_GuildSetRank gsr: err = _w.Guilds.SetRank(p, gsr.CharacterId, gsr.Rank); break;
                case C_GuildMotd gm: err = _w.Guilds.SetMotd(p, gm.Text); break;
                case C_GuildDisband: err = _w.Guilds.Disband(p); break;
                case C_GuildRoster: p.Send(_w.Guilds.BuildRoster(p)); return;
                case C_FriendList: p.Send(_w.Social.BuildList(p)); return;
                case C_FriendAdd fa: _w.Social.AddFriend(p, fa.Text); return;
                case C_FriendRemove fr: _w.Social.RemoveFriend(p, fr.CharacterId); return;
                case C_IgnoreAdd ia: _w.Social.Ignore(p, ia.Text); return;
                case C_IgnoreRemove ir: _w.Social.Unignore(p, ir.CharacterId); return;
                case C_ShareLocation sl: _w.Social.SetShareLocation(p, sl.Share); return;
                case C_ReportPlayer rp: _w.Social.Report(p, rp.Name, rp.Reason, rp.Text); return;
                case C_InviteFavorite ifv: err = _w.Bots.InviteFavorite(p, ifv.CharacterId); if (err == null) p.Send(_w.Social.BuildList(p)); break;
                case C_ResetInstances: err = _w.Instances.Reset(p); break;
                case C_InstanceInfo: p.Send(_w.Instances.BuildInfo(p)); return;
                case C_FinderActivities: p.Send(_w.Finder.BuildActivityList()); return;
                case C_FinderJoin fj: err = _w.Finder.Join(p, fj.Activities, fj.Roles.Where(r => r <= 3).Select(r => (Role)r), fj.Mode); break;
                case C_FinderLeave: _w.Finder.Leave(p); return;
                case C_FinderAccept fac: err = _w.Finder.Answer(p, fac.ProposalId, fac.Accept); break;
                case C_ReadyCheck: err = _w.Groups.StartReadyCheck(p); break;
                case C_ReadyResponse rr: _w.Groups.ReadyResponse(p, rr.Ready); return;
                case C_VoteKick vk: err = _w.Groups.StartKickVote(p, vk.TargetId, vk.Reason ?? ""); break;
                case C_VoteKickResponse vkr: _w.Groups.VoteResponse(p, vkr.VoteId, vkr.Yes); return;
                case C_SetRole sr: err = _w.Groups.SetRole(p, sr.Role); break;
                case C_SetBotRole sbr: err = _w.Groups.SetBotRole(p, sbr.BotEntityId, sbr.Role); break;
                case C_ConvertToRaid: err = _w.Groups.ConvertToRaid(p); break;
                case C_SetSubgroup ssg: err = _w.Groups.SetSubgroup(p, ssg.EntityId, ssg.Subgroup); break;
                case C_LearnProfession lpf: err = _w.Professions.Learn(p, lpf.NpcId); break;
                case C_TrainProfessionRank tpr: err = _w.Professions.TrainRank(p, tpr.NpcId); break;
                case C_LearnRecipe lrc: err = _w.Professions.LearnRecipe(p, lrc.NpcId, lrc.RecipeId ?? ""); break;
                case C_UnlearnProfession upf: err = _w.Professions.Unlearn(p, upf.Profession ?? ""); break;
                case C_Craft cr: err = _w.Professions.Craft(p, cr.RecipeId ?? "", cr.Count); break;
                case C_Skin sk: err = _w.Professions.Skin(p, sk.CorpseId); break;
                case C_Disenchant de: err = _w.Professions.Disenchant(p, de.BagIndex); break;
                case C_ProfessionInfo: p.Send(_w.Professions.Build(p)); return;
                case C_Logout: Logout(); return;
            }
            if (err != null) p.Notice(err, true);
        }

        /// <summary>Befehl aus der Kommandooberfläche – gleiche Logik wie ein Chatbefehl, aber ohne Textverständnis.</summary>
        private string? BotCommandFromUi(Player p, C_BotCommand c)
        {
            var group = p.Group;
            if (group == null || !group.Bots.Any()) return "Ihr habt keine KI-Mitspieler in der Gruppe.";
            if (group.Leader != p) return "Nur der Gruppenanführer kann Befehle geben.";
            if (c.Command == 0 || c.Command > (byte)BotCommand.Retreat) return "Unbekannter Befehl.";
            if (c.TargetId != 0)
            {
                if (p.Zone?.GetUnit(c.TargetId) is not Unit t) return "Ungültiges Ziel.";
                p.Target = t;
            }
            var order = new BotOrder { Command = (BotCommand)c.Command, Side = (OrderSide)Math.Min((int)c.Side, 2) };
            var bots = group.Bots.Where(b => c.BotEntityId == 0 || b.Id == c.BotEntityId).ToList();
            if (bots.Count == 0) return "Dieser KI-Mitspieler ist nicht in Eurer Gruppe.";
            if (c.BotEntityId != 0) order.Addressees.Add(bots[0].Name);
            foreach (var b in bots) b.Bot?.Execute(order, p);
            return null;
        }

        private string? Auctioneer(Player p, int npcId) =>
            _w.Interactions.NpcWithRole(p, npcId, NpcRole.Auctioneer) == null ? "Kein Auktionator in der Nähe." : null;

        /// <summary>Nach einer Inventaränderung: bei Erfolg das neue Inventar senden.</summary>
        private static string? Inv(Player p, string? err)
        {
            if (err == null) p.Send(p.BuildInventory());
            return err;
        }

        private string? WithNpc(Player p, int npcId, Func<Creature, string?> action) =>
            p.Zone?.Get(npcId) is Creature npc ? action(npc) : "Niemand da.";

        private string? CastAbility(Player p, C_CastAbility c)
        {
            if (string.IsNullOrEmpty(c.AbilityId) || c.AbilityId.Length > 64) return "Unbekannte Fähigkeit.";
            int known = p.KnownRank(c.AbilityId);
            if (known <= 0) return "Ihr kennt diese Fähigkeit nicht.";
            int rank = c.Rank <= 0 ? known : Math.Min(c.Rank, known);
            var ab = _w.Content.GetAbilityRank(c.AbilityId, rank);
            if (ab == null) return "Unbekannte Fähigkeit.";
            var target = c.TargetId != 0 ? p.Zone?.GetUnit(c.TargetId) : null;
            if (target == null && c.TargetId != 0 && p.Zone?.Get(c.TargetId) == null)
            {
                // Tote Spieler sind weiterhin Ziel (Wiederbelebung), auch wenn sie ein Geist sind.
                target = _w.GetPlayer(c.TargetId);
            }
            return _w.Combat.TryCast(p, ab, target);
        }

        public void Logout()
        {
            if (Player != null)
            {
                _w.Logout(Player);
                _w.Log($"{Player.Name} hat die Welt verlassen.");
                Player.Connection = null;
                Player = null;
            }
            if (State == SessionState.InWorld) State = SessionState.Authenticated;
            if (Account != null && State != SessionState.Closed) SendCharacterList();
        }

        public void Close()
        {
            if (State == SessionState.Closed) return;
            if (Player != null) { _w.Logout(Player); Player.Connection = null; Player = null; }
            State = SessionState.Closed;
            _registry.Remove(this);
        }
    }

    /// <summary>Alle Sitzungen; verhindert doppelte Anmeldung desselben Kontos.</summary>
    public sealed class SessionRegistry
    {
        private readonly List<WorldSession> _sessions = new();
        public IReadOnlyList<WorldSession> Sessions => _sessions;

        public void Add(WorldSession s) => _sessions.Add(s);
        public void Remove(WorldSession s) => _sessions.Remove(s);

        public void KickOther(long accountId, WorldSession keep)
        {
            foreach (var s in _sessions.Where(s => s != keep && s.Account?.Id == accountId).ToList())
            {
                s.Connection.Send(new S_Notice { IsError = true, Text = "Euer Konto wurde an einem anderen Ort angemeldet." });
                s.Connection.Disconnect("Doppelte Anmeldung");
                s.Close();
            }
        }
    }
}
