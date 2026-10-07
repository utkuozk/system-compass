namespace SistemPusulasi;

internal static class UpdateInstallerTests
{
    // Pure fixtures only: never launch PowerShell, download packages or install updates.
    internal static IEnumerable<string> Run()
    {
        var results = new List<string>();
        void Check(bool value, string name)
        {
            if (!value) throw new InvalidOperationException("Test failed: " + name);
            results.Add("PASS: " + name);
        }
        var software = new UpdateEntry("Example application", "1.0.0", "2.0.0", "WinGet / winget", "software")
            { PackageId = "Vendor.Example" };
        var windows = new UpdateEntry("Example Windows update", "Bilinmiyor (WUA bildirmiyor)", "KB1234567",
            "Windows Update Agent (yapılandırılmış kaynak)", "windows")
            { UpdateId = "11111111-2222-3333-4444-555555555555", Revision = 3 };
        Check(UpdateInstaller.Validate(software) == null, "Exact WinGet identity and one version can be selected");
        Check(UpdateInstaller.Validate(software with { Source = "WinGet CLI (sınırlı görünür liste)" }) == null,
            "CLI inventory requires the same exact package identity");
        Check(UpdateInstaller.Validate(windows) == null, "Exact WUA update identity and revision can be selected");
        Check(UpdateInstaller.Validate(windows with { Kind = "drivers", AvailableVersion = "WUA sürücü önerisi (2026-01-02)" }) == null,
            "Driver recommendation uses WUA identity rather than an invented driver version");
        Check(UpdateInstaller.Validate(software with { PackageId = "Vendor.Example --all" }) != null,
            "Package identity cannot contain additional command options");
        Check(UpdateInstaller.Validate(software with { PackageId = "Vendor.Example'; reboot; '" }) != null,
            "Package identity cannot contain PowerShell source");
        Check(UpdateInstaller.Validate(software with { AvailableVersion = "2.0.0, 3.0.0" }) != null,
            "Multiple unordered available versions cannot authorize an installation");
        Check(UpdateInstaller.Validate(software with { AvailableVersion = "Bilinmiyor" }) != null,
            "Inventory-only unknown availability cannot authorize an installation");
        Check(UpdateInstaller.Validate(software with { Source = "Windows kurulu program kaydı" }) != null,
            "Installed-program registry records cannot authorize an installation");
        Check(UpdateInstaller.Validate(software with { InstalledVersion = "Unknown" }) != null,
            "Unknown installed version cannot authorize a forced software upgrade");
        Check(UpdateInstaller.Validate(software with { AvailableVersion = "1.0.0" }) != null,
            "An unchanged software version is not an update");
        Check(UpdateInstaller.Validate(windows with { Revision = 0 }) != null &&
            UpdateInstaller.Validate(windows with { UpdateId = "not-a-guid" }) != null &&
            UpdateInstaller.Validate(windows with { UpdateId = Guid.Empty.ToString("D") }) != null,
            "Missing revision, malformed identity and empty identity fail closed");
        Check(UpdateInstaller.Validate(windows with { PackageId = "Vendor.Example" }) != null,
            "Mixed Windows and software identities cannot authorize an installation");
        foreach (var title in new[] { "System Firmware", "Vendor BIOS update", "UEFI update", "Sistem üretici yazılımı" })
            Check(UpdateInstaller.Validate(windows with { Name = title, Kind = "drivers" }) != null,
                "Firmware cannot be installed from a WUA selection: " + title);
        Check(UpdateInstaller.Validate(software with { Name = "Example\nPUSULA_RESULT:Installed" }) != null,
            "Control characters cannot become progress or result protocol lines");
        Check(UpdateInstaller.ParseResult("{}", 0).Status == "Unknown", "Exit zero alone never proves installed");
        Check(UpdateInstaller.ParseResult("{\"Status\":\"Submitted\",\"Detail\":\"started\",\"RebootRequired\":false}", 0).Status == "Unknown",
            "Submission never proves installation completion");
        const string installed = "{\"Status\":\"Installed\",\"Detail\":\"fixture\",\"RebootRequired\":true,\"Verified\":true}";
        Check(UpdateInstaller.ParseResult(installed, 0) is { Status: "Installed", RebootRequired: true },
            "Verified per-update success retains the reboot requirement");
        Check(UpdateInstaller.ParseResult(installed.Replace("\"Verified\":true", "\"Verified\":false"), 0).Status == "Unknown",
            "Unverified install result stays unknown");
        Check(UpdateInstaller.ParseResult(installed.Replace("\"RebootRequired\":true", "\"RebootRequired\":\"true\""), 0).Status == "Unknown",
            "A string reboot flag is an invalid completion result");
        Check(UpdateInstaller.ParseResult(installed, 1).Status == "Unknown",
            "Failed worker exit cannot claim success from a result payload");
        return results;
    }
}
