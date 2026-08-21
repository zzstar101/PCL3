namespace PCL3.Minecraft.Libraries;

public sealed record MavenCoordinate(
    string Group,
    string Artifact,
    string Version,
    string? Classifier = null,
    string Extension = "jar")
{
    public string FileName =>
        $"{Artifact}-{Version}{(string.IsNullOrEmpty(Classifier) ? string.Empty : $"-{Classifier}")}.{Extension}";

    /// <summary>
    /// Identity excluding the version. Useful when comparing alternative versions of the same artifact.
    /// </summary>
    public string Identity =>
        $"{Group}:{Artifact}:{Classifier ?? string.Empty}@{Extension}";

    public string RepositoryPath =>
        $"{Group.Replace('.', '/')}/{Artifact}/{Version}/{FileName}";

    public string GetLocalPath(string librariesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(librariesDirectory);

        var groupPath = Group.Replace('.', Path.DirectorySeparatorChar);
        return Path.Combine(
            Path.GetFullPath(librariesDirectory),
            groupPath,
            Artifact,
            Version,
            FileName);
    }

    public static MavenCoordinate Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var coordinatePart = value;
        var extension = "jar";
        var extensionSeparator = value.LastIndexOf('@');

        if (extensionSeparator >= 0)
        {
            if (extensionSeparator == value.Length - 1)
            {
                throw new FormatException($"Maven coordinate '{value}' has an empty extension.");
            }

            coordinatePart = value[..extensionSeparator];
            extension = value[(extensionSeparator + 1)..];
        }

        var parts = coordinatePart.Split(':');
        if (parts.Length is not (3 or 4) || parts.Any(string.IsNullOrWhiteSpace))
        {
            throw new FormatException(
                $"Maven coordinate '{value}' must use group:artifact:version[:classifier][@extension].");
        }

        if (string.IsNullOrWhiteSpace(extension) ||
            extension.Contains('/') ||
            extension.Contains('\\'))
        {
            throw new FormatException($"Maven coordinate '{value}' has an invalid extension.");
        }

        return new MavenCoordinate(
            parts[0],
            parts[1],
            parts[2],
            parts.Length == 4 ? parts[3] : null,
            extension);
    }
}
