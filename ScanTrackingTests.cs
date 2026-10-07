using System.IO;
using System.Text.Json;

namespace SistemPusulasi;

internal static class ScanTrackingTests
{
    internal static IEnumerable<string> Run()
    {
        var results=new List<string>();
        void Check(bool condition,string name) { if(!condition) throw new InvalidOperationException("Test failed: "+name);results.Add("PASS: "+name); }
        var snapshots=new List<ScanProgress>();
        var tracker=new ScanTracker(false,false,snapshots.Add,"test-scan");
        var report=new ScanReport();tracker.Attach(report);tracker.Message("Preparing");
        var initial=snapshots.Last();
        Check(initial.ScanId==report.Id && initial.ScanId=="test-scan" && initial.Steps.Select(s=>s.Order).SequenceEqual(Enumerable.Range(1,14)),"Scan timeline has stable identity and ordered stages");
        Check(initial.Steps.Where(s=>s.Id is "sfc" or "chkdsk").All(s=>s.Status=="Skipped" && s.Outcome==""),"Fast scan excludes full file checks without claiming healthy results");
        tracker.Start("system-data","Collecting together");
        report.Findings.Add(new Finding {Title="Sensor",Status="Unknown",Detail="Unavailable"});
        tracker.Finish("system-data");
        var collected=snapshots.Last();
        Check(collected.Steps.Single(s=>s.Id=="system-data").Status=="Unknown" && collected.PartialFindings.Single().Status=="Unknown","Inconclusive collection publishes partial findings before the next scan stage");
        tracker.Start("dism","Checking");report.Findings.Add(new Finding {Title="Windows",Status="Critical",Detail="Corrupt"});tracker.Finish("dism");
        var dism=snapshots.Last().Steps.Single(s=>s.Id=="dism");
        Check(dism.Status=="Completed" && dism.Outcome=="Critical" && dism.StartedUtc.HasValue && dism.FinishedUtc>=dism.StartedUtc,"A completed command preserves a critical health outcome and timestamps");
        report.Findings[0].Detail="Changed later";
        Check(collected.PartialFindings.Single().Detail=="Unavailable" && collected.Steps.Single(s=>s.Id=="dism").Status=="Pending","Previously published progress remains a detached snapshot");
        tracker.SkipPending(new[]{"sfc","chkdsk"},"No administrator");
        tracker.Start("antivirus","Reading");
        var stale=JsonSerializer.Deserialize<ScanProgress>(JsonSerializer.Serialize(tracker.Snapshot()))!;
        ScanTracker.NormalizeStopped(stale);
        Check(!stale.Running && stale.Interrupted && stale.CurrentStepId=="" && stale.FinishedUtc.HasValue && stale.Steps.Where(s=>s.Id=="antivirus" || s.Id=="report").All(s=>s.Status=="Unknown" && s.Outcome=="Unknown"),"Stopped worker cannot leave running or pending checks looking active");
        Check(stale.Steps.Single(s=>s.Id=="dism").Outcome=="Critical" && stale.Steps.Single(s=>s.Id=="sfc").Status=="Skipped","Interrupted normalization preserves finished findings and deliberately skipped checks");
        var manual=new ScanTracker(false,true);var manualState=manual.Snapshot();
        Check(manualState.DeepScan && manualState.Steps.Single(s=>s.Id=="sfc").Status=="Pending" && manualState.Steps.Where(s=>s.Id.StartsWith("updates-")).All(s=>s.Status=="Skipped"),"Manual repair plans full verification and excludes update inventory");
        var throwing=new ScanTracker(true,false,_=>throw new IOException("Observer unavailable"));
        var interruptedReport=new ScanReport();throwing.Attach(interruptedReport);throwing.Start("dism","Checking");throwing.Finish("dism","Unknown");throwing.Stop("Failed",true);
        Check(!throwing.Snapshot().Running && throwing.Snapshot().Interrupted && interruptedReport.Interrupted && throwing.Snapshot().Steps.All(s=>s.Status is not ("Pending" or "Running")),"Observer failure cannot interrupt servicing and terminal snapshots resolve unfinished stages");
        var completed=new ScanTracker(false,false);completed.Stop("Complete");Check(!completed.Snapshot().Interrupted,"Normal completion remains distinguishable from an interrupted scan");
        var failed=new ScanTracker(true,false);failed.Start("dism","Checking");failed.FailActive("Failed");failed.Stop("Partial report saved");Check(failed.Snapshot().Interrupted,"Saving a partial report after a fatal scan error preserves interruption state");
        Check(report.Steps.Single(s=>s.Id=="dism").Outcome=="Critical","Report retains the execution timeline for history");
        return results;
    }
}
