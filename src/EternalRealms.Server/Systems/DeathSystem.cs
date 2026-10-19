using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Tod und Wiederbelebung: Tod → Geist am Friedhof → zurück zur Leiche laufen (ohne Strafe)
    /// oder beim Geisterheiler sofort wiederbeleben (Haltbarkeitsverlust und Wiederbelebungsschwäche).
    /// </summary>
    public sealed class DeathSystem
    {
        private readonly GameWorld _w;
        public DeathSystem(GameWorld world) { _w = world; }

        public const float ReclaimRange = 40f;

        public void OnPlayerDied(Player p, Unit? killer)
        {
            p.Data.CorpseZone = p.Zone?.Def.Id ?? p.Data.ZoneId;
            p.Data.CorpsePosition = p.Position;
            p.LoseDurability(0.10f);
            p.ClearCombat();
            if (p.Pet != null) { p.Pet.Zone?.Remove(p.Pet); p.Pet = null; }
            p.Send(new S_DeathState { State = DeathState.Dead, CorpsePosition = p.Data.CorpsePosition });
            p.Send(p.BuildInventory());
            p.Notice("Ihr seid gestorben. Lasst Euren Geist frei, um zum Friedhof zu gelangen.");
            p.Bot?.OnDied();
            if (p.Group != null)
            {
                foreach (var b in p.Group.Bots.Where(b => b != p)) b.Bot!.OnGroupMemberDied(p);
                if (p.Group.Members.All(m => m.IsDead)) foreach (var b in p.Group.Bots) b.Bot!.Say("wipe");
            }
        }

        public string? ReleaseSpirit(Player p)
        {
            if (p.DeathState != DeathState.Dead) return "Ihr seid nicht tot.";
            var zone = p.Zone!;
            p.DeathState = DeathState.Ghost;
            p.Health = 1;
            p.MarkStatsDirty();
            _w.MovePlayerWithinZone(p, zone.NearestGraveyard(p.Data.CorpsePosition));
            p.Send(new S_DeathState { State = DeathState.Ghost, CorpsePosition = p.Data.CorpsePosition });
            return null;
        }

        public string? ReclaimCorpse(Player p)
        {
            if (p.DeathState != DeathState.Ghost) return "Ihr seid kein Geist.";
            if (p.Zone?.Def.Id != p.Data.CorpseZone || Vec3.DistanceXZ(p.Position, p.Data.CorpsePosition) > ReclaimRange) return "Ihr seid zu weit von Eurem Körper entfernt.";
            Revive(p, 0.5f, p.Data.CorpsePosition);
            return null;
        }

        public string? SpiritHealer(Player p, Creature healer)
        {
            if (p.DeathState != DeathState.Ghost) return "Nur Geister können das.";
            if (!healer.Template.Roles.HasFlag(NpcRole.SpiritHealer) || p.DistanceTo(healer) > 12) return "Kein Geisterheiler in der Nähe.";
            Revive(p, 0.5f, p.Position);
            p.LoseDurability(0.25f);
            if (p.Level > 10 && _w.Content.Auras.TryGetValue("resurrection_sickness", out var sick))
            {
                var aura = _w.Combat.ApplyAura(p, p, sick, "spirit_healer");
                if (aura != null) aura.Remaining = Math.Min(600, (p.Level - 10) * 60);
            }
            p.Send(p.BuildInventory());
            return null;
        }

        /// <summary>Wiederbelebung durch einen Zauber (Priester, Paladin, Schamane, Druide).</summary>
        public void Resurrect(Player p, float healthPct, Unit caster)
        {
            if (p.IsAlive) return;
            Revive(p, healthPct, p.Data.CorpsePosition);
            p.Bot?.Say("resurrected", caster);
        }

        private void Revive(Player p, float pct, Vec3 at)
        {
            if (p.Zone != null && p.Zone.Def.Id != p.Data.CorpseZone && _w.Content.Zones.ContainsKey(p.Data.CorpseZone))
                _w.Transfer(p, p.Data.CorpseZone, at);
            else if (p.Zone != null) _w.MovePlayerWithinZone(p, at);
            p.DeathState = DeathState.Alive;
            p.MarkStatsDirty();
            p.EnsureStats();
            p.Health = Math.Max(1, (int)(p.MaxHealth * pct));
            if (p.PowerType == PowerType.Mana) p.Power = (int)(p.MaxPower * pct);
            p.Send(new S_DeathState { State = DeathState.Alive });
            p.Zone?.BroadcastFrom(p, p.BuildSpawn(null));
        }
    }
}
