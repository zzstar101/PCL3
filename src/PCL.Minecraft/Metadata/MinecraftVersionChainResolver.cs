namespace PCL3.Minecraft.Metadata;

public static class MinecraftVersionChainResolver
{
    private const int MaximumInheritanceDepth = 64;

    public static async ValueTask<MinecraftVersionChain> ResolveAsync(
        string selectedVersionId,
        IMinecraftVersionMetadataSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedVersionId);
        ArgumentNullException.ThrowIfNull(source);

        var versions = new List<MinecraftVersionMetadata>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var currentId = selectedVersionId;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visited.Add(currentId))
            {
                throw new InvalidDataException(
                    $"Minecraft version inheritance contains a cycle at '{currentId}'.");
            }

            if (versions.Count >= MaximumInheritanceDepth)
            {
                throw new InvalidDataException(
                    $"Minecraft version inheritance exceeded {MaximumInheritanceDepth} levels.");
            }

            var metadata = await source.GetAsync(currentId, cancellationToken).ConfigureAwait(false)
                ?? throw new FileNotFoundException(
                    $"Minecraft version metadata '{currentId}' could not be found.",
                    currentId);

            if (!string.Equals(metadata.Id, currentId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"Minecraft version metadata id '{metadata.Id}' does not match requested id '{currentId}'.");
            }

            versions.Add(metadata);

            var parentId = metadata.InheritsFrom;
            if (string.IsNullOrWhiteSpace(parentId))
            {
                break;
            }

            currentId = parentId;
        }

        return new MinecraftVersionChain(versions);
    }
}
