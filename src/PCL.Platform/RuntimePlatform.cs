using System.Runtime.InteropServices;

namespace PCL3.Platform;

public static class RuntimePlatform
{
    public static PlatformTarget Detect() =>
        new(DetectOperatingSystem(), DetectArchitecture(RuntimeInformation.ProcessArchitecture));

    public static PlatformOperatingSystem DetectOperatingSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return PlatformOperatingSystem.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return PlatformOperatingSystem.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return PlatformOperatingSystem.Linux;
        }

        return PlatformOperatingSystem.Unknown;
    }

    public static PlatformArchitecture DetectArchitecture(Architecture architecture) =>
        architecture switch
        {
            Architecture.X86 => PlatformArchitecture.X86,
            Architecture.X64 => PlatformArchitecture.X64,
            Architecture.Arm => PlatformArchitecture.Arm,
            Architecture.Arm64 => PlatformArchitecture.Arm64,
            _ => PlatformArchitecture.Unknown
        };
}
