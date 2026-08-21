namespace PCL3.Minecraft.Metadata;

public sealed class MinecraftVersionChain
{
    public MinecraftVersionChain(IReadOnlyList<MinecraftVersionMetadata> versions)
    {
        ArgumentNullException.ThrowIfNull(versions);

        if (versions.Count == 0)
        {
            throw new ArgumentException("A Minecraft version chain cannot be empty.", nameof(versions));
        }

        Versions = versions;
    }

    /// <summary>
    /// Versions ordered from the selected/child version to the root/vanilla version.
    /// This ordering intentionally matches PCL2 launch argument inheritance semantics.
    /// </summary>
    public IReadOnlyList<MinecraftVersionMetadata> Versions { get; }

    public MinecraftVersionMetadata Selected => Versions[0];

    public MinecraftVersionMetadata Root => Versions[^1];

    public string? EffectiveMainClass =>
        Versions.Select(version => version.MainClass).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    public MinecraftJavaVersion? EffectiveJavaVersion =>
        Versions.Select(version => version.JavaVersion).FirstOrDefault(value => value is not null);

    public string? EffectiveLegacyMinecraftArguments =>
        Versions.Select(version => version.LegacyMinecraftArguments)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    public IEnumerable<MinecraftArgument> EnumerateJvmArgumentsChildFirst() =>
        Versions.SelectMany(version => version.JvmArguments);

    public IEnumerable<MinecraftArgument> EnumerateGameArgumentsChildFirst() =>
        Versions.SelectMany(version => version.GameArguments);

    public IEnumerable<MinecraftLibrary> EnumerateLibrariesChildFirst() =>
        Versions.SelectMany(version => version.Libraries);
}
