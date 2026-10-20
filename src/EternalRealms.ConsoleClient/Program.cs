using System.Collections.Concurrent;
using System.Globalization;
using EternalRealms.Shared;
using EternalRealms.Shared.Client;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

// Eternal Realms – Textclient. Spielt über dasselbe Netzwerkprotokoll wie der Unity-Client.
// Aufruf: dotnet run --project src/EternalRealms.ConsoleClient -- [host] [port]
string host = args.Length > 0 ? args[0] : "127.0.0.1";
int port = args.Length > 1 && int.TryParse(args[1], out var pp) ? pp : ProtocolInfo.DefaultPort;
var content = ContentDatabase.LoadFromDirectory(FindContent());
var client = new GameClient();
var input = new ConcurrentQueue<string>();
Vec3? walkTarget = null;
int followId = 0;
int lastNotice = 0, lastChat = 0;
double nextMoveSend = 0;

client.MessageReceived += m =>
{
    switch (m)
    {
        case S_CombatEvent ce when client.Me != null && (ce.SourceId == client.Me.Id || ce.TargetId == client.Me.Id || IsGroup(ce.SourceId)):
            var src = Name(ce.SourceId); var dst = Name(ce.TargetId);
            var ab = content.Abilities.TryGetValue(ce.AbilityId, out var ad) ? ad.Name : content.Auras.TryGetValue(ce.AbilityId, out var au) ? au.Name : ce.AbilityId;
            Print(ce.Result is CombatResult.Heal or CombatResult.CritHeal
                ? $"  + {src}: {ab} heilt {dst} um {ce.Amount}{(ce.Result == CombatResult.CritHeal ? " (kritisch)" : "")}"
                : ce.Amount > 0 ? $"  - {src}: {ab} trifft {dst} für {ce.Amount}{(ce.Result == CombatResult.Crit ? " (kritisch)" : "")}"
                : $"  · {src}: {ab} → {dst}: {ce.Result}");
            break;
        case S_UnitDied d: Print($"  ✝ {Name(d.Id)} stirbt."); break;
        case S_XpGain x: Print($"  ★ +{x.Amount} EP{(x.RestedBonus > 0 ? $" (davon {x.RestedBonus} Ruhebonus)" : "")}"); break;
        case S_LevelUp lu when lu.EntityId == client.State.MyId: Print($"  ★★ STUFE {lu.NewLevel}! +{lu.HealthGained} Leben. Talentpunkte frei: {lu.TalentPoints}"); break;
        case S_GuildInvite gi2: Print($"{gi2.From} lädt Euch in die Gilde <{gi2.GuildName}> ein (gjoin / gdecline)."); break;
        case S_GuildRoster gr2:
            if (gr2.Name == "") { Print("Ihr seid in keiner Gilde."); break; }
            Print($"<{gr2.Name}> – {gr2.Members.Count} Mitglieder{(gr2.Motd != "" ? " – " + gr2.Motd : "")}");
            foreach (var gm2 in gr2.Members)
                Print($"  {gr2.RankNames[gm2.Rank],-14} {gm2.Name}{(gm2.IsBot ? " (KI)" : "")} St. {gm2.Level} {(gm2.Online ? "online " + gm2.Zone : "offline")}");
            break;
        case S_FriendList fl2:
            Print($"Freunde ({fl2.Friends.Count}):" + (fl2.ShareLocation ? "" : " [Standort verborgen]"));
            foreach (var f in fl2.Friends) Print($"  {f.Name} – {(f.Online ? $"online, St. {f.Level}, {f.Zone}" : "offline")}");
            Print($"KI-Begleiter ({fl2.Companions.Count}):");
            foreach (var f in fl2.Companions)
                Print($"  {f.Name} – St. {f.Level} {RoleName(f.Role)}, Gegenstandsstufe {f.ItemLevel}, {f.Activity}, {f.Availability}" +
                      (f.SharedAdventures.Count > 0 ? $" – gemeinsam: {string.Join("; ", f.SharedAdventures)}" : ""));
            if (fl2.Ignored.Count > 0) Print("Ignoriert: " + string.Join(", ", fl2.Ignored));
            break;
        case S_InstanceInfo ii:
            Print(ii.Binds.Count == 0 ? "Keine Instanzbindungen." : "Instanzbindungen:");
            foreach (var b in ii.Binds) Print($"  {b.Name} #{b.InstanceId}: {b.BossesKilled}/{b.BossesTotal} Bosse{(b.HoursLeft >= 0 ? $", Reset in {b.HoursLeft:0} h" : "")}");
            break;
        case S_FinderActivities fa2:
            Print("Aktivitäten (lfg <id> [rollen] [nurmenschen|bevorzugt|ki]):");
            foreach (var x in fa2.Activities) Print($"  {x.Id} – {x.Name} ({x.Kind}, Stufe {x.MinLevel}–{x.MaxLevel}, {x.Size} Spieler)");
            break;
        case S_FinderStatus fs2: Print($"Gruppensuche: {fs2.Text}{(fs2.Queued ? $" ({fs2.WaitSeconds:0} s)" : "")}"); break;
        case S_FinderProposal fp2: Print($"Gruppe für {fp2.Activity} gefunden{(fp2.IsReplacement ? " (Ersatz für einen KI-Mitspieler)" : "")}: {string.Join(", ", fp2.Members)} – Eure Rolle: {RoleName(fp2.Role)}. accept-group / decline-group ({fp2.Seconds:0} s)"); break;
        case S_ReadyCheck rc2: Print($"{rc2.From} fragt: Seid Ihr bereit? (ready / ready nein)"); break;
        case S_ReadyCheckResult rcr: Print($"Bereit: {string.Join(", ", rcr.Ready)}{(rcr.NotReady.Count > 0 ? " – nicht bereit: " + string.Join(", ", rcr.NotReady) : "")}"); break;
        case S_VoteKick vk2: Print($"Abstimmung: {vk2.TargetName} entfernen? Grund: {vk2.Reason} (vote / vote nein)"); break;
        case S_ProfessionTrainer pt2:
            if (!content.Professions.TryGetValue(pt2.Profession, out var ptd)) break;
            if (!pt2.Known) { Print($"{ptd.Name} erlernen für {Money(pt2.LearnCost)}: learnprof"); break; }
            if (pt2.NextRank != "") Print($"Nächster Rang: {pt2.NextRank} für {Money(pt2.NextRankCost)} (rankprof)");
            for (int i = 0; i < pt2.Recipes.Count; i++)
                Print($"  {pt2.Recipes[i]} – {content.Recipes[pt2.Recipes[i]].Name} (ab {content.Recipes[pt2.Recipes[i]].SkillRequired}) – {Money(pt2.RecipeCosts[i])}");
            break;
        case S_TradeRequest tq: Print($"{tq.FromName} möchte handeln (tradeyes / tradeno)."); break;
        case S_TradeState ts:
            Print($"Handel mit {ts.PartnerName}{(ts.PartnerIsBot ? " (KI)" : "")} – Stand {ts.Version}:");
            Print($"  Ihr gebt: {TradeList(ts.MyItems)}{(ts.MyMoney > 0 ? " + " + Money(ts.MyMoney) : "")}{(ts.MyAccepted ? " ✔" : "")}");
            Print($"  Ihr erhaltet: {TradeList(ts.TheirItems)}{(ts.TheirMoney > 0 ? " + " + Money(ts.TheirMoney) : "")}{(ts.TheirAccepted ? " ✔" : "")}");
            Print("  tput <Handelsplatz 1-6> <Taschenplatz> | tput <Handelsplatz> 0 (leeren) | tgold <Kupfer> | tok | tcancel");
            break;
        case S_TradeClosed tcl: Print(tcl.Completed ? "Handel abgeschlossen." : "Handel beendet: " + tcl.Reason); break;
        case S_ChatChannels cc2: Print("Kanäle: " + string.Join(", ", cc2.Joined.Select(j => ((ChatChannel)j) switch { ChatChannel.General => "Gebiet", ChatChannel.Trade => "Handel", _ => "Gruppensuche" }))); break;
        case S_Bank: Print("Bank geöffnet (bank, deposit <Platz>, withdraw <Fach>)."); break;
        case S_MailList ml:
            Print(ml.Mails.Count == 0 ? "Keine Post." : "Post:");
            for (int i = 0; i < ml.Mails.Count; i++)
            {
                var ma = ml.Mails[i];
                Print($"  {i + 1}. von {ma.Sender}: „{ma.Subject}“ {(ma.Money > 0 ? Money(ma.Money) : "")} {string.Join(", ", ma.Items.Select(x => $"{x.Count}× {ItemName(x.ItemId)}"))}{(ma.Cod > 0 ? $" Nachnahme {Money(ma.Cod)}" : "")} ({ma.DaysLeft:0} Tage)");
            }
            break;
        case S_NewMail nm: Print($"Ihr habt neue Post ({nm.Unread})."); break;
        case S_AuctionList al:
            Print($"Auktionshaus: {al.Total} Angebote" + (al.Kind == 1 ? " (eigene)" : al.Kind == 2 ? " (Gebote)" : ""));
            for (int i = 0; i < al.Auctions.Count; i++)
            {
                var x = al.Auctions[i];
                Print($"  {i + 1}. {x.Count}× {ItemName(x.ItemId)} – {x.Seller} – Gebot {Money(x.MinBid)}{(x.Buyout > 0 ? $", Sofortkauf {Money(x.Buyout)}" : "")}{(x.IsMyBid ? " [Höchstgebot]" : "")}");
            }
            break;
        case S_LootRollStart rs: Print($"Würfeln um {ItemName(rs.ItemId)} ({rs.Seconds:0} s): need{(rs.CanNeed ? "" : " (nicht möglich)")} / greed / pass"); break;
        case S_LootRollResult rres: Print($"  Würfe um {ItemName(rres.ItemId)}: {string.Join(", ", rres.Rolls.Select(r => $"{r.Name}{(r.IsBot ? " (KI)" : "")} {r.Choice} {r.Value}"))}"); break;
        case S_TalentResetCost trc: Print($"Talente zurücksetzen kostet {Money(trc.Cost)}. Bestätigen mit: resettalents ja"); break;
        case S_LootResult lr: Print($"  Beute: {string.Join(", ", lr.Items.Select(i => $"{i.Count}× {ItemName(i.ItemId)}"))}{(lr.Money > 0 ? $" {Money(lr.Money)}" : "")}"); break;
        case S_NpcInteraction ni: ShowNpc(ni); break;
        case S_BotCandidates bc:
            Print("KI-Mitspieler verfügbar (einladen mit: bot <Nr>):");
            for (int i = 0; i < bc.Candidates.Count; i++)
            {
                var c = bc.Candidates[i];
                Print($"  {i + 1}. {c.Name} – {content.Races[c.Race].Name} {content.Classes[c.Class].Name}, Stufe {c.Level}, {RoleName(c.Role)} ({c.Personality}) – „{c.Greeting}“");
            }
            break;
        case S_GroupMarkers gm:
            Print("Markierungen: " + (gm.Markers.Count == 0 ? "keine" : string.Join(", ", gm.Markers.Select((m, i) => $"{MarkerName(m)} → {Name(gm.Targets[i])}"))));
            break;
        case S_GroupInviteRequest gi: Print($"{gi.FromName} lädt Euch in eine Gruppe ein. (annehmen: join)"); break;
        case S_DeathState ds when ds.State == DeathState.Dead: Print("Ihr seid gestorben. 'release' lässt den Geist frei."); break;
        case S_DeathState ds when ds.State == DeathState.Ghost: Print($"Ihr seid ein Geist. Lauft zur Leiche ({ds.CorpsePosition}) und schreibt 'reclaim' – oder 'talk' zum Geisterheiler."); break;
        case S_CharacterList cl: ShowChars(); break;
        case S_EnterWorld ew: Print($"Ihr betretet {content.Zones[ew.ZoneId].Name} bei {ew.Position}."); walkTarget = null; break;
    }
};

