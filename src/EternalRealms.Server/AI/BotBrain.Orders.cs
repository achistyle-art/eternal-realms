using EternalRealms.Server.World;
using EternalRealms.Shared;
using EternalRealms.Shared.Content;

namespace EternalRealms.Server.AI
{
    /// <summary>Befehle des Gruppenleiters an KI-Mitspieler (Chat oder Kommandooberfläche).</summary>
    public sealed partial class BotBrain
    {
        private double _retreatUntil;
        private Unit? _ccTarget;
        private bool _interruptPriority;
        private Unit? _healRequest;
        public bool Spread { get; private set; }

        /// <summary>Ist dieser Bot mit der Anrede gemeint (Name, Rolle oder Klasse)?</summary>
        public bool IsAddressed(BotOrder order)
        {
            if (order.Addressees.Count == 0) return true;
            foreach (var a in order.Addressees)
            {
                if (a.Equals(Me.Name, StringComparison.OrdinalIgnoreCase)) return true;
                if (BotCommandParser.RoleWords.TryGetValue(a, out var role) && (role == Role || (role == Role.MeleeDps && Role == Role.RangedDps && a.Equals("schaden", StringComparison.OrdinalIgnoreCase)))) return true;
                if (BotCommandParser.ClassWords.TryGetValue(a, out var cls) && cls == Me.Class) return true;
            }
            return false;
        }

        public void OnPartyChat(Player from, string text)
        {
            var group = Me.Group;
            if (group == null) return;
            var order = BotCommandParser.Parse(text, group.Bots.Select(b => b.Name));
            if (order.Command == BotCommand.None || !IsAddressed(order)) return;
            Execute(order, from);
        }

        /// <summary>Führt einen Befehl aus – aus dem Chat oder der Kommandooberfläche (C_BotCommand).</summary>
        public void Execute(BotOrder order, Player from)
        {
            var group = Me.Group!;
            int idx = group.Bots.ToList().IndexOf(Me);
            float d = idx * 0.6f;
            var enemies = EngagedEnemies();
            switch (order.Command)
            {
                case BotCommand.Follow: Following = true; _retreatUntil = 0; Say("follow", delay: d, force: true); break;
                case BotCommand.Stay: Following = false; StayPosition = Me.Position; _moveTarget = null; Say("stay", delay: d, force: true); break;
                case BotCommand.Attack:
                case BotCommand.Pull:
                case BotCommand.Focus:
                {
                    var t = OrderTarget(order, from, enemies);
                    if (t == null) { Say("no_target", delay: d, force: true); break; }
                    if (order.Command == BotCommand.Pull && Role != Role.Tank && order.Addressees.Count == 0) break;
                    _forcedTarget = t;
                    Me.Target = t;
                    _retreatUntil = 0;
                    Say(order.Command == BotCommand.Focus ? "focus" : "attack", t, delay: d * 0.6f, force: true);
                    break;
                }
                case BotCommand.Dungeon:
                    DungeonMode = true;
                    // Der Tank antwortet zuerst, die anderen beziehen sich darauf (Bots reden miteinander).
                    float dd = Role == Role.Tank ? 0.8f : 2.2f + idx * 1.1f;
                    Say("dungeon", delay: dd, force: true);
                    if (Role != Role.Tank && _w.Random.Chance(0.5)) Say("dungeon_reply", delay: dd + 2.5f, force: true);
                    if (Data.Archetype == BotPersonality.Veteran) Say("veteran_dungeon", delay: dd + 4f);
                    _buffTimer = 0;
                    break;
                case BotCommand.Rest:
                case BotCommand.Mana:
                    if (Me.InCombat) { Say("after_fight", delay: d, force: true); break; }
                    Resting = true;
                    StartRestAbilities(Me.PowerType == PowerType.Mana ? Me.PowerPct : 1);
                    Say(order.Command == BotCommand.Mana ? "mana_ack" : "rest", delay: d, force: true);
                    break;
                case BotCommand.Passive: Stance = BotStance.Passive; Say("passive", delay: d, force: true); break;
                case BotCommand.Assist: Stance = BotStance.Assist; Spread = false; Say("defend", delay: d, force: true); break;
                case BotCommand.Aggressive: Stance = BotStance.Aggressive; Say("aggressive", delay: d, force: true); break;
                case BotCommand.Status: Say("status", delay: idx * 0.7f, force: true); break;
                case BotCommand.Buff: _buffTimer = 0; break;
                case BotCommand.AvoidAoe: Spread = true; Say("spread", delay: d, force: true); break;
                case BotCommand.Retreat:
                    _retreatUntil = Now + 8;
                    _forcedTarget = null;
                    Me.AutoAttacking = false;
                    if (Me.Cast != null) _w.Combat.CancelCast(Me, "Rückzug");
                    Say("retreat", delay: d * 0.5f, force: true);
                    break;
                case BotCommand.Interrupt:
                    if (Profile?.Interrupt == null) { if (order.Addressees.Count > 0) Say("cannot", delay: d, force: true); break; }
                    _interruptPriority = true;
                    Say("interrupt_ack", delay: d, force: true);
                    break;
                case BotCommand.Heal:
                    if (Profile == null || Profile.Heals.Count == 0) { if (order.Addressees.Count > 0) Say("cannot", delay: d, force: true); break; }
                    _healRequest = MentionedMember(order.Text, group) ?? from;
                    Say("heal_ack", _healRequest, delay: d * 0.3f, force: true);
                    break;
                case BotCommand.Control:
                {
                    var ability = ControlAbility();
                    if (ability == null) { if (order.Addressees.Count > 0) Say("cannot", delay: d, force: true); break; }
                    // Ohne Anrede übernimmt nur der erste fähige Bot die Kontrolle.
                    if (order.Addressees.Count == 0 && group.Bots.FirstOrDefault(b => b.Bot!.ControlAbility() != null) != Me) break;
                    var t = OrderTarget(order, from, enemies, preferNotTarget: true);
                    if (t == null) { Say("no_target", delay: d, force: true); break; }
                    _ccTarget = t;
                    group.SetMarker(Group.MarkMoon, t.Id);
                    Say("cc_ack", t, delay: d * 0.3f, force: true);
                    break;
                }
            }
        }

