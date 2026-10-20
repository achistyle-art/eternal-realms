using EternalRealms.Shared.Content;

namespace EternalRealms.Shared.Client.Anim
{
    /// <summary>Alle Animationen (Kapitel 33). Namen entsprechen den Zuständen eines Animators, falls echte Modelle eingebunden werden.</summary>
    public enum AnimClip : byte
    {
        Idle, Walk, Run, Backpedal, Jump, CombatIdle, Mounted, Stun, Death, Dead, Ghost,
        // Oberkörper / einmalig
        AttackMelee, AttackUnarmed, Shoot, CastLoop, CastRelease, Channel, Hit, Dodge, Block, Gather,
        // Emotes
        Wave, Bow, Cheer, Dance, Sit, Sleep, Laugh, Cry, Point, Salute, Kneel, Flex, Shrug, Applaud, Nod, HeadShake,
    }

    /// <summary>Was die Darstellung in diesem Moment zeigen soll.</summary>
    public struct AnimPose
    {
        /// <summary>Ganzkörper-Zustand (Beine, Haltung).</summary>
        public AnimClip Base;
        /// <summary>Phase der Grundbewegung 0–1 (Schrittzyklus, Atmen).</summary>
        public float BasePhase;
        /// <summary>Einmalige Oberkörper-Animation oder null.</summary>
        public AnimClip? Overlay;
        /// <summary>Fortschritt der Überlagerung 0–1 (bei Schleifen: Zyklusphase).</summary>
        public float OverlayProgress;
        /// <summary>Gewicht der Überlagerung (ein-/ausblenden).</summary>
        public float OverlayWeight;
    }

    /// <summary>
    /// Zustandsautomat für die Animation einer Einheit – ohne Engine, damit Regeln und Übergänge testbar sind.
    /// Der Client füttert ihn mit Geschwindigkeit, Flags und Ereignissen (Angriff, Treffer, Zauber, Emote) und
    /// bekommt eine <see cref="AnimPose"/>. Bei Modellen mit Animator werden daraus Parameter, sonst Prozedur-Posen.
    /// </summary>
    public sealed class AnimationController
    {
        public const float WalkThreshold = 0.3f, RunThreshold = 3.6f;
        /// <summary>Schrittlänge (m) eines vollen Zyklus beim Gehen bzw. Rennen.</summary>
        public const float WalkStride = 1.6f, RunStride = 3.2f;
        public const float FadeTime = 0.15f;

        private AnimClip? _oneShot;
        private double _oneShotStart, _oneShotEnd;
        private AnimClip? _loopEmote;
        private double _loopStart;
        private double _deathAt = -1;
        private float _phase;
        private double _lastUpdate = -1;

        public AnimClip? LoopEmote => _loopEmote;

        /// <summary>Dauer der einmaligen Animationen in Sekunden.</summary>
        public static float DurationOf(AnimClip c) => c switch
        {
            AnimClip.AttackMelee => 0.6f, AnimClip.AttackUnarmed => 0.5f, AnimClip.Shoot => 0.7f, AnimClip.CastRelease => 0.5f,
            AnimClip.Hit => 0.35f, AnimClip.Dodge => 0.45f, AnimClip.Block => 0.4f, AnimClip.Gather => 1.2f, AnimClip.Jump => 0.7f,
            _ => 2f,
        };

        private void Play(AnimClip clip, double now, float duration)
        {
            // Treffer unterbricht keinen Angriff und keine Emote-Animation; alles andere ersetzt die laufende
            if (clip == AnimClip.Hit && _oneShot != null && now < _oneShotEnd && _oneShot != AnimClip.Hit) return;
            _oneShot = clip;
            _oneShotStart = now;
            _oneShotEnd = now + duration;
        }

        public void OnAttack(double now, bool ranged, bool armed) => Play(ranged ? AnimClip.Shoot : armed ? AnimClip.AttackMelee : AnimClip.AttackUnarmed, now, DurationOf(ranged ? AnimClip.Shoot : AnimClip.AttackMelee));
        public void OnCastRelease(double now) => Play(AnimClip.CastRelease, now, DurationOf(AnimClip.CastRelease));
        public void OnHit(double now) => Play(AnimClip.Hit, now, DurationOf(AnimClip.Hit));
        public void OnAvoid(double now, bool block) => Play(block ? AnimClip.Block : AnimClip.Dodge, now, DurationOf(block ? AnimClip.Block : AnimClip.Dodge));
        public void OnJump(double now) => Play(AnimClip.Jump, now, DurationOf(AnimClip.Jump));
        public void OnGather(double now) => Play(AnimClip.Gather, now, DurationOf(AnimClip.Gather));

