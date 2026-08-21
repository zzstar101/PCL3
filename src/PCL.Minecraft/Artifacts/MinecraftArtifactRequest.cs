namespace PCL3.Minecraft.Artifacts;

public enum MinecraftArtifactPurpose
{
    Library,
    NativeLibrary,
    Client,
    AssetIndex,
    AssetObject
}

public sealed record MinecraftArtifactRequest(
    string Id,
    MinecraftArtifactPurpose Purpose,
    string LocalPath,
    IReadOnlyList<string> Sources,
    string? Sha1,
    long? Size)
{
    public MinecraftArtifactRequest Normalize()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(LocalPath);

        var normalizedSha1 = string.IsNullOrWhiteSpace(Sha1)
            ? null
            : Sha1.Trim().ToLowerInvariant();

        if (normalizedSha1 is not null &&
            (normalizedSha1.Length != 40 || !normalizedSha1.All(Uri.IsHexDigit)))
        {
            throw new ArgumentException($"Artifact '{Id}' has an invalid SHA-1 value.", nameof(Sha1));
        }

        if (Size is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Size));
        }

        return this with
        {
            LocalPath = Path.GetFullPath(LocalPath),
            Sources = Sources
                .Where(source => !string.IsNullOrWhiteSpace(source))
                .Distinct(StringComparer.Ordinal)
                .ToArray(),
            Sha1 = normalizedSha1
        };
    }
}

public sealed record MinecraftArtifactAcquisitionPlan(
    IReadOnlyList<MinecraftArtifactRequest> Artifacts);
