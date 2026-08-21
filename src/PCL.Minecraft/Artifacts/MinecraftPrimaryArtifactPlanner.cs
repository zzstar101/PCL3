using PCL3.Minecraft.Metadata;
using PCL3.Minecraft.Runtime;

namespace PCL3.Minecraft.Artifacts;

public static class MinecraftPrimaryArtifactPlanner
{
    public static MinecraftArtifactAcquisitionPlan Build(
        MinecraftVersionChain versionChain,
        MinecraftRuntimePlan runtimePlan)
    {
        ArgumentNullException.ThrowIfNull(versionChain);
        ArgumentNullException.ThrowIfNull(runtimePlan);

        var artifacts = new List<MinecraftArtifactRequest>();

        foreach (var library in runtimePlan.Libraries.ClasspathArtifacts)
        {
            artifacts.Add(Create(
                $"library:{library.Coordinate}",
                MinecraftArtifactPurpose.Library,
                library.LocalPath,
                library.Url,
                library.Sha1,
                library.Size));
        }

        foreach (var native in runtimePlan.Libraries.NativeArtifacts)
        {
            artifacts.Add(Create(
                $"native:{native.Coordinate}",
                MinecraftArtifactPurpose.NativeLibrary,
                native.LocalPath,
                native.Url,
                native.Sha1,
                native.Size));
        }

        var client = versionChain.EffectiveClientDownload;
        if (client is not null)
        {
            var clientVersionId = versionChain.EffectiveClientJarVersionId;
            artifacts.Add(Create(
                $"client:{clientVersionId}",
                MinecraftArtifactPurpose.Client,
                Path.Combine(
                    runtimePlan.MinecraftDirectory,
                    "versions",
                    clientVersionId,
                    $"{clientVersionId}.jar"),
                client.Url,
                client.Sha1,
                client.Size));
        }

        var assetIndex = versionChain.EffectiveAssetIndex;
        if (assetIndex is not null)
        {
            artifacts.Add(Create(
                $"asset-index:{assetIndex.Id}",
                MinecraftArtifactPurpose.AssetIndex,
                Path.Combine(
                    runtimePlan.MinecraftDirectory,
                    "assets",
                    "indexes",
                    $"{assetIndex.Id}.json"),
                assetIndex.Url,
                assetIndex.Sha1,
                assetIndex.Size));
        }

        return new MinecraftArtifactAcquisitionPlan(Deduplicate(artifacts));
    }

    private static MinecraftArtifactRequest Create(
        string id,
        MinecraftArtifactPurpose purpose,
        string localPath,
        string? source,
        string? sha1,
        long? size) =>
        new MinecraftArtifactRequest(
            id,
            purpose,
            localPath,
            string.IsNullOrWhiteSpace(source) ? Array.Empty<string>() : new[] { source },
            sha1,
            size).Normalize();

    private static IReadOnlyList<MinecraftArtifactRequest> Deduplicate(
        IEnumerable<MinecraftArtifactRequest> artifacts)
    {
        var pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var result = new Dictionary<string, MinecraftArtifactRequest>(pathComparer);

        foreach (var artifact in artifacts)
        {
            if (!result.TryGetValue(artifact.LocalPath, out var existing))
            {
                result.Add(artifact.LocalPath, artifact);
                continue;
            }

            if (!string.Equals(existing.Sha1, artifact.Sha1, StringComparison.OrdinalIgnoreCase) ||
                existing.Size != artifact.Size)
            {
                throw new InvalidDataException(
                    $"Conflicting acquisition metadata targets '{artifact.LocalPath}'.");
            }

            result[artifact.LocalPath] = existing with
            {
                Sources = existing.Sources.Concat(artifact.Sources)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray()
            };
        }

        return result.Values.ToArray();
    }
}
