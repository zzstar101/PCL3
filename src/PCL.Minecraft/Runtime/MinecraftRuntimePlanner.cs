using PCL3.Minecraft.Java;
using PCL3.Minecraft.Libraries;
using PCL3.Minecraft.Metadata;
using PCL3.Minecraft.Natives;

namespace PCL3.Minecraft.Runtime;

public static class MinecraftRuntimePlanner
{
    public static MinecraftRuntimePlan Build(
        MinecraftVersionChain versionChain,
        MinecraftRuleContext ruleContext,
        string minecraftDirectory,
        string nativeDirectory,
        IEnumerable<JavaRuntimeDescriptor> javaRuntimes,
        string? clientJarPath = null)
    {
        ArgumentNullException.ThrowIfNull(versionChain);
        ArgumentNullException.ThrowIfNull(ruleContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(nativeDirectory);
        ArgumentNullException.ThrowIfNull(javaRuntimes);

        var minecraftRoot = Path.GetFullPath(minecraftDirectory);
        var librariesDirectory = Path.Combine(minecraftRoot, "libraries");
        var libraries = MinecraftLibraryResolver.Resolve(
            versionChain,
            ruleContext,
            librariesDirectory);
        var classpath = MinecraftClasspathBuilder.Build(
            libraries.ClasspathArtifacts,
            clientJarPath);
        var nativeExtraction = MinecraftNativeExtractionPlanner.Create(
            libraries.NativeArtifacts,
            nativeDirectory);
        var javaRequirement = MinecraftJavaRequirementResolver.Resolve(versionChain);
        var javaSelection = JavaRuntimeSelector.SelectBest(
            javaRuntimes,
            javaRequirement,
            ruleContext.Platform);

        return new MinecraftRuntimePlan(
            minecraftRoot,
            librariesDirectory,
            libraries,
            classpath,
            nativeExtraction,
            javaRequirement,
            javaSelection);
    }
}
