using System.IO;
using System.Xml.Linq;
namespace SistemPusulasi;
internal static class SelfTests
{
    public static void Run()
    {
        var results=new List<string>();
        void Check(bool condition,string name) { if(!condition) throw new InvalidOperationException("Test failed: "+name); results.Add("PASS: "+name); }
        MainWindow.CheckInventoryUi(Check);
        MainWindow.CheckUpdateSelectionUi(Check);
        MainWindow.CheckHealthUi(Check);
        MainWindow.RenderHealthUiPreview(Path.Combine(AppContext.BaseDirectory,"ui-previews"));
        results.AddRange(ManualRepairTests.Run());
        results.AddRange(UpdateInstallerTests.Run());
        results.AddRange(ScanTrackingTests.Run());
        ScrollBarThemeTests.Run(Check);
        results.AddRange(UpdateScriptTests.Run().GetAwaiter().GetResult());
        Check(UpdateInventory.ParseWingetOutput("Name                     Id                      Version        Available      Source\n---------------------------------------------------------------------------------------\nExample App              Example.App             1.0            2.0            winget",0).Entries.Single().PackageId=="Example.App","Software inventory retains exact package identity");
        var windowsIdentity=new UpdateEntry("Update","Unknown","KB123","Windows Update Agent (yapılandırılmış kaynak)","windows") {UpdateId="11111111-1111-1111-1111-111111111111",Revision=2};
        var identityRoundtrip=System.Text.Json.JsonSerializer.Deserialize<UpdateEntry>(System.Text.Json.JsonSerializer.Serialize(windowsIdentity));
        Check(identityRoundtrip?.UpdateId==windowsIdentity.UpdateId && identityRoundtrip.Revision==2,"WUA identity and revision survive request serialization");
        bool pathRejected=false;try{App.UpdateRequestPath("../outside");}catch(ArgumentException){pathRejected=true;}Check(pathRejected,"Install request path rejects traversal");
        var smallWorkArea=new System.Windows.Rect(0,0,1024,720);
        var fitted=MainWindow.FitWindow(smallWorkArea,1240,880);
        Check(smallWorkArea.Contains(fitted) && fitted.Height<720,"Initial window including caption fits a small work area");
        var offsetWorkArea=new System.Windows.Rect(-1920,40,1920,1040);
        Check(offsetWorkArea.Contains(MainWindow.FitWindow(offsetWorkArea,1240,880)),"Window fitting respects work-area origin");
        Check(GitHubReleaseClient.IsSetupAsset("System-Compass-Setup-1.8.0.exe",new Version(1,8,0)),"Exact versioned Setup EXE is supported");
        Check(!GitHubReleaseClient.IsSetupAsset("System-Compass-Setup-1.8.0.exe",new Version(1,9,0)),"Setup version mismatch is rejected");
        Check(GitHubReleaseClient.IsAllowedDownloadUrl("https://github.com/example/pusula/releases/download/v1.8.0/System-Compass-Setup-1.8.0.exe","example/pusula",new Version(1,8,0)),"Setup download must match repository and version");
        var setupJson=System.Text.Json.JsonSerializer.Serialize(new {draft=false,prerelease=false,tag_name="v1.8.0",html_url="https://github.com/example/pusula/releases/tag/v1.8.0",assets=new[]{new{name="System-Compass-Setup-1.8.0.exe",digest="sha256:"+new string('a',64),state="uploaded",size=120000000L,browser_download_url="https://github.com/example/pusula/releases/download/v1.8.0/System-Compass-Setup-1.8.0.exe"},new{name="Sistem-Pusulasi-1.8.zip",digest="sha256:"+new string('b',64),state="uploaded",size=1000L,browser_download_url="https://github.com/example/pusula/releases/download/v1.8.0/Sistem-Pusulasi-1.8.zip"}}});
        using(var setupDoc=System.Text.Json.JsonDocument.Parse(setupJson)) {
            var setupRelease=GitHubReleaseClient.ParseLatestRelease(setupDoc.RootElement,"example/pusula",new Version(1,7,0));
            Check(setupRelease.Release?.DownloadUrl.EndsWith(".exe")==true,"Official Setup is preferred over compatibility ZIP");
        }
        var settings=new AppSettings();
        Check(settings.AutoRepair && settings.DeepScanIntervalDays==7,"Safe default interval and auto-repair preference");
        var languageTestRoot=Path.Combine(Path.GetTempPath(),"SystemCompassLanguageTest-"+Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(languageTestRoot);
            var markerPath=Path.Combine(languageTestRoot,"installer-language.txt");
            var appliedPath=Path.Combine(languageTestRoot,"user","applied.txt");
            var languageSettings=new AppSettings {Language="tr",GitHubRepository="example/preserved",ScheduleHour=9};
            File.WriteAllText(markerPath,"english|setup-1");
            int saves=0;
            void SaveLanguageSettings(AppSettings value)=>saves++;
            Check(InstallerLanguage.ApplyPending(markerPath,appliedPath,languageSettings,SaveLanguageSettings),"Installer English choice is applied");
            Check(languageSettings.Language=="en" && languageSettings.GitHubRepository=="example/preserved" && languageSettings.ScheduleHour==9,"Installer language update preserves other preferences");
            Check(!InstallerLanguage.ApplyPending(markerPath,appliedPath,languageSettings,SaveLanguageSettings) && saves==1,"Applied installer language is not reapplied on later launches");
            File.WriteAllText(markerPath,"unknown|setup-2");
            Check(!InstallerLanguage.ApplyPending(markerPath,appliedPath,languageSettings,SaveLanguageSettings) && languageSettings.Language=="en","Unknown installer language is ignored");
        } finally { try { Directory.Delete(languageTestRoot,true); } catch { } }
        Check(LocalStore.DeepDue(settings,Array.Empty<ScanReport>()),"First deep scan due");
        var recent=new ScanReport {DeepScan=true,FinishedUtc=DateTimeOffset.UtcNow,Findings=new(){new(){Category="Windows",Status="Unknown"}}};
        Check(LocalStore.DeepDue(settings,new[]{recent}),"Unknown result cannot suppress next deep scan");
        recent.Findings[0].Status="Healthy";
        Check(!LocalStore.DeepDue(settings,new[]{recent}),"Successful recent deep scan respected");
        recent.FinishedUtc=DateTimeOffset.UtcNow.AddDays(100);
        Check(LocalStore.DeepDue(settings,new[]{recent}),"Future timestamps do not suppress scan");
        XNamespace ns="http://schemas.microsoft.com/windows/2004/02/mit/task";
        var doc=XDocument.Parse(SchedulerService.BuildTaskXml("C:\\Test & space\\SistemPusulasi.exe","S-1-5-21-123",18));
        string Value(string name)=>doc.Descendants(ns+name).First().Value;
        Check(Value("Command")=="C:\\Test & space\\SistemPusulasi.exe","Scheduler XML escapes executable path");
        Check(Value("LogonType")=="InteractiveToken" && Value("UserId")!="SYSTEM","User session scheduling, not SYSTEM");
        Check(Value("AllowHardTerminate")=="false" && Value("StopOnIdleEnd")=="false" && Value("StopIfGoingOnBatteries")=="false","No automatic termination during repair");
        Check(Value("WakeToRun")=="false" && Value("MultipleInstancesPolicy")=="IgnoreNew","No wake and no overlapping scheduled scans");
        Check(Value("Arguments")=="--scheduled" && Value("DisallowStartIfOnBatteries")=="true","Fixed scheduled action, AC-only starts");
        Check(DiagnosticsPolicy.Dism("The operation completed successfully.",0)==DiagnosticsPolicy.Integrity.Unknown,"DISM exit zero alone is not healthy");
        Check(DiagnosticsPolicy.Dism("The component store is repairable.",0)==DiagnosticsPolicy.Integrity.Corrupt,"Explicit DISM corruption recognized");
        Check(DiagnosticsPolicy.Dism("No component store corruption detected.",0)==DiagnosticsPolicy.Integrity.Healthy,"Explicit DISM clean recognized");
        Check(DiagnosticsPolicy.Sfc("Windows Resource Protection found integrity violations.",0)==DiagnosticsPolicy.Integrity.Corrupt,"SFC exit zero may contain corruption");
        Check(DiagnosticsPolicy.Sfc("Erişim engellendi.",0)==DiagnosticsPolicy.Integrity.Unknown,"Unrecognized output never healthy");
        Check(DiagnosticsPolicy.Sfc("Windows Resource Protection did not find any integrity violations.",0)==DiagnosticsPolicy.Integrity.Healthy,"SFC clean recognized");
        var now=DateTimeOffset.UtcNow;
        Check(DiagnosticsPolicy.CanAutoRepair(true,true,true,true,false,false,null,now),"Confirmed eligible repair allowed");
        Check(!DiagnosticsPolicy.CanAutoRepair(false,true,true,true,false,false,null,now),"No repair for unknown/counters alone");
        Check(!DiagnosticsPolicy.CanAutoRepair(true,false,true,true,false,false,null,now),"User opt-out respected");
        Check(!DiagnosticsPolicy.CanAutoRepair(true,true,false,true,false,false,null,now),"No repair without admin");
        Check(!DiagnosticsPolicy.CanAutoRepair(true,true,true,false,false,false,null,now),"No repair on battery or unknown AC");
        Check(!DiagnosticsPolicy.CanAutoRepair(true,true,true,true,true,false,null,now),"Pending reboot defers repair");
        Check(!DiagnosticsPolicy.CanAutoRepair(true,true,true,true,false,true,null,now),"Existing servicing defers repair");
        Check(!DiagnosticsPolicy.CanAutoRepair(true,true,true,true,false,false,now.AddDays(-1),now),"Seven day repair cooldown");
        Check(!DiagnosticsPolicy.CanAutoRepair(true,true,true,true,false,false,now.AddYears(96),now),"Future last-attempt never causes repeated repairs");
        Check(MainWindow.DiagnosticLabel(new ScanReport(),"C: dosya sistemi")=="Bu taramada kontrol edilmedi","Missing disk scan never shown healthy");
        Check(MainWindow.DiagnosticLabel(new ScanReport {Findings=new(){new(){Title="Windows bileşen deposu",Status="Unknown"}}},"Windows bileşen deposu")=="Sonuç doğrulanamadı","Unknown diagnostic card stays unknown");
        Check(!ReleaseFeed.IsNewer("1.0.0") && !ReleaseFeed.IsNewer("1.1.0") && ReleaseFeed.IsNewer("2.0.0") && !ReleaseFeed.IsNewer("bad"),"Release notices require a valid newer version");
        Check(UpdateInventory.ParseResult("{}","windows").Status.StartsWith("Unknown:"),"Missing update data cannot imply up-to-date");
        Check(UpdateInventory.ParseResult("{\"Entries\":[],\"Status\":\"Unknown: partial results\"}","windows").Status.StartsWith("Unknown:"),"Partial Windows Update results stay unknown");
        Check(UpdateInventory.ParseResult("{\"Entries\":[],\"Status\":\"Checked: source completed\"}","windows").Status.StartsWith("Checked:"),"Explicit successful source response accepted");
        Check(UpdateInventory.ParseResult("{\"Entries\":[{\"Name\":\"Test\",\"InstalledVersion\":\"1\",\"AvailableVersion\":\"2\",\"Source\":\"Windows Update\",\"Kind\":\"drivers\"}],\"Status\":\"Checked: test\"}","windows").Status.StartsWith("Unknown:"),"Mismatched update category rejected");
        string WingetLine(string name,string id,string installed,string available)=>name.PadRight(24)+id.PadRight(30)+installed.PadRight(16)+available.PadRight(16)+"winget";
        var table=WingetLine("Name","Id","Version","Available")+"\n"+new string('-',100)+"\n"+WingetLine("Example app","Vendor.Example","1.0.0","2.0.0");
        var parsed=UpdateInventory.ParseWingetOutput(table,0);
        Check(parsed.Entries.Count==1 && parsed.Entries[0].AvailableVersion=="2.0.0","WinGet fixed-column version extraction");
        var turkish=table.Replace("Name","Ad  ").Replace("Version","Sürüm  ").Replace("Available","Yeni     ");
        Check(UpdateInventory.ParseWingetOutput(turkish,0).Entries.Count==1,"Localized WinGet headings supported");
        Check(UpdateInventory.ParseWingetOutput(table,1).Status.StartsWith("Unknown:"),"Failed WinGet exit cannot imply valid scan");
        Check(UpdateInventory.ParseWingetOutput("No updates found",0).Status.StartsWith("Unknown:"),"Unstructured empty WinGet output not treated as current");
        ScanReport InterpretFixture(string name,string json) { using var doc=System.Text.Json.JsonDocument.Parse(json); var r=new ScanReport(); DiagnosticsEngine.Interpret(r,name,doc.RootElement); return r; }
        foreach(var empty in new[]{"{}","[]","null","[{},null]"})Check(!InterpretFixture("Events:disk",empty).Findings.Any(f=>f.Status is "Warning" or "Critical"),"Empty event payload never creates warning: "+empty);
        Check(InterpretFixture("FutureEvents:disk","{}").Findings.Count==0,"Empty payload never creates future-date alert");
        Check(InterpretFixture("MemoryTest","{}").Findings.Single().Detail.Contains("sonucu bulunamadı"),"Empty memory history is explained as no test result");
        Check(InterpretFixture("Events:disk","{\"Id\":7}").Findings.Single().Status=="Unknown","Malformed event stays unknown rather than warning");
        Check(InterpretFixture("Events:disk","{\"Id\":7,\"Time\":\"2026-10-07T12:00:00Z\",\"Level\":2,\"Message\":\"fixture\"}").Findings.Single().Status=="Warning","Real event warning retained");
        var avFixture="{\"SchemaVersion\":1,\"CheckedUtc\":\"2026-10-06T12:00:00Z\",\"ProvidersKnown\":true,\"Providers\":[\"Windows Defender\"],\"DefenderKnown\":true,\"Defender\":{\"AMRunningMode\":\"Normal\",\"AMServiceEnabled\":true,\"AntivirusEnabled\":true,\"RealTimeProtectionEnabled\":true},\"ThreatsKnown\":true,\"Threats\":[],\"Action\":{\"Name\":\"read\",\"Result\":\"None\"}}";
        Check(AntivirusService.ParseResult(avFixture).CanScan,"Confirmed active Defender permits actions");
        Check(!AntivirusService.ParseResult(avFixture.Replace("Windows Defender","Kaspersky")).CanScan,"Third-party antivirus blocks Defender actions");
        Check(!AntivirusService.ParseResult(avFixture.Replace("Normal","Passive Mode")).CanScan,"Passive Defender never forced active");
        Check(!AntivirusService.ParseResult(avFixture.Replace("\"ProvidersKnown\":true","\"ProvidersKnown\":false")).CanScan,"Unknown providers block actions");
        Check(!AntivirusService.ParseResult(avFixture.Replace("\"AntivirusEnabled\":true","\"AntivirusEnabled\":\"true\"")).CanScan,"String true cannot authorize action");
        Check(!AntivirusService.ParseResult("{}").CanScan,"Malformed antivirus response stays unknown");
        Check(AntivirusService.ParseResult(avFixture.Replace("\"Threats\":[]","\"Threats\":[{\"Name\":\"Test threat\",\"IsActive\":true}]")).Findings.Any(f=>f.Status=="Critical"),"Active threat is a critical finding");
        Check(!AntivirusService.ParseResult(avFixture.Replace("\"Threats\":[]","\"Threats\":[{\"Name\":\"Test threat\",\"IsActive\":false}]")).Findings.Any(f=>f.Status=="Critical"),"Historical threat not counted active");
        Check(AntivirusService.ParseResult(avFixture.Replace("\"ThreatsKnown\":true","\"ThreatsKnown\":false")).Findings.Any(f=>f.Title=="Etkin Defender tespitleri" && f.Status=="Unknown"),"Unknown threat query never clean");
        Check(!AntivirusService.ParseResult(avFixture.Replace("\"Name\":\"read\",\"Result\":\"None\"","\"Name\":\"quick\",\"Result\":\"Submitted\"")).Findings.Any(f=>f.Status=="Healthy"),"Scan submission not a clean verdict");
        Check(GitHubReleaseClient.NormalizeRepository("https://github.com/example/pusula.git")=="example/pusula","GitHub repository normalization");
        bool rejectedRepo=false;try{GitHubReleaseClient.NormalizeRepository("https://evil.example/example/pusula");}catch(ArgumentException){rejectedRepo=true;}Check(rejectedRepo,"Non-GitHub repository rejected");
        Check(!GitHubReleaseClient.TryParseVersionTag("v1.4.0-beta",out _),"Prerelease tags rejected");
        var releaseJson=System.Text.Json.JsonSerializer.Serialize(new {draft=false,prerelease=false,tag_name="v1.4.0",html_url="https://github.com/example/pusula/releases/tag/v1.4.0",body="Fixture",assets=new[]{new{name="Sistem-Pusulasi-1.4.zip",digest="sha256:"+new string('a',64),state="uploaded",size=100,browser_download_url="https://github.com/example/pusula/releases/download/v1.4.0/Sistem-Pusulasi-1.4.zip"}}});
        GitHubReleaseCheck ParseRelease(string json){using var doc=System.Text.Json.JsonDocument.Parse(json);return GitHubReleaseClient.ParseLatestRelease(doc.RootElement,"example/pusula",new Version(1,3,0));}
        Check(ParseRelease(releaseJson).Release!=null,"Verified release metadata accepted");
        Check(ParseRelease(releaseJson.Replace("sha256:","missing:")).Release==null,"Release without digest cannot download");
        Check(!GitHubReleaseClient.IsAllowedDownloadUrl("https://github.com/other/pusula/releases/download/v1.4.0/Sistem-Pusulasi-1.4.zip","example/pusula",new Version(1,4,0)),"Cross-repository download rejected");
        using(var memory=new MemoryStream()) {
            using(var zip=new System.IO.Compression.ZipArchive(memory,System.IO.Compression.ZipArchiveMode.Create,true)){using var writer=new StreamWriter(zip.CreateEntry("../escape.exe").Open());writer.Write("test");}
            memory.Position=0;using var readZip=new System.IO.Compression.ZipArchive(memory,System.IO.Compression.ZipArchiveMode.Read);
            bool blocked=false;try{GitHubReleaseClient.ValidateArchive(readZip,new Version(1,4,0));}catch(InvalidDataException){blocked=true;}Check(blocked,"ZIP path traversal blocked before extraction");
        }
        Check(!new UpdateRow(new UpdateEntry("Example","1","Bilinmiyor","Windows kurulu program kaydı","software")).HasUpdate,"Inventory-only records never counted as updates");
        Check(new UpdateRow(new UpdateEntry("Example","1","2","WinGet","software")).HasUpdate,"Available software update counted");
        L10n.SetLanguage("en");
        Check(L10n.T("Genel bakış")=="Overview","English navigation translation");
        Check(L10n.T("Sistem Pusulası")=="System Compass","English product branding");
        var englishWindow=new MainWindow(true);englishWindow.InitializeLanguagePicker();
        var englishHeading=((System.Windows.Controls.TextBlock)englishWindow.FindName("PageTitle")).Text;
        Check(englishHeading!="Bilgisayarının durumu" && !string.IsNullOrWhiteSpace(englishHeading),"English WPF resources render");
        Check(((System.Windows.Controls.ComboBox)englishWindow.FindName("LanguagePicker")).SelectedValue?.ToString()=="en","Language selector reflects English");
        Check(((System.Windows.Controls.Button)englishWindow.FindName("RepairButton")).Content?.ToString()=="Repair Windows corruption","English repair action is visible and translated");
        Check(((System.Windows.Controls.Button)englishWindow.FindName("InstallSelectedButton")).Content?.ToString()=="Update selected items (0)","English selected update action is translated");
        Check(!((System.Windows.Controls.Button)englishWindow.FindName("InstallSelectedButton")).IsEnabled,"No installation action without a selected eligible item");
        Check(!((System.Windows.Controls.Button)englishWindow.FindName("RepairButton")).IsEnabled,"Demo and unconfirmed findings cannot start repair");
        Check(englishWindow.WindowStyle==System.Windows.WindowStyle.SingleBorderWindow && englishWindow.ResizeMode==System.Windows.ResizeMode.CanResizeWithGrip,"Native window caption controls and resize grip remain enabled");
        var selectionCell=new System.Windows.Controls.DataGridCell {Style=((System.Windows.Controls.DataGrid)englishWindow.FindName("UpdatesList")).CellStyle,IsSelected=true};
        selectionCell.ApplyTemplate();
        Check(selectionCell.Foreground is System.Windows.Media.SolidColorBrush selectedInk && selectedInk.Color==System.Windows.Media.Color.FromRgb(0x15,0x5E,0x63),"Selected cell retains readable dark text without keyboard focus");
        Check(selectionCell.Background is System.Windows.Media.SolidColorBrush selectedFill && selectedFill.Color==System.Windows.Media.Color.FromRgb(0xE0,0xF1,0xEF),"Selected cell uses matching light background");
        selectionCell.IsSelected=false;
        Check(selectionCell.Foreground is System.Windows.Media.SolidColorBrush normalInk && normalInk.Color==System.Windows.Media.Color.FromRgb(0x17,0x28,0x3F),"Deselected cell restores readable normal text");
        englishWindow.Close();
        var saved=System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(new AppSettings {Language="en",GitHubRepository="example/system-compass"}))!;
        Check(saved.Language=="en" && saved.GitHubRepository=="example/system-compass","Language preference roundtrip preserves repository");
        L10n.SetLanguage("tr");
        var turkishWindow=new MainWindow(true);turkishWindow.InitializeLanguagePicker();
        Check(((System.Windows.Controls.TextBlock)turkishWindow.FindName("PageTitle")).Text=="Bilgisayarının durumu","Turkish resources restored on new window");
        turkishWindow.Close();
        L10n.SetLanguage("unsupported");Check(L10n.Language=="tr","Unsupported language safely falls back");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory,"self-test-results.txt"),results);
    }
}

