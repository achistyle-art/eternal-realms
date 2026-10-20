using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Emotes (Kapitel 33): Text an alle in Sprechweite, dazu ein <see cref="S_Emote"/> für Animation und Klang.
    /// KI-Mitspieler nutzen dieselben Emotes und erwidern Gesten, die ihnen gelten.
    /// </summary>
    public sealed class EmoteSystem
    {
        public const double Cooldown = 1.0;
        private readonly GameWorld _w;
        private readonly Dictionary<int, double> _last = new();
        public EmoteSystem(GameWorld world) { _w = world; }

        public string? Perform(Player p, string emoteId, int targetId)
        {
            if (!_w.Content.Emotes.TryGetValue(emoteId, out var def)) return "Unbekanntes Emote.";
            if (p.IsDead && def.Id != "cry") return "Tote können das nicht.";
            if (!p.IsBot && _last.TryGetValue(p.Id, out var last) && _w.Time - last < Cooldown) return null; // still ignorieren statt Fehlermeldung
            _last[p.Id] = _w.Time;
            Unit? target = targetId != 0 ? p.Zone?.GetUnit(targetId) : null;
            if (target != null && (target == p || p.DistanceTo(target) > ZoneInstance.ViewRange)) target = null;
            string text = (target != null ? def.TextTarget : def.TextNoTarget).Replace("{me}", p.Name).Replace("{target}", target?.Name ?? "");
            var chat = new S_Chat { Channel = ChatChannel.Emote, SenderId = p.Id, SenderName = "", Text = text, SenderIsBot = p.IsBot };
            var emote = new S_Emote { SourceId = p.Id, TargetId = target?.Id ?? 0, EmoteId = def.Id };
            if (p.Zone != null)
                foreach (var listener in p.Zone.PlayersInRange(p.Position, ChatSystem.SayRange))
                {
                    if (_w.Social.Ignores(listener, p)) continue;
                    listener.Send(emote);
                    listener.Send(chat);
                }
            if (target is Player tp && tp.IsBot && !p.IsBot) tp.Bot?.OnEmote(p, def);
            return null;
        }
    }
}
