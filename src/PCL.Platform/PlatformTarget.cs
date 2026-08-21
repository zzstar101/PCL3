namespace PCL3.Platform;

public enum PlatformOperatingSystem
{
    Unknown,
    Windows,
    MacOS,
    Linux
}

public enum PlatformArchitecture
{
    Unknown,
    X86,
    X64,
    Arm,
    Arm64
}

public readonly record struct PlatformTarget(
    PlatformOperatingSystem OperatingSystem,
    PlatformArchitecture Architecture)
{
    public static PlatformTarget Current => RuntimePlatform.Detect();

    public string RuntimeIdentifierPrefix => OperatingSystem switch
    {
        PlatformOperatingSystem.Windows => "win",
        PlatformOperatingSystem.MacOS => "osx",
        PlatformOperatingSystem.Linux => "linux",
        _ => "unknown"
    };

    public override string ToString() =>
        $"{RuntimeIdentifierPrefix}-{Architecture.ToString().ToLowerInvariant()}";
}
