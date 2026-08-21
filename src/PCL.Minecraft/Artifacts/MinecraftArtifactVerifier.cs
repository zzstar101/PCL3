using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;

namespace PCL3.Minecraft.Artifacts;

public enum MinecraftArtifactVerificationStatus
{
    Valid,
    Missing,
    SizeMismatch,
    HashMismatch
}

public sealed record MinecraftArtifactVerificationResult(
    MinecraftArtifactVerificationStatus Status,
    long? ActualSize = null,
    string? ActualSha1 = null)
{
    public bool IsValid => Status is MinecraftArtifactVerificationStatus.Valid;
}

public static class MinecraftArtifactVerifier
{
    [SuppressMessage(
        "Security",
        "CA5350:Do Not Use Weak Cryptographic Algorithms",
        Justification = "Mojang manifests mandate SHA-1 as an artifact integrity/cache identifier; it is not used as a security signature.")]
    public static async Task<MinecraftArtifactVerificationResult> VerifyAsync(
        MinecraftArtifactRequest artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        artifact = artifact.Normalize();

        var file = new FileInfo(artifact.LocalPath);
        if (!file.Exists)
        {
            return new MinecraftArtifactVerificationResult(
                MinecraftArtifactVerificationStatus.Missing);
        }

        if (artifact.Size is { } expectedSize && file.Length != expectedSize)
        {
            return new MinecraftArtifactVerificationResult(
                MinecraftArtifactVerificationStatus.SizeMismatch,
                file.Length);
        }

        if (artifact.Sha1 is null)
        {
            return new MinecraftArtifactVerificationResult(
                MinecraftArtifactVerificationStatus.Valid,
                file.Length);
        }

        await using var stream = new FileStream(
            artifact.LocalPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        var actualSha1 = Convert.ToHexStringLower(hash);

        return string.Equals(actualSha1, artifact.Sha1, StringComparison.OrdinalIgnoreCase)
            ? new MinecraftArtifactVerificationResult(
                MinecraftArtifactVerificationStatus.Valid,
                file.Length,
                actualSha1)
            : new MinecraftArtifactVerificationResult(
                MinecraftArtifactVerificationStatus.HashMismatch,
                file.Length,
                actualSha1);
    }
}
