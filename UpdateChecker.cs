using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DustDesk;

internal sealed record UpdateInfo(string VersionText, string ReleaseUrl, string? DownloadUrl);

internal static class UpdateChecker
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Abyxs/DustDesk-Desktop-Manager/releases/latest";
    private const string ReleasesUrl = "https://github.com/Abyxs/DustDesk-Desktop-Manager/releases/latest";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static string CurrentVersionText => FormatVersion(CurrentVersion);

    private static Version CurrentVersion
    {
        get
        {
            var versionText = Assembly.GetExecutingAssembly()
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;

            return TryParseVersion(versionText, out var version)
                ? version
                : NormalizeVersion(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0));
        }
    }

    public static async Task<UpdateInfo?> CheckLatestAsync(CancellationToken cancellationToken = default)
    {
        using var httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        request.Headers.UserAgent.ParseAdd($"DustDesk/{CurrentVersionText}");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(stream, JsonOptions, cancellationToken);
        if (!TryParseVersion(release?.TagName, out var latestVersion) || latestVersion <= CurrentVersion)
        {
            return null;
        }

        var releaseUrl = string.IsNullOrWhiteSpace(release?.HtmlUrl) ? ReleasesUrl : release.HtmlUrl;
        var downloadUrl = release?.Assets?
            .Where(asset => !string.IsNullOrWhiteSpace(asset.BrowserDownloadUrl))
            .OrderByDescending(asset => asset.Name.Contains("DustDesk", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(asset => asset.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault()
            ?.BrowserDownloadUrl;

        return new UpdateInfo(FormatVersion(latestVersion), releaseUrl, string.IsNullOrWhiteSpace(downloadUrl) ? null : downloadUrl);
    }

    private static bool TryParseVersion(string? text, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var normalized = text.Trim().TrimStart('v', 'V').Split('+')[0].Split('-')[0];
        if (!Version.TryParse(normalized, out var parsed))
        {
            return false;
        }

        version = NormalizeVersion(parsed);
        return true;
    }

    private static Version NormalizeVersion(Version version)
    {
        return new Version(
            Math.Max(0, version.Major),
            Math.Max(0, version.Minor),
            Math.Max(0, version.Build),
            Math.Max(0, version.Revision));
    }

    private static string FormatVersion(Version version)
    {
        return version.Revision > 0
            ? $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}"
            : $"{version.Major}.{version.Minor}.{version.Build}";
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string TagName { get; set; } = "";

        [JsonPropertyName("html_url")]
        public string HtmlUrl { get; set; } = "";

        [JsonPropertyName("assets")]
        public List<GitHubAsset> Assets { get; set; } = new();
    }

    private sealed class GitHubAsset
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; } = "";
    }
}
