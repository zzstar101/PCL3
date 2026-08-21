using System.Collections.Concurrent;

namespace PCL3.Minecraft.Metadata;

public sealed class FileSystemMinecraftVersionMetadataSource : IMinecraftVersionMetadataSource
{
    private readonly string _versionsDirectory;
    private readonly ConcurrentDictionary<string, CacheEntry> _cache =
        new(StringComparer.Ordinal);

    public FileSystemMinecraftVersionMetadataSource(string minecraftDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftDirectory);
        _versionsDirectory = Path.Combine(Path.GetFullPath(minecraftDirectory), "versions");
    }

    public async ValueTask<MinecraftVersionMetadata?> GetAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        ValidateVersionId(id);
        cancellationToken.ThrowIfCancellationRequested();

        var metadataPath = GetMetadataPath(id);
        var info = new FileInfo(metadataPath);

        if (!info.Exists)
        {
            _cache.TryRemove(id, out _);
            return null;
        }

        if (_cache.TryGetValue(id, out var cached) &&
            cached.Length == info.Length &&
            cached.LastWriteTimeUtc == info.LastWriteTimeUtc)
        {
            return cached.Metadata;
        }

        var json = await File.ReadAllTextAsync(metadataPath, cancellationToken).ConfigureAwait(false);
        var metadata = MinecraftVersionJson.Parse(json);

        info.Refresh();
        if (!info.Exists)
        {
            throw new FileNotFoundException(
                $"Minecraft version metadata '{id}' was removed while it was being read.",
                metadataPath);
        }

        _cache[id] = new CacheEntry(
            info.Length,
            info.LastWriteTimeUtc,
            metadata);

        return metadata;
    }

    public string GetMetadataPath(string id)
    {
        ValidateVersionId(id);
        return Path.Combine(_versionsDirectory, id, $"{id}.json");
    }

    public void Invalidate(string id)
    {
        ValidateVersionId(id);
        _cache.TryRemove(id, out _);
    }

    public void ClearCache() => _cache.Clear();

    private static void ValidateVersionId(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        if (id is "." or ".." ||
            Path.IsPathRooted(id) ||
            id.Contains('/') ||
            id.Contains('\\'))
        {
            throw new ArgumentException(
                $"Minecraft version id '{id}' is not a safe single-directory name.",
                nameof(id));
        }
    }

    private sealed record CacheEntry(
        long Length,
        DateTime LastWriteTimeUtc,
        MinecraftVersionMetadata Metadata);
}
