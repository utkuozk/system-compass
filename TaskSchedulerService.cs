using System.IO;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace SistemPusulasi;

/// <summary>Installs the app's per-user daily scan in Windows Task Scheduler.</summary>
public static class SchedulerService
{
    private const string TaskName = "SistemPusulasi-Daily";
    private const string OwnershipMarker = "SistemPusulasi managed task v1";
    private const int CreateOrUpdate = 6;
    private const int InteractiveToken = 3;

    public static string Register(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (settings.ScheduleHour is < 0 or > 23)
            return "Zamanlama saati 0 ile 23 arasında olmalıdır.";
        if (!IsAdministrator())
            return "Zamanlama kaydedilemedi. Yönetici izni gerekli; uygulamayı --enable-schedule ile yükseltilmiş olarak başlatın.";

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return "Uygulama çalıştırılabilir dosyasının yolu belirlenemedi.";

        try
        {
            dynamic service = Connect();
            dynamic root = service.GetFolder("\\");
            try
            {
                dynamic existing = root.GetTask(TaskName);
                if (!IsOurs(existing, exe))
                    return "Aynı adlı görev var ancak bu uygulamaya ait değil. Görev korunuyor; değişiklik yapılmadı.";
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002))
            {
                // ERROR_FILE_NOT_FOUND: this stable task name is available.
            }

            var sid = WindowsIdentity.GetCurrent().User?.Value;
            if (string.IsNullOrWhiteSpace(sid))
                return "Geçerli Windows kullanıcısının SID bilgisi alınamadı.";

            string xml = BuildTaskXml(exe, sid, settings.ScheduleHour);
            _ = root.RegisterTask(TaskName, xml, CreateOrUpdate, null, null, InteractiveToken, null);
            return $"Günlük tarama görevi {settings.ScheduleHour:00}:00 için kaydedildi.";
        }
        catch (Exception ex)
        {
            return $"Zamanlama kaydedilemedi: {ex.GetBaseException().Message}";
        }
    }

    public static string Remove()
    {
        if (!IsAdministrator())
            return "Zamanlama kaldırılamadı. Yönetici izni gerekli.";
        try
        {
            dynamic service = Connect();
            dynamic root = service.GetFolder("\\");
            dynamic task;
            try { task = root.GetTask(TaskName); }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002))
            { return "Kurulu zamanlama görevi bulunamadı."; }

            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe) || !IsOurs(task, exe))
                return "Görev sahiplik doğrulamasından geçmedi; korunuyor.";
            root.DeleteTask(TaskName, 0);
            return "Günlük tarama görevi kaldırıldı.";
        }
        catch (Exception ex)
        {
            return $"Zamanlama kaldırılamadı: {ex.GetBaseException().Message}";
        }
    }

    public static bool IsInstalled()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return false;
            dynamic service = Connect();
            dynamic root = service.GetFolder("\\");
            dynamic task = root.GetTask(TaskName);
            return IsOurs(task, exe);
        }
        catch { return false; }
    }

    /// <summary>Human-readable status; distinguishes missing, foreign, and inaccessible tasks.</summary>
    public static string GetStatus()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exe)) return "Uygulama yolu belirlenemedi.";
            dynamic service = Connect();
            dynamic root = service.GetFolder("\\");
            dynamic task;
            try { task = root.GetTask(TaskName); }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002))
            { return "Zamanlama kurulu değil."; }
            return IsOurs(task, exe) ? "Günlük tarama zamanlaması kurulu." : "Aynı adlı görev var ancak bu uygulamaya ait değil.";
        }
        catch (Exception ex) { return $"Zamanlama durumu okunamadı: {ex.GetBaseException().Message}"; }
    }

    private static dynamic Connect()
    {
        dynamic service = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service")!)!;
        service.Connect();
        return service;
    }

    private static bool IsOurs(dynamic task, string exe)
    {
        try
        {
            string xml = (string)task.Xml;
            var doc = XDocument.Parse(xml);
            XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
            string? description = (string?)doc.Root?.Element(ns + "RegistrationInfo")?.Element(ns + "Description");
            string? command = (string?)doc.Root?.Element(ns + "Actions")?.Element(ns + "Exec")?.Element(ns + "Command");
            return description == OwnershipMarker && PathsEqual(command, exe);
        }
        catch { return false; }
    }

    private static bool PathsEqual(string? left, string right) =>
        !string.IsNullOrWhiteSpace(left) &&
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static bool IsAdministrator()
    {
        if (!OperatingSystem.IsWindows()) return false;
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    internal static string BuildTaskXml(string exe, string sid, int hour)
    {
        var now = DateTime.Now;
        var start = new DateTime(now.Year, now.Month, now.Day, hour, 0, 0);
        if (start <= now) start = start.AddDays(1);
        XNamespace ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";
        var doc = new XDocument(new XElement(ns + "Task", new XAttribute("version", "1.4"),
            new XElement(ns + "RegistrationInfo", new XElement(ns + "Description", OwnershipMarker)),
            new XElement(ns + "Triggers", new XElement(ns + "CalendarTrigger",
                new XElement(ns + "StartBoundary", start.ToString("yyyy-MM-dd'T'HH:mm:ss")),
                new XElement(ns + "Enabled", "true"),
                new XElement(ns + "ScheduleByDay", new XElement(ns + "DaysInterval", "1")))),
            new XElement(ns + "Principals", new XElement(ns + "Principal", new XAttribute("id", "Author"),
                new XElement(ns + "UserId", sid), new XElement(ns + "LogonType", "InteractiveToken"),
                new XElement(ns + "RunLevel", "HighestAvailable"))),
            new XElement(ns + "Settings",
                new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                new XElement(ns + "StartWhenAvailable", "true"),
                new XElement(ns + "DisallowStartIfOnBatteries", "true"),
                new XElement(ns + "StopIfGoingOnBatteries", "false"),
                new XElement(ns + "AllowHardTerminate", "false"),
                new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                new XElement(ns + "WakeToRun", "false"),
                new XElement(ns + "RunOnlyIfIdle", "true"),
                new XElement(ns + "IdleSettings", new XElement(ns + "Duration", "PT10M"),
                    new XElement(ns + "WaitTimeout", "PT2H"), new XElement(ns + "StopOnIdleEnd", "false"),
                    new XElement(ns + "RestartOnIdle", "false"))),
            new XElement(ns + "Actions", new XAttribute("Context", "Author"), new XElement(ns + "Exec",
                new XElement(ns + "Command", exe), new XElement(ns + "Arguments", "--scheduled")))));
        return doc.ToString(SaveOptions.DisableFormatting);
    }
}

