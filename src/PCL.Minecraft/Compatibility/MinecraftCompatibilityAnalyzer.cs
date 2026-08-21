using PCL3.Core.Compatibility;
using PCL3.Minecraft.Java;
using PCL3.Platform;

namespace PCL3.Minecraft.Compatibility;

public static class MinecraftCompatibilityAnalyzer
{
    public static CompatibilityReport AnalyzeJava(
        JavaRuntimeDescriptor runtime,
        JavaRequirement requirement,
        PlatformTarget target)
    {
        var issues = new List<CompatibilityIssue>();

        if (runtime.MajorVersion < requirement.MinimumMajorVersion)
        {
            issues.Add(new CompatibilityIssue(
                "java.version.too-old",
                CompatibilitySeverity.Error,
                $"Java {runtime.MajorVersion} is older than required Java {requirement.MinimumMajorVersion}."));
        }

        if (requirement.MaximumMajorVersion is { } maximum &&
            runtime.MajorVersion > maximum)
        {
            issues.Add(new CompatibilityIssue(
                "java.version.too-new",
                CompatibilitySeverity.Error,
                $"Java {runtime.MajorVersion} is newer than supported Java {maximum}."));
        }

        if (requirement.RequiredArchitecture is { } requiredArchitecture &&
            runtime.Architecture != requiredArchitecture)
        {
            issues.Add(new CompatibilityIssue(
                "java.architecture.unsupported",
                CompatibilitySeverity.Error,
                $"Java architecture {runtime.Architecture} does not match required architecture {requiredArchitecture}."));
        }
        else if (runtime.Architecture is not PlatformArchitecture.Unknown &&
                 target.Architecture is not PlatformArchitecture.Unknown &&
                 runtime.Architecture != target.Architecture)
        {
            issues.Add(new CompatibilityIssue(
                "java.architecture.emulation",
                CompatibilitySeverity.Warning,
                $"Java architecture {runtime.Architecture} differs from the current platform architecture {target.Architecture}; platform emulation may be required."));
        }

        return new CompatibilityReport(issues);
    }
}
