using System;
using System.Collections.Generic;

namespace EternalRealms.Shared.Client.Audio
{
    /// <summary>
    /// Erzeugt alle Klänge des Spiels zur Laufzeit (Kapitel 34): Effekte, Umgebung und Musik. Es gibt keine
    /// Audiodateien und damit keine fremden Rechte; gleicher Schlüssel ergibt immer dasselbe Ergebnis.
    /// Ausgabe: Mono-PCM im Bereich −1…1. Musik und Umgebung sind nahtlos schleifbar.
    /// </summary>
    public static class Synth
    {
        public const int DefaultRate = 22050;
        private const double Tau = Math.PI * 2;

        /// <summary>Kleiner deterministischer Zufallsgenerator (xorshift), unabhängig von System.Random-Implementierungen.</summary>
        private sealed class Rng
        {
            private uint _s;
            public Rng(int seed) { _s = (uint)seed ^ 0x9E3779B9u; if (_s == 0) _s = 1; }
            public uint Next() { _s ^= _s << 13; _s ^= _s >> 17; _s ^= _s << 5; return _s; }
            public double Float() => Next() / 4294967296.0;
            public double Signed() => Float() * 2 - 1;
            public int Range(int min, int max) => min + (int)(Float() * (max - min));
        }

        /// <summary>Stabiler Hash (FNV-1a) – String.GetHashCode ist je Prozess zufällig.</summary>
        public static int Hash(string s)
        {
            unchecked
            {
                uint h = 2166136261;
                foreach (char c in s) { h ^= c; h *= 16777619; }
                return (int)h;
            }
        }

        private static double Midi(double note) => 440.0 * Math.Pow(2, (note - 69) / 12.0);

        private static void Normalize(float[] buf, float peak)
        {
            float max = 0;
            foreach (var v in buf) max = Math.Max(max, Math.Abs(v));
            if (max < 1e-6f) return;
            float k = peak / max;
            for (int i = 0; i < buf.Length; i++) buf[i] *= k;
        }

        /// <summary>Ende auf den Anfang falten, damit die Schleife ohne Knacken weiterläuft.</summary>
        private static float[] FoldLoop(float[] buf, int loopLength)
        {
            var o = new float[loopLength];
            for (int i = 0; i < buf.Length; i++) o[i % loopLength] += buf[i];
            return o;
        }

        private static float[] Buffer(double seconds, int rate) => new float[Math.Max(1, (int)(seconds * rate))];

        // ================================================================== Effekte

        public static float[] Effect(string key, int rate = DefaultRate)
        {
            var rng = new Rng(Hash(key));
            float[] b;
            if (key.StartsWith("spell_", StringComparison.Ordinal))
            {
                var parts = key.Split('_');
                b = Spell(parts.Length > 1 ? parts[1] : "arcane", key.EndsWith("_impact", StringComparison.Ordinal), rate, rng);
            }
            else if (key.StartsWith("footstep", StringComparison.Ordinal)) b = Footstep(key, rate, rng);
            else switch (key)
            {
                case "hit_physical": b = Hit(rate, rng, 0.16, 1.0); break;
                case "hit_crit": b = Hit(rate, rng, 0.28, 1.6); break;
                case "swing_miss": b = Whoosh(rate, rng, 0.25, 600, 2400); break;
                case "block": b = Metal(rate, rng, 0.35); break;
                case "resist": b = Chime(rate, new[] { 84.0, 91 }, 0.35, 0.3); break;
                case "heal": b = Chime(rate, new[] { 72.0, 76, 79, 84 }, 0.6, 0.09); break;
                case "cast_generic": b = Whoosh(rate, rng, 0.35, 300, 1200); break;
                case "levelup": b = Chime(rate, new[] { 60.0, 64, 67, 72, 76, 79, 84 }, 1.4, 0.11); break;
                case "loot": b = Chime(rate, new[] { 79.0, 86 }, 0.3, 0.06); break;
                case "coins": b = Coins(rate, rng); break;
                case "mail": b = Chime(rate, new[] { 76.0, 72 }, 0.5, 0.18); break;
                case "death": b = Sweep(rate, 330, 110, 1.2, 0.6); break;
                case "error": b = Buzz(rate); break;
                case "emote": b = Chime(rate, new[] { 81.0 }, 0.2, 0); break;
                case "ui_click": b = Tick(rate, rng); break;
                case "ui_open": b = Whoosh(rate, rng, 0.18, 800, 3000); break;
                case "ui_close": b = Whoosh(rate, rng, 0.18, 3000, 800); break;
                case "mount": b = Whoosh(rate, rng, 0.4, 200, 900); break;
                case "quest_accept": b = Chime(rate, new[] { 67.0, 72 }, 0.5, 0.12); break;
                case "quest_done": b = Chime(rate, new[] { 72.0, 76, 79, 84 }, 0.9, 0.12); break;
                default: b = Chime(rate, new[] { 60.0 + rng.Range(0, 24) }, 0.25, 0); break;
            }
            Normalize(b, 0.9f);
            return b;
        }

