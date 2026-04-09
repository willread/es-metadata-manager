using Microsoft.Data.Sqlite;

namespace GamelistScraper.Services;

public class CacheDatabase : IDisposable
{
    private readonly SqliteConnection _conn;

    public CacheDatabase()
    {
        var dbDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "GamelistScraper");
        Directory.CreateDirectory(dbDir);
        var dbPath = Path.Combine(dbDir, "cache.db");

        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        InitTables();
    }

    private void InitTables()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS not_found (
                rom_hash TEXT NOT NULL,
                system_id INTEGER NOT NULL,
                rom_name TEXT NOT NULL,
                last_checked TEXT NOT NULL,
                PRIMARY KEY (rom_hash, system_id)
            );

            CREATE TABLE IF NOT EXISTS error_cache (
                rom_hash TEXT NOT NULL,
                system_id INTEGER NOT NULL,
                rom_name TEXT NOT NULL,
                error_message TEXT NOT NULL,
                last_checked TEXT NOT NULL,
                PRIMARY KEY (rom_hash, system_id)
            );

            CREATE TABLE IF NOT EXISTS hash_cache (
                file_path TEXT PRIMARY KEY,
                file_modified TEXT NOT NULL,
                file_size INTEGER NOT NULL,
                md5 TEXT NOT NULL,
                sha1 TEXT NOT NULL,
                crc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS scrape_history (
                file_path TEXT PRIMARY KEY,
                system_name TEXT NOT NULL,
                screenscraper_id INTEGER NOT NULL,
                scraped_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS media_status (
                file_path TEXT NOT NULL,
                media_type TEXT NOT NULL,
                status TEXT NOT NULL,
                url TEXT,
                error_message TEXT,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (file_path, media_type)
            );
            """;
        cmd.ExecuteNonQuery();
    }

    // --- Not-found cache (game confirmed to not exist on ScreenScraper) ---
    public bool IsNotFound(string hash, int systemId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM not_found WHERE rom_hash = @hash AND system_id = @sid";
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@sid", systemId);
        return cmd.ExecuteScalar() != null;
    }

    public void AddNotFound(string hash, int systemId, string romName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO not_found (rom_hash, system_id, rom_name, last_checked)
            VALUES (@hash, @sid, @name, @time)
            """;
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@sid", systemId);
        cmd.Parameters.AddWithValue("@name", romName);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
        // Also remove from error cache if it was there
        RemoveError(hash, systemId);
        cmd.ExecuteNonQuery();
    }

    public void ClearNotFound(int? systemId = null)
    {
        using var cmd = _conn.CreateCommand();
        if (systemId.HasValue)
        {
            cmd.CommandText = "DELETE FROM not_found WHERE system_id = @sid";
            cmd.Parameters.AddWithValue("@sid", systemId.Value);
        }
        else
        {
            cmd.CommandText = "DELETE FROM not_found";
        }
        cmd.ExecuteNonQuery();
    }

    public int GetNotFoundCount(int? systemId = null)
    {
        using var cmd = _conn.CreateCommand();
        if (systemId.HasValue)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM not_found WHERE system_id = @sid";
            cmd.Parameters.AddWithValue("@sid", systemId.Value);
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM not_found";
        }
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // --- Error cache (API/network errors — can be retried) ---
    public bool IsErrorCached(string hash, int systemId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM error_cache WHERE rom_hash = @hash AND system_id = @sid";
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@sid", systemId);
        return cmd.ExecuteScalar() != null;
    }

    public void AddError(string hash, int systemId, string romName, string errorMessage)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO error_cache (rom_hash, system_id, rom_name, error_message, last_checked)
            VALUES (@hash, @sid, @name, @err, @time)
            """;
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@sid", systemId);
        cmd.Parameters.AddWithValue("@name", romName);
        cmd.Parameters.AddWithValue("@err", errorMessage);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    public void RemoveError(string hash, int systemId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM error_cache WHERE rom_hash = @hash AND system_id = @sid";
        cmd.Parameters.AddWithValue("@hash", hash);
        cmd.Parameters.AddWithValue("@sid", systemId);
        cmd.ExecuteNonQuery();
    }

    public void ClearErrors(int? systemId = null)
    {
        using var cmd = _conn.CreateCommand();
        if (systemId.HasValue)
        {
            cmd.CommandText = "DELETE FROM error_cache WHERE system_id = @sid";
            cmd.Parameters.AddWithValue("@sid", systemId.Value);
        }
        else
        {
            cmd.CommandText = "DELETE FROM error_cache";
        }
        cmd.ExecuteNonQuery();
    }

    public int GetErrorCacheCount(int? systemId = null)
    {
        using var cmd = _conn.CreateCommand();
        if (systemId.HasValue)
        {
            cmd.CommandText = "SELECT COUNT(*) FROM error_cache WHERE system_id = @sid";
            cmd.Parameters.AddWithValue("@sid", systemId.Value);
        }
        else
        {
            cmd.CommandText = "SELECT COUNT(*) FROM error_cache";
        }
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    // --- Hash cache ---
    public (string md5, string sha1, string crc)? GetCachedHash(string filePath, DateTime fileModified, long fileSize)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            SELECT md5, sha1, crc FROM hash_cache
            WHERE file_path = @path AND file_modified = @mod AND file_size = @size
            """;
        cmd.Parameters.AddWithValue("@path", filePath);
        cmd.Parameters.AddWithValue("@mod", fileModified.ToString("o"));
        cmd.Parameters.AddWithValue("@size", fileSize);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
            return (reader.GetString(0), reader.GetString(1), reader.GetString(2));
        return null;
    }

    public void SetCachedHash(string filePath, DateTime fileModified, long fileSize, string md5, string sha1, string crc)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO hash_cache (file_path, file_modified, file_size, md5, sha1, crc)
            VALUES (@path, @mod, @size, @md5, @sha1, @crc)
            """;
        cmd.Parameters.AddWithValue("@path", filePath);
        cmd.Parameters.AddWithValue("@mod", fileModified.ToString("o"));
        cmd.Parameters.AddWithValue("@size", fileSize);
        cmd.Parameters.AddWithValue("@md5", md5);
        cmd.Parameters.AddWithValue("@sha1", sha1);
        cmd.Parameters.AddWithValue("@crc", crc);
        cmd.ExecuteNonQuery();
    }

    // --- Scrape history ---
    public bool IsScraped(string filePath)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM scrape_history WHERE file_path = @path";
        cmd.Parameters.AddWithValue("@path", filePath);
        return cmd.ExecuteScalar() != null;
    }

    public int GetScrapedCount(string systemName)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM scrape_history WHERE system_name = @sys";
        cmd.Parameters.AddWithValue("@sys", systemName);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void AddScrapeHistory(string filePath, string systemName, int screenScraperId)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO scrape_history (file_path, system_name, screenscraper_id, scraped_at)
            VALUES (@path, @sys, @ssid, @time)
            """;
        cmd.Parameters.AddWithValue("@path", filePath);
        cmd.Parameters.AddWithValue("@sys", systemName);
        cmd.Parameters.AddWithValue("@ssid", screenScraperId);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    // --- Per-game per-media-type status ---
    public void SetMediaStatus(string filePath, string mediaType, string status, string? url = null, string? error = null)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = """
            INSERT OR REPLACE INTO media_status (file_path, media_type, status, url, error_message, updated_at)
            VALUES (@path, @type, @status, @url, @err, @time)
            """;
        cmd.Parameters.AddWithValue("@path", filePath);
        cmd.Parameters.AddWithValue("@type", mediaType);
        cmd.Parameters.AddWithValue("@status", status);
        cmd.Parameters.AddWithValue("@url", (object?)url ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@err", (object?)error ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@time", DateTime.UtcNow.ToString("o"));
        cmd.ExecuteNonQuery();
    }

    /// <summary>Returns status and cached URL for a media type, or null if no record.</summary>
    public (string status, string? url)? GetMediaStatus(string filePath, string mediaType)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT status, url FROM media_status WHERE file_path = @path AND media_type = @type";
        cmd.Parameters.AddWithValue("@path", filePath);
        cmd.Parameters.AddWithValue("@type", mediaType);
        using var reader = cmd.ExecuteReader();
        if (reader.Read())
            return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
        return null;
    }

    /// <summary>Get all media types with errors for a given file.</summary>
    public List<(string mediaType, string url)> GetMediaErrors(string filePath)
    {
        var results = new List<(string, string)>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT media_type, url FROM media_status WHERE file_path = @path AND status = 'error' AND url IS NOT NULL";
        cmd.Parameters.AddWithValue("@path", filePath);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            results.Add((reader.GetString(0), reader.GetString(1)));
        return results;
    }

    public void ClearMediaErrors()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM media_status WHERE status = 'error'";
        cmd.ExecuteNonQuery();
    }

    public int GetMediaErrorCount()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM media_status WHERE status = 'error'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void ClearMediaNotAvailable()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "DELETE FROM media_status WHERE status = 'not_available'";
        cmd.ExecuteNonQuery();
    }

    public int GetMediaNotAvailableCount()
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM media_status WHERE status = 'not_available'";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public void Dispose()
    {
        _conn.Dispose();
    }
}
