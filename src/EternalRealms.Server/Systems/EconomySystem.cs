using EternalRealms.Server.Persistence;
using EternalRealms.Server.World;

namespace EternalRealms.Server.Systems
{
    /// <summary>
    /// Einzige Stelle, an der sich Geld ändert. Jede Bewegung wird mit Grund protokolliert (Tabelle economy_log) und
    /// nach Quelle/Senke aufsummiert, damit sich Geldschöpfung und -vernichtung (Inflation) beobachten lassen.
    /// </summary>
    public sealed class EconomySystem
    {
        private readonly GameWorld _w;
        private readonly List<LedgerEntry> _pending = new();
        private float _flushTimer = 5;

        /// <summary>Summen je Grund seit Serverstart: positiv = ins Spiel gebracht, negativ = entzogen oder umverteilt.</summary>
        public Dictionary<string, long> FlowByReason { get; } = new();

        public EconomySystem(GameWorld world) { _w = world; }

        public void Credit(Player p, long amount, string reason, string reference = "")
        {
            if (amount <= 0) return;
            p.ApplyMoney(amount);
            Record(p, amount, reason, reference);
        }

        /// <summary>Zieht Geld ab, wenn genug vorhanden ist. Rückgabe: erfolgreich.</summary>
        public bool Debit(Player p, long amount, string reason, string reference = "")
        {
            if (amount < 0) return false;
            if (amount == 0) return true;
            if (p.Money < amount) return false;
            p.ApplyMoney(-amount);
            Record(p, -amount, reason, reference);
            return true;
        }

        private void Record(Player p, long delta, string reason, string reference)
        {
            FlowByReason[reason] = FlowByReason.GetValueOrDefault(reason) + delta;
            _pending.Add(new LedgerEntry { CharacterId = p.Data.Id, CharacterName = p.Name, Delta = delta, Balance = p.Money, Reason = reason, Reference = reference });
        }

        /// <summary>Schreibt die beteiligten Charaktere und alle Änderungen in einer Transaktion.</summary>
        public void Commit(EconomyCommit commit, params Player[] players)
        {
            foreach (var p in players.Distinct())
            {
                if (p.Data.Id <= 0) continue;
                p.SyncToData();
                commit.Characters.Add(p.Data.Clone());
            }
            commit.Ledger.AddRange(_pending);
            _pending.Clear();
            if (commit.IsEmpty || _w.EconomyStore == null || _w.Persistence == null) return;
            var store = _w.EconomyStore;
            _w.Persistence.Enqueue(() => store.CommitAsync(commit));
        }

        public void Update(float dt)
        {
            _flushTimer -= dt;
            if (_flushTimer > 0) return;
            _flushTimer = 5;
            if (_pending.Count > 0) Commit(new EconomyCommit());
        }

        public long Minted => FlowByReason.Values.Where(v => v > 0).Sum();
    }
}
