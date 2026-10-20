namespace EternalRealms.Server.Persistence
{
    /// <summary>Weltzustand, der Neustarts übersteht (Kapitel 42/47): Wiederkehrzeiten dauerhafter Spawns.</summary>
    public interface IWorldRepository
    {
        Task<Dictionary<string, DateTime>> LoadSpawnTimersAsync();
        /// <summary>Zeitpunkt setzen; null löscht den Eintrag (Kreatur lebt wieder).</summary>
        Task SaveSpawnTimerAsync(string key, DateTime? respawnAt);
    }
}
