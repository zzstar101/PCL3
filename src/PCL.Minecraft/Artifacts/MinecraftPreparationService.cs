using PCL3.Minecraft.Metadata;
using PCL3.Minecraft.Natives;
using PCL3.Minecraft.Runtime;

namespace PCL3.Minecraft.Artifacts;

public sealed record MinecraftPreparationResult(
    MinecraftArtifactAcquisitionResult PrimaryArtifacts,
    MinecraftArtifactAcquisitionResult? AssetObjects,
    int MaterializedAssetCount,
    bool NativesExtracted)
{
    public bool IsSuccess =>
        PrimaryArtifacts.IsSuccess &&
        (AssetObjects?.IsSuccess ?? true);
}

public sealed class MinecraftPreparationService
{
    private readonly IMinecraftArtifactAcquirer _acquirer;

    public MinecraftPreparationService(IMinecraftArtifactAcquirer acquirer)
    {
        ArgumentNullException.ThrowIfNull(acquirer);
        _acquirer = acquirer;
    }

    public async Task<MinecraftPreparationResult> PrepareAsync(
        MinecraftVersionChain versionChain,
        MinecraftRuntimePlan runtimePlan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(versionChain);
        ArgumentNullException.ThrowIfNull(runtimePlan);

        var primaryPlan = MinecraftPrimaryArtifactPlanner.Build(versionChain, runtimePlan);
        var primary = await _acquirer.AcquireAsync(primaryPlan, cancellationToken)
            .ConfigureAwait(false);
        if (!primary.IsSuccess)
        {
            return new MinecraftPreparationResult(primary, null, 0, false);
        }

        MinecraftArtifactAcquisitionResult? assetResult = null;
        var materializedCount = 0;
        var assetIndexReference = versionChain.EffectiveAssetIndex;

        if (assetIndexReference is not null)
        {
            var indexRequest = primaryPlan.Artifacts.Single(artifact =>
                artifact.Purpose is MinecraftArtifactPurpose.AssetIndex);
            var json = await File.ReadAllTextAsync(indexRequest.LocalPath, cancellationToken)
                .ConfigureAwait(false);
            var assetIndex = MinecraftAssetIndexJson.Parse(json);
            var assetPlan = MinecraftAssetObjectPlanner.Build(
                assetIndex,
                runtimePlan.MinecraftDirectory);
            assetResult = await _acquirer.AcquireAsync(assetPlan, cancellationToken)
                .ConfigureAwait(false);

            if (!assetResult.IsSuccess)
            {
                return new MinecraftPreparationResult(primary, assetResult, 0, false);
            }

            var materialization = MinecraftAssetMaterializationPlanner.Build(
                assetIndex,
                versionChain.EffectiveAssetsId,
                runtimePlan.MinecraftDirectory);
            await MinecraftAssetMaterializer.MaterializeAsync(materialization, cancellationToken)
                .ConfigureAwait(false);
            materializedCount = materialization.Files.Count;
        }

        var nativesExtracted = runtimePlan.NativeExtraction.Archives.Count != 0;
        if (nativesExtracted)
        {
            await SafeNativeExtractor.ExtractAsync(
                runtimePlan.NativeExtraction,
                cancellationToken).ConfigureAwait(false);
        }

        return new MinecraftPreparationResult(
            primary,
            assetResult,
            materializedCount,
            nativesExtracted);
    }
}