        private static float[] Hit(int rate, Rng rng, double len, double weight)
        {
            var b = Buffer(len, rate);
            double lp = 0;
            for (int i = 0; i < b.Length; i++)
            {
                double t = (double)i / rate, env = Math.Exp(-t * 28 / weight);
                lp += (rng.Signed() - lp) * 0.25;
                double thump = Math.Sin(Tau * (70 + 90 * Math.Exp(-t * 40)) * t) * Math.Exp(-t * 18);
                b[i] = (float)(lp * env * 0.8 + thump * weight * 0.7);
            }
            return b;
        }

        private static float[] Whoosh(int rate, Rng rng, double len, double f0, double f1)
        {
            var b = Buffer(len, rate);
            double y1 = 0, y2 = 0;
            for (int i = 0; i < b.Length; i++)
            {
                double p = (double)i / b.Length, f = f0 + (f1 - f0) * p;
                double r = 0.96, w = Tau * f / rate;
                // Resonanzfilter (Bandpass) über weißem Rauschen
                double y = rng.Signed() * (1 - r) + 2 * r * Math.Cos(w) * y1 - r * r * y2;
                y2 = y1; y1 = y;
                b[i] = (float)(y * Math.Sin(Math.PI * p) * 8);
            }
            return b;
        }

        private static float[] Metal(int rate, Rng rng, double len)
        {
            var b = Buffer(len, rate);
            double[] partials = { 1, 2.76, 5.4, 8.93 };
            double f = 420 + rng.Range(0, 120);
            for (int i = 0; i < b.Length; i++)
            {
                double t = (double)i / rate, s = 0;
                for (int k = 0; k < partials.Length; k++) s += Math.Sin(Tau * f * partials[k] * t) * Math.Exp(-t * (8 + k * 6)) / (k + 1);
                b[i] = (float)s;
            }
            return b;
        }

        /// <summary>Glockenartige Töne nacheinander (MIDI-Noten, Abstand in Sekunden).</summary>
        private static float[] Chime(int rate, double[] notes, double len, double gap)
        {
            var b = Buffer(len + gap * notes.Length, rate);
            for (int n = 0; n < notes.Length; n++)
            {
                double f = Midi(notes[n]);
                int start = (int)(gap * n * rate);
                for (int i = start; i < b.Length; i++)
                {
                    double t = (double)(i - start) / rate;
                    double env = Math.Min(1, t * 200) * Math.Exp(-t * 6);
                    b[i] += (float)((Math.Sin(Tau * f * t) + 0.3 * Math.Sin(Tau * f * 2.01 * t) + 0.12 * Math.Sin(Tau * f * 3.98 * t)) * env);
                }
            }
            return b;
        }

