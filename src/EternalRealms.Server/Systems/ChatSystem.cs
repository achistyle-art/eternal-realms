using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Client.Ui;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    public sealed class ChatSystem
    {
        private readonly GameWorld _w;
        public ChatSystem(GameWorld world) { _w = world; }

        public const float SayRange = 40f, YellRange = 200f;
        public const int MaxLength = 255;

        /// <summary>Öffentliche Kanäle, die man betreten und verlassen kann (Kapitel 31).</summary>
        public static readonly ChatChannel[] PublicChannels = { ChatChannel.General, ChatChannel.Trade, ChatChannel.LookingForGroup };

        // Spamschutz für öffentliche Kanäle: höchstens 6 Nachrichten in 10 s, gleicher Text frühestens nach 30 s erneut
        public const int SpamBurst = 6;
        public const double SpamWindow = 10, RepeatWindow = 30;
        private readonly Dictionary<long, Queue<double>> _recent = new();
        private readonly Dictionary<long, (string text, double at)> _lastPublic = new();

        public static string ChannelName(ChatChannel c) => c switch
        {
            ChatChannel.General => "Gebiet", ChatChannel.Trade => "Handel", ChatChannel.LookingForGroup => "Gruppensuche", _ => c.ToString(),
        };

        public bool InChannel(Player p, ChatChannel c) => !p.Data.LeftChannels.Contains((int)c);

        public void SetChannel(Player p, ChatChannel c, bool join)
        {
            if (Array.IndexOf(PublicChannels, c) < 0) { p.Notice("Diesen Kanal kann man nicht verlassen.", true); return; }
            bool changed = join ? p.Data.LeftChannels.Remove((int)c) : p.Data.LeftChannels.Add((int)c);
            if (changed) p.Notice(join ? $"Kanal {ChannelName(c)} betreten." : $"Kanal {ChannelName(c)} verlassen.");
            p.Send(BuildChannels(p));
        }

        public S_ChatChannels BuildChannels(Player p) => new() { Joined = PublicChannels.Where(c => InChannel(p, c)).Select(c => (byte)c).ToList() };

        private static bool IsPublic(ChatChannel c) => c is ChatChannel.Say or ChatChannel.Yell or ChatChannel.Emote or ChatChannel.General or ChatChannel.Trade or ChatChannel.LookingForGroup;

        /// <summary>Rückgabe: Grund der Sperre oder null.</summary>
        private string? SpamCheck(Player from, ChatChannel channel, string text)
        {
            if (from.IsBot || !IsPublic(channel)) return null;
            long key = from.Data.Id != 0 ? from.Data.Id : -from.Id;
            if (!_recent.TryGetValue(key, out var q)) _recent[key] = q = new Queue<double>();
            while (q.Count > 0 && _w.Time - q.Peek() > SpamWindow) q.Dequeue();
            if (q.Count >= SpamBurst) return "Ihr schreibt zu schnell. Bitte wartet einen Moment.";
            if (_lastPublic.TryGetValue(key, out var last) && last.text == text && _w.Time - last.at < RepeatWindow) return "Ihr habt das gerade schon geschrieben.";
            q.Enqueue(_w.Time);
            _lastPublic[key] = (text, _w.Time);
            return null;
        }

        /// <summary>Empfänger eines Fraktionskanals; Handel nur für Spieler, die gerade in einer Stadt sind.</summary>
        private IEnumerable<Player> FactionListeners(Player from, ChatChannel channel) =>
            _w.AllPlayers.Where(p => !p.IsBot && p.Faction == from.Faction && InChannel(p, channel) && !_w.Social.Ignores(p, from)
                                     && (channel != ChatChannel.Trade || p.CurrentArea?.IsCity == true)
                                     && (channel != ChatChannel.General || p.Zone == from.Zone));

        /// <summary>„/würfeln“ bzw. „/roll“ [max] oder [min-max]: sichtbar für die Gruppe, sonst für die Umgebung.</summary>
        private bool TryRollCommand(Player from, string text)
        {
            var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var cmd = parts[0].ToLowerInvariant();
            if (cmd != "/würfeln" && cmd != "/wuerfeln" && cmd != "/roll") return false;
            int min = 1, max = 100;
            if (parts.Length > 1)
            {
                var r = parts[1].Split('-');
                if (r.Length == 2 && int.TryParse(r[0], out var a) && int.TryParse(r[1], out var b)) { min = a; max = b; }
                else if (int.TryParse(parts[1], out var m)) max = m;
            }
            min = Math.Clamp(min, 0, 1_000_000); max = Math.Clamp(max, min, 1_000_000);
            int value = _w.Random.Range(min, max);
            var msg = new S_Chat { Channel = ChatChannel.System, Text = $"{from.Name} würfelt {value} ({min}–{max})." };
            if (from.Group != null) from.Group.Broadcast(msg);
            else from.Zone?.BroadcastInRange(from.Position, SayRange, msg);
            return true;
        }

        public void Handle(Player from, ChatChannel channel, string text, string whisperTo)
        {
            text = (text ?? "").Trim();
            if (text.Length == 0) return;
            if (text.Length > MaxLength) text = text.Substring(0, MaxLength);
            if (text.StartsWith("#") && _w.Config.DevCommands && !from.IsBot) { _w.DevCommands.Execute(from, text); return; }
            if (TryRollCommand(from, text)) return;
            text = ChatLinks.Sanitize(text, _w.Content);
            if (text.Length == 0) return;
            if (Array.IndexOf(PublicChannels, channel) >= 0 && !InChannel(from, channel)) { from.Notice($"Ihr seid nicht im Kanal {ChannelName(channel)} (/join {ChannelName(channel).ToLowerInvariant()}).", true); return; }
            if (channel == ChatChannel.Trade && from.CurrentArea?.IsCity != true) { from.Notice("Der Handelskanal ist nur in Städten verfügbar.", true); return; }
            var spam = SpamCheck(from, channel, text);
            if (spam != null) { from.Notice(spam, true); return; }
            var msg = new S_Chat { Channel = channel, SenderId = from.Id, SenderName = from.Name, Text = text, SenderIsBot = from.IsBot };
            _w.Social.RememberChat(from, $"[{channel}] {text}");
            switch (channel)
            {
                case ChatChannel.Say:
                case ChatChannel.Emote:
                    InRange(from, SayRange, msg);
                    if (from.Group != null && !from.IsBot) RouteToBots(from, text, saidAloud: true);
                    break;
                case ChatChannel.Yell:
                    InRange(from, YellRange, msg);
                    break;
                case ChatChannel.Guild:
                    _w.Guilds.Chat(from, msg);
                    break;
                case ChatChannel.Party:
                case ChatChannel.Raid:
                    if (from.Group == null) { from.Notice("Ihr seid in keiner Gruppe.", true); return; }
                    from.Group.Broadcast(msg);
                    if (!from.IsBot) RouteToBots(from, text, saidAloud: false);
                    break;
                case ChatChannel.General:
                case ChatChannel.Trade:
                case ChatChannel.LookingForGroup:
                    foreach (var p in FactionListeners(from, channel)) p.Send(msg);
                    break;
                case ChatChannel.Whisper:
                    var to = _w.FindPlayer(whisperTo);
                    if (to == null) { from.Notice($"Niemand namens '{whisperTo}' ist online.", true); return; }
                    if (_w.Social.Ignores(to, from)) { from.Notice($"{to.Name} ignoriert Euch.", true); return; }
                    to.Send(msg);
                    from.Send(new S_Chat { Channel = ChatChannel.Whisper, SenderId = to.Id, SenderName = "An " + to.Name, Text = text });
                    to.Bot?.OnWhisper(from, ChatLinks.Plain(text, _w.Content));
                    break;
                default:
                    from.Notice("Dieser Kanal ist nicht verfügbar.", true);
                    break;
            }
        }

        /// <summary>Sagen/Rufen in Reichweite – ohne Spieler, die den Sprecher ignorieren.</summary>
        private void InRange(Player from, float range, S_Chat msg)
        {
            if (from.Zone == null) return;
            foreach (var p in from.Zone.PlayersInRange(from.Position, range))
                if (!_w.Social.Ignores(p, from)) p.Send(msg);
        }

        private void RouteToBots(Player from, string text, bool saidAloud)
        {
            foreach (var b in from.Group!.Bots.ToList())
                if (!saidAloud || b.DistanceTo(from) <= SayRange) b.Bot!.OnPartyChat(from, ChatLinks.Plain(text, _w.Content));
        }

        public void BotSay(Player bot, ChatChannel channel, string text)
        {
            var msg = new S_Chat { Channel = channel, SenderId = bot.Id, SenderName = bot.Name, Text = text, SenderIsBot = true };
            switch (channel)
            {
                case ChatChannel.Party when bot.Group != null:
                    bot.Group.Broadcast(msg);
                    break;
                case ChatChannel.Guild:
                    _w.Guilds.Chat(bot, msg);
                    break;
                case ChatChannel.General:
                case ChatChannel.Trade:
                case ChatChannel.LookingForGroup:
                    foreach (var p in FactionListeners(bot, channel)) p.Send(msg);
                    break;
                default:
                    bot.Zone?.BroadcastInRange(bot.Position, SayRange, msg);
                    break;
            }
        }

        public void NpcSay(Creature npc, string text, bool yell = false)
        {
            npc.Zone?.BroadcastInRange(npc.Position, yell ? YellRange : SayRange,
                new S_Chat { Channel = yell ? ChatChannel.Yell : ChatChannel.Say, SenderId = npc.Id, SenderName = npc.Name, Text = text });
        }

        public void System(Player p, string text) => p.Send(new S_Chat { Channel = ChatChannel.System, Text = text });
    }
}
