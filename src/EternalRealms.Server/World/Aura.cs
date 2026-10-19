using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Server.World
{
    /// <summary>Ein aktiver Buff oder Debuff auf einer Einheit.</summary>
    public sealed class Aura
    {
        public AuraDef Def { get; }
        public Unit? Caster { get; }
        public int CasterId { get; }
        public string SourceAbility { get; }
        public int Stacks { get; set; } = 1;
        public float Remaining { get; set; }
        public float TickTimer { get; set; }
        public int AbsorbLeft { get; set; }
        /// <summary>Fester Betrag je Tick (z. B. Essen: X % des Maximums).</summary>
        public int? TickOverride { get; set; }
        /// <summary>Zauberkraft des Wirkenden zum Zeitpunkt des Anlegens (für DoTs/HoTs).</summary>
        public int CasterPower { get; set; }
        public bool Expired => !Def.Permanent && Remaining <= 0;

        public Aura(AuraDef def, Unit? caster, string sourceAbility)
        {
            Def = def;
            Caster = caster;
            CasterId = caster?.Id ?? 0;
            SourceAbility = sourceAbility;
            Remaining = def.Permanent ? float.MaxValue : def.Duration;
            TickTimer = def.TickInterval;
        }

        /// <summary>Aurenfamilie ohne Rangsuffix: "priest_fort_r12" -> "priest_fort".</summary>
        public static string Family(string auraId)
        {
            int i = auraId.LastIndexOf("_r", StringComparison.Ordinal);
            if (i > 0 && i + 2 < auraId.Length && auraId.Substring(i + 2).All(char.IsDigit)) return auraId.Substring(0, i);
            return auraId;
        }

        public string FamilyId => Family(Def.Id);

        public AuraData ToNet() => new()
        {
            AuraId = Def.Id, Stacks = Stacks, Remaining = Def.Permanent ? -1 : Remaining, CasterId = CasterId,
        };
    }
}
