using PCL3.Platform;

namespace PCL3.Minecraft.Java;

public sealed record JavaRuntimeDescriptor(
    string HomePath,
    int MajorVersion,
    PlatformArchitecture Architecture,
    string? Vendor = null);

public sealed record JavaRequirement(
    int MinimumMajorVersion,
    int? MaximumMajorVersion = null,
    PlatformArchitecture? RequiredArchitecture = null);
