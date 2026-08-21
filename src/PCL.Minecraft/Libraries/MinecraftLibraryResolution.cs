using PCL3.Minecraft.Metadata;

namespace PCL3.Minecraft.Libraries;

public sealed record ResolvedMinecraftArtifact(
    MavenCoordinate Coordinate,
    string LocalPath,
    string? Url,
    string? Sha1,
    long? Size,
    string SourceVersionId);

public sealed record ResolvedMinecraftNativeArtifact(
    MavenCoordinate Coordinate,
    string LocalPath,
    string? Url,
    string? Sha1,
    long? Size,
    IReadOnlyList<string> Excludes,
    string SourceVersionId);

public sealed record MinecraftLibraryResolution(
    IReadOnlyList<ResolvedMinecraftArtifact> ClasspathArtifacts,
    IReadOnlyList<ResolvedMinecraftNativeArtifact> NativeArtifacts);
