using System.IO;
using System.Text.Json;
namespace SistemPusulasi;

public static class LocalStore
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SistemPusulasi");
    public static string ReportsRoot => Path.Combine(Root, "Reports");
    public static string SettingsPath => Path.Combine(Root, "settings.json");
    public static string ProgressPath => Path.Combine(Root, "progress.json");
    public static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented=true, PropertyNameCaseInsensitive=true };
    public static void Initialize() { Directory.CreateDirectory(Root); Directory.CreateDirectory(ReportsRoot); }
    public static void Write<T>(string path,T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        File.WriteAllText(tmp,JsonSerializer.Serialize(value,JsonOptions));
        File.Move(tmp,path,true);
    }
    public static T? Read<T>(string path) { try { return JsonSerializer.Deserialize<T>(File.ReadAllText(path),JsonOptions); } catch { return default; } }
    public static AppSettings LoadSettings() => Read<AppSettings>(SettingsPath) ?? new();
    public static void SaveSettings(AppSettings settings) => Write(SettingsPath,settings);
    public static List<ScanReport> Reports() => Directory.Exists(ReportsRoot) ? Directory.EnumerateFiles(ReportsRoot,"report.json",SearchOption.AllDirectories).Select(Read<ScanReport>).Where(r=>r!=null && !r.IsDemo).Cast<ScanReport>().OrderByDescending(r=>r.StartedUtc).Take(100).ToList() : new();
    public static void SaveReport(ScanReport report) => Write(Path.Combine(report.ReportDirectory,"report.json"),report);
    public static bool DeepDue(AppSettings settings,IEnumerable<ScanReport> reports)
    {
        var last=reports.Where(r=>r.DeepScan && r.FinishedUtc.HasValue && r.FinishedUtc<=DateTimeOffset.UtcNow && r.Findings.Any(f=>f.Category=="Windows" && f.Status is "Healthy" or "Critical" or "Repaired")).OrderByDescending(r=>r.FinishedUtc).FirstOrDefault();
        return last==null || DateTimeOffset.UtcNow-last.FinishedUtc!.Value>=TimeSpan.FromDays(Math.Clamp(settings.DeepScanIntervalDays,1,30));
    }
}
