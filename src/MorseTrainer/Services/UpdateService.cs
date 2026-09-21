using System.Text.Json;
using System.Text.RegularExpressions;

namespace MorseTrainer.Services;

/// <summary>Сведения о последнем релизе на GitHub.</summary>
public sealed record UpdateInfo(
    Version LatestVersion,
    string ReleasePageUrl,
    string? WindowsInstallerUrl,
    string? AndroidApkUrl,
    string Notes);

/// <summary>
/// Проверка обновлений через публичный API GitHub Releases.
/// Выходит в интернет только по явному запросу пользователя: приложение остаётся автономным.
/// </summary>
public static partial class UpdateService
{
    public const string RepositoryUrl = "https://github.com/nevadimka1415/morse";
    public const string ReleasesPageUrl = RepositoryUrl + "/releases";
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/nevadimka1415/morse/releases/latest";

    public static async Task<UpdateInfo> FetchLatestAsync(HttpClient client, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseApiUrl);
        // GitHub отвечает 403 без User-Agent
        request.Headers.UserAgent.ParseAdd("MorseTrainer-UpdateCheck");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseRelease(json);
    }

    /// <summary>Разбирает ответ GitHub /releases/latest.</summary>
    public static UpdateInfo ParseRelease(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() ?? string.Empty : string.Empty;
        var version = ParseVersion(tag) ?? throw new FormatException($"Не удалось разобрать версию из тега «{tag}».");
        var page = root.TryGetProperty("html_url", out var pageElement) ? pageElement.GetString() : null;
        var notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() ?? string.Empty : string.Empty;

        string? installer = null;
        string? apk = null;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() ?? string.Empty : string.Empty;
                var url = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
                if (name.EndsWith("-Setup-x64.exe", StringComparison.OrdinalIgnoreCase))
                {
                    installer = url;
                }
                else if (name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                {
                    apk = url;
                }
            }
        }

        return new UpdateInfo(version, string.IsNullOrWhiteSpace(page) ? ReleasesPageUrl : page, installer, apk, notes.Trim());
    }

    /// <summary>«v2.3.0» или «2.3» → 2.3.0; строка без номера → null.</summary>
    public static Version? ParseVersion(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var match = VersionPattern().Match(text.Trim().TrimStart('v', 'V'));
        if (!match.Success)
        {
            return null;
        }

        var build = match.Groups[3].Success ? int.Parse(match.Groups[3].Value) : 0;
        return new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), build);
    }

    public static bool IsNewer(Version current, Version latest) => Normalize(latest) > Normalize(current);

    /// <summary>Сравниваем только major.minor.patch: версия сборки 2.3.0.0 равна тегу v2.3.0.</summary>
    public static Version Normalize(Version version) =>
        new(Math.Max(version.Major, 0), Math.Max(version.Minor, 0), Math.Max(version.Build, 0));

    [GeneratedRegex(@"^(\d+)\.(\d+)(?:\.(\d+))?")]
    private static partial Regex VersionPattern();
}
