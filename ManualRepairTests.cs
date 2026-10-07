using static SistemPusulasi.DiagnosticsPolicy;

namespace SistemPusulasi;

internal static class ManualRepairTests
{
    internal static IEnumerable<string> Run()
    {
        var results = new List<string>();
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("Test failed: " + name);
            results.Add("PASS: " + name);
        }
        var now = DateTimeOffset.UtcNow;
        var settings = new AppSettings { AutoRepair = false, LastAutoRepairUtc = now };
        bool Allowed(bool confirmed = true, bool admin = true, bool ac = true, bool reboot = false, bool servicing = false) =>
            RepairPolicy.CanRepair(confirmed, true, settings, admin, ac, reboot, servicing, now);
        Check(Allowed(), "Manual request bypasses automatic opt-out and cooldown");
        Check(!settings.AutoRepair && settings.LastAutoRepairUtc == now, "Eligibility leaves saved repair preference and timestamp untouched");
        Check(!Allowed(confirmed: false), "Manual request cannot repair unverified corruption");
        Check(!Allowed(admin: false), "Manual request requires administrator");
        Check(!Allowed(ac: false), "Manual request requires confirmed AC power");
        Check(!Allowed(reboot: true), "Manual request respects pending reboot");
        Check(!Allowed(servicing: true), "Manual request respects another servicing process");
        Check(!RepairPolicy.CanRepair(true, false, settings, true, true, false, false, now), "Automatic opt-out is still respected");
        settings.AutoRepair = true;
        Check(!RepairPolicy.CanRepair(true, false, settings, true, true, false, false, now), "Automatic cooldown is still respected");
        Check(!RepairPolicy.HasFreshCorruption(false, Integrity.Corrupt, Integrity.Unknown), "Quick stored corruption cannot authorize manual repair");
        Check(RepairPolicy.HasFreshCorruption(true, Integrity.Corrupt, Integrity.Unknown), "Fresh DISM corruption is actionable");
        Check(RepairPolicy.HasFreshCorruption(true, Integrity.Unknown, Integrity.Corrupt), "Fresh SFC corruption is actionable");
        Check(!RepairPolicy.HasFreshCorruption(true, Integrity.Unrepairable, Integrity.Corrupt), "Unrepairable component store blocks writes");
        Check(!RepairPolicy.HasFreshCorruption(true, Integrity.Healthy, Integrity.Unknown), "Unknown verification never authorizes repair");
        var report = new ScanReport { FinishedUtc = now };
        report.Findings.Add(new Finding { Category = "Depolama", Title = "Disk", Status = "Critical" });
        Check(!RepairPolicy.HasActionableFinding(report), "Hardware critical finding never offers Windows repair");
        report.Findings.Add(new Finding { Category = "Windows", Title = "Windows sistem dosyaları", Status = "Critical" });
        Check(RepairPolicy.HasActionableFinding(report), "Canonical Windows corruption offers recheck and repair");
        report.IsDemo = true;
        Check(!RepairPolicy.HasActionableFinding(report), "Demo report cannot offer repair");
        report.IsDemo = false;
        report.FinishedUtc = null;
        Check(!RepairPolicy.HasActionableFinding(report), "Incomplete report cannot offer repair");
        report.FinishedUtc = now;
        report.Findings.Add(new Finding { Category = "Onarım", Title = "Bileşen deposu onarılamıyor", Status = "Critical" });
        Check(!RepairPolicy.HasActionableFinding(report), "Unrepairable report directs recovery instead of repair");
        Check(!RepairPolicy.HasActionableFinding(null), "Missing report cannot offer repair");
        return results;
    }
}