        /// <summary>Ziel eines Befehls: Boss, linker/rechter Gegner (aus Sicht des Anführers) oder das Ziel des Sprechers.</summary>
        private Unit? OrderTarget(BotOrder order, Player from, List<Creature> enemies, bool preferNotTarget = false)
        {
            var zone = Me.Zone!;
            var candidates = enemies.Count > 0 ? enemies : zone.UnitsInRange(from.Position, 40).OfType<Creature>().Where(c => c.IsAlive && Me.CanAttack(c)).ToList();
            if (order.MentionsBoss)
            {
                var boss = candidates.Where(c => c.IsBoss).OrderBy(c => c.DistanceTo(from)).FirstOrDefault()
                           ?? zone.UnitsInRange(from.Position, 60).OfType<Creature>().Where(c => c.IsBoss && c.IsAlive).OrderBy(c => c.DistanceTo(from)).FirstOrDefault();
                if (boss != null) return boss;
            }
            if (order.Side != OrderSide.None && candidates.Count > 0)
            {
                var fwd = new Vec3((float)Math.Sin(from.Facing), 0, (float)Math.Cos(from.Facing));
                // Kreuzprodukt (Y-Komponente): positiv = links vom Blick des Anführers (Unity: Blick +Z, rechts = +X).
                float Side(Creature c) { var v = c.Position - from.Position; return fwd.X * v.Z - fwd.Z * v.X; }
                var pick = order.Side == OrderSide.Left ? candidates.OrderBy(c => -Side(c)).First() : candidates.OrderBy(c => Side(c)).First();
                return pick;
            }
            if (from.Target is Creature ft && Me.CanAttack(ft) && !(preferNotTarget && candidates.Count > 1)) return ft;
            if (preferNotTarget) return candidates.Where(c => c != from.Target && !c.IsBoss).OrderBy(c => c.DistanceTo(from)).FirstOrDefault() ?? from.Target;
            return candidates.OrderBy(c => c.DistanceTo(from)).FirstOrDefault();
        }

        private static Player? MentionedMember(string text, Group g)
        {
            foreach (var m in g.Members) if (text.Contains(m.Name, StringComparison.OrdinalIgnoreCase)) return m;
            if (text.Contains("tank", StringComparison.OrdinalIgnoreCase)) return g.MemberWithRole(Role.Tank);
            return null;
        }

        private static readonly string[] ControlAbilities = { "mage_polymorph", "warlock_fear", "druid_entangling_roots", "rogue_gouge", "paladin_hammer_of_justice" };

        public AbilityDef? ControlAbility()
        {
            foreach (var id in ControlAbilities)
                if (Me.KnownRank(id) > 0) return Ranked(id);
            return null;
        }

        /// <summary>Hält den befohlenen Kontrolleffekt aufrecht. Rückgabe: Aktion ausgeführt.</summary>
        private bool MaintainControl()
        {
            if (_ccTarget == null) return false;
            if (_ccTarget.IsDead || _ccTarget.Removed) { _ccTarget = null; Me.Group?.SetMarker(Group.MarkMoon, 0); return false; }
            var ab = ControlAbility();
            if (ab == null) return false;
            var auraId = ab.Effects.FirstOrDefault(e => e.Type == EffectType.ApplyAura)?.Aura;
            if (auraId != null && _ccTarget.FindAura(auraId) != null) return false;
            if (Me.DistanceTo(_ccTarget) > ab.MaxRange - 1) { MoveTo(_ccTarget.Position, ab.MaxRange - 4); return true; }
            if (_w.Combat.TryCast(Me, ab, _ccTarget) == null) { Decide($"{ab.Name} auf {_ccTarget.Name}"); return true; }
            return false;
        }

        /// <summary>Rückzug: Kampf abbrechen und zum Anführer laufen (Gegner verlieren über die Leine das Interesse).</summary>
        private bool HandleRetreat()
        {
            if (_retreatUntil <= Now) return false;
            Me.AutoAttacking = false;
            Me.Target = null;
            var l = Leader;
            if (l != null) MoveTo(l.Position, 2);
            return true;
        }
    }
}
