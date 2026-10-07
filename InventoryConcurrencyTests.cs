namespace SistemPusulasi;

internal static class InventoryConcurrencyTests
{
    internal static async Task<List<string>> Run()
    {
        var results = new List<string>();
        var gate = new InventoryScanGate();
        using var activeWindows = await gate.EnterAsync("windows");
        // A completed acquisition proves software does not join the Windows Update semaphore.
        var software = gate.EnterAsync("software");
        Check(software.IsCompletedSuccessfully, "Software inventory proceeds while Windows inventory holds the gate", results);
        using var softwareLease = await software;

        using var driverCancellation = new CancellationTokenSource();
        var phases = new ImmediateProgress();
        var queuedDriver = gate.EnterAsync("drivers", driverCancellation.Token, phases);
        Check(!queuedDriver.IsCompleted && phases.Values.Count == 1,
            "Driver inventory queues behind Windows and reports its queued phase", results);
        driverCancellation.Cancel();
        var driverCanceled = false;
        try { using var unexpectedLease = await queuedDriver; }
        catch (OperationCanceledException) { driverCanceled = true; }
        Check(driverCanceled, "Queued driver cancellation propagates OperationCanceledException", results);

        using var nextCancellation = new CancellationTokenSource();
        var nextWindows = gate.EnterAsync("windows", nextCancellation.Token);
        Check(!nextWindows.IsCompleted, "Canceled waiter never releases another inventory's active slot", results);
        activeWindows.Dispose();
        using var nextLease = await nextWindows.WaitAsync(TimeSpan.FromSeconds(2));
        Check(nextWindows.IsCompletedSuccessfully, "Released Windows slot admits the next waiting inventory", results);

        // Disposing a lease twice must not increase the semaphore's capacity.
        activeWindows.Dispose();
        using var finalCancellation = new CancellationTokenSource();
        var finalDriver = gate.EnterAsync("drivers", finalCancellation.Token);
        Check(!finalDriver.IsCompleted, "Inventory gate leases release their slot only once", results);
        finalCancellation.Cancel();
        try { using var unexpectedLease = await finalDriver; }
        catch (OperationCanceledException) { }

        using var preCanceled = new CancellationTokenSource();
        preCanceled.Cancel();
        var softwareCanceled = false;
        try { using var unexpectedLease = await gate.EnterAsync("software", preCanceled.Token); }
        catch (OperationCanceledException) { softwareCanceled = true; }
        Check(softwareCanceled, "Independent software inventory still honors pre-cancellation", results);
        return results;
    }

    private static void Check(bool condition, string description, List<string> results)
    {
        if (!condition) throw new InvalidOperationException("FAIL: " + description);
        results.Add("PASS: " + description);
    }

    private sealed class ImmediateProgress : IProgress<string>
    {
        internal List<string> Values { get; } = new();
        public void Report(string value) => Values.Add(value);
    }
}
