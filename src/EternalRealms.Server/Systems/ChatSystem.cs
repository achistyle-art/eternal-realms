using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    public sealed class ChatSystem
    {
        private readonly GameWorld _w;
        public ChatSystem(GameWorld world) { _w = world; }

        public const float SayRange = 40f, YellRange = 200f;
        public const int MaxLength = 255;

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
            var msg = new S_Chat { Channel = channel, SenderId = from.Id, SenderName = from.Name, Text = text, SenderIsBot = from.IsBot };
            switch (channel)
            {
                case ChatChannel.Say:
                case ChatChannel.Emote:
                    from.Zone?.BroadcastInRange(from.Position, SayRange, msg);
                    if (from.Group != null && !from.IsBot) RouteToBots(from, text, saidAloud: true);
                    break;
                case ChatChannel.Yell:
                    from.Zone?.BroadcastInRange(from.Position, YellRange, msg);
                    break;
                case ChatChannel.Party:
                case ChatChannel.Raid:
                    if (from.Group == null) { from.Notice("Ihr seid in keiner Gruppe.", true); return; }
                    from.Group.Broadcast(msg);
                    if (!from.IsBot) RouteToBots(from, text, saidAloud: false);
                    break;
                case ChatChannel.General:
                    from.Zone?.BroadcastAll(msg);
                    break;
                case ChatChannel.Whisper:
                    var to = _w.FindPlayer(whisperTo);
                    if (to == null) { from.Notice($"Niemand namens '{whisperTo}' ist online.", true); return; }
                    to.Send(msg);
                    from.Send(new S_Chat { Channel = ChatChannel.Whisper, SenderId = to.Id, SenderName = "An " + to.Name, Text = text });
                    to.Bot?.OnWhisper(from, text);
                    break;
                default:
                    from.Notice("Dieser Kanal ist nicht verfügbar.", true);
                    break;
            }
        }

        private void RouteToBots(Player from, string text, bool saidAloud)
        {
            foreach (var b in from.Group!.Bots.ToList())
                if (!saidAloud || b.DistanceTo(from) <= SayRange) b.Bot!.OnPartyChat(from, text);
        }

        public void BotSay(Player bot, ChatChannel channel, string text)
        {
            var msg = new S_Chat { Channel = channel, SenderId = bot.Id, SenderName = bot.Name, Text = text, SenderIsBot = true };
            switch (channel)
            {
                case ChatChannel.Party when bot.Group != null:
                    bot.Group.Broadcast(msg);
                    break;
                case ChatChannel.General:
                    bot.Zone?.BroadcastAll(msg);
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
