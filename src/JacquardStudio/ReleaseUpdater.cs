using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;

namespace JacquardStudio;

/// <summary>Checks the public release manifest and offers a checksum-verified installer download.</summary>
public static class ReleaseUpdater
{
    private const long MaximumInstallerSize = 200L * 1024 * 1024;
    private const int MaximumManifestSize = 16 * 1024;
    private static readonly Uri ManifestEndpoint = new("https://www.mirkhalili.com/app/download.php?manifest=1");
    private static readonly Uri InstallerEndpoint = new("https://www.mirkhalili.com/app/download.php");
    private static readonly Regex VersionPattern = new(
        @"\A(?<numbers>[0-9]{1,9}(?:\.[0-9]{1,9}){1,3})(?:-(?<pre>[A-Za-z0-9][A-Za-z0-9.-]{0,31}))?(?:\+(?<build>[A-Za-z0-9][A-Za-z0-9.-]{0,31}))?\z",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Md5Pattern = new(@"\A[a-fA-F0-9]{32}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly Regex Sha256Pattern = new(@"\A[a-fA-F0-9]{64}\z", RegexOptions.CultureInvariant | RegexOptions.Compiled);
    private static readonly HttpClient CheckClient = CreateClient(TimeSpan.FromSeconds(15));
    private static readonly HttpClient DownloadClient = CreateClient(Timeout.InfiniteTimeSpan);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ReleaseManifest(
        [property: JsonPropertyName("product")] string? Product,
        [property: JsonPropertyName("version")] string? Version,
        [property: JsonPropertyName("md5")] string? Md5,
        [property: JsonPropertyName("sha256")] string? Sha256,
        [property: JsonPropertyName("size")] long Size);

    private sealed record ParsedVersion(int[] Components, string[] Prerelease);

    private static HttpClient CreateClient(TimeSpan timeout) => new(new HttpClientHandler
    {
        AllowAutoRedirect = false,
        AutomaticDecompression = DecompressionMethods.None,
    }) { Timeout = timeout };

    /// <summary>Returns a SemVer-like ordering for the numeric and prerelease parts used by version.txt.</summary>
    public static int CompareVersions(string left, string right)
    {
        var a = ParseVersion(left);
        var b = ParseVersion(right);
        for (int i = 0; i < Math.Max(a.Components.Length, b.Components.Length); i++)
        {
            int av = i < a.Components.Length ? a.Components[i] : 0;
            int bv = i < b.Components.Length ? b.Components[i] : 0;
            int numberComparison = av.CompareTo(bv);
            if (numberComparison != 0) return numberComparison;
        }

        if (a.Prerelease.Length == 0 || b.Prerelease.Length == 0)
            return a.Prerelease.Length.CompareTo(b.Prerelease.Length) * -1; // A final release is newer than its prerelease.

        for (int i = 0; i < Math.Min(a.Prerelease.Length, b.Prerelease.Length); i++)
        {
            string av = a.Prerelease[i];
            string bv = b.Prerelease[i];
            bool aNumeric = IsAsciiDigits(av);
            bool bNumeric = IsAsciiDigits(bv);
            int comparison;
            if (aNumeric && bNumeric)
            {
                string an = av.TrimStart('0');
                string bn = bv.TrimStart('0');
                comparison = an.Length.CompareTo(bn.Length);
                if (comparison == 0) comparison = string.CompareOrdinal(an, bn);
            }
            else if (aNumeric != bNumeric) comparison = aNumeric ? -1 : 1;
            else comparison = string.CompareOrdinal(av, bv);
            if (comparison != 0) return Math.Sign(comparison);
        }
        return a.Prerelease.Length.CompareTo(b.Prerelease.Length);
    }

    private static ParsedVersion ParseVersion(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 64)
            throw new FormatException("Version is invalid.");
        var match = VersionPattern.Match(value);
        if (!match.Success) throw new FormatException("Version is invalid.");
        var components = match.Groups["numbers"].Value.Split('.').Select(part =>
            int.Parse(part, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();
        string pre = match.Groups["pre"].Value;
        return new ParsedVersion(components, pre.Length == 0 ? Array.Empty<string>() : pre.Split('.'));
    }

    private static bool IsAsciiDigits(string value) => value.Length > 0 && value.All(ch => ch is >= '0' and <= '9');

    /// <param name="owner">Window that owns update prompts.</param>
    /// <param name="manual">When true, report no-update and network-error results to the user.</param>
    public static async Task CheckForUpdatesAsync(Window owner, bool manual = false)
    {
        bool updateAccepted = false;
        try
        {
            ReleaseManifest? release = await ReadManifestAsync();
            if (release is null)
            {
                if (manual && owner.IsVisible)
                    MessageBox.Show(owner, "هنوز نسخه‌ای برای به‌روزرسانی در سایت منتشر نشده است.", "بررسی به‌روزرسانی", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (CompareVersions(release.Version!, AppVersion.Display) <= 0)
            {
                if (manual && owner.IsVisible)
                    MessageBox.Show(owner, $"نسخهٔ نصب‌شده ({AppVersion.Display}) به‌روز است.", "بررسی به‌روزرسانی", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string prompt = $"نسخهٔ تازهٔ {release.Version} در دسترس است؛ نسخهٔ نصب‌شده: {AppVersion.Display}.\n\nآیا فایل نصب را دریافت و اجرا می‌کنید؟ پس از تأیید، اندازه و MD5 و SHA-256 فایل بررسی می‌شود. این بررسی از همان وب‌سایت می‌آید و جایگزین امضای دیجیتال ناشر نیست.";
            if (!owner.IsVisible || MessageBox.Show(owner, prompt, "به‌روزرسانی Jacquard Studio", MessageBoxButton.YesNo, MessageBoxImage.Information) != MessageBoxResult.Yes)
                return;
            updateAccepted = true;

            string installerPath = await DownloadAndVerifyAsync(release);
            _ = Process.Start(new ProcessStartInfo { FileName = installerPath, UseShellExecute = true })
                ?? throw new InvalidOperationException("Windows could not start the installer.");
        }
        catch (Exception) when (!manual && !updateAccepted)
        {
            // Update checks are best-effort and never block startup or the design workflow.
        }
        catch (Exception)
        {
            if (owner.IsVisible)
                MessageBox.Show(owner, "بررسی یا آماده‌سازی به‌روزرسانی انجام نشد. اتصال اینترنت و انتشار فایل را بررسی و دوباره تلاش کنید.", "خطای به‌روزرسانی", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private static async Task<ReleaseManifest?> ReadManifestAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var response = await CheckClient.GetAsync(ManifestEndpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaximumManifestSize)
            throw new InvalidDataException("Release manifest is too large.");

        await using var input = await response.Content.ReadAsStreamAsync();
        using var output = new MemoryStream();
        byte[] buffer = new byte[4096];
        while (true)
        {
            int read = await input.ReadAsync(buffer.AsMemory(), timeout.Token);
            if (read == 0) break;
            if (output.Length + read > MaximumManifestSize) throw new InvalidDataException("Release manifest is too large.");
            output.Write(buffer, 0, read);
        }

        ReleaseManifest manifest = JsonSerializer.Deserialize<ReleaseManifest>(output.ToArray(), JsonOptions)
            ?? throw new InvalidDataException("Release manifest is empty.");
        if (manifest.Product != "JacquardStudio" || manifest.Version is null || manifest.Version.Length > 64 ||
            !VersionPattern.IsMatch(manifest.Version) || manifest.Md5 is null || !Md5Pattern.IsMatch(manifest.Md5) ||
            manifest.Sha256 is null || !Sha256Pattern.IsMatch(manifest.Sha256) ||
            manifest.Size is < 1024 or > MaximumInstallerSize)
            throw new InvalidDataException("Release manifest is invalid.");
        return manifest;
    }

    private static async Task<string> DownloadAndVerifyAsync(ReleaseManifest release)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(15));
        using var response = await DownloadClient.GetAsync(InstallerEndpoint, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is long contentLength && contentLength != release.Size)
            throw new InvalidDataException("Installer length does not match the release manifest.");

        string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JacquardStudio", "Updates");
        Directory.CreateDirectory(directory);
        string tempPath = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".download");
        string installerPath = Path.Combine(directory, "JacquardStudio-Setup-" + release.Version + "-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using var input = await response.Content.ReadAsStreamAsync();
            await using (var output = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                byte[] buffer = new byte[128 * 1024];
                byte[] signature = new byte[2];
                int signatureLength = 0;
                long total = 0;
                while (true)
                {
                    int read = await input.ReadAsync(buffer.AsMemory(), timeout.Token);
                    if (read == 0) break;
                    total += read;
                    if (total > MaximumInstallerSize || total > release.Size) throw new InvalidDataException("Installer exceeds its declared size.");
                    if (signatureLength < signature.Length)
                    {
                        int copy = Math.Min(signature.Length - signatureLength, read);
                        Buffer.BlockCopy(buffer, 0, signature, signatureLength, copy);
                        signatureLength += copy;
                    }
                    md5.AppendData(buffer, 0, read);
                    sha256.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), timeout.Token);
                }
                await output.FlushAsync(timeout.Token);
                if (total != release.Size || signatureLength != 2 || signature[0] != (byte)'M' || signature[1] != (byte)'Z')
                    throw new InvalidDataException("Downloaded installer is incomplete or invalid.");
            }

            byte[] expectedMd5 = Convert.FromHexString(release.Md5!);
            byte[] expectedSha256 = Convert.FromHexString(release.Sha256!);
            if (!CryptographicOperations.FixedTimeEquals(md5.GetHashAndReset(), expectedMd5) ||
                !CryptographicOperations.FixedTimeEquals(sha256.GetHashAndReset(), expectedSha256))
                throw new InvalidDataException("Downloaded installer checksum does not match.");
            File.Move(tempPath, installerPath);
            return installerPath;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch (IOException) { }
            try { if (File.Exists(installerPath)) File.Delete(installerPath); } catch (IOException) { }
            throw;
        }
    }
}
