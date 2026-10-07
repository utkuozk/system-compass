namespace SistemPusulasi;

/// <summary>Coordinates only inventories backed by Windows Update; software runs independently.</summary>
internal sealed class InventoryScanGate
{
    private readonly SemaphoreSlim windowsUpdate = new(1, 1);

    internal async Task<IDisposable> EnterAsync(string kind, CancellationToken cancellationToken = default,
        IProgress<string>? progress = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (kind is not ("windows" or "drivers")) return new Lease(null);
        if (!await windowsUpdate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            progress?.Report("Diğer Windows Update taramasının tamamlanması için sırada bekleniyor…");
            await windowsUpdate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        return new Lease(windowsUpdate);
    }

    private sealed class Lease(SemaphoreSlim? semaphore) : IDisposable
    {
        private SemaphoreSlim? ownedSemaphore = semaphore;
        public void Dispose() => Interlocked.Exchange(ref ownedSemaphore, null)?.Release();
    }
}