        private static float[] Coins(int rate, Rng rng)
        {
            var b = Buffer(0.6, rate);
            for (int c = 0; c < 6; c++)
            {
                int start = (int)(rng.Float() * 0.4 * rate);
                double f = 2500 + rng.Float() * 2000;
                for (int i = start; i < b.Length; i++)
                {
                    double t = (double)(i - start) / rate;
                    b[i] += (float)(Math.Sin(Tau * f * t) * Math.Exp(-t * 40) + Math.Sin(Tau * f * 1.5 * t) * Math.Exp(-t * 60) * 0.5);
                }
            }
            return b;
        }

        private static float[] Sweep(int rate, double f0, double f1, double len, double vibrato)
        {
            var b = Buffer(len, rate);
            double phase = 0;
            for (int i = 0; i < b.Length; i++)
            {
                double p = (double)i / b.Length, t = (double)i / rate;
                double f = f0 * Math.Pow(f1 / f0, p) * (1 + 0.02 * vibrato * Math.Sin(Tau * 5 * t));
                phase += Tau * f / rate;
                b[i] = (float)((Math.Sin(phase) + 0.3 * Math.Sin(phase * 2)) * Math.Sin(Math.PI * p));
            }
            return b;
        }

        private static float[] Buzz(int rate)
        {
            var b = Buffer(0.3, rate);
            for (int i = 0; i < b.Length; i++)
            {
                double t = (double)i / rate;
                double gate = (t < 0.12 || t > 0.17) ? 1 : 0;
                double saw = 2 * (t * 140 % 1) - 1;
                b[i] = (float)(saw * 0.6 * gate * Math.Exp(-t * 4));
            }
            return b;
        }

        private static float[] Tick(int rate, Rng rng)
        {
            var b = Buffer(0.05, rate);
            for (int i = 0; i < b.Length; i++)
            {
                double t = (double)i / rate;
                b[i] = (float)((Math.Sin(Tau * 1800 * t) * 0.6 + rng.Signed() * 0.4) * Math.Exp(-t * 120));
            }
            return b;
        }

        private static float[] Footstep(string key, int rate, Rng rng)
        {
            bool soft = key.Contains("wald") || key.Contains("gras");
            bool hard = key.Contains("gruft") || key.Contains("stein") || key.Contains("stadt");
            var b = Buffer(0.12, rate);
            double lp = 0, a = soft ? 0.08 : hard ? 0.5 : 0.2;
            for (int i = 0; i < b.Length; i++)
            {
                double t = (double)i / rate;
                lp += (rng.Signed() - lp) * a;
                b[i] = (float)((lp + Math.Sin(Tau * 80 * t) * 0.4) * Math.Exp(-t * 45));
            }
            return b;
        }

        private static float[] Spell(string school, bool impact, int rate, Rng rng)
        {
            double len = impact ? 0.35 : 0.6;
            var b = Buffer(len, rate);
            double lp = 0, phase = 0;
            for (int i = 0; i < b.Length; i++)
            {
                double t = (double)i / rate, p = (double)i / b.Length;
                double env = impact ? Math.Min(1, t * 400) * Math.Exp(-t * 12) : Math.Sin(Math.PI * p);
                double s;
                switch (school)
                {
                    case "fire":
                        lp += (rng.Signed() - lp) * 0.3;
                        s = lp * 1.2 + (rng.Float() > 0.995 ? rng.Signed() * 2 : 0) + Math.Sin(Tau * 60 * t) * 0.4;
                        break;
                    case "frost":
                        s = Math.Sin(Tau * 1760 * t) * 0.4 + Math.Sin(Tau * 2637 * t) * 0.3 + Math.Sin(Tau * 3520 * t + Math.Sin(Tau * 7 * t)) * 0.3;
                        break;
                    case "nature":
                        s = Math.Sin(Tau * Midi(67 + 5 * Math.Floor(p * 4)) * t) * Math.Exp(-(t * 8 % 1) * 6);
                        break;
                    case "shadow":
                        s = (2 * (t * 55 % 1) - 1) * 0.5 + (2 * (t * 55.7 % 1) - 1) * 0.5;
                        lp += (s - lp) * 0.08; s = lp * 2;
                        break;
                    case "holy":
                        s = Math.Sin(Tau * Midi(76) * t) + Math.Sin(Tau * Midi(79) * t) * 0.7 + Math.Sin(Tau * Midi(84) * t) * 0.5;
                        break;
                    default: // arkan
                        phase += Tau * (440 + 220 * Math.Sin(Tau * 9 * t)) / rate;
                        s = Math.Sin(phase + 2 * Math.Sin(Tau * 660 * t));
                        break;
                }
                b[i] = (float)(s * env);
            }
            return b;
        }

