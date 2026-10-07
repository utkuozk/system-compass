using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SistemPusulasi;

public sealed class GitHubRelease
{
    public string Version { get; init; } = "";
    public string Notes { get; init; } = "";
    public string PageUrl { get; init; } = "";
    public string DownloadUrl { get; init; } = "";
    public string Sha256 { get; init; } = "";
    public long Size { get; init; }
}

public sealed class GitHubReleaseCheck
{
    public GitHubRelease? Release { get; init; }
    public string Message { get; init; } = "";
}
public sealed record PackageProgress(long Bytes,long Total,string Stage);

// Public releases only: no tokens, shell execution, installation, or unchecked URLs.
public static class GitHubReleaseClient
{
    internal const long MaxPackageBytes = 100L * 1024 * 1024;
    private const int MaxJsonBytes = 2 * 1024 * 1024;
    private static readonly string[] RequiredFiles =
    ["SistemPusulasi.exe", "SistemPusulasi-Kur.exe", "SistemPusulasi.dll",
     "SistemPusulasi.deps.json", "SistemPusulasi.runtimeconfig.json", "KULLANIM.txt", "self-test-results.txt"];
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false,
            UseDefaultCredentials = false, Credentials = null
        };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SistemPusulasi/1.0");
        return client;
    }

    public static async Task<GitHubReleaseCheck> CheckAsync(string repository)
    {
        try
        {
            string repo = NormalizeRepository(repository);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repo}/releases/latest");
            request.Headers.Accept.ParseAdd("application/vnd.github+json");
            request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return Result("Herkese açık depoda yayımlanmış bir sürüm bulunamadı. Depo adresini kontrol edin.");
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
                return Result("GitHub istek sınırına ulaşıldı veya erişim reddedildi. Daha sonra tekrar deneyin.");
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxJsonBytes)
                throw new InvalidDataException("GitHub yanıtı izin verilen boyutu aşıyor.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var data = new MemoryStream();
            await CopyLimitedAsync(input, data, MaxJsonBytes, timeout.Token).ConfigureAwait(false);
            using var json = JsonDocument.Parse(data.ToArray(), new JsonDocumentOptions { MaxDepth = 32 });
            return ParseLatestRelease(json.RootElement, repo, ReleaseFeed.Current);
        }
        catch (OperationCanceledException) { return Result("GitHub sürüm kontrolü zaman aşımına uğradı."); }
        catch (HttpRequestException) { return Result("GitHub'a bağlanılamadı. İnternet bağlantısını kontrol edin."); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or JsonException or InvalidOperationException or FormatException)
        { return Result("Sürüm bilgisi doğrulanamadı: " + ex.Message); }
    }

    public static async Task<string> DownloadAsync(GitHubRelease release, string repository, IProgress<PackageProgress>? progress=null)
    {
        ArgumentNullException.ThrowIfNull(release);
        string repo = NormalizeRepository(repository);
        if (!TryParseVersionTag(release.Version, out var version) || !IsNewer(version, ReleaseFeed.Current))
            throw new InvalidDataException("İndirilecek sürüm geçerli veya daha yeni değil.");
        if (!IsSha256(release.Sha256) || release.Size <= 0 || release.Size > MaxPackageBytes)
            throw new InvalidDataException("Paketin SHA-256 değeri veya boyutu doğrulanamadı.");
        if (!IsReleasePageUrl(release.PageUrl, repo, version) || !IsAllowedDownloadUrl(release.DownloadUrl, repo, version))
            throw new InvalidDataException("Paket adresi bu GitHub deposuna ait değil.");
        if (GetUrlSegments(release.PageUrl)[4] != GetUrlSegments(release.DownloadUrl)[4])
            throw new InvalidDataException("Paket ve sürüm sayfası etiketleri eşleşmiyor.");

        string updates = Path.GetFullPath(Path.Combine(LocalStore.Root, "Updates"));
        Directory.CreateDirectory(updates);
        EnsureNoReparsePoint(updates);
        string staging = Path.Combine(updates, version.ToString(3) + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        string archivePath = Path.Combine(staging, "package.zip");
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
            progress?.Report(new PackageProgress(0,release.Size,"downloading"));
            using var response = await OpenDownloadAsync(new Uri(release.DownloadUrl), repo, version, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is long length && length != release.Size)
                throw new InvalidDataException("Paket boyutu sürüm bilgisiyle eşleşmiyor.");
            await using (var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false))
            await using (var output = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
            {
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > release.Size || total > MaxPackageBytes) throw new InvalidDataException("Paket izin verilen boyutu aşıyor.");
                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token).ConfigureAwait(false);
                    progress?.Report(new PackageProgress(total,release.Size,"downloading"));
                }
                if (total != release.Size || !CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(release.Sha256)))
                    throw new InvalidDataException("Paketin SHA-256 doğrulaması başarısız. Kurulum hazırlanmadı.");
            }
            progress?.Report(new PackageProgress(release.Size,release.Size,"verifying"));
            using (var zip = ZipFile.OpenRead(archivePath))
            {
                var entries = ValidateArchive(zip, version);
                foreach (var entry in entries)
                {
                    timeout.Token.ThrowIfCancellationRequested();
                    await using var input = entry.Open();
                    await using var output = new FileStream(Path.Combine(staging, entry.Name), FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                    long extracted = await CopyLimitedAsync(input, output, entry.Length, timeout.Token).ConfigureAwait(false);
                    if (extracted != entry.Length) throw new InvalidDataException("ZIP dosya boyutu doğrulanamadı.");
                }
            }
            File.Delete(archivePath);
            progress?.Report(new PackageProgress(release.Size,release.Size,"ready"));
            return staging;
        }
        catch
        {
            // Only this newly-created GUID staging folder is removed on failure.
            try { Directory.Delete(staging, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    internal static string NormalizeRepository(string repository)
    {
        string value = (repository ?? "").Trim();
        if (value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!TrySecureUri(value, "github.com", out var uri) || uri.Query.Length != 0 || HasUnsafeUrlPath(value))
                throw new ArgumentException("Depo owner/repo veya https://github.com/owner/repo biçiminde olmalıdır.");
            value = uri.AbsolutePath.Trim('/');
        }
        if (value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        var parts = value.Split('/');
        if (parts.Length != 2 || !Regex.IsMatch(parts[0], @"\A[A-Za-z0-9](?:[A-Za-z0-9-]{0,37}[A-Za-z0-9])?\z")
            || parts[0].Contains("--") || !Regex.IsMatch(parts[1], @"\A[A-Za-z0-9_.-]{1,100}\z")
            || parts[1] is "." or "..")
            throw new ArgumentException("Geçerli bir GitHub owner/repo adresi girin.");
        return string.Join('/', parts);
    }

    internal static bool TryParseVersionTag(string tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (tag == null || !Regex.IsMatch(tag, @"\Av?(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:\.(0|[1-9][0-9]*))?\z")) return false;
        string numeric = tag.StartsWith('v') ? tag[1..] : tag;
        if (!Version.TryParse(numeric, out var parsed)) return false;
        version = new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build));
        return true;
    }

    internal static GitHubReleaseCheck ParseLatestRelease(JsonElement root, string repository, Version current)
    {
        string repo = NormalizeRepository(repository);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("draft", out var draft) || draft.ValueKind != JsonValueKind.False
            || !root.TryGetProperty("prerelease", out var prerelease) || prerelease.ValueKind != JsonValueKind.False)
            return Result("Taslak veya ön sürüm güncellemesi kullanılmaz.");
        string tag = GetString(root, "tag_name");
        if (!TryParseVersionTag(tag, out var version)) return Result("Sürüm etiketi geçerli bir sayısal sürüm değil.");
        if (!IsNewer(version, current)) return Result("En güncel sürüm zaten kullanılıyor.");
        string page = GetString(root, "html_url");
        if (!IsReleasePageUrl(page, repo, version)) throw new InvalidDataException("Sürüm sayfası bu depoya ait değil.");
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return Result("Bu sürümde kurulum ZIP paketi bulunamadı.");
        var matches = assets.EnumerateArray().Where(a => IsAssetName(GetString(a, "name"), version)).ToList();
        if (matches.Count != 1) return Result("Sürüm için tek bir uygun kurulum ZIP paketi bulunamadı.");
        var asset = matches[0];
        string digest = GetString(asset, "digest");
        if (!digest.StartsWith("sha256:", StringComparison.Ordinal) || !IsSha256(digest[7..]))
            return Result("GitHub paketinin SHA-256 doğrulama bilgisi eksik. Doğrulanmış paket yayımlanmalıdır.");
        if (GetString(asset, "state") != "uploaded" || !asset.TryGetProperty("size", out var sizeElement)
            || !sizeElement.TryGetInt64(out long size) || size <= 0 || size > MaxPackageBytes)
            return Result("Paket yükleme durumu veya boyutu geçersiz (en fazla 100 MB).");
        string download = GetString(asset, "browser_download_url");
        if (!IsAllowedDownloadUrl(download, repo, version)) throw new InvalidDataException("Paket indirme adresi doğrulanamadı.");
        if (GetUrlSegments(page)[4] != GetUrlSegments(download)[4] || GetUrlSegments(page)[4] != tag)
            throw new InvalidDataException("Paket ve sürüm etiketleri eşleşmiyor.");
        return new GitHubReleaseCheck
        {
            Release = new GitHubRelease { Version = version.ToString(3), Notes = GetString(root, "body"), PageUrl = page,
                DownloadUrl = download, Sha256 = digest[7..].ToLowerInvariant(), Size = size },
            Message = $"Yeni sürüm hazır: {version.ToString(3)}. Paket kurulumdan önce doğrulanır."
        };
    }

    internal static bool IsAllowedDownloadUrl(string value, string repository, Version version)
    {
        if (!TrySecureUri(value, "github.com", out var uri) || uri.Query.Length != 0 || HasUnsafeUrlPath(value)) return false;
        string[] segments = GetUrlSegments(value);
        return segments.Length == 6 && SameRepository(segments, repository) && segments[2] == "releases" && segments[3] == "download"
            && TryParseVersionTag(segments[4], out var tagVersion) && tagVersion == version && IsAssetName(segments[5], version);
    }

    private static bool IsReleasePageUrl(string value, string repository, Version version)
    {
        if (!TrySecureUri(value, "github.com", out var uri) || uri.Query.Length != 0 || HasUnsafeUrlPath(value)) return false;
        var segments = GetUrlSegments(value);
        return segments.Length == 5 && SameRepository(segments, repository) && segments[2] == "releases" && segments[3] == "tag"
            && TryParseVersionTag(segments[4], out var parsed) && parsed == version;
    }

    private static bool SameRepository(string[] segments, string repository) =>
        string.Equals(segments[0] + "/" + segments[1], repository, StringComparison.OrdinalIgnoreCase);
    private static string[] GetUrlSegments(string url) => new Uri(url).AbsolutePath.TrimStart('/').Split('/');
    private static bool IsAssetName(string name, Version version) => name == $"Sistem-Pusulasi-{PackageVersion(version)}.zip" || name == $"Sistem-Pusulasi-{version.ToString(3)}.zip";
    private static string PackageVersion(Version version) => version.Build == 0 ? version.ToString(2) : version.ToString(3);
    private static bool IsSha256(string value) => value != null && Regex.IsMatch(value, @"\A[0-9a-fA-F]{64}\z");
    private static bool IsNewer(Version version, Version current) => new Version(version.Major, version.Minor, Math.Max(0, version.Build), Math.Max(0, version.Revision)) > new Version(current.Major, current.Minor, Math.Max(0, current.Build), Math.Max(0, current.Revision));
    private static bool HasUnsafeUrlPath(string value) => value.Contains('%') || value.Contains('\\')
        || value[8..].Contains("//") || value[8..].Split('/').Any(p => p is "." or "..");
    private static string GetString(JsonElement element, string name) => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() ?? "" : "";
    private static GitHubReleaseCheck Result(string message) => new() { Message = message };

    private static bool TrySecureUri(string value, string host, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(value, UriKind.Absolute, out uri!) && uri.Scheme == Uri.UriSchemeHttps
            && uri.IdnHost.Equals(host, StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort
            && uri.UserInfo.Length == 0 && uri.Fragment.Length == 0;
    }

    private static async Task<HttpResponseMessage> OpenDownloadAsync(Uri initial, string repository, Version version, CancellationToken token)
    {
        Uri uri = initial;
        for (int redirect = 0; redirect <= 4; redirect++)
        {
            bool allowed = IsAllowedDownloadUrl(uri.AbsoluteUri, repository, version)
                || TrySecureUri(uri.AbsoluteUri, "release-assets.githubusercontent.com", out _)
                || TrySecureUri(uri.AbsoluteUri, "objects.githubusercontent.com", out _);
            if (!allowed) throw new InvalidDataException("GitHub dışına indirme yönlendirmesi reddedildi.");
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            if ((int)response.StatusCode is 301 or 302 or 303 or 307 or 308)
            {
                Uri? location = response.Headers.Location;
                response.Dispose();
                if (location == null || redirect == 4) throw new InvalidDataException("Paket yönlendirmesi geçersiz.");
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }
            return response;
        }
        throw new InvalidDataException("Paket yönlendirme sınırı aşıldı.");
    }

    internal static IReadOnlyList<ZipArchiveEntry> ValidateArchive(ZipArchive zip, Version version)
    {
        if (zip.Entries.Count is < 7 or > 9) throw new InvalidDataException("ZIP beklenen paket içeriğine sahip değil.");
        var files = new List<ZipArchiveEntry>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? folder = null;
        bool directorySeen = false;
        bool? nested = null;
        long total = 0;
        foreach (var entry in zip.Entries)
        {
            string path = entry.FullName;
            int unixType = (entry.ExternalAttributes >> 16) & 0xF000;
            if (unixType is not (0 or 0x8000 or 0x4000) || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0
                || path.Contains('\\') || path.Contains(':') || path.Contains('\0') || path.StartsWith('/') || path.Contains("//"))
                throw new InvalidDataException("ZIP güvensiz bir yol veya bağlantı içeriyor.");
            var parts = path.Split('/');
            if (parts.Any(p => p is "." or "..")) throw new InvalidDataException("ZIP üst klasöre erişmeye çalışıyor.");
            bool directory = path.EndsWith('/');
            if (directory)
            {
                if (parts.Length != 2 || parts[1] != "" || !IsPackageFolder(parts[0], version) || entry.Length != 0 || directorySeen
                    || (folder != null && folder != parts[0]))
                    throw new InvalidDataException("ZIP beklenmeyen bir klasör içeriyor.");
                directorySeen = true;
                folder = parts[0];
                continue;
            }
            if (unixType == 0x4000 || (entry.ExternalAttributes & (int)FileAttributes.Directory) != 0
                || parts.Length is < 1 or > 2 || (parts.Length == 2 && !IsPackageFolder(parts[0], version)))
                throw new InvalidDataException("ZIP dosya yolu geçersiz.");
            bool isNested = parts.Length == 2;
            if (nested.HasValue && nested != isNested) throw new InvalidDataException("ZIP paket klasörleri karışık.");
            nested = isNested;
            if (isNested)
            {
                if (folder != null && folder != parts[0]) throw new InvalidDataException("ZIP birden fazla paket klasörü içeriyor.");
                folder ??= parts[0];
            }
            string name = parts[^1];
            if ((!RequiredFiles.Contains(name, StringComparer.Ordinal) && name != "SistemPusulasi.pdb") || !names.Add(name) || entry.Length <= 0)
                throw new InvalidDataException("ZIP beklenmeyen, yinelenen veya boş bir dosya içeriyor.");
            if (entry.Length > MaxPackageBytes) throw new InvalidDataException("ZIP dosyası 100 MB sınırını aşıyor.");
            total += entry.Length;
            if (total > MaxPackageBytes) throw new InvalidDataException("ZIP açılmış boyutu 100 MB sınırını aşıyor.");
            files.Add(entry);
        }
        if (nested == false && folder != null || RequiredFiles.Any(name => !names.Contains(name)))
            throw new InvalidDataException("ZIP gerekli kurulum dosyalarını içermiyor.");
        return files;
    }

    private static bool IsPackageFolder(string folder, Version version) => folder == $"Sistem-Pusulasi-{PackageVersion(version)}" || folder == $"Sistem-Pusulasi-{version.ToString(3)}";

    private static void EnsureNoReparsePoint(string path)
    {
        for (var dir = new DirectoryInfo(path); dir != null; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("Güncelleme klasörü bir dosya sistemi bağlantısı içeriyor.");
    }

    private static async Task<long> CopyLimitedAsync(Stream input, Stream output, long limit, CancellationToken token)
    {
        byte[] buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > limit) throw new InvalidDataException("İçerik izin verilen boyutu aşıyor.");
            await output.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }
        return total;
    }
}
