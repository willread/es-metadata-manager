using System.Net.Http;
using GamelistScraper.Models;

namespace GamelistScraper.Services;

public class MediaDownloadService
{
    private readonly HttpClient _httpClient;
    private readonly FrontendConfigService _frontend;

    public MediaDownloadService(HttpClient httpClient, FrontendConfigService frontend)
    {
        _httpClient = httpClient;
        _frontend = frontend;
    }

    public async Task<int> DownloadMedia(GameEntry game, string systemName, ScraperConfig config,
        Action<string> log, CancellationToken ct)
    {
        var enabledTypes = config.GetEnabledMediaTypes(systemName);
        var downloaded = 0;

        foreach (var mediaType in enabledTypes)
        {
            ct.ThrowIfCancellationRequested();

            var ssField = MediaTypeInfo.ScreenScraperMediaField(mediaType);
            if (!game.MediaUrls.TryGetValue(ssField, out var url) || string.IsNullOrEmpty(url))
                continue;

            try
            {
                var ext = GetExtensionFromUrl(url, mediaType);
                var basePath = _frontend.GetMediaPath(systemName, mediaType, game.FileBaseName);
                var fullPath = basePath + ext;

                if (File.Exists(fullPath) && !config.ForceRedownloadMedia)
                {
                    log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Already exists, skipping");
                    continue;
                }

                var dir = Path.GetDirectoryName(fullPath)!;
                Directory.CreateDirectory(dir);

                using var response = await _httpClient.GetAsync(url, ct);
                if (!response.IsSuccessStatusCode)
                {
                    log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Download failed: HTTP {(int)response.StatusCode}");
                    continue;
                }

                await using var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write);
                await response.Content.CopyToAsync(fileStream, ct);
                downloaded++;
                log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Downloaded");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Error: {ex.Message}");
            }
        }

        return downloaded;
    }

    /// <summary>
    /// Delete media files for disabled media types in a system.
    /// Returns the number of files deleted.
    /// </summary>
    public int CleanupDisabledMedia(string systemName, ScraperConfig config, Action<string> log)
    {
        var deleted = 0;

        foreach (var mediaType in Enum.GetValues<MediaType>())
        {
            if (config.IsMediaTypeEnabled(mediaType, systemName))
                continue;

            var folder = MediaTypeInfo.EsDeFolder(mediaType);
            var mediaDir = Path.Combine(_frontend.MediaDirectory, systemName, folder);

            if (!Directory.Exists(mediaDir))
                continue;

            try
            {
                var files = Directory.GetFiles(mediaDir);
                if (files.Length == 0)
                    continue;

                foreach (var file in files)
                {
                    try
                    {
                        File.Delete(file);
                        deleted++;
                    }
                    catch (Exception ex)
                    {
                        log($"  Failed to delete {Path.GetFileName(file)}: {ex.Message}");
                    }
                }

                log($"  [{MediaTypeInfo.DisplayName(mediaType)}] Cleaned up {files.Length} files from {systemName}");
            }
            catch (Exception ex)
            {
                log($"  Error cleaning {folder} for {systemName}: {ex.Message}");
            }
        }

        return deleted;
    }

    /// <summary>
    /// Get media file status for a system: how many files exist per media type.
    /// </summary>
    public Dictionary<MediaType, int> GetMediaStatus(string systemName)
    {
        var result = new Dictionary<MediaType, int>();
        foreach (var mediaType in Enum.GetValues<MediaType>())
        {
            var folder = MediaTypeInfo.EsDeFolder(mediaType);
            var mediaDir = Path.Combine(_frontend.MediaDirectory, systemName, folder);
            result[mediaType] = Directory.Exists(mediaDir)
                ? Directory.GetFiles(mediaDir).Length
                : 0;
        }
        return result;
    }

    private static string GetExtensionFromUrl(string url, MediaType mediaType)
    {
        // Try to get extension from URL
        var uri = new Uri(url);
        var ext = Path.GetExtension(uri.AbsolutePath).ToLowerInvariant();

        if (!string.IsNullOrEmpty(ext))
            return ext;

        // Fallback based on media type
        return mediaType switch
        {
            MediaType.Video => ".mp4",
            MediaType.Manual => ".pdf",
            _ => ".png"
        };
    }
}
