using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;
using EternalRealms.Shared;

namespace EternalRealms.Server.AI
{
    /// <summary>
    /// Verhalten in Bosskämpfen (Kapitel 43–45, Gruppen bis 5): Bodeneffekten ausweichen, Pflicht-Zauber unterbrechen,
    /// vorrangige Adds angreifen und den Boss übernehmen, wenn der Tank fällt. Wie gut das gelingt, hängt von der
    /// Schwierigkeitsstufe ab (Kapitel 48) – Anfänger bemerken Gefahr später und übersehen sie manchmal.
    /// </summary>
    public sealed partial class BotBrain
    {
        private int _dodgeZoneId;
        private double _dodgeNoticeAt;
        private bool _standIn;
        private object? _mustCast;
        private bool _mustReacts;

        /// <summary>
        /// Zeit bis zum Bemerken eines Bodeneffekts und Anteil übersehener Effekte je Schwierigkeitsstufe.
        /// Werte aus der Abstimmung mit der Kampfsimulation (siehe docs/KI-MITSPIELER.md).
        /// </summary>
        public static (float Min, float Max, float Miss) DodgeProfile(BotDifficulty d) => d switch
        {
            BotDifficulty.Anfaenger => (0.8f, 2.4f, 0.25f),
            BotDifficulty.Normal => (0.35f, 1.3f, 0.08f),
            BotDifficulty.Erfahren => (0.15f, 0.7f, 0.02f),
            _ => (0f, 0.3f, 0f),
        };

        /// <summary>Chance, einen angesagten Pflicht-Zauber zu unterbrechen, sofern die Unterbrechung bereit ist.</summary>
        public static float MustInterruptChance(float skill) => 0.25f + skill * 0.7f;

        /// <summary>Steht der Bot in (oder am Rand) einer Gefahr, läuft er heraus. true = dieser Denkschritt ist verbraucht.</summary>
        private bool DodgeGround()
        {
            var danger = _w.Encounters.DangerAt(Me.Zone, Me.Position, 0.8f);
            if (danger == null) { _dodgeZoneId = 0; return false; }
            if (_dodgeZoneId != danger.Id)
            {
                _dodgeZoneId = danger.Id;
                var (min, max, miss) = DodgeProfile(Data.Difficulty);
                _dodgeNoticeAt = _w.Random.Chance(miss) ? double.MaxValue : Now + min + _w.Random.NextDouble() * (max - min);
            }
            if (Now < _dodgeNoticeAt) return false;
            var target = Me.Target ?? (Unit?)danger.Boss;
            var safe = SafeSpot(target, danger.Def.Radius + 6);
            if (safe == null) return false;
            if (Me.Cast != null) _w.Combat.CancelCast(Me, "Ausweichen");
            MoveTo(safe.Value, 0.3f);
            if (_w.Random.Chance(0.15)) Say("dodge");
            Decide("weicht aus: " + danger.Payload.Name);
            return true;
        }

        /// <summary>
        /// Nächster sicherer Punkt in Reichweite: in 16 Richtungen schrittweise suchen und den kürzesten Weg wählen, der nicht
        /// zu weit vom Ziel wegführt. null = nirgends sicher (dann lieber stehen bleiben und weiterkämpfen).
        /// </summary>
        private Vec3? SafeSpot(Unit? target, float maxStep)
        {
            Vec3? best = null;
            float bestScore = float.MaxValue;
            float range = Profile?.Ranged == true || Role == Role.Healer ? 28 : 4;
            for (int i = 0; i < 16; i++)
            {
                float a = i * (float)Math.PI / 8;
                var dir = new Vec3((float)Math.Sin(a), 0, (float)Math.Cos(a));
                for (float step = 1.5f; step <= maxStep; step += 1.5f)
                {
                    var p = Me.Position + dir * step;
                    if (!_w.Encounters.IsSafe(Me.Zone, p, 1.2f)) continue;
                    float score = step + (target == null ? 0 : Math.Max(0, Vec3.DistanceXZ(p, target.Position) - range) * 0.6f);
                    if (score < bestScore) { bestScore = score; best = p; }
                    break;
                }
            }
            return best;
        }

        /// <summary>Kampfplatz verschieben, wenn der geplante Platz in einer liegenden Fläche liegt (statt hinein- und herauszulaufen).</summary>
        private Vec3 SafeApproach(Unit target, Vec3 spot, float reach)
        {
            if (_w.Encounters.IsSafe(Me.Zone, spot, 1f)) return spot;
            for (int i = 0; i < 16; i++)
            {
                float a = i * (float)Math.PI / 8;
                var p = target.Position + new Vec3((float)Math.Sin(a), 0, (float)Math.Cos(a)) * reach;
                if (_w.Encounters.IsSafe(Me.Zone, p, 1f)) return p;
            }
            return spot;
        }

        /// <summary>
        /// Ist der Tank gefallen (oder fehlt), übernimmt genau ein Bot: bevorzugt eine Klasse mit Rüstung und Spott (Krieger,
        /// Paladin, Druide), sonst der robusteste Schadensverursacher. Heiler übernehmen nie – sie halten den Ersatz am Leben.
        /// </summary>
        private bool IsStandInTank()
        {
            if (Role == Role.Tank || Role == Role.Healer || !InGroup) return false;
            var group = Me.Group!;
            if (group.MemberWithRole(Role.Tank) is Player tank && tank.Zone == Me.Zone) { _standIn = false; return false; }
            var pick = group.Bots.Where(b => b.IsAlive && b.Zone == Me.Zone && b.Bot != null && b.Bot.Role is not Role.Healer and not Role.Tank)
                .OrderBy(b => b.Class is CharClass.Warrior or CharClass.Paladin or CharClass.Druid ? 0 : 1)
                .ThenByDescending(b => b.MaxHealth).ThenBy(b => b.Id).FirstOrDefault();
            if (pick != Me) { _standIn = false; return false; }
            if (!_standIn)
            {
                _standIn = true;
                Say("tank_takeover", (Unit?)group.Leader.Target ?? Me.Target, force: true);
                Decide("übernimmt als Tank");
            }
            return true;
        }

        /// <summary>Spott: aus dem Tank-Profil, sonst eine bekannte Klassenfähigkeit (für Ersatz-Tanks).</summary>
        private string? TauntAbility =>
            Profile?.Taunt ?? (Me.KnownRank("warrior_taunt") > 0 ? "warrior_taunt" : Me.KnownRank("druid_growl") > 0 ? "druid_growl" : null);

        /// <summary>Vorrangiges Add des Bosskampfs (am schwächsten zuerst).</summary>
        private static Creature? PriorityAdd(List<Creature> usable) =>
            usable.Where(e => e.PriorityTarget).OrderBy(e => e.HealthPct).FirstOrDefault();
    }
}
