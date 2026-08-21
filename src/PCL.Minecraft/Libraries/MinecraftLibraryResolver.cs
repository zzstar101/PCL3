using PCL3.Minecraft.Metadata;
using PCL3.Platform;

namespace PCL3.Minecraft.Libraries;

public static class MinecraftLibraryResolver
{
    private const string MojangLibrariesBaseUrl = "https://libraries.minecraft.net/";

    public static MinecraftLibraryResolution Resolve(
        MinecraftVersionChain versionChain,
        MinecraftRuleContext ruleContext,
        string librariesDirectory)
    {
        ArgumentNullException.ThrowIfNull(versionChain);
        ArgumentNullException.ThrowIfNull(ruleContext);
        ArgumentException.ThrowIfNullOrWhiteSpace(librariesDirectory);

        var rootDirectory = Path.GetFullPath(librariesDirectory);
        var classpath = new Dictionary<string, ResolvedMinecraftArtifact>(StringComparer.Ordinal);
        var natives = new Dictionary<string, ResolvedMinecraftNativeArtifact>(StringComparer.Ordinal);

        foreach (var version in versionChain.Versions)
        {
            foreach (var library in version.Libraries)
            {
                if (library.Rules.Count != 0 &&
                    !MinecraftRuleEvaluator.IsAllowed(library.Rules, ruleContext))
                {
                    continue;
                }

                var coordinate = MavenCoordinate.Parse(library.Name);
                var regularArtifact = ResolveRegularArtifact(
                    coordinate,
                    library,
                    version.Id,
                    rootDirectory);

                AddPreferNewer(classpath, coordinate.Identity, regularArtifact);

                var nativeClassifier = ResolveNativeClassifier(library, ruleContext.Platform);
                if (nativeClassifier is null)
                {
                    continue;
                }

                var nativeCoordinate = coordinate with { Classifier = nativeClassifier };
                var nativeArtifact = ResolveNativeArtifact(
                    nativeCoordinate,
                    nativeClassifier,
                    library,
                    version.Id,
                    rootDirectory);

                AddPreferNewer(natives, nativeCoordinate.Identity, nativeArtifact);
            }
        }

        return new MinecraftLibraryResolution(
            classpath.Values.ToArray(),
            natives.Values.ToArray());
    }

    private static ResolvedMinecraftArtifact ResolveRegularArtifact(
        MavenCoordinate coordinate,
        MinecraftLibrary library,
        string sourceVersionId,
        string librariesDirectory)
    {
        var download = library.Downloads?.Artifact;
        var localPath = ResolveLocalPath(
            librariesDirectory,
            download?.Path,
            coordinate.RepositoryPath);
        var repositoryPath = GetRepositoryPath(download?.Path, coordinate.RepositoryPath);

        return new ResolvedMinecraftArtifact(
            coordinate,
            localPath,
            ResolveUrl(download?.Url, library.RepositoryUrl, repositoryPath),
            download?.Sha1,
            download?.Size,
            sourceVersionId);
    }

    private static ResolvedMinecraftNativeArtifact ResolveNativeArtifact(
        MavenCoordinate coordinate,
        string classifier,
        MinecraftLibrary library,
        string sourceVersionId,
        string librariesDirectory)
    {
        MinecraftDownloadArtifact? download = null;
        if (library.Downloads is not null)
        {
            library.Downloads.Classifiers.TryGetValue(classifier, out download);
        }

        var localPath = ResolveLocalPath(
            librariesDirectory,
            download?.Path,
            coordinate.RepositoryPath);
        var repositoryPath = GetRepositoryPath(download?.Path, coordinate.RepositoryPath);

        return new ResolvedMinecraftNativeArtifact(
            coordinate,
            localPath,
            ResolveUrl(download?.Url, library.RepositoryUrl, repositoryPath),
            download?.Sha1,
            download?.Size,
            library.Extract?.Exclude ?? Array.Empty<string>(),
            sourceVersionId);
    }

    private static string? ResolveNativeClassifier(
        MinecraftLibrary library,
        PlatformTarget platform)
    {
        var operatingSystemName = platform.OperatingSystem switch
        {
            PlatformOperatingSystem.Windows => "windows",
            PlatformOperatingSystem.MacOS => "osx",
            PlatformOperatingSystem.Linux => "linux",
            _ => null
        };

        if (operatingSystemName is null ||
            !library.Natives.TryGetValue(operatingSystemName, out var template))
        {
            return null;
        }

        var architectureBits = platform.Architecture switch
        {
            PlatformArchitecture.X86 or PlatformArchitecture.Arm => "32",
            PlatformArchitecture.X64 or PlatformArchitecture.Arm64 => "64",
            _ => null
        };

        if (template.Contains("${arch}", StringComparison.Ordinal) && architectureBits is null)
        {
            return null;
        }

        return architectureBits is null
            ? template
            : template.Replace("${arch}", architectureBits, StringComparison.Ordinal);
    }

    private static void AddPreferNewer<TArtifact>(
        IDictionary<string, TArtifact> artifacts,
        string key,
        TArtifact candidate)
        where TArtifact : notnull
    {
        if (!artifacts.TryGetValue(key, out var existing))
        {
            artifacts[key] = candidate;
            return;
        }

        var candidateCoordinate = GetCoordinate(candidate);
        var existingCoordinate = GetCoordinate(existing);
        if (MavenVersionComparer.Instance.Compare(
                candidateCoordinate.Version,
                existingCoordinate.Version) > 0)
        {
            artifacts[key] = candidate;
        }
    }

    private static MavenCoordinate GetCoordinate<TArtifact>(TArtifact artifact) =>
        artifact switch
        {
            ResolvedMinecraftArtifact regular => regular.Coordinate,
            ResolvedMinecraftNativeArtifact native => native.Coordinate,
            _ => throw new InvalidOperationException(
                $"Unsupported resolved library artifact type {typeof(TArtifact).FullName}.")
        };

    private static string ResolveLocalPath(
        string librariesDirectory,
        string? metadataPath,
        string fallbackRepositoryPath)
    {
        var relativePath = GetRepositoryPath(metadataPath, fallbackRepositoryPath);
        if (Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException($"Library path '{relativePath}' must be relative.");
        }

        var normalizedRelativePath = relativePath
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(librariesDirectory, normalizedRelativePath));
        var relativeToRoot = Path.GetRelativePath(librariesDirectory, fullPath);

        if (Path.IsPathRooted(relativeToRoot) ||
            relativeToRoot.Equals("..", StringComparison.Ordinal) ||
            relativeToRoot.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Library path '{relativePath}' escapes the libraries directory.");
        }

        return fullPath;
    }

    private static string GetRepositoryPath(string? metadataPath, string fallbackRepositoryPath) =>
        string.IsNullOrWhiteSpace(metadataPath)
            ? fallbackRepositoryPath
            : metadataPath.Replace('\\', '/');

    private static string ResolveUrl(
        string? directUrl,
        string? repositoryUrl,
        string repositoryPath)
    {
        if (!string.IsNullOrWhiteSpace(directUrl))
        {
            return directUrl;
        }

        var baseUrl = string.IsNullOrWhiteSpace(repositoryUrl)
            ? MojangLibrariesBaseUrl
            : repositoryUrl;

        return $"{baseUrl.TrimEnd('/')}/{repositoryPath.TrimStart('/')}";
    }
}
