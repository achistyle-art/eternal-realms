using System;

namespace EternalRealms.Shared
{
    /// <summary>
    /// Laufende Zeitstatistik (Kapitel 52) über die letzten N Messungen: Mittel, Perzentile, Maximum.
    /// Wird für Bildraten im Client und Taktdauer im Server verwendet.
    /// </summary>
    public sealed class FrameStats
    {
        private readonly float[] _samples;
        private int _count, _next;

        public FrameStats(int window = 600) { _samples = new float[Math.Max(1, window)]; }

        public int Count => _count;
        public long Total { get; private set; }

        public void Add(float ms)
        {
            _samples[_next] = ms;
            _next = (_next + 1) % _samples.Length;
            if (_count < _samples.Length) _count++;
            Total++;
        }

        public float Average
        {
            get { if (_count == 0) return 0; double s = 0; for (int i = 0; i < _count; i++) s += _samples[i]; return (float)(s / _count); }
        }

        public float Max
        {
            get { float m = 0; for (int i = 0; i < _count; i++) m = Math.Max(m, _samples[i]); return m; }
        }

        /// <summary>Wert, unter dem p Prozent der Messungen liegen (z. B. 99).</summary>
        public float Percentile(float p)
        {
            if (_count == 0) return 0;
            var copy = new float[_count];
            Array.Copy(_samples, copy, _count);
            Array.Sort(copy);
            int idx = (int)Math.Ceiling(p / 100f * _count) - 1;
            return copy[Math.Max(0, Math.Min(_count - 1, idx))];
        }

        /// <summary>Bilder pro Sekunde aus der mittleren Bildzeit.</summary>
        public float Fps => Average > 0 ? 1000f / Average : 0;

        /// <summary>„1 % Tiefstwerte“: Bildrate der langsamsten 1 % – zeigt Ruckler besser als der Mittelwert.</summary>
        public float OnePercentLowFps { get { float p = Percentile(99); return p > 0 ? 1000f / p : 0; } }

        public void Clear() { _count = 0; _next = 0; }
    }
}
