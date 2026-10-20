using EternalRealms.Shared.Content;
using EternalRealms.Shared.Net;

namespace EternalRealms.Shared.Client.Audio
{
    /// <summary>Ein abzuspielendes Geräusch: Schlüssel für den Synthesizer, Ort (Einheit) und Lautstärke 0–1.</summary>
    public struct SoundCue
    {
        public string Key;
        public int EntityId;
        public float Volume;
        /// <summary>Oberflächen- und Spieler-eigene Geräusche ohne Raumklang.</summary>
        public bool Ui;
    }

    /// <summary>Ordnet Nachrichten des Servers Geräusche zu (Kapitel 34) – nur Beteiligung des Spielers oder seiner Gruppe, damit es nicht lärmt.</summary>
    public static class SoundEvents
    {
        public static string School(School s) => s.ToString().ToLowerInvariant();

        public static SoundCue? For(Message m, GameClient c, ContentDatabase content)
        {
            int me = c.State.MyId;
            switch (m)
            {
                case S_CombatEvent e:
                    bool mine = e.SourceId == me || e.TargetId == me || c.Group.Exists(g => g.EntityId == e.SourceId || g.EntityId == e.TargetId);
                    if (!mine) return null;
                    string key = e.Result switch
                    {
                        CombatResult.Heal or CombatResult.CritHeal => "heal",
                        CombatResult.Miss or CombatResult.Dodge or CombatResult.Evade => "swing_miss",
                        CombatResult.Parry or CombatResult.Block => "block",
                        CombatResult.Resist or CombatResult.Immune or CombatResult.Absorb => "resist",
                        CombatResult.Crit => e.School == Shared.School.Physical ? "hit_crit" : "spell_" + School(e.School) + "_impact",
                        _ => e.School == Shared.School.Physical ? "hit_physical" : "spell_" + School(e.School) + "_impact",
                    };
                    return new SoundCue { Key = key, EntityId = e.TargetId, Volume = e.Result is CombatResult.Crit or CombatResult.CritHeal ? 1f : 0.7f };
                case S_CastStart cs when cs.CasterId == me || c.Group.Exists(g => g.EntityId == cs.CasterId):
                    var ab = content.Abilities.TryGetValue(cs.AbilityId, out var a) ? a : null;
                    return new SoundCue { Key = ab != null && ab.School != Shared.School.Physical ? "spell_" + School(ab.School) + "_cast" : "cast_generic", EntityId = cs.CasterId, Volume = 0.6f };
                case S_LevelUp lu when lu.EntityId == me: return new SoundCue { Key = "levelup", Ui = true, Volume = 1f };
                case S_LootResult: return new SoundCue { Key = "loot", Ui = true, Volume = 0.7f };
                case S_NewMail: return new SoundCue { Key = "mail", Ui = true, Volume = 0.6f };
                case S_UnitDied d when d.Id == me: return new SoundCue { Key = "death", Ui = true, Volume = 1f };
                case S_TradeClosed tc when tc.Completed: return new SoundCue { Key = "coins", Ui = true, Volume = 0.7f };
                case S_Notice n when n.IsError: return new SoundCue { Key = "error", Ui = true, Volume = 0.5f };
                case S_Emote em: return new SoundCue { Key = "emote", EntityId = em.SourceId, Volume = 0.4f };
            }
            return null;
        }
    }
}
