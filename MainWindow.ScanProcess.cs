using System.Diagnostics;

namespace SistemPusulasi;

public partial class MainWindow
{
    private static bool IsScanAlive(ScanProgress? progress)
    {
        if(progress?.Running!=true || !IsAlive(progress.ProcessId))return false;
        if(!progress.ProcessStartedUtc.HasValue)return true; // Older progress files have only a PID.
        try {
            using var process=Process.GetProcessById(progress.ProcessId);
            return (new DateTimeOffset(process.StartTime.ToUniversalTime())-progress.ProcessStartedUtc.Value).Duration()<TimeSpan.FromSeconds(1);
        } catch(System.ComponentModel.Win32Exception) {
            // A readable live process name remains evidence of activity when start-time access is denied.
            return IsAlive(progress.ProcessId);
        } catch {return false;}
    }
}
