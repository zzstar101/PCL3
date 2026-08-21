namespace PCL3.Minecraft.Libraries;

public static class MinecraftClasspathBuilder
{
    public static string Build(
        IEnumerable<ResolvedMinecraftArtifact> artifacts,
        string? clientJarPath = null)
    {
        ArgumentNullException.ThrowIfNull(artifacts);

        var entries = artifacts
            .Select(artifact => artifact.LocalPath)
            .ToList();

        if (!string.IsNullOrWhiteSpace(clientJarPath))
        {
            entries.Add(Path.GetFullPath(clientJarPath));
        }

        return string.Join(Path.PathSeparator, entries);
    }
}
