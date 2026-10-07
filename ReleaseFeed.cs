using System.IO;
namespace SistemPusulasi;

// Local release notices only. A manifest never executes or installs a payload.
public sealed class ReleaseNotice
{
    public string Version { get; set; } = "";
    public string Notes { get; set; } = "";
}
public static class ReleaseFeed
{
    public static readonly string FeedDirectory=AppContext.BaseDirectory;
    public static Version Current => typeof(ReleaseFeed).Assembly.GetName().Version ?? new Version(1,1,0);
    private static Version Normalize(Version v)=>new(v.Major,v.Minor,Math.Max(v.Build,0),Math.Max(v.Revision,0));
    public static bool IsNewer(string candidate) => Version.TryParse(candidate,out var version) && Normalize(version)>Normalize(Current);
    public static ReleaseNotice? Check()
    {
        var notice=LocalStore.Read<ReleaseNotice>(Path.Combine(FeedDirectory,"release.json"));
        return notice!=null && IsNewer(notice.Version) ? notice : null;
    }
}