        public void OnEmote(double now, EmoteDef emote)
        {
            if (emote.Animation.Length == 0 || !System.Enum.TryParse<AnimClip>(emote.Animation, out var clip)) return;
            if (emote.Loop) { _loopEmote = clip; _loopStart = now; _oneShot = null; }
            else { _loopEmote = null; Play(clip, now, emote.Duration); }
        }

        /// <summary>Einen Schritt weiterrechnen. speed = waagerechtes Tempo in m/s (negativ = rückwärts).</summary>
        public AnimPose Update(double now, float speed, UnitFlags flags, bool inCombat, bool casting, bool channeling, bool mounted)
        {
            float dt = _lastUpdate < 0 ? 0 : (float)(now - _lastUpdate);
            _lastUpdate = now;
            var pose = new AnimPose();
            bool dead = (flags & UnitFlags.Dead) != 0;
            if ((flags & UnitFlags.Ghost) != 0) dead = false;
            if (dead)
            {
                _loopEmote = null; _oneShot = null;
                if (_deathAt < 0) _deathAt = now;
                float t = (float)(now - _deathAt);
                pose.Base = t < 1f ? AnimClip.Death : AnimClip.Dead;
                pose.BasePhase = System.Math.Min(1f, t);
                return pose;
            }
            _deathAt = -1;
            float abs = System.Math.Abs(speed);
            bool moving = abs > WalkThreshold;
            if (moving) _loopEmote = null; // Bewegung beendet Sitzen, Tanzen, Schlafen
            if (casting || channeling || inCombat) { if (_loopEmote is AnimClip.Sit or AnimClip.Sleep or AnimClip.Kneel or AnimClip.Dance) _loopEmote = null; }

            if ((flags & UnitFlags.Stunned) != 0) pose.Base = AnimClip.Stun;
            else if (mounted) pose.Base = AnimClip.Mounted;
            else if (moving) pose.Base = speed < 0 ? AnimClip.Backpedal : abs >= RunThreshold ? AnimClip.Run : AnimClip.Walk;
            else if (_loopEmote != null) pose.Base = _loopEmote.Value;
            else if ((flags & UnitFlags.Ghost) != 0) pose.Base = AnimClip.Ghost;
            else pose.Base = inCombat ? AnimClip.CombatIdle : AnimClip.Idle;

            // Phase: beim Laufen an die zurückgelegte Strecke gekoppelt (keine rutschenden Füße), sonst langsames Atmen
            float cycle = pose.Base switch
            {
                AnimClip.Walk or AnimClip.Backpedal => abs / WalkStride,
                AnimClip.Run => abs / RunStride,
                AnimClip.Mounted => moving ? abs / 6f : 0.25f,
                AnimClip.Dance => 1.2f,
                _ => 0.25f,
            };
            _phase = (_phase + cycle * dt) % 1f;
            pose.BasePhase = pose.Base == _loopEmote && pose.Base != AnimClip.Dance ? System.Math.Min(1f, (float)(now - _loopStart) / 0.6f) : _phase;

            if (_oneShot != null && now < _oneShotEnd)
            {
                float len = (float)(_oneShotEnd - _oneShotStart);
                float t = (float)(now - _oneShotStart);
                pose.Overlay = _oneShot;
                pose.OverlayProgress = len > 0 ? t / len : 1;
                pose.OverlayWeight = Fade(t, len);
            }
            else
            {
                _oneShot = null;
                if (channeling) { pose.Overlay = AnimClip.Channel; pose.OverlayProgress = _phase; pose.OverlayWeight = 1; }
                else if (casting) { pose.Overlay = AnimClip.CastLoop; pose.OverlayProgress = _phase; pose.OverlayWeight = 1; }
            }
            return pose;
        }

        private static float Fade(float t, float len)
        {
            if (len <= 2 * FadeTime) return 1;
            if (t < FadeTime) return t / FadeTime;
            if (t > len - FadeTime) return System.Math.Max(0, (len - t) / FadeTime);
            return 1;
        }
    }
}
