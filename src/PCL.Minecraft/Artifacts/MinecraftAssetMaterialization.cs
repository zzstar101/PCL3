namespace PCL3.Minecraft.Artifacts;

public sealed record MinecraftAssetMaterialization(
    string SourcePath,
    string TargetPath,
    string TargetRoot,
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
        var virtualRoot = string.IsNullOrWhiteSpace(assetsId)
            ? null
            : Path.Combine(minecraftRoot, "assets", "virtual", assetsId);
        var resourcesRoot = Path.Combine(minecraftRoot, "resources");
        var files = new List<MinecraftAssetMaterialization>();

        if (assetIndex.Virtual && virtualRoot is null)
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
                    ResolveLogicalTarget(virtualRoot!, asset.Name),
                    Path.GetFullPath(virtualRoot!),
                    asset.Name));
            }

            if (assetIndex.MapToResources)
            {
                files.Add(new MinecraftAssetMaterialization(
                    source,
                    ResolveLogicalTarget(resourcesRoot, asset.Name),
                    Path.GetFullPath(resourcesRoot),
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

            Directory.CreateDirectory(file.TargetRoot);
            EnsureNotReparsePoint(file.TargetRoot);

            var directory = Path.GetDirectoryName(file.TargetPath) ??
                throw new InvalidDataException($"Asset target '{file.TargetPath}' has no parent directory.");
            EnsureSafeDirectoryChain(file.TargetRoot, directory);
            Directory.CreateDirectory(directory);
            EnsureSafeDirectoryChain(file.TargetRoot, directory);

            if ((File.Exists(file.TargetPath) || Directory.Exists(file.TargetPath)) &&
                (File.GetAttributes(file.TargetPath) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    $"Asset materialization refuses to replace reparse-point target '{file.TargetPath}'.");
            }

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

    private static void EnsureSafeDirectoryChain(string root, string directory)
    {
        var relative = Path.GetRelativePath(root, directory);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"Asset materialization target '{directory}' escapes root '{root}'.");
        }

        var current = root;
        if (relative is ".")
        {
            EnsureNotReparsePoint(current);
            return;
        }

        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current))
            {
                EnsureNotReparsePoint(current);
            }
        }
    }

    private static void EnsureNotReparsePoint(string directory)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException(
                $"Asset materialization refuses to traverse reparse-point directory '{directory}'.");
        }
    }
}
