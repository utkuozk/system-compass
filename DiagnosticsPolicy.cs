namespace SistemPusulasi;

// Only explicit tool summaries can authorize writes. Exit code alone is never evidence.
public static class DiagnosticsPolicy
{
    public enum Integrity { Healthy, Corrupt, Unknown, Unrepairable }
    public static Integrity Dism(string output, int exitCode)
    {
        if (exitCode != 0) return Integrity.Unknown;
        if (output.Contains("The component store is repairable.", StringComparison.OrdinalIgnoreCase)) return Integrity.Corrupt;
        if (output.Contains("The component store cannot be repaired.", StringComparison.OrdinalIgnoreCase)) return Integrity.Unrepairable;
        if (output.Contains("No component store corruption detected.", StringComparison.OrdinalIgnoreCase)) return Integrity.Healthy;
        return Integrity.Unknown;
    }
    public static Integrity Sfc(string output, int exitCode)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(output, @"\s+", " ");
        if (exitCode != 0) return Integrity.Unknown;
        if (Contains(normalized, "Windows Resource Protection did not find any integrity violations.") ||
            Contains(normalized, "Windows Kaynak Koruması herhangi bir bütünlük ihlali bulamadı.") ||
            Contains(normalized, "Windows Kaynak Koruması bütünlük ihlali ile karşılaşmadı.")) return Integrity.Healthy;
        if (Contains(normalized, "Windows Resource Protection found integrity violations.") ||
            Contains(normalized, "Windows Resource Protection found corrupt files but was unable to fix some of them.") ||
            Contains(normalized, "Windows Kaynak Koruması bütünlük ihlalleri buldu.") ||
            Contains(normalized, "Windows Kaynak Koruması bozuk dosyalar buldu ancak bazılarını onaramadı.") ||
            Contains(normalized, "Windows Kaynak Koruması bozuk dosyalar buldu, ancak bazılarını onaramadı.") ||
            Contains(normalized, "Windows Kaynak Koruması bozuk dosyalar buldu ancak bazılarını düzeltemedi.")) return Integrity.Corrupt;
        return Integrity.Unknown;
    }
    public static bool DismRepairSucceeded(string output, int exitCode) => exitCode == 0 &&
        Contains(output, "The restore operation completed successfully.");
    public static bool SfcRepairSucceeded(string output, int exitCode) => exitCode == 0 &&
        (Contains(output, "Windows Resource Protection found corrupt files and successfully repaired them.") ||
         Contains(output, "Windows Kaynak Koruması bozuk dosyalar buldu ve bunları başarıyla onardı.") || Sfc(output, exitCode) == Integrity.Healthy);
    public static bool CanAutoRepair(bool confirmedCorruption, bool enabled, bool admin, bool acOnline, bool pendingReboot, bool otherServicing, DateTimeOffset? lastAttempt, DateTimeOffset now) =>
        confirmedCorruption && enabled && admin && acOnline && !pendingReboot && !otherServicing &&
        (!lastAttempt.HasValue || now - lastAttempt.Value >= TimeSpan.FromDays(7));
    private static bool Contains(string output, string phrase) => output.Contains(phrase, StringComparison.OrdinalIgnoreCase);
}
