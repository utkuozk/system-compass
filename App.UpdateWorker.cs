using System.IO;
namespace SistemPusulasi;
public partial class App
{
    internal static string UpdateInstallProgressPath=>Path.Combine(LocalStore.Root,"update-install-progress.json");
    internal static string UpdateInstallResultPath=>Path.Combine(LocalStore.Root,"update-install-result.json");
    internal static string UpdateProgressPath(string id)
    {
        if(!Guid.TryParseExact(id,"N",out var parsed))throw new ArgumentException("Geçersiz işlem kimliği.");
        return Path.Combine(LocalStore.Root,"UpdateHistory",parsed.ToString("N")+"-progress.json");
    }
    internal static string UpdateRequestPath(string id)
    {
        if(!Guid.TryParseExact(id,"N",out var parsed))throw new ArgumentException("Geçersiz işlem kimliği.");
        return Path.Combine(LocalStore.Root,"UpdateRequests",parsed.ToString("N")+".json");
    }
    private static async Task RunUpdateWorker(string id)
    {
        var path=UpdateRequestPath(id);
        UpdateInstallResult result;
        FileStream? workerLock=null;
        try {workerLock=new FileStream(Path.Combine(LocalStore.Root,"update-worker.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
        catch(IOException) {
            LocalStore.Write(Path.Combine(LocalStore.Root,"UpdateHistory",id+".json"),new UpdateInstallResult("Busy","Başka bir güncelleme kurulumu çalışıyor; tamamlanmasını bekleyin."));
            try{File.Delete(path);}catch{}return;
        }
        using var ownership=workerLock;
        try {
            var entry=LocalStore.Read<UpdateEntry>(path)??throw new InvalidDataException("Güncelleme isteği okunamadı.");
            var invalid=UpdateInstaller.Validate(entry);
            if(invalid!=null)throw new InvalidDataException(invalid);
            if(entry.Kind is "windows" or "drivers" && !IsAdmin())throw new InvalidOperationException("Windows/sürücü kurulumu yönetici izni gerektirir.");
            void Progress(string message)
            {
                var state=new ScanProgress {ProcessId=Environment.ProcessId,Running=true,Message=message};
                LocalStore.Write(UpdateProgressPath(id),state);
                LocalStore.Write(UpdateInstallProgressPath,state);
            }
            Progress("Kurulum koşulları denetleniyor…");
            result=await UpdateInstaller.RunAsync(entry,Progress);
            result=result with {Detail=entry.Name+" — "+result.Detail};
        } catch(Exception ex) {result=new UpdateInstallResult("Unknown",ex.Message);}
        finally {try{File.Delete(path);}catch{}}
        LocalStore.Write(UpdateInstallResultPath,result);
        LocalStore.Write(Path.Combine(LocalStore.Root,"UpdateHistory",id+".json"),result);
        LocalStore.Write(UpdateInstallProgressPath,new ScanProgress {ProcessId=Environment.ProcessId,Running=false,Message=result.Detail});
        LocalStore.Write(UpdateProgressPath(id),new ScanProgress {ProcessId=Environment.ProcessId,Running=false,Message=result.Detail});
    }
}
