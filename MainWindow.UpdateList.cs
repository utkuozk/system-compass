using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;

public partial class MainWindow
{
    private List<UpdateRow> updateRows=new();
    private readonly Dictionary<string,HashSet<UpdateEntry>> checkedUpdates=new();
    private bool changingUpdateChecks;
    private void PopulateUpdates(UpdateInventoryResult? result)
    {
        foreach(var row in updateRows)row.PropertyChanged-=UpdateRow_PropertyChanged;
        updateRows=result?.Entries.Select(x=>new UpdateRow(x)).ToList()??new();
        var selected=checkedUpdates.GetValueOrDefault(updateKind);
        foreach(var row in updateRows) {row.IsChecked=row.CanInstall && selected?.Contains(row.Entry)==true;row.PropertyChanged+=UpdateRow_PropertyChanged;}
        if(result!=null && selected!=null)selected.IntersectWith(updateRows.Where(x=>x.CanInstall).Select(x=>x.Entry));
        UpdateCount.Text=F("{0} güncelleme · {1} karşılaştırılamayan kayıt",updateRows.Count(x=>x.HasUpdate),updateRows.Count(x=>!x.HasUpdate));
        UpdateLastChecked.Text=result==null?T("Henüz taranmadı"):F("Son kontrol: {0:g}",result.CheckedUtc.ToLocalTime());
        UpdateSelectionDetail.Text=T("Ayrıntıları görmek için listeden bir satır seçin.");
        ApplyUpdateSearch();
        RenderInstallAction();
    }
    private void ApplyUpdateSearch()
    {
        if(UpdateSearch==null || UpdatesList==null)return;
        var text=UpdateSearch.Text.Trim();
        var shown=updateRows.Where(x=>text.Length==0 || x.Name.Contains(text,StringComparison.CurrentCultureIgnoreCase) || x.Source.Contains(text,StringComparison.CurrentCultureIgnoreCase)).ToList();
        UpdatesList.ItemsSource=shown;
        UpdateEmpty.Text=shown.Count>0?"":T(text.Length>0?"Aramanıza uygun kayıt bulunamadı.":"Listelenecek kayıt yok. Kontrolün başarılı olup olmadığını yukarıdaki durum açıklamasından görebilirsiniz.");
        UpdateEmpty.Visibility=shown.Count==0?Visibility.Visible:Visibility.Collapsed;
        RenderUpdateChecks();RenderInstallAction();
    }
    private void UpdateRow_PropertyChanged(object? sender,PropertyChangedEventArgs e)
    {
        if(e.PropertyName!=nameof(UpdateRow.IsChecked) || changingUpdateChecks)return;
        RememberUpdateChecks();RenderUpdateChecks();RenderInstallAction();
    }
    private void RememberUpdateChecks()=>checkedUpdates[updateKind]=updateRows.Where(x=>x.IsChecked && x.CanInstall).Select(x=>x.Entry).ToHashSet();
    private void RenderUpdateChecks()
    {
        if(SelectAllUpdates==null)return;
        var eligible=(UpdatesList.ItemsSource as IEnumerable<UpdateRow>)?.Where(x=>x.CanInstall).ToList()??new();
        changingUpdateChecks=true;
        SelectAllUpdates.IsEnabled=eligible.Count>0;
        SelectAllUpdates.IsChecked=eligible.Count==0 || eligible.All(x=>!x.IsChecked)?false:eligible.All(x=>x.IsChecked)?true:null;
        changingUpdateChecks=false;
    }
    private void SelectAllUpdates_Click(object sender,RoutedEventArgs e)
    {
        if(changingUpdateChecks)return;
        var eligible=((UpdatesList.ItemsSource as IEnumerable<UpdateRow>)??Array.Empty<UpdateRow>()).Where(x=>x.CanInstall).ToList();
        bool check=eligible.Any(x=>!x.IsChecked);
        changingUpdateChecks=true;
        foreach(var row in eligible)row.IsChecked=check;
        changingUpdateChecks=false;
        RememberUpdateChecks();RenderUpdateChecks();RenderInstallAction();
    }
    internal static UpdateEntry[] SnapshotUpdateQueue(IEnumerable<UpdateRow> rows)=>rows.Where(x=>x.IsChecked && x.CanInstall).Select(x=>x.Entry with {}).Distinct().ToArray();
    private void UpdateSearch_TextChanged(object sender,TextChangedEventArgs e)=>ApplyUpdateSearch();
    private void UpdateSelection_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(UpdateSelectionDetail==null)return;
        RenderInstallAction();
        if(UpdatesList.SelectedItem is UpdateRow row)UpdateSelectionDetail.Text=F("{0}\nMevcut: {1} · Yeni: {2}\nKaynak: {3}\n{4}",row.Name,row.InstalledVersion,row.AvailableVersion,row.Source,T(row.HasUpdate?"Bu kayıt güncelleme kaynağında listelendi. Aşağıda kurulum desteği ve kullanılabilir işlem gösterilir.":"Bu kaydın yeni sürümü karşılaştırılamadı; güncel veya eski olduğu söylenemez."));
        if(UpdatesList.SelectedItem is UpdateRow invalid && !invalid.CanInstall)UpdateSelectionDetail.Text+="\n"+invalid.Eligibility;
        if(UpdatesList.SelectedItem is UpdateRow software && software.Entry.Kind=="software")UpdateSelectionDetail.Text+="\n"+T("Yazılım kurulumu desteklenen MSIX, MSI, WiX, Burn ve Inno paketleriyle sınırlıdır. Paket türü kurulumdan önce doğrulanır; desteklenmeyen türler kurulmaz. Diğer yazılımlar için uygulamanın kendi güncelleyicisini kullanın.");
    }
    internal static void CheckUpdateSelectionUi(Action<bool,string> check)
    {
        var window=new MainWindow(true);
        var first=new UpdateEntry("Python","1.0","2.0","WinGet / winget","software"){PackageId="Python.Python"};
        var second=first with {Name="Ollama",PackageId="Ollama.Ollama"};
        var unknown=first with {Name="Unknown",AvailableVersion="Bilinmiyor"};
        var unsupported=first with {Name="Unsupported",PackageId=""};
        try {
            window.updateKind="software";window.Page("updates");
            window.PopulateUpdates(new(){Entries=new(){first,second,unknown,unsupported}});
            window.SelectAllUpdates.IsChecked=true;window.SelectAllUpdates_Click(window,new());
            check(window.updateRows.Count(x=>x.IsChecked)==2 && !window.updateRows[2].CanInstall && !window.updateRows[3].CanInstall,"Select-all includes only valid installation records");
            window.UpdateSearch.Text="Python";
            check(SnapshotUpdateQueue(window.updateRows).Length==2 && window.SelectAllUpdates.IsChecked==true,"Searching preserves checked rows outside the visible results");
            window.SelectAllUpdates.IsChecked=false;window.SelectAllUpdates_Click(window,new());
            check(!window.updateRows[0].IsChecked && window.updateRows[1].IsChecked,"Filtered select-all changes only visible eligible rows");
            var snapshot=SnapshotUpdateQueue(window.updateRows);
            window.updateRows[1].IsChecked=false;
            check(snapshot.Length==1 && snapshot[0].Name=="Ollama","Queue snapshot remains immutable after checkbox changes");
            window.updateRows[0].IsChecked=true;
            window.PopulateUpdates(null);window.PopulateUpdates(new(){Entries=new(){first,second}});
            check(window.updateRows[0].IsChecked && !window.updateRows[1].IsChecked,"Selections survive returning to cached inventory");
            window.updateKind="drivers";window.PopulateUpdates(new(){Entries=new(){first with {Kind="drivers"}}});
            check(!window.updateRows[0].IsChecked,"Independent inventory pages do not inherit another page's selections");
            var queued=new UpdateQueueItem(first);window.installQueue.Add(queued);window.currentInstall=queued;window.installQueueRunning=true;
            window.Page("settings");window.RenderInstallAction();
            check(window.SettingsPanel.Visibility==Visibility.Visible && window.ShowInstallProgressButton.Visibility==Visibility.Visible,"Other pages and queue monitor remain available during installation");
            check(App.UpdateProgressPath(queued.Id)!=App.UpdateInstallProgressPath,"Queue progress uses its own job identity rather than global history");
            check(App.UpdateProgressPath(queued.Id)!=App.UpdateProgressPath(new UpdateQueueItem(second).Id),"Different queued items use distinct progress files");
            check(InstallStateLabel(new("Installed","verified"))==T("Tamamlandı") && InstallStateLabel(new("Unknown","uncertain"))==T("Sonuç doğrulanamadı"),"Verified installation and uncertain outcomes have distinct labels");
            check(ShouldStopUpdateQueue(null) && ShouldStopUpdateQueue(new("Unknown","uncertain")) && ShouldStopUpdateQueue(new("Busy","locked")) && ShouldStopUpdateQueue(new("Installed","restart",true)),"Unknown outcomes, lock conflicts and restart requirements stop pending installs");
            check(!ShouldStopUpdateQueue(new("Installed","verified")) && !ShouldStopUpdateQueue(new("Rejected","unsupported")),"Known completion and explicit unsupported rejection allow the next item");
            var language=L10n.Language;
            try {
                L10n.SetLanguage("en");
                const string installed="Seçilen yazılım sürümünün kurulu olduğu doğrulandı. Otomatik yeniden başlatma yapılmadı.";
                var detail=InstallDetail(first,new("Installed",first.Name+" — "+installed,true));
                check(detail.StartsWith(T(installed),StringComparison.Ordinal) && !detail.Contains(first.Name+" — ") && !detail.Contains("Seçilen"),"Job-prefixed software results translate correctly with immutable package names");
                check(detail.Contains(T("Yeniden başlatma gerekiyor; bilgisayar otomatik yeniden başlatılmadı.")),"English queue result preserves the explicit restart requirement");
            } finally {L10n.SetLanguage(language);}
        } finally {window.Close();}
        RenderUpdateUiPreview(System.IO.Path.Combine(AppContext.BaseDirectory,"ui-previews"));
    }
}
public sealed class UpdateRow(UpdateEntry entry):INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private bool isChecked;
    public bool IsChecked {get=>isChecked;set {value=value && CanInstall;if(isChecked==value)return;isChecked=value;PropertyChanged?.Invoke(this,new(nameof(IsChecked)));}}
    public bool CanInstall=>HasUpdate && UpdateInstaller.Validate(entry)==null;
    public string Eligibility=>T(!HasUpdate?"Yeni sürüm doğrulanmadığı için kurulum yapılamaz.":UpdateInstaller.Validate(entry)??"Kurulum için seç");
    public UpdateEntry Entry=>entry;
    public string Name=>entry.Name;
    public string InstalledVersion=>DynamicTranslations.KnownText(entry.InstalledVersion);
    public string AvailableVersion=>DynamicTranslations.KnownText(entry.AvailableVersion).Replace("availabilityUnknown",T("karşılaştırılamadı"));
    public string Source=>DynamicTranslations.KnownText(entry.Source);
    public string Kind=>T(entry.Kind switch {"software"=>"Yazılım","drivers"=>"Sürücü",_=>"Windows"});
    public bool HasUpdate=>!string.IsNullOrWhiteSpace(entry.AvailableVersion) && !entry.AvailableVersion.StartsWith("Bilinmiyor",StringComparison.OrdinalIgnoreCase) && !entry.Source.Contains("kurulu program",StringComparison.OrdinalIgnoreCase);
    public string Status=>T(HasUpdate?"Güncelleme var":"Karşılaştırılamadı");
}
