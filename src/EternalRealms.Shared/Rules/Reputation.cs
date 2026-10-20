namespace EternalRealms.Shared.Rules
{
    public static class Reputation
    {
        public const int Min = -42000, Max = 42999;

        /// <summary>Rufstufen (Kapitel 24): Hasserfüllt bis Ehrfürchtig.</summary>
        public static string Standing(int value) => value switch
        {
            < -6000 => "Hasserfüllt",
            < -3000 => "Feindselig",
            < 0 => "Unfreundlich",
            < 3000 => "Neutral",
            < 9000 => "Freundlich",
            < 21000 => "Wohlwollend",
            < 42000 => "Respektvoll",
            _ => "Ehrfürchtig",
        };

        /// <summary>Fortschritt innerhalb der aktuellen Stufe (für die Rufleiste).</summary>
        public static (int current, int size) WithinStanding(int value)
        {
            int[] bounds = { Min, -6000, -3000, 0, 3000, 9000, 21000, 42000, Max + 1 };
            for (int i = 0; i < bounds.Length - 1; i++)
                if (value < bounds[i + 1]) return (value - bounds[i], bounds[i + 1] - bounds[i]);
            return (1000, 1000);
        }
    }
}
