using PCL3.Platform;

namespace PCL3.Minecraft.Java;

public static class JavaInstallationLocator
{
    public static IReadOnlyList<JavaInstallationCandidate> Discover(JavaDiscoveryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.UserHome);

        var pathComparer = options.Platform.OperatingSystem is PlatformOperatingSystem.Windows
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        var candidates = new Dictionary<string, JavaInstallationCandidate>(pathComparer);
        var executableName = options.Platform.OperatingSystem is PlatformOperatingSystem.Windows
            ? "java.exe"
            : "java";

        void AddExecutable(string executablePath, JavaDiscoverySource source)
        {
            try
            {
                var fullPath = Path.GetFullPath(executablePath);
                if (!File.Exists(fullPath))
                {
                    return;
                }

                var binDirectory = Path.GetDirectoryName(fullPath);
                var homePath = binDirectory is null
                    ? null
                    : Path.GetDirectoryName(binDirectory);
                if (string.IsNullOrWhiteSpace(homePath))
                {
                    return;
                }

                candidates.TryAdd(
                    fullPath,
                    new JavaInstallationCandidate(homePath, fullPath, source));
            }
            catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
            {
                // Discovery is best-effort. Invalid/unreadable candidates are ignored and will
                // never prevent candidates from other sources from being considered.
            }
        }

        void AddHome(string? homePath, JavaDiscoverySource source)
        {
            if (string.IsNullOrWhiteSpace(homePath))
            {
                return;
            }

            AddExecutable(Path.Combine(homePath, "bin", executableName), source);
        }

        void AddImmediateChildHomes(
            string root,
            JavaDiscoverySource source,
            Func<string, string>? transform = null,
            Func<string, bool>? predicate = null)
        {
            try
            {
                if (!Directory.Exists(root))
                {
                    return;
                }

                foreach (var directory in Directory.EnumerateDirectories(root))
                {
                    if (predicate is not null && !predicate(directory))
                    {
                        continue;
                    }

                    AddHome(transform?.Invoke(directory) ?? directory, source);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Keep discovery best-effort.
            }
        }

        options.EnvironmentVariables.TryGetValue("JAVA_HOME", out var javaHome);
        options.EnvironmentVariables.TryGetValue("JDK_HOME", out var jdkHome);
        AddHome(javaHome, JavaDiscoverySource.Environment);
        AddHome(jdkHome, JavaDiscoverySource.Environment);

        if (options.EnvironmentVariables.TryGetValue("PATH", out var pathValue) &&
            !string.IsNullOrWhiteSpace(pathValue))
        {
            foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                AddExecutable(
                    Path.Combine(entry.Trim().Trim('"'), executableName),
                    JavaDiscoverySource.Path);
            }
        }

        if (!string.IsNullOrWhiteSpace(options.MinecraftDirectory))
        {
            var runtimeDirectory = Path.Combine(options.MinecraftDirectory, "runtime");
            try
            {
                if (Directory.Exists(runtimeDirectory))
                {
                    foreach (var executable in Directory.EnumerateFiles(
                                 runtimeDirectory,
                                 executableName,
                                 SearchOption.AllDirectories))
                    {
                        if (string.Equals(
                                Path.GetFileName(Path.GetDirectoryName(executable)),
                                "bin",
                                StringComparison.OrdinalIgnoreCase))
                        {
                            AddExecutable(executable, JavaDiscoverySource.MinecraftRuntime);
                        }
                    }
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Keep discovery best-effort.
            }
        }

        AddImmediateChildHomes(
            Path.Combine(options.UserHome, ".sdkman", "candidates", "java"),
            JavaDiscoverySource.UserInstallation);
        AddImmediateChildHomes(
            Path.Combine(options.UserHome, ".jdks"),
            JavaDiscoverySource.UserInstallation);

        switch (options.Platform.OperatingSystem)
        {
            case PlatformOperatingSystem.Windows:
                AddWindowsRoots(options.ProgramFilesDirectory);
                AddWindowsRoots(options.ProgramFilesX86Directory);
                break;

            case PlatformOperatingSystem.MacOS:
                AddImmediateChildHomes(
                    "/Library/Java/JavaVirtualMachines",
                    JavaDiscoverySource.SystemInstallation,
                    directory => Path.Combine(directory, "Contents", "Home"));
                AddImmediateChildHomes(
                    Path.Combine(options.UserHome, "Library", "Java", "JavaVirtualMachines"),
                    JavaDiscoverySource.UserInstallation,
                    directory => Path.Combine(directory, "Contents", "Home"));
                AddHomebrewRoot("/opt/homebrew/opt");
                AddHomebrewRoot("/usr/local/opt");
                break;

            case PlatformOperatingSystem.Linux:
                AddImmediateChildHomes(
                    "/usr/lib/jvm",
                    JavaDiscoverySource.SystemInstallation);
                AddImmediateChildHomes(
                    "/usr/java",
                    JavaDiscoverySource.SystemInstallation);
                break;
        }

        return candidates.Values
            .OrderBy(candidate => candidate.Source)
            .ThenBy(candidate => candidate.ExecutablePath, pathComparer)
            .ToArray();

        void AddWindowsRoots(string? programFiles)
        {
            if (string.IsNullOrWhiteSpace(programFiles))
            {
                return;
            }

            foreach (var vendorDirectory in new[]
                     {
                         "Java",
                         "Eclipse Adoptium",
                         "Amazon Corretto",
                         "Zulu",
                         "Microsoft"
                     })
            {
                AddImmediateChildHomes(
                    Path.Combine(programFiles, vendorDirectory),
                    JavaDiscoverySource.SystemInstallation);
            }
        }

        void AddHomebrewRoot(string root)
        {
            AddImmediateChildHomes(
                root,
                JavaDiscoverySource.SystemInstallation,
                directory =>
                {
                    var bundleHome = Path.Combine(
                        directory,
                        "libexec",
                        "openjdk.jdk",
                        "Contents",
                        "Home");
                    return Directory.Exists(bundleHome) ? bundleHome : directory;
                },
                directory => Path.GetFileName(directory)
                    .StartsWith("openjdk", StringComparison.OrdinalIgnoreCase));
        }
    }
}
