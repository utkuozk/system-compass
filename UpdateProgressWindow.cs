using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;

internal sealed class UpdateQueueItem(UpdateEntry entry):INotifyPropertyChanged
{
    public UpdateEntry Entry {get;}=entry;
    public string Id {get;}=Guid.NewGuid().ToString("N");
    public string Name=>Entry.Name;
    private string state=T("Sırada");
    private string detail="";
    public string State {get=>state;set {state=value;PropertyChanged?.Invoke(this,new(nameof(State)));}}
    public string Detail {get=>detail;set {detail=value;PropertyChanged?.Invoke(this,new(nameof(Detail)));}}
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>A modeless monitor. Hiding this window never interrupts servicing.</summary>
internal sealed class UpdateProgressWindow:Window
{
    private bool allowClose;
    private readonly TextBlock heading=new(){FontSize=22,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock current=new(){FontSize=14,FontWeight=FontWeights.SemiBold,Margin=new(0,14,0,6),TextWrapping=TextWrapping.Wrap};
    private readonly TextBlock phase=new(){FontSize=13,TextWrapping=TextWrapping.Wrap,Margin=new(0,0,0,12)};
    private readonly ProgressBar activity=new(){Height=5,IsIndeterminate=true,Foreground=new SolidColorBrush(Color.FromRgb(8,127,140)),Background=new SolidColorBrush(Color.FromRgb(221,237,235))};
    private readonly TextBlock summary=new(){FontSize=12,TextWrapping=TextWrapping.Wrap,Margin=new(0,13,0,15)};
    private readonly Button hide=new(){Margin=new(0,12,0,0),HorizontalAlignment=HorizontalAlignment.Right,Padding=new(15,9,15,9)};
    internal UpdateProgressWindow(ObservableCollection<UpdateQueueItem> items)
    {
        Title=T("Güncelleme kurulumu");Width=650;Height=540;MinWidth=460;MinHeight=380;
        Background=new SolidColorBrush(Color.FromRgb(245,248,251));WindowStartupLocation=WindowStartupLocation.CenterOwner;
        var panel=new DockPanel {Margin=new Thickness(24)};
        var top=new StackPanel();top.Children.Add(heading);top.Children.Add(current);top.Children.Add(phase);top.Children.Add(activity);top.Children.Add(summary);
        DockPanel.SetDock(top,Dock.Top);panel.Children.Add(top);
        hide.Content=T("Menüleri kullanmaya devam et");hide.Click+=(_,_)=>Hide();DockPanel.SetDock(hide,Dock.Bottom);panel.Children.Add(hide);
        var list=new ListBox {ItemsSource=items,Background=Brushes.White,BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Stretch};
        var template=new DataTemplate(typeof(UpdateQueueItem));
        var row=new FrameworkElementFactory(typeof(StackPanel));row.SetValue(FrameworkElement.MarginProperty,new Thickness(10,8,10,8));
        foreach(var (property,size,bold) in new[]{("Name",13d,true),("State",12d,true),("Detail",12d,false)}) {
            var text=new FrameworkElementFactory(typeof(TextBlock));text.SetBinding(TextBlock.TextProperty,new System.Windows.Data.Binding(property));text.SetValue(TextBlock.TextWrappingProperty,TextWrapping.Wrap);text.SetValue(TextBlock.FontSizeProperty,size);text.SetValue(FrameworkElement.MarginProperty,new Thickness(0,0,0,4));if(bold)text.SetValue(TextBlock.FontWeightProperty,FontWeights.SemiBold);row.AppendChild(text);
        }
        template.VisualTree=row;list.ItemTemplate=template;panel.Children.Add(list);Content=panel;
        Closing+=(_,e)=>{if(!allowClose){e.Cancel=true;Hide();}};
        Update(0,items.Count,"",T("Kurulum hazırlanıyor…"),true);
    }
    internal void CloseMonitor(){allowClose=true;Close();}
    internal void Update(int completed,int total,string name,string message,bool running)
    {
        heading.Text=T(running?"Seçili güncellemeler kuruluyor":"Güncelleme kuyruğu tamamlandı");
        current.Text=name;
        phase.Text=message;
        activity.Visibility=running?Visibility.Visible:Visibility.Collapsed;
        summary.Text=F("{0} / {1} işlem tamamlandı",completed,total)+"\n"+T(running?"Bu pencereyi gizleyip diğer menüleri kullanabilirsiniz. Kurulum devam eder. Otomatik yeniden başlatma yapılmaz.":"Her kaydın sonucunu aşağıda kontrol edin. Güncelleme listesini yeniden tarayarak doğrulayın.");
        hide.Content=T(running?"Menüleri kullanmaya devam et":"Kapat");
    }
}