        // ================================================================== Umgebung

        public static float[] Ambience(string key, double seconds = 20, int rate = DefaultRate)
        {
            var rng = new Rng(Hash(key));
            int loop = (int)(seconds * rate);
            var b = new float[loop + rate * 2];
            bool night = key.EndsWith("_night", StringComparison.Ordinal);
            double lp = 0, lp2 = 0;
            // Grundrauschen: Wind bzw. Raumton
            double windAmount = key.Contains("wind") || key.Contains("steppe") ? 0.6 : key.Contains("crypt") ? 0.15 : 0.25;
            for (int i = 0; i < b.Length; i++)
            {
                double t = (double)i / rate;
                lp += (rng.Signed() - lp) * 0.02;
                lp2 += (lp - lp2) * 0.05;
                double gust = 0.6 + 0.4 * Math.Sin(Tau * t / 7.3) * Math.Sin(Tau * t / 3.1);
                b[i] = (float)(lp2 * 6 * windAmount * gust);
            }
            int events = (int)seconds * (key.Contains("birds") && !night ? 2 : key.Contains("crypt") ? 1 : night ? 6 : 1);
            for (int e = 0; e < events; e++)
            {
                int start = (int)(rng.Float() * loop);
                if (key.Contains("crypt"))
                {
                    double f = 1200 + rng.Float() * 1500;
                    for (int k = 0; k < 3; k++) // Tropfen mit Echo
                        AddTone(b, start + k * rate / 5, rate, f * (1 - 0.3 * (1 - Math.Exp(-k))), 0.15, 30, 0.35 / (k + 1));
                }
                else if (night)
                {
                    double f = 4200 + rng.Float() * 600; // Grillen
                    for (int k = 0; k < 4; k++) AddTone(b, start + k * rate / 25, rate, f, 0.02, 80, 0.12);
                }
                else
                {
                    double f = 2500 + rng.Float() * 1800; // Vogelruf: kleine Tonfolge
                    int notes = rng.Range(2, 5);
                    for (int k = 0; k < notes; k++) AddChirp(b, start + k * rate / 9, rate, f * (1 + 0.15 * rng.Signed()), 0.08, 0.2);
                }
            }
            var o = FoldLoop(b, loop);
            Normalize(o, 0.6f);
            return o;
        }

        private static void AddTone(float[] b, int start, int rate, double f, double len, double decay, double amp)
        {
            int n = (int)(len * rate);
            for (int i = 0; i < n && start + i < b.Length; i++)
            {
                double t = (double)i / rate;
                b[start + i] += (float)(Math.Sin(Tau * f * t) * Math.Exp(-t * decay) * amp);
            }
        }

        private static void AddChirp(float[] b, int start, int rate, double f, double len, double amp)
        {
            int n = (int)(len * rate);
            double phase = 0;
            for (int i = 0; i < n && start + i < b.Length; i++)
            {
                double p = (double)i / n;
                phase += Tau * f * (1 + 0.4 * Math.Sin(Math.PI * p)) / rate;
                b[start + i] += (float)(Math.Sin(phase) * Math.Sin(Math.PI * p) * amp);
            }
        }

        // ================================================================== Musik

        /// <summary>Stimmung eines Musikstücks, abgeleitet aus dem Schlüssel.</summary>
        public sealed class Mood
        {
            public int[] Scale = { 0, 2, 4, 5, 7, 9, 11 };
            public double Bpm = 84;
            public int Root = 57;
            public bool Drums, Arpeggio = true;
            public double Brightness = 1;
            public string Name = "Wald";
        }

