using System.Threading.Channels;

namespace EternalRealms.Server.Persistence
{
    /// <summary>
    /// Führt alle Schreib- und Ladevorgänge der Datenbank nacheinander in Auftragsreihenfolge aus. Dadurch kann ein
    /// älterer Speicherstand nie einen neueren überschreiben, und ein Charakter wird erst geladen, wenn seine letzte
    /// Speicherung abgeschlossen ist. Wirtschaftsvorgänge (Post, Auktionen, Handel) werden zusammen mit den beteiligten
    /// Charakteren in einer Transaktion geschrieben – so entstehen auch bei einem Absturz keine doppelten Gegenstände.
    /// </summary>
    public sealed class PersistenceQueue : IDisposable
    {
        private readonly Channel<Func<Task>> _jobs = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true });
        private readonly Action<Exception> _onError;
        private readonly Task _worker;
        private int _pending;

        public PersistenceQueue(Action<Exception> onError)
        {
            _onError = onError;
            _worker = Task.Run(RunAsync);
        }

        public int Pending => Volatile.Read(ref _pending);

        public void Enqueue(Func<Task> job)
        {
            Interlocked.Increment(ref _pending);
            if (!_jobs.Writer.TryWrite(job)) Interlocked.Decrement(ref _pending);
        }

        /// <summary>Auftrag mit Ergebnis (z. B. Laden eines Charakters nach allen offenen Speicherungen).</summary>
        public Task<T> EnqueueAsync<T>(Func<Task<T>> job)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            Enqueue(async () =>
            {
                try { tcs.SetResult(await job()); }
                catch (Exception ex) { tcs.SetException(ex); }
            });
            return tcs.Task;
        }

        private async Task RunAsync()
        {
            await foreach (var job in _jobs.Reader.ReadAllAsync())
            {
                try { await job(); }
                catch (Exception ex) { _onError(ex); }
                finally { Interlocked.Decrement(ref _pending); }
            }
        }

        /// <summary>Wartet, bis alle Aufträge erledigt sind. Rückgabe: true, wenn die Warteschlange leer ist.</summary>
        public bool Flush(TimeSpan timeout)
        {
            var until = DateTime.UtcNow + timeout;
            while (Pending > 0 && DateTime.UtcNow < until) Thread.Sleep(10);
            return Pending == 0;
        }

        public void Dispose()
        {
            _jobs.Writer.TryComplete();
            _worker.Wait(TimeSpan.FromSeconds(10));
        }
    }
}
