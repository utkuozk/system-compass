using System.Diagnostics;
using System.IO;
namespace SistemPusulasi;
public static class InstallService
{
    public static void Install()
    {
        if(!App.IsAdmin()) { App.Elevate("--install"); return; }
        string source=AppContext.BaseDirectory;
        string target=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"SistemPusulasi");
        Directory.CreateDirectory(target);
        foreach(var process in Process.GetProcessesByName("SistemPusulasi")) {
            using(process) {
                if(process.Id==Environment.ProcessId)continue;
                string? runningPath;
                try {runningPath=process.MainModule?.FileName;} catch {throw new IOException("Çalışan Sistem Pusulası işlemi doğrulanamadı. Bakımın bitmesini bekleyip uygulamayı kapatın.");}
                if(string.Equals(runningPath,Path.Combine(target,"SistemPusulasi.exe"),StringComparison.OrdinalIgnoreCase))throw new IOException("Sistem Pusulası çalışıyor. Devam eden bakım varsa bitmesini bekleyin; ardından uygulamayı kapatıp kurulumu tekrar açın.");
            }
        }
        if(!string.Equals(Path.GetFullPath(source).TrimEnd('\\'),target,StringComparison.OrdinalIgnoreCase)) {
            foreach(var name in new[]{"SistemPusulasi.exe","SistemPusulasi.dll","SistemPusulasi.deps.json","SistemPusulasi.runtimeconfig.json","KULLANIM.txt"}) {
                var file=Path.Combine(source,name); if(File.Exists(file)) File.Copy(file,Path.Combine(target,name),true);
            }
        }
        string exe=Path.Combine(target,"SistemPusulasi.exe");
        if(!File.Exists(exe)) throw new IOException("Kurulum dosyası bulunamadı. ZIP paketini tamamen çıkarıp yeniden dene.");
        dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        foreach(var folder in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.Programs)}) {
            dynamic shortcut=shell.CreateShortcut(Path.Combine(folder,"System Compass.lnk")); shortcut.TargetPath=exe; shortcut.WorkingDirectory=target; shortcut.Description="Windows health diagnostics and maintenance"; shortcut.Save();
        }
        Process.Start(new ProcessStartInfo(exe,"--finish-install") {UseShellExecute=true});
    }
}