        public static Mood MoodFor(string key)
        {
            var m = new Mood();
            string k = key.ToLowerInvariant();
            if (k.Contains("boss")) { m.Name = "Boss"; m.Scale = new[] { 0, 1, 3, 5, 7, 8, 10 }; m.Bpm = 132; m.Root = 50; m.Drums = true; m.Brightness = 0.7; }
            else if (k.Contains("combat")) { m.Name = "Kampf"; m.Scale = new[] { 0, 2, 3, 5, 7, 8, 10 }; m.Bpm = 120; m.Root = 52; m.Drums = true; m.Brightness = 0.8; }
            else if (k.Contains("ghost")) { m.Name = "Geisterwelt"; m.Scale = new[] { 0, 2, 4, 6, 8, 10 }; m.Bpm = 56; m.Root = 62; m.Arpeggio = false; m.Brightness = 1.3; }
            else if (k.Contains("dungeon") || k.Contains("crypt") || k.Contains("roots") || k.Contains("gruft")) { m.Name = "Gewölbe"; m.Scale = new[] { 0, 2, 3, 5, 7, 9, 10 }; m.Bpm = 64; m.Root = 45; m.Brightness = 0.6; }
            else if (k.Contains("village") || k.Contains("inn") || k.Contains("camp") || k.Contains("city")) { m.Name = "Siedlung"; m.Scale = new[] { 0, 2, 4, 5, 7, 9, 11 }; m.Bpm = 100; m.Root = 60; m.Brightness = 1.1; }
            else if (k.Contains("steppe") || k.Contains("aschen") || k.Contains("sand")) { m.Name = "Steppe"; m.Scale = new[] { 0, 2, 4, 5, 7, 9, 10 }; m.Bpm = 90; m.Root = 52; m.Brightness = 0.9; }
            else { m.Name = "Wald"; m.Scale = new[] { 0, 2, 4, 6, 7, 9, 11 }; m.Bpm = 80; m.Root = 57; }
            if (k.Contains("night")) { m.Bpm *= 0.8; m.Scale = new[] { 0, 2, 3, 5, 7, 8, 10 }; m.Brightness *= 0.7; m.Drums = false; }
            return m;
        }

        /// <summary>Ein schleifbares Musikstück (16 Takte) aus Flächenklang, Arpeggio, Bass und – im Kampf – Schlagwerk.</summary>
        public static float[] Music(string key, int rate = DefaultRate, int bars = 16)
        {
            var rng = new Rng(Hash(key));
            var mood = MoodFor(key);
            double beat = 60.0 / mood.Bpm;
            const int beatsPerBar = 4;
            int loop = (int)(bars * beatsPerBar * beat * rate);
            var b = new float[loop + rate * 3];
            // Akkordfolge aus Stufen der Tonleiter, je Takt ein Akkord
            int[][] progressions = { new[] { 0, 5, 3, 4 }, new[] { 0, 3, 4, 3 }, new[] { 0, 4, 5, 3 }, new[] { 5, 3, 0, 4 }, new[] { 0, 2, 3, 4 } };
            var prog = progressions[rng.Range(0, progressions.Length)];
            int Note(int degree, int octave)
            {
                int n = mood.Scale.Length;
                int d = ((degree % n) + n) % n;
                return mood.Root + mood.Scale[d] + 12 * (octave + (int)Math.Floor((double)degree / n));
            }
            int[] arp = { 0, 2, 4, 2, 7, 4, 2, 4 };
            for (int bar = 0; bar < bars; bar++)
            {
                int deg = prog[bar % prog.Length];
                int barStart = (int)(bar * beatsPerBar * beat * rate);
                double barLen = beatsPerBar * beat;
                foreach (int add in new[] { 0, 2, 4 })
                    AddPad(b, barStart, rate, Midi(Note(deg + add, 0)), barLen * 1.4, 0.12 * mood.Brightness);
                for (int q = 0; q < beatsPerBar; q += 2)
                    AddPluck(b, barStart + (int)(q * beat * rate), rate, Midi(Note(deg, -1)), beat * 1.8, 0.35, 3);
                if (mood.Arpeggio)
                    for (int e = 0; e < beatsPerBar * 2; e++)
                    {
                        if (rng.Float() < 0.25) continue;
                        int step = arp[e] + (rng.Float() < 0.2 ? 1 : 0);
                        AddPluck(b, barStart + (int)(e * beat / 2 * rate), rate, Midi(Note(deg + step, 1)), beat * 1.2, 0.16 * mood.Brightness, 6);
                    }
                if (mood.Drums)
                    for (int e = 0; e < beatsPerBar * 2; e++)
                    {
                        int pos = barStart + (int)(e * beat / 2 * rate);
                        if (e % 4 == 0) AddKick(b, pos, rate, 0.5);
                        else if (e % 4 == 2 && mood.Name == "Boss") AddKick(b, pos, rate, 0.35);
                        AddHat(b, pos, rate, rng, e % 2 == 0 ? 0.06 : 0.04);
                    }
            }
            var o = FoldLoop(b, loop);
            Normalize(o, 0.75f);
            return o;
        }

