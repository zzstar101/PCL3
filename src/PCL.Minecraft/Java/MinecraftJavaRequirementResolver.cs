using PCL3.Minecraft.Metadata;

namespace PCL3.Minecraft.Java;

public static class MinecraftJavaRequirementResolver
{
    private const int LegacyDefaultMajorVersion = 8;

    public static JavaRequirement Resolve(MinecraftVersionChain versionChain)
    {
        ArgumentNullException.ThrowIfNull(versionChain);

        var preferredMajor = versionChain.EffectiveJavaVersion?.MajorVersion ??
            LegacyDefaultMajorVersion;

        return new JavaRequirement(
            preferredMajor,
            PreferredMajorVersion: preferredMajor);
    }
}
