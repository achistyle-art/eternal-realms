using System.Collections.Concurrent;
using System.Globalization;
using EternalRealms.Shared;
using EternalRealms.Shared.Client;
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

while (true)
{
    client.Poll();
    while (lastNotice < client.Notices.Count) Print("» " + client.Notices[lastNotice++]);
    while (lastChat < client.ChatLog.Count)
    {
        var c = client.ChatLog[lastChat++];
        string ch = c.Channel switch { ChatChannel.Party => "[Gruppe] ", ChatChannel.Yell => "[schreit] ", ChatChannel.Whisper => "[flüstert] ", ChatChannel.General => "[Allgemein] ", ChatChannel.System => "[System] ", _ => "" };
        Print($"{ch}{(c.SenderName != "" ? c.SenderName + (c.SenderIsBot ? " (KI)" : "") + ": " : "")}{c.Text}");
    }
    UpdateWalking();
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
        case "bots": client.Send(new C_BotSearch()); break;
        case "bot": client.Send(new C_BotInvite { CandidateId = client.BotCandidates[Int(0) - 1].CandidateId }); break;
        case "dismiss": client.Send(new C_BotDismiss { EntityId = Int(0) }); break;
        case "group": Group(); break;
        case "invite": client.Send(new C_GroupInvite { Name = rest }); break;
        case "join": client.Send(new C_GroupAccept { Accept = true }); break;
        case "leave": client.Send(new C_GroupLeave()); break;
        case "p": client.Say(rest, ChatChannel.Party); break;
        case "s": client.Say(rest); break;
        case "y": client.Say(rest, ChatChannel.Yell); break;
        case "g": client.Say(rest, ChatChannel.General); break;
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
    for (int i = 0; i < inv.Bag.Count; i++) if (inv.Bag[i].ItemId != "") Print($"  {i + 1,2}. {inv.Bag[i].Count}× {ItemName(inv.Bag[i].ItemId)}");
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
string ItemName(string id) => content.Items.TryGetValue(id, out var i) ? i.Name : id;
string RoleName(Role r) => r switch { Role.Tank => "Tank", Role.Healer => "Heiler", Role.MeleeDps => "Nahkampf", _ => "Fernkampf" };
string Money(long c) => $"{c / 10000}g {c / 100 % 100}s {c % 100}k";
void Print(string s) => Console.WriteLine(s);

void Help() => Print(@"Befehle:
  register <konto> <passwort> | login <konto> <passwort> | races | create <Name> <Volk> <Klasse> [w] | chars | play <Nr>
  look | status | tab | target <id> | attack [id] | stop | cast <fähigkeit|name> [id] | spells
  go <id> | go <x> <z> | follow <id> | talk [id] | loot [id] | accept <Nr> | turnin <Nr> [Belohnung] | quests | abandon <quest>
  inv | equip <Platz> | item <Platz> | buy <Nr> [Anzahl] | sell <Platz> | train | talents | talent <id>
  bots | bot <Nr> | dismiss <id> | group | invite <Name> | join | leave
  s <Text> | p <Text> (Gruppe – Bots verstehen z. B. 'folgt mir', 'wartet', 'Angriff', 'Wir gehen in den Dungeon') | y | g | w <Name> <Text>
  release | reclaim | rez | fly <Nr> | ping | quit   (.pvp schaltet PvP um; #-Befehle nur mit ER_DEV_COMMANDS)");

static string FindContent()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "content"))) dir = dir.Parent;
    return Path.Combine(dir?.FullName ?? ".", "content");
}