        private static void AddPad(float[] b, int start, int rate, double f, double len, double amp)
        {
            int n = (int)(len * rate);
            for (int i = 0; i < n && start + i < b.Length; i++)
            {
                double t = (double)i / rate, p = (double)i / n;
                double env = Math.Min(1, t / 0.6) * Math.Min(1, (1 - p) * 3);
                double s = Math.Sin(Tau * f * t) + Math.Sin(Tau * f * 1.003 * t) * 0.8 + Math.Sin(Tau * f * 2.001 * t) * 0.2;
                b[start + i] += (float)(s * env * amp);
            }
        }

        private static void AddPluck(float[] b, int start, int rate, double f, double len, double amp, double decay)
        {
            int n = (int)(len * rate);
            for (int i = 0; i < n && start + i < b.Length; i++)
            {
                double t = (double)i / rate;
                double env = Math.Min(1, t * 300) * Math.Exp(-t * decay);
                double tri = 2 * Math.Abs(2 * (t * f % 1) - 1) - 1;
                b[start + i] += (float)((tri * 0.6 + Math.Sin(Tau * f * t) * 0.4) * env * amp);
            }
        }

        private static void AddKick(float[] b, int start, int rate, double amp)
        {
            int n = (int)(0.3 * rate);
            double phase = 0;
            for (int i = 0; i < n && start + i < b.Length; i++)
            {
                double t = (double)i / rate;
                phase += Tau * (45 + 110 * Math.Exp(-t * 30)) / rate;
                b[start + i] += (float)(Math.Sin(phase) * Math.Exp(-t * 9) * amp);
            }
        }

        private static void AddHat(float[] b, int start, int rate, Rng rng, double amp)
        {
            int n = (int)(0.05 * rate);
            double prev = 0;
            for (int i = 0; i < n && start + i < b.Length; i++)
            {
                double t = (double)i / rate, x = rng.Signed();
                b[start + i] += (float)((x - prev) * Math.Exp(-t * 80) * amp);
                prev = x;
            }
        }

        /// <summary>Alle Effektschlüssel, die das Spiel verwendet (für Vorab-Erzeugung und Tests).</summary>
        public static IEnumerable<string> KnownEffects()
        {
            foreach (var s in new[] { "hit_physical", "hit_crit", "swing_miss", "block", "resist", "heal", "cast_generic", "levelup", "loot", "coins", "mail", "death", "error", "emote", "ui_click", "ui_open", "ui_close", "mount", "quest_accept", "quest_done" })
                yield return s;
            foreach (var school in new[] { "holy", "fire", "nature", "frost", "shadow", "arcane" })
            {
                yield return $"spell_{school}_cast";
                yield return $"spell_{school}_impact";
            }
            foreach (var g in new[] { "wald", "steppe", "gruft", "stadt" }) yield return "footstep_" + g;
        }
    }
}
