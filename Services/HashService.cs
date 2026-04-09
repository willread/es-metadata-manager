using System.Security.Cryptography;

namespace GamelistScraper.Services;

public class HashService
{
    private readonly CacheDatabase _cache;

    public HashService(CacheDatabase cache)
    {
        _cache = cache;
    }

    public record FileHashes(string Md5, string Sha1, string Crc, long FileSize);

    public async Task<FileHashes> GetHashes(string filePath, CancellationToken ct = default)
    {
        var fileInfo = new FileInfo(filePath);
        if (!fileInfo.Exists)
            throw new FileNotFoundException("ROM file not found", filePath);

        // Check cache first
        var cached = _cache.GetCachedHash(filePath, fileInfo.LastWriteTimeUtc, fileInfo.Length);
        if (cached.HasValue)
            return new FileHashes(cached.Value.md5, cached.Value.sha1, cached.Value.crc, fileInfo.Length);

        // Calculate all three hashes in a single pass
        using var md5 = MD5.Create();
        using var sha1 = SHA1.Create();
        uint crcValue = 0xFFFFFFFF;

        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read,
            FileShare.Read, bufferSize: 81920, useAsync: true);

        var buffer = new byte[81920];
        int bytesRead;
        while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
        {
            md5.TransformBlock(buffer, 0, bytesRead, null, 0);
            sha1.TransformBlock(buffer, 0, bytesRead, null, 0);
            for (int i = 0; i < bytesRead; i++)
            {
                crcValue = Crc32Table[((crcValue) ^ buffer[i]) & 0xFF] ^ (crcValue >> 8);
            }
        }

        md5.TransformFinalBlock([], 0, 0);
        sha1.TransformFinalBlock([], 0, 0);
        crcValue ^= 0xFFFFFFFF;

        var result = new FileHashes(
            Md5: Convert.ToHexString(md5.Hash!).ToLowerInvariant(),
            Sha1: Convert.ToHexString(sha1.Hash!).ToLowerInvariant(),
            Crc: crcValue.ToString("x8"),
            FileSize: fileInfo.Length
        );

        _cache.SetCachedHash(filePath, fileInfo.LastWriteTimeUtc, fileInfo.Length,
            result.Md5, result.Sha1, result.Crc);

        return result;
    }

    private static readonly uint[] Crc32Table = GenerateCrc32Table();

    private static uint[] GenerateCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            table[i] = crc;
        }
        return table;
    }
}