Print("Eternal Realms – Textclient. 'hilfe' zeigt alle Befehle.");
if (!client.Connect(host, port)) { Print("Verbindung fehlgeschlagen."); return; }
Print($"Verbinde mit {host}:{port} …");
var reader = new Thread(() => { while (true) { var l = Console.ReadLine(); if (l == null) { input.Enqueue("quit"); break; } input.Enqueue(l); } }) { IsBackground = true };
reader.Start();

var lastPing = DateTime.MinValue;
while (true)
{
    client.Poll();
    while (lastNotice < client.Notices.Count) Print("» " + client.Notices[lastNotice++]);
    while (lastChat < client.ChatLog.Count)
    {
        var c = client.ChatLog[lastChat++];
        string ch = c.Channel switch { ChatChannel.Yell => "[schreit] ", ChatChannel.Whisper => "[flüstert] ", ChatChannel.General => "[Gebiet] ", ChatChannel.Trade => "[Handel] ", ChatChannel.LookingForGroup => "[Gruppensuche] ", ChatChannel.System => "[System] ", _ => ChatFormat.ChannelPrefix(c.Channel) };
        string who = c.SenderName == "" ? "" : c.SenderName + (c.SenderIsBot ? " (KI)" : "") + (c.Channel == ChatChannel.Emote ? " " : ": ");
        Print($"{ch}{who}{ChatLinks.Plain(c.Text, content)}");
    }
    UpdateWalking();
    if (client.Connected && (DateTime.UtcNow - lastPing).TotalSeconds > 5) { lastPing = DateTime.UtcNow; client.SendPing(); }
    while (input.TryDequeue(out var line))
    {
        if (line.Trim() == "quit") { client.Send(new C_Logout()); client.Poll(); Thread.Sleep(200); client.Disconnect(); return; }
        try { Command(line.Trim()); } catch (Exception ex) { Print("Fehler: " + ex.Message); }
    }
    Thread.Sleep(30);
}

