namespace SistemPusulasi;

public sealed class AppSettings
{
    public bool AutoRepair { get; set; } = true;
    public int DeepScanIntervalDays { get; set; } = 7;
    public int ScheduleHour { get; set; } = 18;
    public bool ShowReportAfterScheduledScan { get; set; } = true;
    public DateTimeOffset? LastAutoRepairUtc { get; set; }
    public string GitHubRepository { get; set; } = "utkuozk/system-compass";
    public bool CheckAppUpdates { get; set; } = true;
    public string Language { get; set; } = "tr";
}
public sealed class Finding
{
    public string Category { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "Unknown";
    public string Detail { get; set; } = "";
}
public sealed class ScanReport
{
    public string Id { get; set; } = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }
    public bool DeepScan { get; set; }
    public bool IsDemo { get; set; }
    public bool Interrupted { get; set; }
    public string AppVersion { get; set; } = "";
    public string Summary { get; set; } = "Kontrol bekleniyor";
    public bool RepairAttempted { get; set; }
    public bool RebootRequired { get; set; }
    public List<Finding> Findings { get; set; } = new();
    public List<ScanStep> Steps { get; set; } = new();
    public string ReportDirectory { get; set; } = "";
}
public sealed class ScanProgress
{
    public int ProcessId { get; set; }
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Message { get; set; } = "";
    public bool Running { get; set; }
    public bool Interrupted { get; set; }
    public string ScanId { get; set; } = "";
    public bool DeepScan { get; set; }
    public bool ManualRepair { get; set; }
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedUtc { get; set; }
    public DateTimeOffset? ProcessStartedUtc { get; set; }
    public string CurrentStepId { get; set; } = "";
    public List<ScanStep> Steps { get; set; } = new();
    public List<Finding> PartialFindings { get; set; } = new();
}
public sealed class ScanStep
{
    public string Id { get; set; } = "";
    public int Order { get; set; }
    public string Title { get; set; } = "";
    // Execution state is separate from the result: Completed never means Healthy.
    public string Status { get; set; } = "Pending";
    public string Outcome { get; set; } = "";
    public string Detail { get; set; } = "";
    public DateTimeOffset? StartedUtc { get; set; }
    public DateTimeOffset? FinishedUtc { get; set; }
}
