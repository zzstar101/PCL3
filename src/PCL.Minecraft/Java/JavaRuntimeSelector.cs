using PCL3.Minecraft.Compatibility;
using PCL3.Platform;

namespace PCL3.Minecraft.Java;

public sealed record JavaRuntimeSelection(
    JavaRuntimeDescriptor Runtime,
    int PreferenceScore);

public static class JavaRuntimeSelector
{
    public static JavaRuntimeSelection? SelectBest(
        IEnumerable<JavaRuntimeDescriptor> runtimes,
        JavaRequirement requirement,
        PlatformTarget target)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        ArgumentNullException.ThrowIfNull(requirement);

        var preferredMajor = requirement.PreferredMajorVersion ?? requirement.MinimumMajorVersion;

        return runtimes
            .Where(runtime => MinecraftCompatibilityAnalyzer
                .AnalyzeJava(runtime, requirement, target)
                .IsCompatible)
            .Select(runtime => new JavaRuntimeSelection(
                runtime,
                Score(runtime, preferredMajor, target)))
            .OrderBy(selection => selection.PreferenceScore)
            .ThenBy(selection => selection.Runtime.HomePath, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static int Score(
        JavaRuntimeDescriptor runtime,
        int preferredMajor,
        PlatformTarget target)
    {
        var versionDistance = Math.Abs(runtime.MajorVersion - preferredMajor);
        var architecturePenalty = runtime.Architecture == target.Architecture
            ? 0
            : runtime.Architecture == PlatformArchitecture.Unknown ||
              target.Architecture == PlatformArchitecture.Unknown
                ? 250
                : 500;
        var exactVersionPenalty = runtime.MajorVersion == preferredMajor ? 0 : 1000;

        return checked(exactVersionPenalty + architecturePenalty + versionDistance);
    }
}
