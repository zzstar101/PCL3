namespace PCL3.Minecraft.Metadata;

public sealed record MinecraftJavaVersion(
    string Component,
    int MajorVersion);

public sealed record MinecraftArgument(
    IReadOnlyList<string> Values,
    IReadOnlyList<MinecraftRule> Rules);

public sealed record MinecraftLibrary(
    string Name,
    string? RepositoryUrl,
    IReadOnlyList<MinecraftRule> Rules,
    IReadOnlyDictionary<string, string> Natives);

public sealed record MinecraftVersionMetadata(
    string Id,
    string? Type,
    string? MainClass,
    string? InheritsFrom,
    MinecraftJavaVersion? JavaVersion,
    IReadOnlyList<MinecraftArgument> JvmArguments,
    IReadOnlyList<MinecraftArgument> GameArguments,
    string? LegacyMinecraftArguments,
    IReadOnlyList<MinecraftLibrary> Libraries);
