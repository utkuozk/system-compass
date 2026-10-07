using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;

namespace SistemPusulasi;

/// <summary>A read-only guide to the Overview screen. Creating or opening it performs no diagnostics.</summary>
internal sealed class OverviewHelpWindow : Window
{
    private readonly List<string> helpText = new();
    internal bool IsEnglish { get; }
    internal string HelpText => string.Join("\n", helpText);
    internal int SectionCount { get; private set; }
    internal TabControl HelpTabs { get; } = new();

    internal OverviewHelpWindow(bool english)
    {
        IsEnglish = english;
        Title = S("Genel bakış yardımı — System Compass", "Overview help — System Compass");
        Width = 790; Height = 710; MinWidth = 620; MinHeight = 470;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        Background = Color("#F3F7FA"); Foreground = Color("#20354B");
        FontFamily = new FontFamily("Segoe UI"); FontSize = 14;
        Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(HelpStyles));
        var layout = new Grid();
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new() { Height = new GridLength(1, GridUnitType.Star) });
        layout.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var heading = new StackPanel();
        heading.Children.Add(Text(S("EKRAN REHBERİ", "SCREEN GUIDE"), 11, "#8CE8DA", true));
        heading.Children.Add(Text(S("Sonuçları güvenle okuyun", "Understand your results"), 26, "#FFFFFF", true, new Thickness(0, 7, 0, 8)));
        heading.Children.Add(Text(S("Kontrollerin kapsamı, sonuçların anlamı ve bir sonraki adım.", "What the checks cover, what the results mean, and what to do next."), 14, "#CFDCE8"));
        layout.Children.Add(new Border { Background = Color("#102137"), Padding = new Thickness(26, 21, 26, 23), Child = heading });

        HelpTabs.Margin = new Thickness(24, 16, 24, 0);
        HelpTabs.Background = Brushes.Transparent; HelpTabs.BorderThickness = new Thickness(0);
        HelpTabs.SetResourceReference(StyleProperty, "HelpTabs");
        Grid.SetRow(HelpTabs, 1); layout.Children.Add(HelpTabs);
        BuildGuide();

        var footer = new Grid { Margin = new Thickness(26, 15, 26, 20) };
        footer.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        footer.Children.Add(Text(S("Yardım açıkken ana ekranı kullanabilirsiniz.", "You can keep using the main window while help is open."), 12, "#60758A", false, new Thickness(0, 0, 18, 0)));
        var close = new Button { Content = S("Kapat", "Close"), Padding = new Thickness(22, 9, 22, 9), MinWidth = 88, IsCancel = false };
        close.SetResourceReference(StyleProperty, "HelpCloseButton");
        close.Click += (_, _) => Close(); Grid.SetColumn(close, 1); footer.Children.Add(close);
        Grid.SetRow(footer, 2); layout.Children.Add(footer); Content = layout;
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
    }

    private void BuildGuide()
    {
        var scans = Tab("Kontroller", "Checks");
        Card(scans, "Şimdi kontrol et: hafif kontrol", "Check now: a quick check",
            "Sistem, donanım, disk ve güç verilerini, son yedi günün olay ve güvenilirlik kayıtlarını okur. DISM /CheckHealth yalnızca daha önce kaydedilmiş Windows bileşen deposu bozulmasını sorgular; tüm dosyaları taramaz. Antivirüsün koruma durumu okunur; virüs taraması başlatılmaz.",
            "Reads system, hardware, disk and power data, plus event and reliability records from the last seven days. DISM /CheckHealth only queries previously recorded Windows component-store corruption; it does not scan every file. Antivirus protection status is read; a virus scan is not started.");
        Card(scans, "Kapsamlı tarama: daha ayrıntılı doğrulama", "Deep scan: deeper verification",
            "Hafif kontrolün veri toplamasına ek olarak DISM /ScanHealth bileşen deposunu tarar, SFC /verifyonly korunan Windows sistem dosyalarını doğrular ve CHKDSK C: dosya sistemini salt okunur denetler. Windows, sürücü ve WinGet yazılım güncelleme envanterleri de kontrol edilir; güncellemeler kendiliğinden kurulmaz. Bu tarama daha uzun sürebilir.",
            "Adds DISM /ScanHealth to scan the component store, SFC /verifyonly to verify protected Windows system files, and a read-only CHKDSK check of the C: file system. It also checks Windows, driver and WinGet software update inventories; updates are not installed automatically. This scan can take longer.");
        Card(scans, "Denetim ile onarım ayrı adımlardır", "Checking and repairing are separate steps",
            "Bütünlük denetimleri ve CHKDSK salt okunur çalışır. Otomatik onarım ayarı açıksa, kapsamlı taramada güncel ve onarılabilir Windows bozulması doğrulandığında ayrıca DISM/SFC onarımı yapılabilir. Hafif kontrol SFC, CHKDSK veya güncelleme envanterlerini çalıştırmaz; kayıtlı DISM bozulması için kapsamlı tarama gerekir.",
            "Integrity checks and CHKDSK run read-only. If automatic repair is enabled, a deep scan may separately run DISM/SFC repairs after confirming current, repairable Windows corruption. A quick check does not run SFC, CHKDSK or update inventories; recorded DISM corruption needs a deep scan.");
        Card(scans, "Yönetici izni ve erişilemeyen kontroller", "Administrator permission and unavailable checks",
            "Kontrol başlatılırken Windows yönetici izni isteyebilir. İzin verilmezse işlem başlamaz. İzin, erişim veya veri eksikliği; başka bir DISM/SFC işlemi; komut hatası veya zaman aşımı, ilgili kontrolün atlanmasına ya da doğrulanamamasına neden olabilir. Ayrıntı satırında nedeni okuyun.",
            "Windows may request administrator permission when you start a check. Without permission, the action does not start. Missing permissions, access or data; another DISM/SFC process; a command error or a timeout can leave a check skipped or unverified. Read the reason in its detail row.");

        var results = Tab("Sonuçları okuma", "Reading results");
        Card(results, "Tamamlandı, sorun yok demek değildir", "Completed does not mean healthy",
            "Tarama durumu sekmesindeki Durum sütunu adımın ilerleyişini, Sonuç sütunu ne bulunduğunu gösterir. Tamamlandı + Sorun bulundu, kontrolün bittiği ve bir sorun bulduğu anlamına gelir. Bekliyor henüz başlamadı; Çalışıyor sürüyor; Atlandı bu taramada uygulanmadı; Doğrulanamadı güvenilir bir sonuç alınamadı demektir. Atlanan veya doğrulanamayan adımlar başarı sayılmaz.",
            "In Scan progress, the Status column shows whether a step ran; Result shows what it found. Completed + Problem found means the check finished and found a problem. Pending has not started; Running is in progress; Skipped was not run in this scan; Unverified has no reliable outcome. Skipped and unverified steps do not count as success.", true);
        Card(results, "Bulgu etiketleri", "Finding labels",
            "Sorun yok: bu kontrol kapsamında sorun bildirilmedi. İncelenmeli: uyarı veya geçmiş olay değerlendirme istiyor. Sorun bulundu: kontrol bir sorun bildirdi; ayrıntısına bakın. Onarıldı: onarım sonrası doğrulama başarılı. Bilgi: açıklayıcı veri. Doğrulanmadı: sonuç bilinmiyor; sağlıklı ya da arızalı olarak yorumlamayın.",
            "No problems: no issue was reported within this check's scope. Needs review: a warning or historical event needs attention. Problem found: the check reported a problem; read its details. Repaired: verification after repair passed. Information: context or informational data. Not verified: the outcome is unknown; do not treat it as healthy or faulty.");
        Card(results, "Üstteki sayılar nasıl hesaplanır?", "How to read the summary counts",
            "Dikkat isteyen sayısı İncelenmeli ve Sorun bulundu etiketli bulguları sayar. Değerlendirilen X / Y, Doğrulanmadı dışındaki bulguların toplam bulgulara oranıdır; Bilgi satırları da buna dahildir. Bu bir sağlık puanı, bozulma yüzdesi veya tamamlanan tarama yüzdesi değildir. Dikkat isteyenler sekmesi bilinmeyen sonuçları da gösterdiği için satır sayısı üstteki dikkat sayısından fazla olabilir.",
            "Needs attention counts Needs review and Problem found findings. Assessed X / Y counts findings other than Not verified against all findings, including informational rows. It is not a health score, corruption percentage or scan-completion percentage. The Needs attention tab also includes unknown outcomes, so it can contain more rows than the summary's attention count.");
        Card(results, "Canlı, tamamlanmış ve geçmiş rapor", "Live, completed and historical reports",
            "Canlı sonuçlar tarama sürdükçe gelir; kalan kontroller henüz değerlendirilmemiştir. Yeni tarama sırasında üst özet önceki rapora ait olabilir; sonuç listesinin üzerindeki bağlam ve tarihe bakın. Kesilmiş/kısmi rapor eksik kontrolleri içerir; tamamlanmış rapor da doğrulanamayan bulgular içerebilir. Geçmişten açılan rapor o tarihin kaydıdır, bilgisayarın şu anki durumu değildir.",
            "Live findings arrive while the scan runs; remaining checks are not yet assessed. During a new scan, the top summary may belong to the previous report; check the context and date above the results list. Interrupted or partial reports have unfinished checks; completed reports can still contain unverified findings. A historical report records that time, rather than the computer's current condition.");

        var navigation = Tab("Sekmeler ve düğmeler", "Tabs and buttons");
        Card(navigation, "Sekmelerde ne var?", "What is in each tab?",
            "Tarama durumu: sıralı adımlar, ilerleyiş, gerçek sonuçlar ve geçen süre. Dikkat isteyenler: uyarılar, sorunlar ve doğrulanamayanlar. Windows: bütünlük, onarım, güncelleme ve güvenlik bulguları. Depolama: disk ve dosya sistemi. Donanım / güç: aygıt, işlemci, bellek ve güç verileri. Olaylar / diğer: olay kayıtları, kararlılık ve diğer kontroller. Bir satırı seçerek alttaki ayrıntının tamamını okuyun; ayrıntı alanını kaydırabilir ve metni kopyalayabilirsiniz.",
            "Scan progress: ordered steps, progress, actual outcomes and elapsed time. Needs attention: warnings, issues and unverified checks. Windows: integrity, repair, updates and security findings. Storage: disks and file systems. Hardware / power: devices, processor, memory and power data. Events / other: event logs, stability and other checks. Select a row to read its complete details below; you can scroll the detail area and copy its text.");
        Card(navigation, "Şimdi kontrol et / Kapsamlı tarama", "Check now / Deep scan",
            "Yeni bir kontrol başlatır ve tarama durumu sekmesini açar. Çalışan tarama veya kurulum varken bu düğmeler devre dışı kalabilir. Geçen süre bir tahmini bitiş zamanı değildir; adımların süresi farklıdır. Sıradan kontrol ile ayrı antivirüs virüs taraması birbirinden farklıdır.",
            "Starts a new check and opens Scan progress. Check now runs a quick check. These buttons may be disabled during a scan or installation. Elapsed time is not an estimated completion time; steps take different amounts of time. A health check and a separate antivirus virus scan are different actions.");
        Card(navigation, "Rapor klasörü / Rapor geçmişi", "Report folder / Report history",
            "Rapor klasörü görüntülenen raporun kayıtlarını açar; rapor seçilmemişse genel rapor klasörünü açar. Kayıtlar ayrıntılı tanılama çıktısını içerir. Rapor geçmişi önceki taramaları açar. Geçerli durumu yeniden ölçmek için yeni kontrol başlatın. Kaynak Windows çıktıları özgün dilinde kalabilir.",
            "Report folder opens the displayed report's saved files, or the general reports folder if no report is selected. Files contain detailed diagnostic output. Report history opens earlier scans. Run a new check to measure the current state. Source Windows output may remain in its original language.");
        Card(navigation, "Windows bozulmasını onar", "Repair Windows corruption",
            "Uygun bir Windows bozulması bulgusu varsa kullanılabilir. Onay ve yönetici izninden sonra yeni kapsamlı doğrulama yapar; eski rapor tek başına onarım başlatmaz. Bozulma sürüyorsa ve koşullar uygunsa DISM/SFC ile onarır. Donanım uyarıları, geçmiş olaylar ve belirsiz sonuçlar bu düğmeyle onarılmaz. Düğme kapalıysa açıklamasını görmek için üzerine gelin.",
            "Available when the report contains an eligible Windows corruption finding. After confirmation and administrator permission, it performs a fresh comprehensive verification; an old report alone cannot trigger repair. If corruption remains and conditions permit, it repairs with DISM/SFC. Hardware warnings, historical events and unknown results are outside this repair action. Hover over a disabled button to read its explanation.");
        Card(navigation, "Güncelleme ve antivirüs sayfaları", "Update and antivirus pages",
            "Menüden Windows, yazılım veya sürücü güncellemelerini ayrıntılı inceleyebilirsiniz. Denetleme listeyi okur; kurulum için uygun kayıtları seçip güncelleme işlemini ayrıca başlatın. Antivirüs sayfası koruma durumunu ve kullanılabilen ayrı virüs taraması seçeneklerini gösterir. Genel bakıştan yardım açmak bu işlemlerin hiçbirini başlatmaz.",
            "Use the menu to inspect Windows, software or driver updates. Checking reads the list; select eligible entries and separately start installation. The Antivirus page shows protection status and available separate virus-scan options. Opening this help starts none of these actions.");

        var scope = Tab("Sınırlar ve onarım", "Limits and repair");
        Card(scope, "RAM testi yapılmadı ne demek?", "What does RAM not tested mean?",
            "Bellek bilgilerini okumak RAM donanımını test etmek değildir. Uygulama Windows Bellek Tanılama testini başlatmaz; varsa eski test kayıtlarını okuyabilir. Yeniden başlatma isteyen bir bellek testi bu taramada yapılmadığından Doğrulanmadı görmek normaldir. Bu etiket tek başına RAM arızası göstermez.",
            "Reading memory information does not test RAM hardware. The app does not launch Windows Memory Diagnostic; it may read existing test records. A memory test requiring restart is not performed by this scan, so Not verified is expected. That label alone does not indicate faulty RAM.");
        Card(scope, "Disk, sıcaklık ve geçmiş olayların sınırları", "Limits of disk, temperature and event data",
            "CHKDSK dosya sistemini denetler; fiziksel SSD/HDD yüzey veya dayanıklılık testi değildir. Windows'un bildirdiği disk/aygıt durumu ve erişilebilen sensör verileri sınırlıdır. Beklenmeyen kapanma, WHEA veya hizmet hatası kayıtları geçmiş olaylardır; tek başına mevcut donanım arızasını kanıtlamaz. Kayıt bulunmaması da donanımın kesin sağlam olduğu anlamına gelmez.",
            "CHKDSK checks the file system; it is not a physical SSD/HDD surface or endurance test. Windows-reported disk/device status and available sensor data have limits. Unexpected shutdowns, WHEA records and service errors are historical events; alone, they do not prove a current hardware fault. No matching records also cannot guarantee healthy hardware.");
        Card(scope, "Güncelleme kapsamı tam envanter değildir", "Update coverage is limited",
            "Sürücüler Windows Update'in bu bilgisayara sunduğu kayıtlarla sınırlıdır; bütün üretici sürücülerini kapsamaz. Yazılım denetimi WinGet'in tanıyabildiği uygulamaları kapsar. Desteklenmeyen veya sürümü karşılaştırılamayan yazılımlar güncel kabul edilmez; sonuç bilinmiyor olabilir.",
            "Driver results cover what Windows Update offers this computer, rather than every manufacturer's driver. Software checks cover apps recognized by WinGet. Unsupported apps or incomparable versions cannot be assumed current; their status may be unknown.");
        Card(scope, "Onarımın çalışması için gerekenler", "Conditions needed for repair",
            "Güncel, onarılabilir bozulma; yönetici yetkisi; doğrulanmış şebeke gücü; bekleyen yeniden başlatma olmaması ve başka DISM/SFC işlemi bulunmaması gerekir. Otomatik onarım ayrıca açık olmalı ve önceki otomatik onarım girişiminden en az yedi gün geçmiş olmalıdır. Tek seferlik Onar isteği otomatik onarım tercihini değiştirmez. Komutun bitmesi yeterli değildir; Onarıldı sonucu onarım sonrası doğrulamayla verilir.",
            "Repair needs current, repairable corruption, administrator rights, confirmed mains power, no pending restart, and no other DISM/SFC process. Automatic repair must also be enabled, with at least seven days since the previous automatic repair attempt. A one-time Repair request does not change the automatic-repair preference. A finished repair command is not enough; Repaired requires verification afterward.");
        Card(scope, "Yeniden başlatma sizin kontrolünüzde", "Restart remains your choice",
            "Uygulama bilgisayarı otomatik yeniden başlatmaz. Windows yeniden başlatma istiyorsa bunu raporda gösterir ve bazı işlemleri erteler. Çalışmanızı kaydedip uygun zamanda Windows üzerinden yeniden başlatın; ardından sonucu yeni bir kontrolle doğrulayın. Örnek veri / önizleme ekranı gerçek ölçüm değildir ve sistem işlemi yapmaz.",
            "The app does not restart the computer automatically. If Windows needs a restart, the report shows it and some actions are deferred. Save your work and restart through Windows at a suitable time, then run a new check to verify the result. Sample data or preview mode is not a real measurement and performs no system actions.", true);
    }

    private string S(string turkish, string english)
    {
        var text = IsEnglish ? english : turkish;
        helpText.Add(text); return text;
    }

    private StackPanel Tab(string turkish, string english)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 15, 8, 0) };
        var scroll = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, CanContentScroll = false };
        HelpTabs.Items.Add(new TabItem { Header = S(turkish, english), Content = scroll });
        return panel;
    }

    private void Card(StackPanel panel, string titleTr, string titleEn, string bodyTr, string bodyEn, bool emphasis = false)
    {
        var body = new StackPanel();
        body.Children.Add(Text(S(titleTr, titleEn), 16, "#116A74", true));
        body.Children.Add(Text(S(bodyTr, bodyEn), 14, "#3D5268", false, new Thickness(0, 8, 0, 0)));
        panel.Children.Add(new Border { Child = body, Background = Color(emphasis ? "#E8F4F1" : "#FFFFFF"), BorderBrush = Color(emphasis ? "#BFDCD6" : "#DCE5ED"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(18, 16, 18, 17), Margin = new Thickness(0, 0, 0, 12) });
        SectionCount++;
    }

    private static Brush Color(string value) => (Brush)new BrushConverter().ConvertFromString(value)!;
    private static TextBlock Text(string value, double size, string color, bool bold = false, Thickness margin = default) => new()
    {
        Text = value, FontSize = size, Foreground = Color(color), FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        TextWrapping = TextWrapping.Wrap, LineHeight = size * 1.55, Margin = margin, VerticalAlignment = VerticalAlignment.Center
    };

    private const string HelpStyles = """
        <ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
          <Style TargetType="TabItem">
            <Setter Property="FontSize" Value="13"/><Setter Property="Foreground" Value="#60758A"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabItem">
              <Border x:Name="TabBorder" Background="Transparent" CornerRadius="7,7,0,0" Padding="12,11" BorderThickness="0,0,0,2" BorderBrush="Transparent" Margin="0,0,3,0"><ContentPresenter ContentSource="Header"/></Border>
              <ControlTemplate.Triggers>
                <Trigger Property="IsSelected" Value="True"><Setter Property="Foreground" Value="#087F8C"/><Setter Property="FontWeight" Value="SemiBold"/><Setter TargetName="TabBorder" Property="Background" Value="#E3F1EE"/><Setter TargetName="TabBorder" Property="BorderBrush" Value="#087F8C"/></Trigger>
                <Trigger Property="IsMouseOver" Value="True"><Setter TargetName="TabBorder" Property="Background" Value="#EAF1F5"/></Trigger>
                <Trigger Property="IsKeyboardFocusWithin" Value="True"><Setter TargetName="TabBorder" Property="BorderBrush" Value="#087F8C"/></Trigger>
              </ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="HelpTabs" TargetType="TabControl">
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="TabControl"><Grid>
              <Grid.RowDefinitions><RowDefinition Height="Auto"/><RowDefinition Height="*"/></Grid.RowDefinitions>
              <TabPanel IsItemsHost="True"/><ContentPresenter Grid.Row="1" ContentSource="SelectedContent"/>
            </Grid></ControlTemplate></Setter.Value></Setter>
          </Style>
          <Style x:Key="HelpCloseButton" TargetType="Button">
            <Setter Property="Background" Value="#087F8C"/><Setter Property="Foreground" Value="White"/><Setter Property="BorderBrush" Value="#087F8C"/>
            <Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="ButtonBorder" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" CornerRadius="7" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border>
              <ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="ButtonBorder" Property="Background" Value="#0B6975"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="ButtonBorder" Property="BorderBrush" Value="#20354B"/><Setter TargetName="ButtonBorder" Property="BorderThickness" Value="2"/></Trigger></ControlTemplate.Triggers>
            </ControlTemplate></Setter.Value></Setter>
          </Style>
        </ResourceDictionary>
        """;
}