void UpdateWalking()
{
    var me = client.Me;
    if (me == null || client.Now < nextMoveSend) return;
    nextMoveSend = client.Now + 0.1;
    if (followId != 0 && client.State.Get(followId) is ClientEntity f) walkTarget = f.Position;
    if (walkTarget == null) return;
    var target = walkTarget.Value;
    float dist = Vec3.DistanceXZ(me.Position, target);
    float stopAt = followId != 0 ? 3f : 0.5f;
    if (dist <= stopAt) { if (followId == 0) { walkTarget = null; Print("Angekommen."); } return; }
    float step = Math.Min(dist - stopAt + 0.1f, 7f * 0.1f * (client.State.DeathState == DeathState.Ghost ? 1.25f : 1f));
    var next = Vec3.MoveTowards(me.Position, target, step);
    client.Move(next, Vec3.FacingTowards(me.Position, target));
}

void Command(string line)
{
    if (line.Length == 0) return;
    var parts = line.Split(' ', 2);
    string cmd = parts[0].ToLowerInvariant();
    string rest = parts.Length > 1 ? parts[1] : "";
    string[] a = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    int Int(int i, int def = 0) => a.Length > i && int.TryParse(a[i], out var v) ? v : def;
    var me = client.Me;
    switch (cmd)
    {
        case "hilfe": case "help": Help(); break;
        case "register": client.Login(a[0], a[1], true); break;
        case "login": client.Login(a[0], a[1], false); break;
        case "chars": ShowChars(); break;
        case "races":
            foreach (var r in content.Races.Values) Print($"  {r.Id} ({content.Factions[r.Faction].Name}): {string.Join(", ", r.Classes.Select(c => c.ToString()))}");
            break;
        case "create":
        {
            var race = Enum.Parse<Race>(a[1], true);
            var cls = Enum.Parse<CharClass>(a[2], true);
            var r = content.Races[race];
            var g = a.Length > 3 && a[3].StartsWith("w") ? Gender.Female : Gender.Male;
            client.Send(new C_CreateCharacter { Name = a[0], Race = race, Class = cls, Gender = g, Appearance = new AppearanceData { Height = (r.MinHeight + r.MaxHeight) / 2 } });
            break;
        }
        case "play": client.Send(new C_EnterWorld { CharacterId = client.Characters[Int(0, 1) - 1].Id }); break;
        case "look": case "l": Look(); break;
        case "status": case "st": Status(); break;
        case "target": case "t": client.Target(Int(0)); Print($"Ziel: {Name(Int(0))}"); break;
        case "tab":
        {
            var cur = me?.TargetId ?? 0;
            var next = client.NextEnemyTarget(cur);
            if (next != null) { client.Target(next.Id); me!.TargetId = next.Id; Print($"Ziel: {Describe(next)}"); } else Print("Kein Gegner in der Nähe.");
            break;
        }
        case "attack": case "a": client.Send(new C_AttackStart { TargetId = a.Length > 0 ? Int(0) : me?.TargetId ?? 0 }); break;
        case "stop": client.Send(new C_AttackStop()); walkTarget = null; followId = 0; break;
        case "cast": case "c":
        {
            var id = ResolveAbility(a[0]);
            client.Cast(id, a.Length > 1 ? Int(1) : me?.TargetId ?? 0);
            break;
        }
        case "spells":
            foreach (var kv in client.SpellBook)
            {
                var ab = content.GetAbilityRank(kv.Key, kv.Value);
                if (ab != null) Print($"  {kv.Key,-36} {ab.Name} (Rang {kv.Value}) – {ab.Description}");
            }
            break;
        case "go":
            if (int.TryParse(rest, out var gid) && client.State.Get(gid) is ClientEntity ge) { walkTarget = ge.Position; followId = 0; Print($"Laufe zu {ge.Name}."); }
            else if (a.Length >= 2) { walkTarget = new Vec3(float.Parse(a[0], CultureInfo.InvariantCulture), 0, float.Parse(a[1], CultureInfo.InvariantCulture)); followId = 0; }
            break;
        case "follow": followId = Int(0); Print($"Folge {Name(followId)}."); break;
        case "talk": case "use": client.Send(new C_Interact { EntityId = a.Length > 0 ? Int(0) : me?.TargetId ?? 0 }); break;
        case "loot": client.Send(new C_Loot { CorpseId = a.Length > 0 ? Int(0) : me?.TargetId ?? 0 }); break;
        case "accept": client.Send(new C_QuestAccept { NpcId = client.LastInteraction?.NpcId ?? 0, QuestId = QuestFrom(a[0], client.LastInteraction?.QuestsAvailable) }); break;
        case "turnin": client.Send(new C_QuestTurnIn { NpcId = client.LastInteraction?.NpcId ?? 0, QuestId = QuestFrom(a[0], client.LastInteraction?.QuestsToTurnIn), ChoiceIndex = Int(1, 0) - 1 }); break;
        case "quests": Quests(); break;
        case "abandon": client.Send(new C_QuestAbandon { QuestId = a[0] }); break;
        case "inv": Inventory(); break;
        case "equip": client.Send(new C_EquipItem { BagIndex = Int(0) - 1 }); break;
        case "item": client.Send(new C_UseItem { BagIndex = Int(0) - 1, TargetId = me?.TargetId ?? 0 }); break;
        case "buy": client.Send(new C_BuyItem { NpcId = client.LastInteraction?.NpcId ?? 0, ItemId = client.LastInteraction?.VendorItems[Int(0) - 1] ?? "", Count = Int(1, 1) }); break;
        case "sell": client.Send(new C_SellItem { NpcId = client.LastInteraction?.NpcId ?? 0, BagIndex = Int(0) - 1 }); break;
        case "train":
            if (client.LastInteraction == null) break;
            foreach (var r in client.LastInteraction.Trainable) client.Send(new C_TrainAbility { NpcId = client.LastInteraction.NpcId, AbilityId = r.Split('#')[0] });
            break;
        case "talent": client.Send(new C_LearnTalent { TalentId = a[0] }); break;
        case "talents": Talents(); break;
        case "bots":
        {
            // bots [tank|heiler|dd|fern] [anfänger|normal|erfahren|veteran]
            var search = new C_BotSearch();
            foreach (var w in a)
                switch (w.ToLowerInvariant())
                {
                    case "tank": search.Role = 0; break;
                    case "heiler": search.Role = 1; break;
                    case "dd": case "nah": search.Role = 2; break;
                    case "fern": search.Role = 3; break;
                    case "anfänger": case "anfaenger": search.Difficulty = 0; break;
                    case "normal": search.Difficulty = 1; break;
                    case "erfahren": search.Difficulty = 2; break;
                    case "veteran": search.Difficulty = 3; break;
                }
            client.Send(search);
            break;
        }
        case "fav": client.Send(new C_BotFavorite { EntityId = Int(0), Favorite = a.Length < 2 || a[1] != "aus" }); break;
        case "cmd":
        {
            // cmd <befehl> [botId] – z. B. "cmd folgen", "cmd angriff 12", "cmd kontrolle"
            int order = a.Length == 0 ? 0 : a[0].ToLowerInvariant() switch
            {
                "folgen" => 1, "halten" => 2, "angriff" => 3, "pause" => 5, "passiv" => 6, "verteidigen" => 7, "aggressiv" => 8,
                "status" => 9, "fokus" => 12, "unterbrechen" => 13, "heilen" => 14, "mana" => 15, "verteilen" => 16, "kontrolle" => 17, "rückzug" => 18, "rueckzug" => 18,
                _ => 0,
            };
            if (order == 0) { Print("Befehle: folgen, halten, angriff, pause, passiv, verteidigen, aggressiv, status, fokus, unterbrechen, heilen, mana, verteilen, kontrolle, rückzug"); break; }
            client.Send(new C_BotCommand { Command = (byte)order, BotEntityId = Int(1, 0), TargetId = me?.TargetId ?? 0 });
            break;
        }
        // ---- Taschen und Bank
        case "move": client.Send(new C_MoveItem { From = Int(0) - 1, To = Int(1) - 1 }); break;
        case "split": client.Send(new C_SplitStack { From = Int(0) - 1, Count = Int(1, 1) }); break;
        case "sort": client.Send(new C_SortBags()); break;
        case "destroy": client.Send(new C_DestroyItem { BagIndex = Int(0) - 1 }); break;
        case "bag": client.Send(new C_EquipBag { BagIndex = Int(0) - 1 }); break;
        case "unbag": client.Send(new C_UnequipBag { ContainerSlot = Int(0) - 1 }); break;
        case "bank": ShowBank(); break;
        case "deposit": client.Send(new C_BankDeposit { NpcId = Nearest(NpcRole.Banker), BagIndex = Int(0) - 1 }); break;
        case "withdraw": client.Send(new C_BankWithdraw { NpcId = Nearest(NpcRole.Banker), BankIndex = Int(0) - 1 }); break;
        // ---- Post
        case "mail": client.Send(new C_MailList { ObjectId = Mailbox() }); break;
        case "send":
        {
            // send <Name> <Kupfer> <Betreff …> [| Taschenplatz,Taschenplatz]
            var bar = rest.Split('|');
            var head = bar[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (head.Length < 2) { Print("send <Name> <Kupfer> <Betreff> [| 3,4]"); break; }
            var ms = new C_MailSend { ObjectId = Mailbox(), To = head[0], Money = long.TryParse(head[1], out var mny) ? mny : 0, Subject = string.Join(" ", head.Skip(2)) };
            if (bar.Length > 1) foreach (var x in bar[1].Split(',', StringSplitOptions.RemoveEmptyEntries)) if (int.TryParse(x.Trim(), out var bi)) ms.BagIndices.Add(bi - 1);
            client.Send(ms);
            break;
        }
        case "take" or "delmail" or "return" when Int(0) < 1 || Int(0) > client.Mail.Count:
            Print("Zuerst die Post öffnen (mail) und eine gültige Nummer angeben."); break;
        case "take": client.Send(new C_MailTake { ObjectId = Mailbox(), MailId = client.Mail[Int(0) - 1].Id, Index = 255 }); break;
        case "delmail": client.Send(new C_MailDelete { ObjectId = Mailbox(), MailId = client.Mail[Int(0) - 1].Id }); break;
        case "return": client.Send(new C_MailReturn { ObjectId = Mailbox(), MailId = client.Mail[Int(0) - 1].Id }); break;
        // ---- Auktionshaus
        case "ah": client.Send(new C_AuctionSearch { NpcId = Nearest(NpcRole.Auctioneer), Query = rest, Page = 0 }); break;
        case "ahmine": client.Send(new C_AuctionMine { NpcId = Nearest(NpcRole.Auctioneer), Bids = a.Length > 0 && a[0] == "gebote" }); break;
        case "ahsell": client.Send(new C_AuctionPost { NpcId = Nearest(NpcRole.Auctioneer), BagIndex = Int(0) - 1, Count = Int(1, 1), StartBid = Int(2, 100), Buyout = Int(3, 0), DurationHours = Int(4, 24) }); break;
        case "ahbid" or "ahbuy" or "ahcancel" when client.Auctions == null || Int(0) < 1 || Int(0) > client.Auctions.Auctions.Count:
            Print("Zuerst suchen (ah …) und eine gültige Nummer angeben."); break;
        case "ahbid": client.Send(new C_AuctionBid { NpcId = Nearest(NpcRole.Auctioneer), AuctionId = client.Auctions!.Auctions[Int(0) - 1].Id, Amount = Int(1, 0) }); break;
        case "ahbuy":
        {
            var it = client.Auctions!.Auctions[Int(0) - 1];
            client.Send(new C_AuctionBid { NpcId = Nearest(NpcRole.Auctioneer), AuctionId = it.Id, Amount = it.Buyout });
            break;
        }
        case "ahcancel": client.Send(new C_AuctionCancel { NpcId = Nearest(NpcRole.Auctioneer), AuctionId = client.Auctions!.Auctions[Int(0) - 1].Id }); break;
        // ---- Beute und Talente
        case "need": case "greed": case "pass":
            foreach (var r in client.OpenRolls.Keys.ToList())
                client.Roll(r, cmd == "need" ? RollChoice.Need : cmd == "greed" ? RollChoice.Greed : RollChoice.Pass);
            break;
        case "lootrule":
        {
            var method = (a.Length > 0 ? a[0] : "") switch { "frei" => LootMethod.FreeForAll, "reihum" => LootMethod.RoundRobin, "bedarf" => LootMethod.NeedBeforeGreed, "meister" => LootMethod.MasterLooter, _ => LootMethod.GroupLoot };
            client.Send(new C_SetLootMethod { Method = method, Threshold = ItemQuality.Uncommon, MasterLooterId = client.State.MyId });
            break;
        }
        case "resettalents": client.Send(new C_ResetTalents { NpcId = client.LastInteraction?.NpcId ?? 0, Confirm = a.Length > 0 && a[0] == "ja" }); break;
        case "mount": client.Send(new C_Mount { MountId = a.Length > 0 ? a[0] : "" }); break;
        case "dismount": client.Send(new C_Dismount()); break;
        case "riding": client.Send(new C_LearnRiding { NpcId = Nearest(NpcRole.RidingTrainer) }); break;
        case "mounts":
            Print($"Reitfertigkeit {client.MountJournal.RidingSkill}; Reittiere: " + (client.MountJournal.Mounts.Count == 0 ? "keine" :
                  string.Join(", ", client.MountJournal.Mounts.Select(m => content.Mounts.TryGetValue(m, out var md) ? $"{md.Name} [{m}]" : m))));
            break;
        // ---- Gilde und Freunde
        case "gcreate": client.Send(new C_GuildCreate { Text = rest }); break;
        case "ginvite": client.Send(new C_GuildInvite { Text = rest }); break;
        case "gjoin": client.Send(new C_GuildRespond { Accept = true }); break;
        case "gdecline": client.Send(new C_GuildRespond { Accept = false }); break;
        case "gleave": client.Send(new C_GuildLeave()); break;
        case "gdisband": client.Send(new C_GuildDisband()); break;
        case "gmotd": client.Send(new C_GuildMotd { Text = rest }); break;
        case "gkick": client.Send(new C_GuildKick { CharacterId = MemberId(a.Length > 0 ? a[0] : "") }); break;
        case "grank": client.Send(new C_GuildSetRank { CharacterId = MemberId(a.Length > 0 ? a[0] : ""), Rank = Int(1, 3) }); break;
        case "guild": client.Send(new C_GuildRoster()); break;
        case "gu": client.Say(rest, ChatChannel.Guild); break;
        case "friends": client.Send(new C_FriendList()); break;
        case "friend": client.Send(new C_FriendAdd { Text = rest }); break;
        case "unfriend": client.Send(new C_FriendRemove { CharacterId = client.Friends.Friends.FirstOrDefault(f => f.Name.Equals(rest, StringComparison.OrdinalIgnoreCase))?.CharacterId ?? 0 }); break;
        case "ignore": client.Send(new C_IgnoreAdd { Text = rest }); break;
        case "unignore":
        {
            int ix = client.Friends.Ignored.FindIndex(n => n.Equals(rest, StringComparison.OrdinalIgnoreCase));
            if (ix >= 0) client.Send(new C_IgnoreRemove { CharacterId = client.Friends.IgnoredIds[ix] });
            break;
        }
        case "report": client.Send(new C_ReportPlayer { Name = a.Length > 0 ? a[0] : "", Reason = "Spielermeldung", Text = rest.Length > 0 && a.Length > 0 ? rest.Substring(a[0].Length).Trim() : "" }); break;
        case "share": client.Send(new C_ShareLocation { Share = !(a.Length > 0 && a[0] == "aus") }); break;
        case "summon": client.Send(new C_InviteFavorite { CharacterId = client.Friends.Companions.FirstOrDefault(f => f.Name.Equals(rest, StringComparison.OrdinalIgnoreCase))?.CharacterId ?? 0 }); break;
        case "instances": client.Send(new C_InstanceInfo()); break;
        case "resetinstances": client.Send(new C_ResetInstances()); break;
        // ---- Gruppenfinder und Gruppenwerkzeuge
        case "lfg":
        {
            // lfg <aktivität|liste> [tank,heiler,dd,fern] [nurmenschen|bevorzugt|ki]
            if (a.Length == 0 || a[0] == "liste") { client.Send(new C_FinderActivities()); break; }
            var join = new C_FinderJoin { Activities = a[0].Split(',').ToList() };
            if (a.Length > 1)
                foreach (var r in a[1].Split(','))
                    join.Roles.Add(r switch { "tank" => (byte)0, "heiler" => (byte)1, "fern" => (byte)3, _ => (byte)2 });
            if (a.Length > 2) join.Mode = a[2] switch { "nurmenschen" => FinderAiMode.HumansOnly, "ki" => FinderAiMode.FillWithAi, _ => FinderAiMode.PreferHumans };
            client.Send(join);
            break;
        }
        case "lfgleave": client.Send(new C_FinderLeave()); break;
        case "accept-group": if (client.FinderProposal != null) client.Send(new C_FinderAccept { ProposalId = client.FinderProposal.ProposalId, Accept = true }); break;
        case "decline-group": if (client.FinderProposal != null) client.Send(new C_FinderAccept { ProposalId = client.FinderProposal.ProposalId, Accept = false }); break;
        case "readycheck": client.Send(new C_ReadyCheck()); break;
        case "ready": client.Send(new C_ReadyResponse { Ready = !(a.Length > 0 && a[0] == "nein") }); break;
        case "votekick": client.Send(new C_VoteKick { TargetId = Int(0), Reason = rest.Length > a[0].Length ? rest.Substring(a[0].Length).Trim() : "" }); break;
        case "vote": if (client.VoteKick != null) client.Send(new C_VoteKickResponse { VoteId = client.VoteKick.VoteId, Yes = !(a.Length > 0 && a[0] == "nein") }); break;
        case "role": client.Send(new C_SetRole { Role = (a.Length > 0 ? a[0] : "") switch { "tank" => Role.Tank, "heiler" => Role.Healer, "fern" => Role.RangedDps, _ => Role.MeleeDps } }); break;
        case "botrole": client.Send(new C_SetBotRole { BotEntityId = Int(0), Role = (a.Length > 1 ? a[1] : "") switch { "tank" => Role.Tank, "heiler" => Role.Healer, "fern" => Role.RangedDps, _ => Role.MeleeDps } }); break;
        case "raid": client.Send(new C_ConvertToRaid()); break;
        case "subgroup": client.Send(new C_SetSubgroup { EntityId = Int(0), Subgroup = (byte)Int(1, 1) }); break;
        // ---- Berufe
        case "profs": ShowProfessions(); break;
        case "learnprof": client.Send(new C_LearnProfession { NpcId = client.LastInteraction?.NpcId ?? 0 }); break;
        case "rankprof": client.Send(new C_TrainProfessionRank { NpcId = client.LastInteraction?.NpcId ?? 0 }); break;
        case "learnrecipe": client.Send(new C_LearnRecipe { NpcId = client.LastInteraction?.NpcId ?? 0, RecipeId = a.Length > 0 ? a[0] : "" }); break;
        case "unlearn": client.Send(new C_UnlearnProfession { Profession = a.Length > 0 ? a[0] : "" }); break;
        case "craft": client.Send(new C_Craft { RecipeId = a.Length > 0 ? a[0] : "", Count = Int(1, 1) }); break;
        case "skin": client.Send(new C_Skin { CorpseId = a.Length > 0 ? Int(0) : me?.TargetId ?? 0 }); break;
        case "disenchant": client.Send(new C_Disenchant { BagIndex = Int(0) - 1 }); break;
        case "mark": client.Send(new C_SetMarker { Marker = (byte)Int(0, 8), TargetId = a.Length > 1 ? Int(1) : me?.TargetId ?? 0 }); break;
        case "bot": client.Send(new C_BotInvite { CandidateId = client.BotCandidates[Int(0) - 1].CandidateId }); break;
        case "dismiss": client.Send(new C_BotDismiss { EntityId = Int(0) }); break;
        case "group": Group(); break;
        case "invite": client.Send(new C_GroupInvite { Name = rest }); break;
        case "join": client.Send(new C_GroupAccept { Accept = true }); break;
        case "leave": client.Send(new C_GroupLeave()); break;
        case "p": client.Say(rest, ChatChannel.Party); break;
        case "s": client.Say(rest); break;
        case "y": client.Say(rest, ChatChannel.Yell); break;
        case "g": client.Say(Links(rest), ChatChannel.General); break;
        case "h": client.Say(Links(rest), ChatChannel.Trade); break;
        case "suche": client.Say(Links(rest), ChatChannel.LookingForGroup); break;
        case "e":
            if (a.Length > 0 && content.EmoteCommands.TryGetValue(a[0].TrimStart('/').ToLowerInvariant(), out var emo))
                client.Send(new C_Emote { EmoteId = emo.Id, TargetId = client.Me?.TargetId ?? 0 });
            else client.Say(rest, ChatChannel.Emote);
            break;
        case "emotes": Print("Emotes: " + string.Join(", ", content.Emotes.Values.Select(x => x.Commands[0]))); break;
        case "perf":
        {
            var sp = client.ServerPerf;
            Print($"Ping {client.Ping} ms – Server-Takt Ø {sp.TickMs:0.00} ms, 99 % {sp.TickP99Ms:0.00} ms – {sp.Players} Spieler/KI, {sp.Entities} Einheiten");
            break;
        }
        case "kanal":
        {
            var chan = (a.Length > 0 ? a[0] : "").ToLowerInvariant() switch { "gebiet" => ChatChannel.General, "handel" => ChatChannel.Trade, "suche" or "gruppensuche" => ChatChannel.LookingForGroup, _ => (ChatChannel?)null };
            if (chan == null) { Print("kanal gebiet|handel|suche an|aus"); break; }
            client.Send(new C_ChatJoin { Channel = chan.Value, Join = !(a.Length > 1 && a[1] == "aus") });
            break;
        }
        case "trade":
        {
            var target = int.TryParse(rest, out var tid) ? client.State.Get(tid) : client.State.Entities.Values.FirstOrDefault(e => e.Name.Equals(rest, StringComparison.OrdinalIgnoreCase));
            if (target == null) { Print("Wen? trade <id|Name>"); break; }
            client.Send(new C_TradeRequest { TargetId = target.Id });
            break;
        }
        case "tradeyes": client.Send(new C_TradeRespond { Accept = true }); break;
        case "tradeno": client.Send(new C_TradeRespond { Accept = false }); break;
        case "tput": client.Send(new C_TradeSetItem { Slot = Int(0, 1) - 1, BagIndex = Int(1, 0) - 1 }); break;
        case "tgold": client.Send(new C_TradeSetMoney { Money = Int(0, 0) }); break;
        case "tok": client.TradeAccept(true); break;
        case "tcancel": client.Send(new C_TradeCancel()); break;
        case "w": client.Send(new C_Chat { Channel = ChatChannel.Whisper, WhisperTo = a[0], Text = rest.Substring(a[0].Length).Trim() }); break;
        case "release": client.Send(new C_ReleaseSpirit()); break;
        case "reclaim": client.Send(new C_ReclaimCorpse()); break;
        case "rez": client.Send(new C_SpiritHealerResurrect { NpcId = client.LastInteraction?.NpcId ?? 0 }); break;
        case "fly": client.Send(new C_TakeFlight { NpcId = client.LastInteraction?.NpcId ?? 0, Destination = client.LastInteraction?.FlightDestinations[Int(0) - 1] ?? "" }); break;
        case "ping": client.SendPing(); Print($"Latenz: {client.Ping} ms"); break;
        default:
            if (line.StartsWith("#") || line.StartsWith(".")) client.Say(line); else Print("Unbekannter Befehl. 'hilfe' zeigt alle Befehle.");
            break;
    }
}

string ResolveAbility(string s)
{
    if (client.SpellBook.ContainsKey(s)) return s;
    var match = client.SpellBook.Keys.FirstOrDefault(k => content.AbilityRanks[k][0].Name.StartsWith(s, StringComparison.OrdinalIgnoreCase) || k.EndsWith(s, StringComparison.OrdinalIgnoreCase));
    return match ?? s;
}

string QuestFrom(string s, List<string>? list) => int.TryParse(s, out var n) && list != null && n >= 1 && n <= list.Count ? list[n - 1] : s;

void Look()
{
    var me = client.Me;
    if (me == null) return;
    var list = client.State.Entities.Values.Where(e => e.Id != me.Id).OrderBy(e => Vec3.DistanceXZ(e.Position, me.Position)).Take(25);
    foreach (var e in list) Print("  " + Describe(e));
}

string Describe(ClientEntity e)
{
    var me = client.Me!;
    string marker = e.Marker switch { QuestMarker.Available => " [!]", QuestMarker.ReadyToTurnIn => " [?]", QuestMarker.Incomplete => " [?grau]", QuestMarker.AvailableLowLevel => " [!grau]", _ => "" };
    string kind = e.Kind switch
    {
        EntityKind.Player => e.IsBot ? $"KI {content.Classes[e.Class].Name}" : $"Spieler {content.Classes[e.Class].Name}",
        EntityKind.GameObject => "Objekt",
        EntityKind.Npc => e.Roles != NpcRole.None ? e.Roles.ToString() : "NPC",
        EntityKind.Pet => "Begleiter",
        _ => (e.Flags & UnitFlags.Elite) != 0 ? "Elite" : (e.Flags & UnitFlags.Rare) != 0 ? "Selten" : e.Faction == Faction.Neutral ? "neutral" : "feindlich",
    };
    string hp = e.Kind == EntityKind.GameObject ? "" : $" {e.Health}/{e.MaxHealth}";
    return $"#{e.Id,-5} {e.Name}{marker} ({kind}, St. {e.Level}){hp}{(e.IsDead ? " (tot)" : "")} – {Vec3.DistanceXZ(e.Position, me.Position):0} m";
}

void Status()
{
    var s = client.State.Stats; var me = client.Me;
    if (s == null || me == null) return;
    Print($"Stufe {s.Level} – EP {s.Xp}/{s.XpToNext} (Ruhe {s.RestedXp}) – Geld {Money(s.Money)}");
    Print($"Leben {me.Health}/{me.MaxHealth}  {me.PowerType} {me.Power}/{me.MaxPower}  Position {me.Position}");
    Print($"Stärke {s.Strength:0} Bew. {s.Agility:0} Ausd. {s.Stamina:0} Int. {s.Intellect:0} Will. {s.Spirit:0} – Rüstung {s.Armor} AK {s.AttackPower} ZM {s.SpellPower} – Krit {s.MeleeCrit:0.0}%/{s.SpellCrit:0.0}%");
    if (client.State.World != null) Print($"{(int)client.State.World.GameHour:00}:{(int)(client.State.World.GameHour % 1 * 60):00} Uhr, Wetter: {client.State.World.Weather}");
}

void Quests()
{
    var log = client.State.QuestLog;
    if (log == null || log.Quests.Count == 0) { Print("Keine aktiven Quests."); return; }
    foreach (var q in log.Quests)
    {
        var def = content.Quests[q.QuestId];
        Print($"  {def.Title} (Stufe {def.Level}) – {(q.State == QuestState.Complete ? "ABGESCHLOSSEN" : q.State == QuestState.Failed ? "FEHLGESCHLAGEN" : "aktiv")}");
        for (int i = 0; i < def.Objectives.Count; i++) Print($"     {def.Objectives[i].Description}: {q.Counts[i]}/{def.Objectives[i].Count}");
    }
}

void Inventory()
{
    var inv = client.State.Inventory;
    if (inv == null) return;
    Print($"Geld: {Money(inv.Money)}");
    for (int i = 0; i < inv.Equipment.Count; i++) if (inv.Equipment[i].ItemId != "") Print($"  angelegt {(EquipSlot)i,-10} {ItemName(inv.Equipment[i].ItemId)}");
    for (int i = 0; i < inv.Containers.Count; i++) if (inv.Containers[i].ItemId != "") Print($"  Tasche {i + 1}: {ItemName(inv.Containers[i].ItemId)}");
    Print($"  Plätze: {inv.Bag.Count(s => s.ItemId != "")}/{inv.BagSize}");
    for (int i = 0; i < inv.Bag.Count; i++) if (inv.Bag[i].ItemId != "") Print($"  {i + 1,2}. {inv.Bag[i].Count}× {ItemName(inv.Bag[i].ItemId)}");
}

int Nearest(NpcRole role)
{
    var me = client.Me;
    if (me == null) return 0;
    return client.State.Entities.Values.Where(e => (e.Roles & role) != 0).OrderBy(e => Vec3.DistanceXZ(e.Position, me.Position)).FirstOrDefault()?.Id ?? 0;
}

long MemberId(string name) => client.Guild.Members.FirstOrDefault(m => m.Name.Equals(name, StringComparison.OrdinalIgnoreCase))?.CharacterId ?? 0;

void ShowProfessions()
{
    if (client.Professions.Count == 0) { Print("Ihr beherrscht keinen Beruf."); return; }
    foreach (var pr in client.Professions)
    {
        Print($"{content.Professions[pr.Id].Name}: {pr.Skill}/{pr.MaxSkill}");
        foreach (var r in pr.Recipes)
            if (content.Recipes.TryGetValue(r, out var rd))
            {
                string col = pr.Skill < rd.Yellow ? "orange" : pr.Skill < rd.Green ? "gelb" : pr.Skill < rd.Gray ? "grün" : "grau";
                Print($"  {r} – {rd.Name} [{col}] – " + string.Join(", ", rd.Reagents.Select(g => $"{g.Count}× {ItemName(g.Item)}")) + (rd.Station != null ? $" (an: {rd.Station})" : ""));
            }
    }
}

int Mailbox()
{
    var me = client.Me;
    if (me == null) return 0;
    return client.State.Entities.Values.Where(e => e.TemplateId == "obj_mailbox").OrderBy(e => Vec3.DistanceXZ(e.Position, me.Position)).FirstOrDefault()?.Id ?? 0;
}

void ShowBank()
{
    var b = client.Bank;
    if (b == null) { Print("Sprecht zuerst einen Bankier an."); return; }
    for (int i = 0; i < b.Slots.Count; i++) if (b.Slots[i].ItemId != "") Print($"  Fach {i + 1,2}: {b.Slots[i].Count}× {ItemName(b.Slots[i].ItemId)}");
}

void Talents()
{
    var me = client.Me;
    var cls = me?.Class ?? CharClass.None;
    if (!content.Classes.TryGetValue(cls, out var cd)) return;
    var mine = client.State.Talents;
    Print($"Freie Talentpunkte: {mine?.FreePoints ?? 0}");
    foreach (var treeId in cd.TalentTrees)
    {
        var tree = content.TalentTrees[treeId];
        Print($"  {tree.Name}: {tree.Description}");
        foreach (var t in tree.Talents)
        {
            int idx = mine?.TalentIds.IndexOf(t.Id) ?? -1;
            int rank = idx >= 0 ? mine!.Ranks[idx] : 0;
            Print($"     [{t.Tier}] {t.Id,-26} {t.Name} {rank}/{t.MaxRank} – {t.Description}");
        }
    }
}

void Group()
{
    foreach (var m in client.Group) Print($"  #{m.EntityId} {m.Name}{(m.IsBot ? " (KI, " + RoleName(m.Role) + ")" : "")} St. {m.Level} {content.Classes[m.Class].Name} {m.Health}/{m.MaxHealth}{(m.EntityId == client.GroupLeaderId ? " ★" : "")}");
}

void ShowChars()
{
    if (client.Characters.Count == 0) { Print("Noch keine Charaktere. Erschafft einen: create <Name> <Volk> <Klasse> [w] – Völker und Klassen: races"); return; }
    for (int i = 0; i < client.Characters.Count; i++)
    {
        var c = client.Characters[i];
        Print($"  {i + 1}. {c.Name} – Stufe {c.Level} {content.Races[c.Race].Name} {content.Classes[c.Class].Name} ({content.Zones[c.ZoneId].Name})");
    }
    Print("Spielen mit: play <Nr>");
}

void ShowNpc(S_NpcInteraction ni)
{
    var npc = content.Creatures[ni.TemplateId];
    Print($"{npc.Name}{(npc.Title != "" ? " <" + npc.Title + ">" : "")}: {npc.Gossip}");
    for (int i = 0; i < ni.QuestsAvailable.Count; i++)
    {
        var q = content.Quests[ni.QuestsAvailable[i]];
        Print($"  [!] {i + 1}. {q.Title} (Stufe {q.Level}): {q.Text}");
        Print($"       Ziel: {q.ObjectivesText}");
        if (q.RewardChoices.Count > 0) Print($"       Belohnung nach Wahl: {string.Join(" | ", q.RewardChoices.Select(c => ItemName(c.Item)))}");
    }
    for (int i = 0; i < ni.QuestsToTurnIn.Count; i++)
    {
        var q = content.Quests[ni.QuestsToTurnIn[i]];
        Print($"  [?] {i + 1}. {q.Title}: {q.CompletionText}  (abgeben: turnin {i + 1}{(q.RewardChoices.Count > 0 ? " <Belohnung 1-" + q.RewardChoices.Count + ">" : "")})");
    }
    foreach (var id in ni.QuestsInProgress) Print($"  [ ] {content.Quests[id].Title}: {content.Quests[id].ProgressText}");
    for (int i = 0; i < ni.VendorItems.Count; i++) Print($"  Ware {i + 1}. {ItemName(ni.VendorItems[i])} – {Money(content.Items[ni.VendorItems[i]].BuyPrice)}");
    if (ni.Trainable.Count > 0) Print($"  Lernbar ('train' lernt alles): {string.Join(", ", ni.Trainable.Select(r => content.Abilities[r].Name + " " + content.Abilities[r].Rank + " (" + Money(content.Abilities[r].TrainingCost) + ")"))}");
    for (int i = 0; i < ni.FlightDestinations.Count; i++) Print($"  Flug {i + 1}. {content.TravelNodes[ni.FlightDestinations[i]].Name} (fly {i + 1})");
    if (ni.CanResurrect) Print("  Der Geisterheiler kann Euch wiederbeleben ('rez') – gegen Haltbarkeit und Wiederbelebungsschwäche.");
}

bool IsGroup(int id) => client.Group.Any(m => m.EntityId == id);
string Name(int id) => client.State.Get(id)?.Name ?? (id == 0 ? "?" : "#" + id);
string TradeList(List<ItemStackData> items)
{
    var l = items.Where(i => i.ItemId != "").Select(i => $"{i.Count}× {ItemName(i.ItemId)}").ToList();
    return l.Count == 0 ? "nichts" : string.Join(", ", l);
}

/// <summary>"#3" im Text wird zum Link auf den Gegenstand in Taschenplatz 3.</summary>
string Links(string text) => System.Text.RegularExpressions.Regex.Replace(text, @"#(\d+)", m =>
{
    int i = int.Parse(m.Groups[1].Value) - 1;
    var bag = client.State.Inventory?.Bag;
    return bag != null && i >= 0 && i < bag.Count && bag[i].ItemId != "" ? ChatLinks.Item(bag[i].ItemId) : m.Value;
});

string ItemName(string id) => content.Items.TryGetValue(id, out var i) ? i.Name : id;
string RoleName(Role r) => r switch { Role.Tank => "Tank", Role.Healer => "Heiler", Role.MeleeDps => "Nahkampf", _ => "Fernkampf" };
string MarkerName(int m) => m switch { 8 => "Totenkopf", 7 => "Kreuz", 6 => "Quadrat", 5 => "Mond", 4 => "Dreieck", 3 => "Raute", 2 => "Kreis", 1 => "Stern", _ => "?" };
string Money(long c) => $"{c / 10000}g {c / 100 % 100}s {c % 100}k";
void Print(string s) => Console.WriteLine(s);

void Help() => Print(@"Befehle:
  register <konto> <passwort> | login <konto> <passwort> | races | create <Name> <Volk> <Klasse> [w] | chars | play <Nr>
  look | status | tab | target <id> | attack [id] | stop | cast <fähigkeit|name> [id] | spells
  go <id> | go <x> <z> | follow <id> | talk [id] | loot [id] | accept <Nr> | turnin <Nr> [Belohnung] | quests | abandon <quest>
  inv | equip <Platz> | item <Platz> | buy <Nr> [Anzahl] | sell <Platz> | train | talents | talent <id>
  bots [tank|heiler|dd|fern] [anfänger|normal|erfahren|veteran] | bot <Nr> | dismiss <id> | fav <id> [aus]
  move <von> <nach> | split <Platz> <Anzahl> | sort | destroy <Platz> | bag <Platz> | unbag <1-4>
  bank | deposit <Platz> | withdraw <Fach>   (beim Bankier)
  mail | send <Name> <Kupfer> <Betreff> [| 3,4] | take <Nr> | delmail <Nr> | return <Nr>   (am Briefkasten)
  ah [Suchtext] | ahmine [gebote] | ahsell <Platz> <Anzahl> <Gebot> [Sofortkauf] [12|24|48] | ahbid <Nr> <Betrag> | ahbuy <Nr> | ahcancel <Nr>
  gcreate <Name> | ginvite <Name> | gjoin | gdecline | gleave | gkick <Name> | grank <Name> <0-4> | gmotd <Text> | guild | gu <Text> | gdisband
  friends | friend <Name> | unfriend <Name> | ignore <Name> | unignore <Name> | report <Name> <Text> | share [aus] | summon <KI-Begleiter>
  lfg liste | lfg <id> [tank,heiler,dd,fern] [nurmenschen|bevorzugt|ki] | lfgleave | accept-group | decline-group
  readycheck | ready [nein] | votekick <id> <Grund> | vote [nein] | role <tank|heiler|dd|fern> | botrole <id> <rolle> | raid | subgroup <id> <1-8>
  instances | resetinstances
  profs | learnprof | rankprof | learnrecipe <id> (beim Lehrer) | unlearn <beruf> | craft <rezept> [anzahl] | skin [id] | disenchant <Platz>
  riding (beim Stallmeister) | mounts | mount [id] | dismount
  need | greed | pass | lootrule [frei|reihum|gruppe|bedarf|meister] | resettalents [ja] | /würfeln
  cmd <befehl> [botId] | mark <1-8> [id]   (8 = Totenkopf, 7 = Kreuz, 5 = Mond/Kontrolle; 0 als Ziel löscht)
  Chatbefehle: p Tank, greif den Boss an. | p Heiler, bitte Mana auffüllen. | p Magier, kontrolliere den linken Gegner. | p Alle zurückziehen.
  group | invite <Name> | join | leave
  g <Text> Gebiet | h <Text> Handel (nur in Städten) | suche <Text> Gruppensuche | kanal gebiet|handel|suche an|aus | #3 im Text verlinkt Taschenplatz 3
  e <Emote> [mit Ziel: zuerst 'target'] z. B. e winken | e <freier Text> | emotes | perf
  trade <id|Name> | tradeyes | tradeno | tput <1-6> <Taschenplatz> | tgold <Kupfer> | tok | tcancel
  s <Text> | p <Text> (Gruppe – Bots verstehen z. B. 'folgt mir', 'wartet', 'Angriff', 'Wir gehen in den Dungeon') | y | g | w <Name> <Text>
  release | reclaim | rez | fly <Nr> | ping | quit   (.pvp schaltet PvP um; #-Befehle nur mit ER_DEV_COMMANDS)");

static string FindContent()
{
    if (Environment.GetEnvironmentVariable("ER_CONTENT") is { Length: > 0 } env) return env;
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "content"))) dir = dir.Parent;
    return Path.Combine(dir?.FullName ?? ".", "content");
}
