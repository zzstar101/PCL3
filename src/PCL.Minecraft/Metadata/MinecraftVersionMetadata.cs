namespace PCL3.Minecraft.Metadata;

public sealed record MinecraftJavaVersion(
    string Component,
    int MajorVersion);

public sealed record MinecraftArgument(
    IReadOnlyList<string> Values,
    IReadOnlyList<MinecraftRule> Rules);

public sealed record MinecraftDownloadArtifact(
    string? Path,
    string? Url,
    string? Sha1,
    long? Size);

public sealed record MinecraftVersionDownloads(
    MinecraftDownloadArtifact? Client);

public sealed record MinecraftAssetIndexReference(
    string Id,
    string? Url,
    string? Sha1,
    long? Size,
    long? TotalSize);

public sealed record MinecraftLibraryDownloads(
    MinecraftDownloadArtifact? Artifact,
    IReadOnlyDictionary<string, MinecraftDownloadArtifact> Classifiers);

public sealed record MinecraftLibraryExtract(
    IReadOnlyList<string> Exclude);

public sealed record MinecraftLibrary(
    string Name,
    string? RepositoryUrl,
    IReadOnlyList<MinecraftRule> Rules,
    IReadOnlyDictionary<string, string> Natives,
    MinecraftLibraryDownloads? Downloads = null,
    MinecraftLibraryExtract? Extract = null);

public sealed record MinecraftVersionMetadata(
    string Id,
    string? Type,
    string? MainClass,
    string? InheritsFrom,
    MinecraftJavaVersion? JavaVersion,
    IReadOnlyList<MinecraftArgument> JvmArguments,
    IReadOnlyList<MinecraftArgument> GameArguments,
    string? LegacyMinecraftArguments,
    IReadOnlyList<MinecraftLibrary> Libraries,
    string? Jar = null,
    string? Assets = null,
    MinecraftVersionDownloads? Downloads = null,
    MinecraftAssetIndexReference? AssetIndex = null);
