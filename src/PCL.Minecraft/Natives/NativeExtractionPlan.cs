using PCL3.Minecraft.Libraries;

namespace PCL3.Minecraft.Natives;

public sealed record NativeExtractionArchive(
    string ArchivePath,
    IReadOnlyList<string> Excludes,
    string SourceVersionId);

public sealed record NativeExtractionPlan(
    string DestinationDirectory,
    IReadOnlyList<NativeExtractionArchive> Archives);

public static class MinecraftNativeExtractionPlanner
{
    public static NativeExtractionPlan Create(
        IEnumerable<ResolvedMinecraftNativeArtifact> nativeArtifacts,
        string destinationDirectory)
    {
        ArgumentNullException.ThrowIfNull(nativeArtifacts);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        return new NativeExtractionPlan(
            Path.GetFullPath(destinationDirectory),
            nativeArtifacts
                .Select(artifact => new NativeExtractionArchive(
                    artifact.LocalPath,
                    artifact.Excludes,
                    artifact.SourceVersionId))
                .ToArray());
    }
}
