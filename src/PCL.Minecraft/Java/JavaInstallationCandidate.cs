using PCL3.Platform;

namespace PCL3.Minecraft.Java;

public enum JavaDiscoverySource
{
    Environment,
    Path,
    MinecraftRuntime,
    UserInstallation,
    SystemInstallation
}

public sealed record JavaInstallationCandidate(
    string HomePath,
    string ExecutablePath,
    JavaDiscoverySource Source);

public sealed record JavaDiscoveryOptions(
    PlatformTarget Platform,
    string UserHome,
    string? MinecraftDirectory,
    string? ProgramFilesDirectory,
    string? ProgramFilesX86Directory,
    IReadOnlyDictionary<string, string?> EnvironmentVariables)
{
    public static JavaDiscoveryOptions ForCurrent(string? minecraftDirectory = null)
    {
        var platform = PlatformTarget.Current;
        var userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var resolvedMinecraftDirectory = minecraftDirectory ?? platform.OperatingSystem switch
        {
            PlatformOperatingSystem.Windows => Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                ".minecraft"),
            PlatformOperatingSystem.MacOS => Path.Combine(
                userHome,
                "Library",
                "Application Support",
                "minecraft"),
            _ => Path.Combine(userHome, ".minecraft")
        };

        return new JavaDiscoveryOptions(
            platform,
            userHome,
            resolvedMinecraftDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["JAVA_HOME"] = Environment.GetEnvironmentVariable("JAVA_HOME"),
                ["JDK_HOME"] = Environment.GetEnvironmentVariable("JDK_HOME"),
                ["PATH"] = Environment.GetEnvironmentVariable("PATH")
            });
    }
}
