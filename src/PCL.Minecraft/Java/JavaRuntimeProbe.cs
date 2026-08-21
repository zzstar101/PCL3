using System.Diagnostics;
using System.Text.RegularExpressions;
using PCL3.Platform;

namespace PCL3.Minecraft.Java;

public static partial class JavaRuntimeProbe
{
    public static async Task<JavaRuntimeDescriptor> ProbeAsync(
        JavaInstallationCandidate candidate,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var startInfo = new ProcessStartInfo
        {
            FileName = candidate.ExecutablePath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-XshowSettings:properties");
        startInfo.ArgumentList.Add("-version");

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new InvalidOperationException(
                $"Failed to start Java runtime '{candidate.ExecutablePath}'.");
        }

        var standardOutputTask = process.StandardOutput.ReadToEndAsync();
        var standardErrorTask = process.StandardError.ReadToEndAsync();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout ?? TimeSpan.FromSeconds(15));

        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException(
                $"Java runtime probe exceeded the allowed duration for '{candidate.ExecutablePath}'.");
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var output = string.Concat(
            await standardOutputTask.ConfigureAwait(false),
            Environment.NewLine,
            await standardErrorTask.ConfigureAwait(false));

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Java runtime probe exited with code {process.ExitCode} for '{candidate.ExecutablePath}'.");
        }

        return Parse(candidate.ExecutablePath, output);
    }

    public static JavaRuntimeDescriptor Parse(string executablePath, string output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executablePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(output);

        var javaVersion = GetProperty(output, "java.version") ??
            JavaVersionRegex().Match(output) is { Success: true } match
                ? match.Groups[1].Value
                : null;

        if (string.IsNullOrWhiteSpace(javaVersion))
        {
            throw new FormatException("Java probe output does not contain a Java version.");
        }

        var majorVersion = ParseMajorVersion(javaVersion);
        var architecture = ParseArchitecture(GetProperty(output, "os.arch"));
        var vendor = GetProperty(output, "java.vendor");
        var reportedHome = GetProperty(output, "java.home");
        var binDirectory = Path.GetDirectoryName(Path.GetFullPath(executablePath));
        var inferredHome = binDirectory is null ? null : Path.GetDirectoryName(binDirectory);
        var homePath = !string.IsNullOrWhiteSpace(reportedHome)
            ? Path.GetFullPath(reportedHome)
            : inferredHome ?? throw new FormatException(
                $"Cannot infer Java home from executable '{executablePath}'.");

        return new JavaRuntimeDescriptor(
            homePath,
            majorVersion,
            architecture,
            vendor,
            Path.GetFullPath(executablePath));
    }

    private static string? GetProperty(string output, string propertyName)
    {
        var prefix = propertyName + " = ";
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[prefix.Length..].Trim();
            }
        }

        return null;
    }

    private static int ParseMajorVersion(string version)
    {
        var match = LeadingVersionRegex().Match(version);
        if (!match.Success)
        {
            throw new FormatException($"Cannot parse Java version '{version}'.");
        }

        var first = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        if (first != 1)
        {
            return first;
        }

        if (!int.TryParse(
                match.Groups[2].Value,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var legacyMajor))
        {
            throw new FormatException($"Cannot parse legacy Java version '{version}'.");
        }

        return legacyMajor;
    }

    private static PlatformArchitecture ParseArchitecture(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return PlatformArchitecture.Unknown;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "amd64" or "x86_64" or "x64" => PlatformArchitecture.X64,
            "x86" or "i386" or "i486" or "i586" or "i686" => PlatformArchitecture.X86,
            "aarch64" or "arm64" => PlatformArchitecture.Arm64,
            var architecture when architecture.StartsWith("arm", StringComparison.Ordinal) =>
                PlatformArchitecture.Arm,
            _ => PlatformArchitecture.Unknown
        };
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    [GeneratedRegex("(?:java|openjdk) version \\\"([^\\\"]+)\\\"", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex JavaVersionRegex();

    [GeneratedRegex("^([0-9]+)(?:\\.([0-9]+))?", RegexOptions.CultureInvariant)]
    private static partial Regex LeadingVersionRegex();
}
