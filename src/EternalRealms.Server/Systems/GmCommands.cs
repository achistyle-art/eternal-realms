using System.Globalization;
using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>Wer einen Spielleiter-Befehl ausführt: ein Spielleiter im Spiel oder die Serverkonsole (volle Rechte).</summary>
    public sealed class GmContext
    {
        public Player? Player { get; init; }
        public long? AccountId { get; init; }
        public string Name { get; init; } = "";
        public int Level { get; init; }
        public string Ip { get; init; } = "";
        public Action<string> Reply { get; init; } = _ => { };

        public static GmContext ForPlayer(Player p, Account a) => new()
        {
            Player = p, AccountId = a.Id, Name = $"{p.Name} ({a.Username})", Level = a.GmLevel, Ip = p.Connection?.RemoteIp ?? "",
            Reply = text => p.Send(new S_Chat { Channel = ChatChannel.System, Text = text }),
        };

        public static GmContext Console(Action<string> reply) => new() { Name = "Konsole", Level = GmLevels.Admin, Reply = reply };
    }

    /// <summary>
    /// Werkzeuge der Spielleitung (Kapitel 50) als Chatbefehle mit Punkt (z. B. „.kick Name Grund“) und über die Serverkonsole.
    /// Spielleiter (Stufe 1) moderieren, Admins (Stufe 2) dürfen zusätzlich Gold, Gegenstände und Rechte vergeben.
    /// Jede verändernde Handlung landet im Prüfprotokoll; niemand kann gegen Gleich- oder Höhergestellte vorgehen.
    /// </summary>
    public sealed class GmCommands
    {
        private readonly GameWorld _w;
        public GmCommands(GameWorld world) { _w = world; }

        private sealed record Command(string Name, int Level, string Usage, Action<GmContext, string[]> Run);

        private Dictionary<string, Command>? _commands;
        private Dictionary<string, Command> Commands => _commands ??= Build();

        private Dictionary<string, Command> Build()
        {
            var list = new[]
            {
                new Command("hilfe", 1, ".hilfe – diese Übersicht", Help),
                new Command("wer", 1, ".wer [Filter] – Spieler online", Who),
                new Command("info", 1, ".info Name – Charakter und Konto", Info),
                new Command("ansage", 1, ".ansage Text – Nachricht an alle", Announce),
                new Command("kick", 1, ".kick Name [Grund] – Verbindung trennen", Kick),
                new Command("stumm", 1, ".stumm Name Minuten [Grund] – Chat sperren", Mute),
                new Command("laut", 1, ".laut Name – Stummschaltung aufheben", Unmute),
                new Command("bann", 1, ".bann Name Minuten|dauerhaft Grund – Konto sperren", Ban),
                new Command("entbann", 1, ".entbann Name – Sperre aufheben", Unban),
                new Command("gehezu", 1, ".gehezu Name – zum Spieler reisen", Goto),
                new Command("hol", 1, ".hol Name – Spieler herbeirufen", Summon),
                new Command("tele", 1, ".tele Zone [x z] – an einen Ort reisen", Tele),
                new Command("auffaellig", 1, ".auffaellig – Meldungen der Anti-Cheat-Erkennung", Flags),
                new Command("meldungen", 1, ".meldungen – offene Spielermeldungen", Reports),
                new Command("erledigt", 1, ".erledigt Nummer – Meldung als bearbeitet markieren", ReportDone),
                new Command("protokoll", 1, ".protokoll [Aktion|Name] – letzte Einträge im Prüfprotokoll", AuditLog),
                new Command("wartung", 1, ".wartung an [Text] | aus | leeren – Wartungsmodus", MaintenanceCmd),
                new Command("speichern", 1, ".speichern – alle Charaktere speichern", SaveAll),
                new Command("gold", 2, ".gold Name Gold – Gold gutschreiben (Admin)", Gold),
                new Command("gegenstand", 2, ".gegenstand Name Gegenstand [Anzahl] – Gegenstand geben (Admin)", GiveItem),
                new Command("pruefen", 2, ".pruefen – gespeicherte Daten gegen den Content prüfen (ändert nichts)", Inspect),
                new Command("version", 1, ".version – Content-Version, Betriebsart und Spieler", Version),
                new Command("rechte", 2, ".rechte Kontoname 0|1|2 – Spielleiter-Stufe setzen (Admin)", SetLevel),
            };
            return list.ToDictionary(c => c.Name);
        }

        /// <summary>Führt einen Befehl aus (mit oder ohne führenden Punkt).</summary>
        public void Execute(GmContext ctx, string text)
        {
            var parts = text.Trim().TrimStart('.').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            if (!Commands.TryGetValue(parts[0].ToLowerInvariant(), out var cmd)) { ctx.Reply($"Unbekannter Befehl „{parts[0]}“ – .hilfe zeigt alle."); return; }
            if (ctx.Level < cmd.Level) { ctx.Reply("Dafür fehlen Euch die Rechte."); return; }
            try { cmd.Run(ctx, parts); }
            catch (Exception ex) { ctx.Reply("Befehl fehlgeschlagen: " + ex.Message); _w.Log($"GM-Befehl {text}: {ex}"); }
        }

        // ------------------------------------------------------------ Hilfen

        private static string Rest(string[] a, int from) => a.Length > from ? string.Join(' ', a.Skip(from)) : "";

        private Player? Online(string name) =>
            _w.AllPlayers.FirstOrDefault(p => !p.IsBot && string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

        private void Audit(GmContext ctx, string action, string target, string detail) =>
            _w.Audit(new AuditEntry { AccountId = ctx.AccountId, Actor = ctx.Name, Action = "gm." + action, Target = target, Detail = detail, Ip = ctx.Ip });

        private bool Need(GmContext ctx, string[] a, int count, string command)
        {
            if (a.Length > count) return true;
            ctx.Reply("Aufruf: " + Commands[command].Usage);
            return false;
        }

        /// <summary>Asynchron (Datenbank) und Antwort im Spielthread.</summary>
        private void Async(GmContext ctx, Func<Task<string>> job) => Task.Run(async () =>
        {
            string reply;
            try { reply = await job(); }
            catch (Exception ex) { reply = "Fehler: " + ex.Message; }
            _w.Post(() => ctx.Reply(reply));
        });

        /// <summary>Konto zu einem Charakter- oder Kontonamen (Charaktername hat Vorrang).</summary>
        private async Task<Account?> ResolveAccount(string name)
        {
            var repo = _w.Accounts ?? throw new InvalidOperationException("Keine Kontodatenbank.");
            if (_w.Characters is IEconomyRepository eco && await eco.FindCharacterAsync(name) is { AccountId: long id }) return await repo.GetAccountAsync(id);
            return await repo.FindByNameAsync(name);
        }

        private static string? Outranks(GmContext ctx, Account target) =>
            target.GmLevel >= ctx.Level && ctx.Player != null ? "Gegen Gleich- oder Höhergestellte könnt Ihr nichts unternehmen." : null;

        private static bool TryMinutes(string s, out TimeSpan? duration)
        {
            duration = null;
            if (s.Equals("dauerhaft", StringComparison.OrdinalIgnoreCase) || s.Equals("perm", StringComparison.OrdinalIgnoreCase)) return true;
            if (!int.TryParse(s, out var m) || m <= 0 || m > 525_600) return false;
            duration = TimeSpan.FromMinutes(m);
            return true;
        }

        // ------------------------------------------------------------ Übersicht

        private void Help(GmContext ctx, string[] a)
        {
            foreach (var c in Commands.Values.Where(c => c.Level <= ctx.Level)) ctx.Reply(c.Usage);
        }

        private void Who(GmContext ctx, string[] a)
        {
            string filter = Rest(a, 1);
            var list = _w.AllPlayers.Where(p => !p.IsBot && (filter == "" || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                                                              p.Zone?.Def.Id.Contains(filter, StringComparison.OrdinalIgnoreCase) == true))
                .OrderBy(p => p.Name).ToList();
            ctx.Reply($"{list.Count} Spieler online ({_w.AllPlayers.Count(p => p.IsBot)} KI-Mitspieler, {_w.Sessions.Queue.Count} in der Warteschlange):");
            foreach (var p in list.Take(40))
                ctx.Reply($"{p.Name} – Stufe {p.Level} {p.ClassDef.Name}, {p.Zone?.Def.Name ?? "?"}{(p.IsLinkdead ? " (Verbindung getrennt)" : "")}{(p.IsGm ? " [GM]" : "")}");
        }

        private void Info(GmContext ctx, string[] a)
        {
            if (!Need(ctx, a, 1, "info")) return;
            var p = Online(a[1]);
            string online = p == null ? "offline" :
                $"online in {p.Zone?.Def.Name} ({p.Position.X:0}, {p.Position.Z:0}), Stufe {p.Level} {p.RaceDef.Name} {p.ClassDef.Name}, {p.Money / 10000.0:0.##} Gold, " +
                $"Anti-Cheat {_w.AntiCheat.ScoreOf(p):0} Punkte, Adresse {p.Connection?.RemoteIp ?? "-"}";
            string name = a[1];
            Async(ctx, async () =>
            {
                var acc = await ResolveAccount(name);
                if (acc == null) return p == null ? $"Niemand namens „{name}“ gefunden." : $"{name}: {online}";
                var now = DateTime.UtcNow;
                string ban = acc.IsBanned ? "dauerhaft gesperrt" : acc.BanUntil > now ? $"gesperrt bis {acc.BanUntil:dd.MM. HH:mm} UTC" : "nicht gesperrt";
                if (acc.BanReason.Length > 0 && acc.IsBannedAt(now)) ban += $" ({acc.BanReason})";
                string mute = acc.MutedUntil > now ? $", stumm bis {acc.MutedUntil:HH:mm} UTC" : "";
                return $"{name}: {online}. Konto {acc.Username} (#{acc.Id}), erstellt {acc.CreatedAt:dd.MM.yyyy}, Stufe {acc.GmLevel}, {ban}{mute}, " +
                       $"Zwei-Faktor {(acc.TotpSecret != null ? "an" : "aus")}.";
            });
        }

        private void Flags(GmContext ctx, string[] a)
        {
            var flags = _w.AntiCheat.Flags.Reverse().Take(15).ToList();
            if (flags.Count == 0) { ctx.Reply("Keine Auffälligkeiten seit dem Serverstart."); return; }
            foreach (var f in flags) ctx.Reply($"{f.At:HH:mm} {f.Name}: {f.Kind} – {f.Detail} ({f.Score} Punkte{(f.Kicked ? ", getrennt" : "")})");
        }

        private void Reports(GmContext ctx, string[] a)
        {
            if (_w.Characters is not ISocialRepository repo) { ctx.Reply("Keine Datenbank für Meldungen."); return; }
            Async(ctx, async () =>
            {
                var list = await repo.ListOpenReportsAsync(10);
                if (list.Count == 0) return "Keine offenen Meldungen.";
                return string.Join("\n", list.Select(r => $"#{r.Id} {r.At:dd.MM. HH:mm}: {r.ReporterName} meldet {r.TargetName} ({r.Reason}) {r.Text}" +
                                                          (r.Evidence.Count > 0 ? $" – letzte Zeile: {r.Evidence[^1]}" : "")));
            });
        }

        private void ReportDone(GmContext ctx, string[] a)
        {
            if (!Need(ctx, a, 1, "erledigt") || _w.Characters is not ISocialRepository repo) return;
            if (!long.TryParse(a[1].TrimStart('#'), out var id)) { ctx.Reply("Ungültige Nummer."); return; }
            Async(ctx, async () =>
            {
                if (!await repo.MarkReportHandledAsync(id)) return $"Meldung #{id} nicht gefunden oder schon erledigt.";
                Audit(ctx, "report.done", "#" + id, "");
                return $"Meldung #{id} als bearbeitet markiert.";
            });
        }

        private void AuditLog(GmContext ctx, string[] a)
        {
            var repo = _w.Accounts;
            if (repo == null) { ctx.Reply("Kein Prüfprotokoll verfügbar."); return; }
            string arg = Rest(a, 1);
            bool isAction = arg.Contains('.') || arg is "gm" or "login" or "account" or "anticheat";
            Async(ctx, async () =>
            {
                var list = await repo.ListAuditAsync(isAction && arg != "" ? arg : null, !isAction && arg != "" ? arg : null, 12);
                if (list.Count == 0) return "Keine Einträge.";
                return string.Join("\n", list.Select(e => $"{e.CreatedAt:dd.MM. HH:mm} {e.Action} {e.Actor} → {e.Target} {e.Detail}".TrimEnd()));
            });
        }

        // ------------------------------------------------------------ Moderation

        private void Announce(GmContext ctx, string[] a)
        {
            string text = Rest(a, 1);
            if (text.Length == 0) { ctx.Reply("Aufruf: " + Commands["ansage"].Usage); return; }
            if (text.Length > 300) text = text.Substring(0, 300);
            foreach (var p in _w.AllPlayers.Where(p => !p.IsBot))
            {
                p.Send(new S_Chat { Channel = ChatChannel.System, Text = "[Ansage] " + text });
                p.Notice(text);
            }
            Audit(ctx, "announce", "", text);
            ctx.Reply("Ansage verschickt.");
        }

        private void Kick(GmContext ctx, string[] a)
        {
            if (!Need(ctx, a, 1, "kick")) return;
            var p = Online(a[1]);
            if (p == null) { ctx.Reply($"{a[1]} ist nicht online."); return; }
            if (p.GmLevel >= ctx.Level && ctx.Player != null) { ctx.Reply("Gegen Gleich- oder Höhergestellte könnt Ihr nichts unternehmen."); return; }
            string reason = Rest(a, 2);
            Disconnect(p, "Ihr wurdet von der Spielleitung getrennt." + (reason.Length > 0 ? " Grund: " + reason : ""));
            Audit(ctx, "kick", p.Name, reason);
            ctx.Reply($"{p.Name} wurde getrennt.");
        }

        private void Disconnect(Player p, string reason)
        {
            var s = _w.Sessions.ForPlayer(p);
            if (s != null) s.Kick(reason);
            else _w.Logout(p); // verbindungslos: direkt abmelden
        }

        private void Mute(GmContext ctx, string[] a)
        {
            if (!Need(ctx, a, 2, "stumm")) return;
            if (!int.TryParse(a[2], out var minutes) || minutes <= 0 || minutes > 43_200) { ctx.Reply("Minuten: 1 bis 43200."); return; }
            string name = a[1], reason = Rest(a, 3);
            ChangeAccount(ctx, name, "mute", $"{minutes} min {reason}".Trim(), acc =>
            {
                acc.MutedUntil = DateTime.UtcNow.AddMinutes(minutes);
                return $"{name} ist für {minutes} Minuten stummgeschaltet.";
            }, p =>
            {
                p.MutedUntil = DateTime.UtcNow.AddMinutes(minutes);
                p.Notice($"Ihr wurdet für {minutes} Minuten stummgeschaltet." + (reason.Length > 0 ? " Grund: " + reason : ""), true);
            });
        }

        private void Unmute(GmContext ctx, string[] a)
        {
            if (!Need(ctx, a, 1, "laut")) return;
            string name = a[1];
            ChangeAccount(ctx, name, "unmute", "", acc => { acc.MutedUntil = null; return $"{name} darf wieder schreiben."; },
                p => { p.MutedUntil = null; p.Notice("Eure Stummschaltung wurde aufgehoben."); });
        }

        private void Ban(GmContext ctx, string[] a)
        {
            if (!Need(ctx, a, 3, "bann")) return;
            if (!TryMinutes(a[2], out var duration)) { ctx.Reply("Dauer: Minuten (bis 525600) oder „dauerhaft“."); return; }
            string name = a[1], reason = Rest(a, 3);
            ChangeAccount(ctx, name, "ban", $"{(duration == null ? "dauerhaft" : $"{duration.Value.TotalMinutes:0} min")} – {reason}", acc =>
            {
                if (duration == null) acc.IsBanned = true;
                else acc.BanUntil = DateTime.UtcNow + duration.Value;
                acc.BanReason = reason;
                return $"Konto {acc.Username} ist {(duration == null ? "dauerhaft" : $"für {duration.Value.TotalMinutes:0} Minuten")} gesperrt.";
            }, null, kickAccount: "Euer Konto wurde gesperrt. Grund: " + reason);
        }

        private void Unban(GmContext ctx, string[] a)
        {
            if (!Need(ctx, a, 1, "entbann")) return;
            ChangeAccount(ctx, a[1], "unban", "", acc =>
            {
                acc.IsBanned = false;
                acc.BanUntil = null;
                acc.BanReason = "";
                acc.LockedUntil = null;
                acc.FailedLogins = 0;
                return $"Konto {acc.Username} ist entsperrt.";
            }, null);
        }

        /// <summary>Lädt das Konto, ändert es, speichert, protokolliert – und wendet die Änderung auf alle Charaktere des Kontos in der Welt an.</summary>
        private void ChangeAccount(GmContext ctx, string name, string action, string detail, Func<Account, string> change, Action<Player>? online, string? kickAccount = null)
        {
            var repo = _w.Accounts;
            if (repo == null) { ctx.Reply("Keine Kontodatenbank."); return; }
            Task.Run(async () =>
            {
                string reply;
                Account? acc = null;
                try
                {
                    acc = await ResolveAccount(name);
                    if (acc == null) reply = $"Niemand namens „{name}“ gefunden.";
                    else if (Outranks(ctx, acc) is string denied) { reply = denied; acc = null; }
                    else
                    {
                        reply = change(acc);
                        await repo.UpdateSecurityAsync(acc);
                    }
                }
                catch (Exception ex) { reply = "Fehler: " + ex.Message; acc = null; }
                _w.Post(() =>
                {
                    if (acc != null)
                    {
                        Audit(ctx, action, acc.Username, detail);
                        foreach (var p in _w.AllPlayers.Where(p => !p.IsBot && p.Data.AccountId == acc.Id).ToList())
                        {
                            online?.Invoke(p);
                            if (kickAccount != null) Disconnect(p, kickAccount);
                        }
                        if (kickAccount != null)
                            foreach (var s in _w.Sessions.Sessions.Where(s => s.Account?.Id == acc.Id).ToList()) s.Kick(kickAccount);
                        foreach (var s in _w.Sessions.Sessions.Where(s => s.Account?.Id == acc.Id))
                        {
                            s.Account!.MutedUntil = acc.MutedUntil;
                            s.Account.GmLevel = acc.GmLevel;
                        }
                    }
                    ctx.Reply(reply);
                });
            });
        }

        // ------------------------------------------------------------ Bewegung

        private void Goto(GmContext ctx, string[] a)
        {
            if (ctx.Player == null) { ctx.Reply("Nur im Spiel möglich."); return; }
            if (!Need(ctx, a, 1, "gehezu")) return;
            var t = Online(a[1]);
            if (t?.Zone == null) { ctx.Reply($"{a[1]} ist nicht online."); return; }
            _w.Transfer(ctx.Player, t.Zone.Def.Id, t.Position + new Vec3(1.5f, 0, 0), t.Zone.Def.IsInstance ? t.Zone.InstanceKey : null);
            Audit(ctx, "goto", t.Name, "");
        }

        private void Summon(GmContext ctx, string[] a)
        {
            if (ctx.Player?.Zone == null) { ctx.Reply("Nur im Spiel möglich."); return; }
            if (!Need(ctx, a, 1, "hol")) return;
            var t = Online(a[1]);
            if (t == null) { ctx.Reply($"{a[1]} ist nicht online."); return; }
            var me = ctx.Player;
            _w.Transfer(t, me.Zone.Def.Id, me.Position + new Vec3(1.5f, 0, 0), me.Zone.Def.IsInstance ? me.Zone.InstanceKey : null);
            t.Notice($"Ihr wurdet von {me.Name} (Spielleitung) gerufen.");
            Audit(ctx, "summon", t.Name, "");
        }

        private void Tele(GmContext ctx, string[] a)
        {
            if (ctx.Player?.Zone == null) { ctx.Reply("Nur im Spiel möglich."); return; }
            if (!Need(ctx, a, 1, "tele")) return;
            var zone = _w.Zone(a[1]);
            if (zone == null) { ctx.Reply("Unbekannte Region. Offen: " + string.Join(", ", _w.OpenZones.Select(z => z.Def.Id))); return; }
            float size = zone.Def.Size;
            float x = a.Length > 3 && float.TryParse(a[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var px) ? px : size / 2;
            float z = a.Length > 3 && float.TryParse(a[3], NumberStyles.Float, CultureInfo.InvariantCulture, out var pz) ? pz : size / 2;
            _w.Transfer(ctx.Player, zone.Def.Id, new Vec3(Math.Clamp(x, 0, size), 0, Math.Clamp(z, 0, size)));
            Audit(ctx, "tele", zone.Def.Id, $"{x:0} {z:0}");
        }

        // ------------------------------------------------------------ Betrieb

        private void MaintenanceCmd(GmContext ctx, string[] a)
        {
            string mode = a.Length > 1 ? a[1].ToLowerInvariant() : "";
            switch (mode)
            {
                case "an":
                    _w.Maintenance = true;
                    if (a.Length > 2) _w.MaintenanceMessage = Rest(a, 2);
                    foreach (var p in _w.AllPlayers.Where(p => !p.IsBot)) p.Send(new S_Chat { Channel = ChatChannel.System, Text = "[Wartung] " + _w.MaintenanceMessage });
                    ctx.Reply("Wartungsmodus an: nur Spielleiter können sich anmelden. „.wartung leeren“ trennt alle übrigen Spieler.");
                    break;
                case "aus":
                    _w.Maintenance = false;
                    ctx.Reply("Wartungsmodus aus.");
                    break;
                case "leeren":
                    if (!_w.Maintenance) { ctx.Reply("Zuerst „.wartung an“."); return; }
                    var kicked = _w.AllPlayers.Where(p => !p.IsBot && !p.IsGm).ToList();
                    foreach (var p in kicked) Disconnect(p, _w.MaintenanceMessage);
                    foreach (var s in _w.Sessions.Sessions.Where(s => s.Account != null && s.Account.GmLevel < GmLevels.GameMaster).ToList()) s.Kick(_w.MaintenanceMessage);
                    ctx.Reply($"{kicked.Count} Spieler gespeichert und getrennt.");
                    break;
                default:
                    ctx.Reply($"Wartungsmodus ist {(_w.Maintenance ? "an" : "aus")}. Aufruf: {Commands["wartung"].Usage}");
                    return;
            }
            Audit(ctx, "maintenance", mode, Rest(a, 2));
        }

        private void SaveAll(GmContext ctx, string[] a)
        {
            _w.SaveAll();
            ctx.Reply("Alle Charaktere werden gespeichert.");
        }

        // ------------------------------------------------------------ Admin

        private void Gold(GmContext ctx, string[] a)
        {
            if (!Need(ctx, a, 2, "gold")) return;
            var p = Online(a[1]);
            if (p == null) { ctx.Reply($"{a[1]} ist nicht online (Gutschriften nur an Spieler in der Welt)."); return; }
            if (!long.TryParse(a[2], out var gold) || gold <= 0 || gold > 100_000) { ctx.Reply("Gold: 1 bis 100000."); return; }
            _w.Economy.Credit(p, gold * 10000, "gm", ctx.Name);
            _w.Economy.Commit(new EconomyCommit(), p);
            p.Send(p.BuildPlayerState());
            p.Notice($"Die Spielleitung hat Euch {gold} Gold gutgeschrieben.");
            Audit(ctx, "gold", p.Name, gold + " Gold");
            ctx.Reply($"{p.Name} erhält {gold} Gold.");
        }

        private void GiveItem(GmContext ctx, string[] a)
        {
            if (!Need(ctx, a, 2, "gegenstand")) return;
            var p = Online(a[1]);
            if (p == null) { ctx.Reply($"{a[1]} ist nicht online."); return; }
            if (!_w.Content.Items.TryGetValue(a[2], out var item)) { ctx.Reply("Unbekannter Gegenstand."); return; }
            int count = a.Length > 3 && int.TryParse(a[3], out var n) ? Math.Clamp(n, 1, Math.Max(1, item.MaxStack)) : 1;
            if (p.SpaceFor(item.Id) < count) { ctx.Reply($"{p.Name} hat keinen Platz in den Taschen."); return; }
            p.AddItem(item.Id, count);
            _w.Economy.Commit(new EconomyCommit(), p);
            p.Send(p.BuildInventory());
            _w.Quests.OnItemsChanged(p);
            p.Notice($"Die Spielleitung hat Euch {count}× {item.Name} gegeben.");
            Audit(ctx, "item", p.Name, $"{count}× {item.Id}");
            ctx.Reply($"{p.Name} erhält {count}× {item.Name}.");
        }

        private void Version(GmContext ctx, string[] a) =>
            ctx.Reply($"Content {_w.Content.Version} ({_w.Content.Migrations.Count} Content-Migrationen), Betriebsart {ServerConfig.ModeLabel(_w.Config.Mode)}, " +
                      $"{_w.AllPlayers.Count(p => !p.IsBot)} Spieler, {_w.AllPlayers.Count(p => p.IsBot)} KI-Mitspieler");

        private void Inspect(GmContext ctx, string[] a)
        {
            if (_w.Characters == null) { ctx.Reply("Keine Datenbank."); return; }
            var chars = _w.Characters;
            var content = _w.Content;
            Async(ctx, async () =>
            {
                var (n, affected, lines) = await ContentMigrator.InspectAsync(chars, content, 20);
                return $"Geprüft: {n} Charaktere, {affected} würden beim nächsten Laden angepasst." + (lines.Count > 0 ? "\n" + string.Join("\n", lines) : "");
            });
        }

        private void SetLevel(GmContext ctx, string[] a)
        {
            if (!Need(ctx, a, 2, "rechte")) return;
            if (!int.TryParse(a[2], out var level) || level < GmLevels.Player || level > GmLevels.Admin) { ctx.Reply("Stufe: 0 Spieler, 1 Spielleiter, 2 Admin."); return; }
            var repo = _w.Accounts;
            if (repo == null) { ctx.Reply("Keine Kontodatenbank."); return; }
            string name = a[1];
            ChangeAccount(ctx, name, "setlevel", level.ToString(), acc =>
            {
                acc.GmLevel = level;
                return $"Konto {acc.Username} hat jetzt Stufe {level}.";
            }, p => { p.GmLevel = level; p.Notice(level > 0 ? $"Ihr habt jetzt Spielleiter-Rechte (Stufe {level}). .hilfe zeigt die Befehle." : "Eure Spielleiter-Rechte wurden entzogen."); });
        }
    }
}
