using PCL3.Minecraft.Java;
using PCL3.Minecraft.Libraries;
using PCL3.Minecraft.Natives;

namespace PCL3.Minecraft.Runtime;

public sealed record MinecraftRuntimePlan(
    string MinecraftDirectory,
    string LibrariesDirectory,
    MinecraftLibraryResolution Libraries,
    string Classpath,
    NativeExtractionPlan NativeExtraction,
    JavaRequirement JavaRequirement,
    JavaRuntimeSelection? JavaSelection)
{
    public IReadOnlyDictionary<string, string> CreateLaunchVariables()
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["classpath"] = Classpath,
            ["classpath_separator"] = Path.PathSeparator.ToString(),
            ["library_directory"] = LibrariesDirectory,
            ["natives_directory"] = NativeExtraction.DestinationDirectory
        };
    }

    public JavaRuntimeDescriptor RequireJavaRuntime() =>
        JavaSelection?.Runtime ?? throw new InvalidOperationException(
            $"No compatible Java runtime is available for required Java {JavaRequirement.MinimumMajorVersion}+.");
}
