using static SistemPusulasi.DiagnosticsPolicy;

namespace SistemPusulasi;

internal static class RepairPolicy
{
    // This only advertises a recheck action. Saved findings never authorize DISM/SFC repair.
    internal static bool HasActionableFinding(ScanReport? report) => report is { IsDemo: false, FinishedUtc: not null } &&
        !report.Findings.Any(f => f.Category == "Onarım" && f.Title == "Bileşen deposu onarılamıyor" && f.Status == "Critical") &&
        report.Findings.Any(f => f.Category == "Windows" && f.Status == "Critical" &&
            f.Title is "Windows bileşen deposu" or "Windows sistem dosyaları");

    internal static bool HasFreshCorruption(bool deep, Integrity dism, Integrity sfc) =>
        deep && dism != Integrity.Unrepairable && (dism == Integrity.Corrupt || sfc == Integrity.Corrupt);

    // An explicit one-shot request overrides preference and automatic cooldown only.
    internal static bool CanRepair(bool confirmedCorruption, bool manualRepair, AppSettings settings,
        bool admin, bool acOnline, bool pendingReboot, bool otherServicing, DateTimeOffset now) =>
        CanAutoRepair(confirmedCorruption, manualRepair || settings.AutoRepair, admin, acOnline,
            pendingReboot, otherServicing, manualRepair ? null : settings.LastAutoRepairUtc, now);
}
