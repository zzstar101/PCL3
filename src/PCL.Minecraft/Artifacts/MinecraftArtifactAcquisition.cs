namespace PCL3.Minecraft.Artifacts;

public enum MinecraftArtifactAcquisitionStatus
{
    AlreadyValid,
    Downloaded,
    Failed
}

public sealed record MinecraftArtifactAcquisitionItemResult(
    MinecraftArtifactRequest Artifact,
    MinecraftArtifactAcquisitionStatus Status,
    string? Source = null,
    string? Error = null);

public sealed record MinecraftArtifactAcquisitionResult(
    IReadOnlyList<MinecraftArtifactAcquisitionItemResult> Items)
{
    public bool IsSuccess => Items.All(item => item.Status is not MinecraftArtifactAcquisitionStatus.Failed);

    public int DownloadedCount => Items.Count(item => item.Status is MinecraftArtifactAcquisitionStatus.Downloaded);

    public int CachedCount => Items.Count(item => item.Status is MinecraftArtifactAcquisitionStatus.AlreadyValid);
}

public interface IMinecraftArtifactAcquirer
{
    Task<MinecraftArtifactAcquisitionResult> AcquireAsync(
        MinecraftArtifactAcquisitionPlan plan,
        CancellationToken cancellationToken = default);
}

public sealed record MinecraftArtifactAcquirerOptions(
    int MaxConcurrency = 6,
    int AttemptsPerSource = 2,
    bool AllowInsecureHttp = false);
