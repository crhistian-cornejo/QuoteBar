using System.Text.Json.Serialization;

namespace QuoteBar.Core.Services;

/// <summary>
/// GitHub Release information for auto-update
/// </summary>
public sealed class GitHubRelease
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("tag_name")]
    public string TagName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("body")]
    public string Body { get; set; } = string.Empty;

    [JsonPropertyName("draft")]
    public bool Draft { get; set; }

    [JsonPropertyName("prerelease")]
    public bool Prerelease { get; set; }

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("published_at")]
    public DateTime PublishedAt { get; set; }

    [JsonPropertyName("html_url")]
    public string HtmlUrl { get; set; } = string.Empty;

    [JsonPropertyName("assets")]
    public List<GitHubReleaseAsset> Assets { get; set; } = new();

    /// <summary>
    /// Get version number from tag (e.g., "v1.0.0" -> "1.0.0")
    /// </summary>
    public string Version => TagName.StartsWith('v') ? TagName[1..] : TagName;

    /// <summary>
    /// Find the WinUI 3 portable release asset
    /// </summary>
    public GitHubReleaseAsset? FindPortableAsset()
    {
        return Assets.FirstOrDefault(a =>
            a.Name.EndsWith(".zip") &&
            (a.Name.Contains("win") || a.Name.Contains("portable") || a.Name.Contains("WinUI")));
    }
}

public sealed class GitHubReleaseAsset
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; } = string.Empty;

    [JsonPropertyName("created_at")]
    public DateTime CreatedAt { get; set; }

    [JsonPropertyName("updated_at")]
    public DateTime UpdatedAt { get; set; }

    [JsonPropertyName("content_type")]
    public string ContentType { get; set; } = string.Empty;

    /// <summary>
    /// Format file size for display
    /// </summary>
    public string FormattedSize => FormatBytes(Size);

    private static string FormatBytes(long bytes)
    {
        string[] sizes = { "B", "KB", "MB", "GB" };
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}
