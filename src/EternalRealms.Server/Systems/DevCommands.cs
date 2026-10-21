using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Rules;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Entwicklerbefehle im Chat (nur mit ER_DEV_COMMANDS=true): beschleunigen das Testen von Inhalten.
    /// #level 15 · #xp 500 · #gold 10 · #item item_id [Anzahl] · #tele zone_id x z · #heal · #kill · #spawn creature_id · #where
    /// </summary>
    public sealed class DevCommands
    {
        private readonly GameWorld _w;
        public DevCommands(GameWorld world) { _w = world; }

        public void Execute(Player p, string text)
        {
            var parts = text.Substring(1).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return;
            string Arg(int i, string def = "") => parts.Length > i ? parts[i] : def;
            int IntArg(int i, int def) => int.TryParse(Arg(i), out var v) ? v : def;
            switch (parts[0].ToLowerInvariant())
            {
                case "level":
                    int target = Math.Clamp(IntArg(1, p.Level + 1), 1, _w.Content.LevelCap);
                    while (p.Level < target) _w.Progression.LevelUp(p);
                    p.Data.Xp = 0;
                    p.Send(p.BuildPlayerState());
                    break;
                case "xp": _w.Progression.GiveXp(p, IntArg(1, 100), "dev"); break;
                case "gold": _w.Economy.Credit(p, IntArg(1, 1) * 10000L, "dev"); p.Send(p.BuildPlayerState()); p.Send(p.BuildInventory()); break;
                case "item":
                    if (!_w.Content.Items.ContainsKey(Arg(1))) { p.Notice("Unbekannter Gegenstand.", true); return; }
                    p.AddItem(Arg(1), IntArg(2, 1));
                    p.Send(p.BuildInventory());
                    _w.Quests.OnItemsChanged(p);
                    break;
                case "tele":
                    _w.Transfer(p, Arg(1, p.Zone!.Def.Id), new Vec3(IntArg(2, (int)p.Position.X), 0, IntArg(3, (int)p.Position.Z)));
                    break;
                case "heal": p.Health = p.MaxHealth; p.Power = p.MaxPower; break;
                case "kill":
                    if (p.Target != null && p.Target != p) _w.Combat.DealDamage(p, p.Target, (p.Target.Health + 1000) * 100f, School.Holy, null, "dev", false, false); // Rüstung und Widerstand sicher übertreffen
                    break;
                case "spawn":
                    if (_w.Content.Creatures.ContainsKey(Arg(1))) p.Zone!.SpawnCreature(Arg(1), p.Position + new Vec3(5, 0, 0));
                    break;
                case "event":
                    if (Arg(1) == "") { p.Notice("Ereignisse: " + string.Join(", ", _w.Content.WorldEvents.Keys)); break; }
                    p.Notice(_w.Events.Start(Arg(1), skipWarmup: true) ?? "Ereignis gestartet.");
                    break;
                case "time":
                    if (float.TryParse(Arg(1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var hour)) _w.SetGameHour(hour);
                    p.Notice($"Es ist {_w.GameHour:0.0} Uhr.");
                    break;
                case "weather":
                    if (Enum.TryParse<Weather>(Arg(1), true, out var wth)) { p.Zone!.SetWeather(wth, 1f); p.Notice("Wetter: " + wth); }
                    else p.Notice("Wetter: Clear, Rain, Snow, Fog, Storm");
                    break;
                case "perf":
                    var ts = _w.TickStats;
                    p.Notice($"Takt Ø {ts.Average:0.00} ms, 99 % {ts.Percentile(99):0.00} ms, max {ts.Max:0.00} ms, {_w.TickOverruns} Überläufe – " +
                             $"{_w.AllPlayers.Count()} Spieler/Bots, {_w.EntityCount} Einheiten, {_w.AllZones.Count()} Zonen");
                    break;
                case "where": p.Notice($"{p.Zone!.Def.Id} {p.Position} – {p.CurrentArea?.Name ?? "-"} – {_w.GameHour:0.0} Uhr"); break;
                default: p.Notice("Unbekannter Befehl.", true); break;
            }
        }
    }
}
