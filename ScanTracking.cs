using System.Diagnostics;

namespace SistemPusulasi;

/// <summary>Ordered execution progress. Observers receive detached snapshots and cannot interrupt a scan.</summary>
public sealed class ScanTracker
{
    private readonly ScanProgress state;
    private readonly Action<ScanProgress>? observer;
    private ScanReport? report;
    private int findingsAtStart;

    public ScanTracker(bool deep, bool manualRepair, Action<ScanProgress>? observer = null, string? scanId = null)
    {
        this.observer = observer;
        DateTimeOffset? processStarted = null;
        try { using var process = Process.GetCurrentProcess(); processStarted = process.StartTime.ToUniversalTime(); } catch { }
        state = new ScanProgress { ScanId = scanId ?? DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"), DeepScan = deep || manualRepair, ManualRepair = manualRepair, ProcessId = Environment.ProcessId, ProcessStartedUtc = processStarted, Running = true };
        var stages = new (string Id, string Title)[] {
            ("system-data", "Sistem, donanım ve olay kayıtları"), ("dism", "Windows bileşen deposu (DISM)"),
            ("sfc", "Windows sistem dosyaları (SFC)"), ("chkdsk", "C: dosya sistemi (CHKDSK)"),
            ("repair-eligibility", "Onarım gereksinimi ve koşulları"), ("dism-repair", "DISM onarımı"),
            ("sfc-repair", "SFC onarımı"), ("dism-verify", "Onarım sonrası DISM doğrulaması"), ("sfc-verify", "Onarım sonrası SFC doğrulaması"),
            ("updates-windows", "Windows güncelleme envanteri"), ("updates-drivers", "Sürücü güncelleme envanteri"),
            ("updates-software", "Yazılım güncelleme envanteri"), ("antivirus", "Antivirüs koruma durumu"), ("report", "Raporun kaydedilmesi") };
        state.Steps = stages.Select((stage, index) => new ScanStep { Id = stage.Id, Title = stage.Title, Order = index + 1 }).ToList();
        if (!state.DeepScan) {
            foreach(var id in new[]{"sfc","chkdsk"}) { var step=Step(id); step.Status="Skipped";step.Detail="Hızlı taramada çalıştırılmaz; ayrıntılı tarama gerekir.";step.FinishedUtc=state.StartedUtc; }
        }
        if (!state.DeepScan || manualRepair) {
            foreach(var id in new[]{"updates-windows","updates-drivers","updates-software"}) { var step=Step(id); step.Status="Skipped";step.Detail=manualRepair?"İstenen onarımın kapsamı dışında.":"Güncelleme envanteri ayrıntılı taramada kontrol edilir.";step.FinishedUtc=state.StartedUtc; }
        }
    }

    public string ScanId => state.ScanId;
    public DateTimeOffset StartedUtc => state.StartedUtc;
    public void Attach(ScanReport value) { report = value; report.Id = ScanId; report.StartedUtc = StartedUtc; }
    public void Message(string message) { state.Message = message; Publish(); }
    public void Start(string id, string message)
    {
        var step = Step(id);
        step.Status = "Running"; step.StartedUtc = DateTimeOffset.UtcNow; step.FinishedUtc = null;
        step.Detail = message; state.CurrentStepId = id; state.Message = message;
        findingsAtStart = report?.Findings.Count ?? 0; Publish();
    }
    public void FailActive(string reason)
    {
        state.Interrupted=true;
        foreach (var step in state.Steps.Where(s=>s.Status=="Running")) {
            step.Status="Unknown";step.Outcome="Unknown";step.Detail=reason;step.FinishedUtc=DateTimeOffset.UtcNow;
        }
        state.CurrentStepId=""; state.Message=reason; Publish();
    }
    public void Finish(string id, string? outcome = null, string? detail = null)
    {
        var step = Step(id);
        step.Outcome = outcome ?? Outcome(report?.Findings.Skip(findingsAtStart) ?? Array.Empty<Finding>());
        step.Status = step.Outcome == "Unknown" ? "Unknown" : "Completed";
        step.Detail = detail ?? string.Join("\n", report?.Findings.Skip(findingsAtStart).Select(f => f.Title + ": " + f.Detail) ?? Array.Empty<string>());
        step.FinishedUtc = DateTimeOffset.UtcNow;
        if (state.CurrentStepId == id) state.CurrentStepId = "";
        Publish();
    }
    public void Skip(string id, string reason)
    {
        var step = Step(id); step.Status = "Skipped"; step.Outcome = ""; step.Detail = reason; step.FinishedUtc = DateTimeOffset.UtcNow;
        if (state.CurrentStepId == id) state.CurrentStepId = ""; Publish();
    }
    public void SkipPending(IEnumerable<string> ids, string reason) { foreach (var id in ids) if (Step(id).Status == "Pending") Skip(id, reason); }
    public void ResolveUnfinished(string reason, params string[] exceptIds)
    {
        foreach(var step in state.Steps.Where(s=>(s.Status is "Running" or "Pending") && !exceptIds.Contains(s.Id))) {
            step.Status="Unknown";step.Outcome="Unknown";step.Detail=reason;step.FinishedUtc=DateTimeOffset.UtcNow;
            if(state.CurrentStepId==step.Id) state.CurrentStepId="";
        }
        Publish();
    }
    public void Stop(string message, bool interrupted = false)
    {
        state.Running = false; state.Interrupted |= interrupted; state.FinishedUtc = DateTimeOffset.UtcNow; state.CurrentStepId = ""; state.Message = message;
        foreach (var step in state.Steps.Where(s => s.Status is "Running" or "Pending")) {
            step.Status = "Unknown"; step.Outcome = "Unknown"; step.FinishedUtc = state.FinishedUtc;
            step.Detail = interrupted ? "Tarama tamamlanamadı; bu adımın sonucu doğrulanamadı." : "Bu adımın tamamlandığı doğrulanamadı.";
        }
        Publish();
    }
    private ScanStep Step(string id) => state.Steps.Single(s => s.Id == id);
    private void Publish()
    {
        state.UpdatedUtc = DateTimeOffset.UtcNow;
        state.PartialFindings = report?.Findings.Select(Clone).ToList() ?? new();
        if (report != null) { report.Steps = state.Steps.Select(CloneStep).ToList(); report.Interrupted = state.Interrupted; }
        try { observer?.Invoke(Snapshot()); } catch { /* Persistence/UI observers must never interrupt servicing. */ }
    }
    internal ScanProgress Snapshot() => new() {
        ScanId=state.ScanId, ProcessId=state.ProcessId, ProcessStartedUtc=state.ProcessStartedUtc, Running=state.Running, Interrupted=state.Interrupted,
        DeepScan=state.DeepScan, ManualRepair=state.ManualRepair, StartedUtc=state.StartedUtc, FinishedUtc=state.FinishedUtc,
        UpdatedUtc=state.UpdatedUtc, Message=state.Message, CurrentStepId=state.CurrentStepId,
        PartialFindings=state.PartialFindings.Select(Clone).ToList(), Steps=state.Steps.Select(CloneStep).ToList() };
    private static ScanStep CloneStep(ScanStep s) => new() { Id=s.Id,Order=s.Order,Title=s.Title,Status=s.Status,Outcome=s.Outcome,Detail=s.Detail,StartedUtc=s.StartedUtc,FinishedUtc=s.FinishedUtc };
    private static Finding Clone(Finding f) => new() { Category=f.Category,Title=f.Title,Status=f.Status,Detail=f.Detail };
    internal static string Outcome(IEnumerable<Finding> findings)
    {
        var statuses=findings.Select(f=>f.Status).ToHashSet();
        return statuses.Contains("Critical")?"Critical":statuses.Contains("Warning")?"Warning":statuses.Contains("Unknown")?"Unknown":statuses.Contains("Repaired")?"Repaired":statuses.Contains("Healthy")?"Healthy":"Info";
    }
    public static ScanProgress NormalizeStopped(ScanProgress snapshot)
    {
        if (!snapshot.Running) return snapshot;
        snapshot.Running=false; snapshot.Interrupted=true; snapshot.CurrentStepId=""; snapshot.FinishedUtc ??= snapshot.UpdatedUtc;
        snapshot.Message="Tarama işlemi durdu; tamamlanmayan kontrollerin sonucu bilinmiyor.";
        foreach(var step in snapshot.Steps.Where(s=>s.Status is "Running" or "Pending")) {
            step.Status="Unknown";step.Outcome="Unknown";step.FinishedUtc ??= snapshot.FinishedUtc;
            step.Detail="Tarama işlemi durdu; bu kontrolün sonucu doğrulanamadı.";
        }
        return snapshot;
    }
}
