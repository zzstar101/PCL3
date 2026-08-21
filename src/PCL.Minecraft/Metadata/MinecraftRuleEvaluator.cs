using System.Text.RegularExpressions;
using PCL3.Platform;

namespace PCL3.Minecraft.Metadata;

public static class MinecraftRuleEvaluator
{
    public static bool IsAllowed(
        IReadOnlyList<MinecraftRule>? rules,
        MinecraftRuleContext context)
    {
        if (rules is null || rules.Count == 0)
        {
            return true;
        }

        var allowed = false;

        foreach (var rule in rules)
        {
            if (Matches(rule, context))
            {
                allowed = rule.Action is MinecraftRuleAction.Allow;
            }
        }

        return allowed;
    }

    private static bool Matches(MinecraftRule rule, MinecraftRuleContext context) =>
        MatchesOperatingSystem(rule.Os, context) &&
        MatchesFeatures(rule.Features, context.Features);

    private static bool MatchesOperatingSystem(
        MinecraftOsRule? rule,
        MinecraftRuleContext context)
    {
        if (rule is null)
        {
            return true;
        }

        if (rule.Name is not null &&
            !string.Equals(
                rule.Name,
                GetMojangOperatingSystemName(context.Platform.OperatingSystem),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (rule.ArchitecturePattern is not null &&
            !Regex.IsMatch(
                GetMojangArchitectureName(context.Platform.Architecture),
                rule.ArchitecturePattern,
                RegexOptions.CultureInvariant))
        {
            return false;
        }

        return rule.VersionPattern is null ||
               Regex.IsMatch(
                   context.OperatingSystemVersion,
                   rule.VersionPattern,
                   RegexOptions.CultureInvariant);
    }

    private static bool MatchesFeatures(
        IReadOnlyDictionary<string, bool>? requiredFeatures,
        IReadOnlyDictionary<string, bool> actualFeatures)
    {
        if (requiredFeatures is null || requiredFeatures.Count == 0)
        {
            return true;
        }

        foreach (var (name, expectedValue) in requiredFeatures)
        {
            if (!actualFeatures.TryGetValue(name, out var actualValue))
            {
                // Mojang feature flags are false unless explicitly enabled.
                if (expectedValue)
                {
                    return false;
                }

                continue;
            }

            if (actualValue != expectedValue)
            {
                return false;
            }
        }

        return true;
    }

    private static string GetMojangOperatingSystemName(PlatformOperatingSystem operatingSystem) =>
        operatingSystem switch
        {
            PlatformOperatingSystem.Windows => "windows",
            PlatformOperatingSystem.MacOS => "osx",
            PlatformOperatingSystem.Linux => "linux",
            _ => "unknown"
        };

    private static string GetMojangArchitectureName(PlatformArchitecture architecture) =>
        architecture switch
        {
            PlatformArchitecture.X86 => "x86",
            PlatformArchitecture.X64 => "x86_64",
            PlatformArchitecture.Arm => "arm",
            PlatformArchitecture.Arm64 => "arm64",
            _ => "unknown"
        };
}
