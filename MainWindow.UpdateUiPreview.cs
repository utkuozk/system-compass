using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static SistemPusulasi.L10n;
namespace SistemPusulasi;

public partial class MainWindow
{
    // Synthetic rendering uses demo data and never invokes inventory or installer workers.
    internal static void RenderUpdateUiPreview(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var window=new MainWindow(true);
        UpdateEntry AppEntry(string name,string id,string installed,string available)=>new(name,installed,available,"WinGet / winget","software"){PackageId=id};
        var python=AppEntry("Python 3.14","Python.Python.3.14","3.14.0","3.14.1");
        var ollama=AppEntry("Ollama","Ollama.Ollama","0.11.0","0.12.0");
        var browser=AppEntry("Mozilla Firefox","Mozilla.Firefox","142.0","143.0");
        try {
            window.updateKind="software";window.Page("updates");
            window.UpdateTitle.Text=T("Yazılım güncellemeleri");
            window.UpdateDescription.Text=T("WinGet'in tanıyabildiği uygulamalar kontrol edilir. Desteklenmeyen yazılımlar için güncellik doğrulanamaz.");
            window.PopulateUpdates(new(){Entries=new(){python,ollama,browser,AppEntry("Publisher utility","","1.0","Bilinmiyor")}});
            window.UpdateStatus.Text=T("ÖRNEK VERİ • Bu önizleme gerçek tarama yapmaz ve ayarları değiştirmez.");
            window.updateRows[0].IsChecked=window.updateRows[1].IsChecked=true;
            window.UpdatesList.SelectedItem=window.updateRows[0];
            RenderPreview((FrameworkElement)window.Content,1240,800,Path.Combine(outputDirectory,"update-selection.png"));
            var items=new ObservableCollection<UpdateQueueItem>{new(python){State=T("Tamamlandı"),Detail=python.InstalledVersion+" → "+python.AvailableVersion},new(ollama){State=T("Çalışıyor"),Detail=T("Kurulum koşulları denetleniyor…")},new(browser)};
            var monitor=new UpdateProgressWindow(items);
            try {
                monitor.Update(1,3,ollama.Name,T("Seçilen güncellemenin kimliği ve kullanılabilirliği yeniden doğrulanıyor…"),true);
                RenderPreview((FrameworkElement)monitor.Content,640,500,Path.Combine(outputDirectory,"update-progress.png"));
            } finally {monitor.CloseMonitor();}
        } finally {window.Close();}
    }
    private static void RenderPreview(FrameworkElement content,int width,int height,string path)
    {
        content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
        // DataGrid completes star-column widths through a deferred layout callback.
        // Let that callback run before capturing a window that has never been shown.
        content.Dispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ContextIdle);
        content.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);
        var background=new DrawingVisual();
        using(var drawing=background.RenderOpen())drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(244,247,251)),null,new Rect(0,0,width,height));
        bitmap.Render(background);bitmap.Render(content);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(path);encoder.Save(file);
    }
}
