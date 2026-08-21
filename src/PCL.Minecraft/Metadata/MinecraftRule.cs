using PCL3.Platform;

namespace PCL3.Minecraft.Metadata;

public enum MinecraftRuleAction
{
    Allow,
    Disallow
}

public sealed record MinecraftOsRule(
    string? Name = null,
    string? ArchitecturePattern = null,
    string? VersionPattern = null);

public sealed record MinecraftRule(
    MinecraftRuleAction Action,
    MinecraftOsRule? Os = null,
    IReadOnlyDictionary<string, bool>? Features = null);

public sealed record MinecraftRuleContext(
    PlatformTarget Platform,
    string OperatingSystemVersion,
    IReadOnlyDictionary<string, bool> Features);
