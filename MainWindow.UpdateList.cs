using System.Windows;
using System.Windows.Controls;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;

public partial class MainWindow
{
    private List<UpdateRow> updateRows=new();
    private void PopulateUpdates(UpdateInventoryResult? result)
    {
        updateRows=result?.Entries.Select(x=>new UpdateRow(x)).ToList()??new();
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
    }
    private void UpdateSearch_TextChanged(object sender,TextChangedEventArgs e)=>ApplyUpdateSearch();
    private void UpdateSelection_Changed(object sender,SelectionChangedEventArgs e)
    {
        if(UpdateSelectionDetail==null)return;
        RenderInstallAction();
        if(UpdatesList.SelectedItem is UpdateRow row)UpdateSelectionDetail.Text=F("{0}\nMevcut: {1} · Yeni: {2}\nKaynak: {3}\n{4}",row.Name,row.InstalledVersion,row.AvailableVersion,row.Source,T(row.HasUpdate?"Bu kayıt güncelleme kaynağında listelendi. Aşağıda kurulum desteği ve kullanılabilir işlem gösterilir.":"Bu kaydın yeni sürümü karşılaştırılamadı; güncel veya eski olduğu söylenemez."));
    }
}
public sealed class UpdateRow(UpdateEntry entry)
{
    public UpdateEntry Entry=>entry;
    public string Name=>entry.Name;
    public string InstalledVersion=>DynamicTranslations.KnownText(entry.InstalledVersion);
    public string AvailableVersion=>DynamicTranslations.KnownText(entry.AvailableVersion).Replace("availabilityUnknown",T("karşılaştırılamadı"));
    public string Source=>DynamicTranslations.KnownText(entry.Source);
    public string Kind=>T(entry.Kind switch {"software"=>"Yazılım","drivers"=>"Sürücü",_=>"Windows"});
    public bool HasUpdate=>!string.IsNullOrWhiteSpace(entry.AvailableVersion) && !entry.AvailableVersion.StartsWith("Bilinmiyor",StringComparison.OrdinalIgnoreCase) && !entry.Source.Contains("kurulu program",StringComparison.OrdinalIgnoreCase);
    public string Status=>T(HasUpdate?"Güncelleme var":"Karşılaştırılamadı");
}
