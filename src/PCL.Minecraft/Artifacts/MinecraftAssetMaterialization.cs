namespace PCL3.Minecraft.Artifacts;

public sealed record MinecraftAssetMaterialization(
    string SourcePath,
    string TargetPath,
    string LogicalName);

public sealed record MinecraftAssetMaterializationPlan(
    IReadOnlyList<MinecraftAssetMaterialization> Files);

public static class MinecraftAssetMaterializationPlanner
{
    public static MinecraftAssetMaterializationPlan Build(
        MinecraftAssetIndex assetIndex,
        string? assetsId,
        string minecraftDirectory)
    {
        ArgumentNullException.ThrowIfNull(assetIndex);
        ArgumentException.ThrowIfNullOrWhiteSpace(minecraftDirectory);

        var minecraftRoot = Path.GetFullPath(minecraftDirectory);
        var objectRoot = Path.Combine(minecraftRoot, "assets", "objects");
        var files = new List<MinecraftAssetMaterialization>();

        if (assetIndex.Virtual && string.IsNullOrWhiteSpace(assetsId))
        {
            throw new InvalidDataException("A virtual asset index requires an assets id.");
        }

        foreach (var asset in assetIndex.Objects)
        {
            var source = Path.Combine(objectRoot, asset.Hash[..2], asset.Hash);

            if (assetIndex.Virtual)
            {
                files.Add(new MinecraftAssetMaterialization(
                    source,
                    ResolveLogicalTarget(
                        Path.Combine(minecraftRoot, "assets", "virtual", assetsId!),
                        asset.Name),
                    asset.Name));
            }

            if (assetIndex.MapToResources)
            {
                files.Add(new MinecraftAssetMaterialization(
                    source,
                    ResolveLogicalTarget(Path.Combine(minecraftRoot, "resources"), asset.Name),
                    asset.Name));
            }
        }

        return new MinecraftAssetMaterializationPlan(files);
    }

    private static string ResolveLogicalTarget(string root, string logicalName)
    {
        if (string.IsNullOrWhiteSpace(logicalName) ||
            Path.IsPathRooted(logicalName))
        {
            throw new InvalidDataException($"Unsafe asset logical name '{logicalName}'.");
        }

        var normalized = logicalName
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);
        var target = Path.GetFullPath(Path.Combine(root, normalized));
        var relative = Path.GetRelativePath(root, target);

        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Asset logical name '{logicalName}' escapes its materialization root.");
        }

        return target;
    }
}

public static class MinecraftAssetMaterializer
{
    public static async Task MaterializeAsync(
        MinecraftAssetMaterializationPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        foreach (var file in plan.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!File.Exists(file.SourcePath))
            {
                throw new FileNotFoundException(
                    $"Asset object for '{file.LogicalName}' is missing.",
                    file.SourcePath);
            }

            var directory = Path.GetDirectoryName(file.TargetPath) ??
                throw new InvalidDataException($"Asset target '{file.TargetPath}' has no parent directory.");
            Directory.CreateDirectory(directory);

            var temporary = file.TargetPath + $".pcl3-{Guid.NewGuid():N}.tmp";
            try
            {
                await using (var input = new FileStream(
                    file.SourcePath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    bufferSize: 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                await using (var output = new FileStream(
                    temporary,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 128 * 1024,
                    FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporary, file.TargetPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                {
                    File.Delete(temporary);
                }
            }
        }
    }
}
